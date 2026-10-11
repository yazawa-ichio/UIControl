using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace UIControl
{
	public enum AnimationTimeMode { Unscaled, Scaled }

	public abstract class AnimationAdapter : MonoBehaviour
	{
		public abstract bool SupportsExternalTime { get; }
		public abstract Task PlayAsync(AnimationTimeMode timeMode, CancellationToken cancellation);
		public virtual void Advance(float deltaTime) { throw new NotSupportedException("External animation time is not supported."); }
		public abstract void Stop();
		public abstract void ApplyFinal();

		protected internal static void ValidateDeltaTime(float deltaTime)
		{
			if (float.IsNaN(deltaTime) || float.IsInfinity(deltaTime) || deltaTime < 0) { throw new ArgumentOutOfRangeException(nameof(deltaTime)); }
		}
		protected internal static void ValidateTimeMode(AnimationTimeMode mode)
		{
			if (mode < AnimationTimeMode.Unscaled || mode > AnimationTimeMode.Scaled) { throw new ArgumentOutOfRangeException(nameof(mode)); }
		}
		protected internal static CancellationTokenRegistration RegisterCancellation(CancellationToken cancellation, Action cancel)
		{
			SynchronizationContext context = SynchronizationContext.Current;
			if (context == null) { throw new InvalidOperationException("Animation must start on Unity's main thread."); }
			int thread = Thread.CurrentThread.ManagedThreadId;
			return cancellation.Register(() =>
			{
				if (thread == Thread.CurrentThread.ManagedThreadId) { cancel(); }
				else { context.Post(_ => cancel(), null); }
			});
		}
	}
}
