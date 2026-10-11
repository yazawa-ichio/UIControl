using System;

namespace UIControl
{
	public enum StateStatus
	{
		Completed,
		Superseded,
		Cancelled,
		Missing,
		Faulted
	}

	public readonly struct StateResult
	{
		public StateStatus Status { get; }
		public Exception Exception { get; }

		public StateResult(StateStatus status, Exception exception = null)
		{
			Status = status;
			Exception = exception;
		}
	}
}
