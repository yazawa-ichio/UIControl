using System;
using System.Threading;
using System.Threading.Tasks;
using GameStateStructure;

namespace UIControl.GSS
{
	public sealed class PageExitTask : IExitTask
	{
		private readonly PageHandle m_Handle;
		private readonly SynchronizationContext m_Context;
		private readonly int m_Thread;

		public PageExitTask(PageHandle handle)
		{
			m_Handle = handle ?? throw new ArgumentNullException(nameof(handle));
			m_Context = SynchronizationContext.Current ?? throw new InvalidOperationException("Create the exit task on Unity's main thread.");
			m_Thread = Thread.CurrentThread.ManagedThreadId;
		}

		public async Task Run(CancellationToken cancellation)
		{
			CheckThread();
			using (cancellation.Register(() =>
			{
				if (Thread.CurrentThread.ManagedThreadId == m_Thread) { ForceRelease(); }
				else { m_Context.Post(_ => ForceRelease(), null); }
			}))
			{
				CloseResult result = await m_Handle.Close();
				if (result.Status == CloseStatus.Faulted)
				{
					throw result.Exception ?? new InvalidOperationException("Page close failed.");
				}
			}
		}

		public void ForceRelease()
		{
			CheckThread();
			m_Handle.ForceRelease();
		}

		private void CheckThread()
		{
			if (Thread.CurrentThread.ManagedThreadId != m_Thread)
			{
				throw new InvalidOperationException("Run and ForceRelease must be called on Unity's main thread.");
			}
		}
	}
}
