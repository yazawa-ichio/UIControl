using System;
using System.Threading.Tasks;

namespace UIControl
{
	public sealed class PageHandle
	{
		private readonly UIStack m_Owner;
		internal readonly Entry Entry;
		internal readonly TaskCompletionSource<OpenResult> OpenCompletion = new TaskCompletionSource<OpenResult>(TaskCreationOptions.RunContinuationsAsynchronously);
		internal readonly TaskCompletionSource<CloseResult> CloseCompletion = new TaskCompletionSource<CloseResult>(TaskCreationOptions.RunContinuationsAsynchronously);
		public Task<OpenResult> Opened => OpenCompletion.Task;
		public Task<CloseResult> Closed => CloseCompletion.Task;
		public EntryState State { get; private set; } = EntryState.Queued;
		public Page Page => Entry.Ended ? null : Entry.Instance?.Page;
		public event Action<PageHandle> StateChanged;

		internal PageHandle(UIStack owner, Entry entry)
		{
			m_Owner = owner;
			Entry = entry;
		}

		public Task<CloseResult> Close()
		{
			m_Owner.CheckThread();
			if (!Entry.Ended) { m_Owner.RequestClose(Entry); }
			return Closed;
		}

		public void ForceRelease()
		{
			m_Owner.CheckThread();
			if (!Entry.Ended) { m_Owner.Release(Entry, OpenStatus.Forced, CloseStatus.Forced); }
		}

		internal void SetState(EntryState state)
		{
			State = state;
			if (StateChanged == null) { return; }
			foreach (Action<PageHandle> observer in StateChanged.GetInvocationList())
			{
				try { observer(this); }
				catch (Exception exception) { m_Owner.ReportError(exception); }
			}
		}
	}
}
