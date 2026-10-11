using System;

namespace UIControl.Samples
{
	/// <summary>表示中の最初の回答だけを受理する。Unityメインスレッドから呼ぶ。</summary>
	public sealed class RequestResponder<T>
	{
		private readonly Func<RequestStatus, T, bool> m_Resolve;
		internal RequestResponder(Func<RequestStatus, T, bool> resolve) { m_Resolve = resolve; }
		public bool TryAccept(T value) { return m_Resolve(RequestStatus.Accepted, value); }
		public bool TryDismiss() { return m_Resolve(RequestStatus.Dismissed, default); }
	}
}
