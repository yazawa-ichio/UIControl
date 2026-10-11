using System;

namespace UIControl.Samples
{
	public enum RequestStatus { Accepted, Dismissed, Cancelled, Faulted }

	/// <summary>CloseとFocus再評価後の結果。DecisionStatusはClose失敗前の回答意図を残す。</summary>
	public sealed class RequestResult<T>
	{
		private readonly T m_Value;
		public RequestStatus Status { get; }
		public RequestStatus DecisionStatus { get; }
		public Exception Error { get; }
		public bool WasForced { get; }
		public T Value => Status == RequestStatus.Accepted ? m_Value : throw new InvalidOperationException("Only an accepted request has a value.");
		/// <summary>Close失敗時の診断用。業務処理の再実行を意味しない。</summary>
		public T DecisionValue => DecisionStatus == RequestStatus.Accepted ? m_Value : throw new InvalidOperationException("There was no accepted decision.");
		internal RequestResult(RequestStatus status, T value, RequestStatus decision, Exception error, bool forced)
		{
			Status = status;
			m_Value = value;
			DecisionStatus = decision;
			Error = error;
			WasForced = forced;
		}
	}

}
