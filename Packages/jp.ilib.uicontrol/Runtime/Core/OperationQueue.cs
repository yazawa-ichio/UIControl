using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace UIControl
{
	internal sealed class OperationQueue
	{
		private readonly Queue<(Func<Task> Operation, TaskCompletionSource<bool> Completion)> m_Operations = new Queue<(Func<Task>, TaskCompletionSource<bool>)>();
		private bool m_IsRunning;

		internal Task Run(Func<Task> operation)
		{
			TaskCompletionSource<bool> completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
			m_Operations.Enqueue((operation, completion));
			if (!m_IsRunning) { Pump(); }
			return completion.Task;
		}

		private async void Pump()
		{
			m_IsRunning = true;
			await Task.Yield();
			try
			{
				while (m_Operations.Count > 0)
				{
					(Func<Task> operation, TaskCompletionSource<bool> completion) = m_Operations.Dequeue();
					try
					{
						await operation();
						completion.TrySetResult(true);
					}
					catch (Exception exception) { completion.TrySetException(exception); }
				}
			}
			finally { m_IsRunning = false; }
		}
	}
}
