using System;

namespace UIControl
{
	public enum EntryState { Queued, Loading, Preparing, Opening, Live, Closing, Released }
	public enum Presentation { Visible, Covered, Hidden }
	public enum ChangeReason { Open, Close, Cover, Hide, Restore }
	public enum OpenStatus { Opened, Cancelled, ClosedBeforeOpen, Replaced, Forced, Faulted }
	public enum CloseStatus { Closed, Replaced, Forced, Faulted, Empty }

	/// <summary>
	/// 前面Pageの演出と背面の演出の順序。前面は開く・閉じるPage自身、背面はBackdropで退く／戻る後ろのPageと、Replaceの旧Page。
	/// ForegroundFirst: 前面 → 背面（Replaceでは新Open → 旧Close）。
	/// BackgroundFirst: 背面 → 前面（Replaceでは旧Close → 新Open）。
	/// Concurrent: 同時。
	/// 解決は Options → Page Prefab → UIStack の順で、UIStackが終端。
	/// </summary>
	public enum TransitionOrder { Inherit, ForegroundFirst, BackgroundFirst, Concurrent }

	public readonly struct OpenResult
	{
		public OpenStatus Status { get; }
		public Exception Exception { get; }
		public OpenResult(OpenStatus status, Exception exception = null)
		{
			Status = status;
			Exception = exception;
		}
	}

	public readonly struct CloseResult
	{
		public CloseStatus Status { get; }
		public Exception Exception { get; }
		public CloseResult(CloseStatus status, Exception exception = null)
		{
			Status = status;
			Exception = exception;
		}
	}
}
