using System;
using System.Collections.Generic;
using System.Threading;

namespace UIControl
{
	public sealed class PrepareContext
	{
		private readonly List<Action> m_Cleanup = new List<Action>();
		private bool m_IsAlive = true;
		private readonly int m_Thread = Thread.CurrentThread.ManagedThreadId;
		public Page Page { get; }
		public CancellationToken Cancellation { get; }
		public bool IsAlive
		{
			get
			{
				CheckThread();
				return m_IsAlive && !Cancellation.IsCancellationRequested && Page != null;
			}
		}

		private void CheckThread()
		{
			if (Thread.CurrentThread.ManagedThreadId != m_Thread)
			{
				throw new InvalidOperationException("PrepareContext operations must run on Unity's main thread.");
			}
		}

		internal PrepareContext(Page screen, CancellationToken cancellation)
		{
			Page = screen;
			Cancellation = cancellation;
		}

		public void ThrowIfEnded()
		{
			if (!IsAlive) { throw new OperationCanceledException(Cancellation); }
		}

		public bool Own(IDisposable resource)
		{
			CheckThread();
			if (resource == null) { throw new ArgumentNullException(nameof(resource)); }
			if (!IsAlive)
			{
				resource.Dispose();
				return false;
			}
			m_Cleanup.Add(resource.Dispose);
			return true;
		}

		public void OnRelease(Action cleanup)
		{
			CheckThread();
			if (cleanup == null) { throw new ArgumentNullException(nameof(cleanup)); }
			if (!IsAlive) { cleanup(); }
			else { m_Cleanup.Add(cleanup); }
		}

		internal void Release(Action<Exception> report)
		{
			m_IsAlive = false;
			for (int i = m_Cleanup.Count - 1; i >= 0; i--)
			{
				try { m_Cleanup[i](); }
				catch (Exception exception) { report(exception); }
			}
			m_Cleanup.Clear();
		}

		internal void Invalidate() { m_IsAlive = false; }
	}
}
