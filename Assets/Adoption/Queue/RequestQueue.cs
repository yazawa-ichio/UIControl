using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace UIControl.Samples
{
	/// <summary>PageHandleを使って要求を直列表示する利用例。VMを所有しない。公開操作はUnityメインスレッド専用。</summary>
	public sealed class RequestQueue : IDisposable
	{
		private readonly IUIStack m_Stack;
		private readonly SynchronizationContext m_Context;
		private readonly int m_Thread;
		private readonly LinkedList<Request> m_Waiting = new LinkedList<Request>();
		private readonly TaskCompletionSource<bool> m_Shutdown = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		private Request m_Current;
		private bool m_Running;
		private bool m_Stopped;
		private bool m_Disposed;

		public RequestQueue(IUIStack stack)
		{
			m_Stack = stack ?? throw new ArgumentNullException(nameof(stack));
			m_Context = SynchronizationContext.Current ?? throw new InvalidOperationException("Create the queue on Unity's main thread.");
			m_Thread = Thread.CurrentThread.ManagedThreadId;
		}

		/// <summary>回答後もCloseとFocusの確定を待つ。Waitingの取消は生成せずに完了する。</summary>
		public Task<RequestResult<T>> Enqueue<T>(Func<RequestResponder<T>, UIParameter> createParameter,
			OpenOptions options = null, CancellationToken cancellation = default)
		{
			CheckThread();
			if (m_Stopped) { throw new ObjectDisposedException(nameof(RequestQueue)); }
			if (createParameter == null) { throw new ArgumentNullException(nameof(createParameter)); }
			Request<T> request = new Request<T>(this, createParameter, options ?? OpenOptions.Default);
			request.Node = m_Waiting.AddLast(request);
			request.Registration = cancellation.Register(() =>
			{
				if (Thread.CurrentThread.ManagedThreadId == m_Thread) { Cancel(request); }
				else { m_Context.Post(_ => Cancel(request), null); }
			});
			if (request.Finished) { request.Registration.Dispose(); }
			if (!m_Running && !m_Stopped && m_Waiting.Count != 0)
			{
				m_Running = true;
				Pump();
			}
			return request.Completion.Task;
		}

		/// <summary>新規受付とWaitingを停止し、現在の表示は通常Closeで回収する。</summary>
		public Task ShutdownGracefully()
		{
			CheckThread();
			m_Stopped = true;
			CancelWaiting(null);
			if (m_Current != null) { Cancel(m_Current); }
			else { EndBatch(); }
			return m_Shutdown.Task;
		}

		/// <summary>自分のHandleだけを強制回収し、全結果を同期で確定する。</summary>
		public void Dispose()
		{
			CheckThread();
			Abort(null);
		}

		private void Abort(Exception error)
		{
			if (m_Disposed) { return; }
			m_Disposed = true;
			m_Stopped = true;
			CancelWaiting(error);
			Request current = m_Current;
			if (current != null && !current.Finished)
			{
				current.Decide(RequestStatus.Cancelled);
				current.Handle?.ForceRelease();
				CloseResult closed = current.Handle?.Closed.IsCompleted == true ? current.Handle.Closed.Result : new CloseResult(CloseStatus.Forced);
				Finish(current, closed, error);
			}
			EndBatch();
			if (error != null) { UnityEngine.Debug.LogException(error); }
		}

		private void Cancel(Request request)
		{
			if (request.Finished || request.Decided) { return; }
			request.Decide(RequestStatus.Cancelled);
			if (request.Node != null)
			{
				m_Waiting.Remove(request.Node);
				request.Node = null;
				request.Complete(new CloseResult(CloseStatus.Closed), null);
			}
			else if (!request.Showing) { request.OpenCancellation.Cancel(); }
		}

		private void CancelWaiting(Exception error)
		{
			while (m_Waiting.First != null)
			{
				Request request = m_Waiting.First.Value;
				m_Waiting.RemoveFirst();
				request.Node = null;
				request.Decide(RequestStatus.Cancelled);
				request.Complete(new CloseResult(CloseStatus.Closed), error);
			}
		}

		private async void Pump()
		{
			await Task.Yield();
			try
			{
				while (!m_Stopped && m_Waiting.First != null)
				{
					Request request = m_Waiting.First.Value;
					m_Waiting.RemoveFirst();
					request.Node = null;
					m_Current = request;
					try
					{
						UIParameter parameter = request.CreateParameter();
						if (request.Finished || m_Stopped) { continue; }
						request.Handle = m_Stack.Push(parameter, request.Options, request.OpenCancellation.Token);
						// Push publishes activity before returning. A subscriber may cancel the queue there.
						if (request.Finished || m_Stopped) { request.Handle.ForceRelease(); continue; }
						OpenResult opened = await request.Handle.Opened;
						if (request.Finished) { continue; }
						Exception error = opened.Exception;
						if (opened.Status == OpenStatus.Opened)
						{
							request.Showing = true;
							await Task.WhenAny(request.Decision.Task, request.Handle.Closed);
						}
						else { request.Decide(opened.Status == OpenStatus.ClosedBeforeOpen ? RequestStatus.Dismissed : RequestStatus.Cancelled); }
						if (request.Finished) { continue; }
						CloseResult closed = await request.Handle.Close();
						if (!request.Finished) { Finish(request, closed, error); }
					}
					catch (Exception exception)
					{
						if (!request.Finished)
						{
							request.Handle?.ForceRelease();
							Finish(request, new CloseResult(CloseStatus.Forced), exception);
						}
					}
					finally { if (m_Current == request) { m_Current = null; } }
				}
			}
			finally { m_Running = false; EndBatch(); }
		}

		private void Finish(Request request, CloseResult closed, Exception error)
		{
			if (request.Finished) { return; }
			request.Decide(closed.Status == CloseStatus.Forced ? RequestStatus.Cancelled : RequestStatus.Dismissed);
			if (m_Waiting.Count == 0 || m_Stopped) { EndBatch(); }
			request.Complete(closed, error);
		}

		private void EndBatch()
		{
			if (m_Stopped && (m_Current == null || m_Current.Finished)) { m_Shutdown.TrySetResult(true); }
		}

		private void CheckThread()
		{
			if (Thread.CurrentThread.ManagedThreadId != m_Thread) { throw new InvalidOperationException("RequestQueue operations require Unity's main thread."); }
		}

		private abstract class Request
		{
			internal readonly OpenOptions Options;
			internal readonly CancellationTokenSource OpenCancellation = new CancellationTokenSource();
			internal readonly TaskCompletionSource<bool> Decision = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
			internal CancellationTokenRegistration Registration;
			internal LinkedListNode<Request> Node;
			internal PageHandle Handle;
			internal bool Showing;
			internal bool Finished;
			internal bool Decided;
			internal RequestStatus Status;
			protected Request(OpenOptions options) { Options = options; }
			internal bool Decide(RequestStatus status)
			{
				if (Finished || Decided) { return false; }
				Decided = true;
				Status = status;
				Decision.TrySetResult(true);
				return true;
			}
			internal abstract UIParameter CreateParameter();
			internal abstract void Complete(CloseResult closed, Exception error);
		}

		private sealed class Request<T> : Request
		{
			private Func<RequestResponder<T>, UIParameter> m_CreateParameter;
			private readonly RequestResponder<T> m_Responder;
			private T m_Value;
			internal readonly TaskCompletionSource<RequestResult<T>> Completion = new TaskCompletionSource<RequestResult<T>>(TaskCreationOptions.RunContinuationsAsynchronously);
			internal Request(RequestQueue owner, Func<RequestResponder<T>, UIParameter> createParameter, OpenOptions options) : base(options)
			{
				m_CreateParameter = createParameter;
				m_Responder = new RequestResponder<T>((status, value) =>
				{
					owner.CheckThread();
					if (!Showing || Finished || Handle.State != EntryState.Live || !Decide(status)) { return false; }
					m_Value = value;
					return true;
				});
			}
			internal override UIParameter CreateParameter() { return m_CreateParameter(m_Responder); }
			internal override void Complete(CloseResult closed, Exception error)
			{
				if (Finished) { return; }
				Finished = true;
				Registration.Dispose();
				OpenCancellation.Dispose();
				m_CreateParameter = null;
				Exception failure = error ?? closed.Exception;
				RequestStatus status = failure != null || closed.Status == CloseStatus.Faulted ? RequestStatus.Faulted : Status;
				Completion.TrySetResult(new RequestResult<T>(status, m_Value, Status, failure, closed.Status == CloseStatus.Forced));
			}
		}
	}
}
