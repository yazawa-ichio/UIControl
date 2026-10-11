using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LitMotion;
using UnityEngine;
using UnityEngine.UI;

namespace UIControl.LitMotion
{
	public enum TweenProperty { AnchoredPosition, LocalScale, LocalRotation, Alpha, Color, Circle }

	public sealed class LitMotionAnimationAdapter : AnimationAdapter
	{
		[Serializable]
		public sealed class Tween
		{
			[SerializeField] internal TweenProperty m_Property;
			[SerializeField] internal RectTransform m_Rect;
			[SerializeField] internal CanvasGroup m_Group;
			[SerializeField] internal Graphic m_Graphic;
			[SerializeField] internal bool m_FromCurrent = true;
			[SerializeField] internal bool m_ApplyStartImmediately;
			[SerializeField] internal Vector3 m_From;
			[SerializeField] internal Vector3 m_To = Vector3.one;
			[SerializeField] internal float m_FromAlpha;
			[SerializeField] internal float m_ToAlpha = 1;
			[SerializeField] internal Color m_FromColor = Color.white;
			[SerializeField] internal Color m_ToColor = Color.white;
			[SerializeField, Min(0)] internal float m_Duration = 0.2f;
			[SerializeField, Min(0)] internal float m_Delay;
			[SerializeField] internal Ease m_Ease = Ease.OutQuad;
			[SerializeField] internal Vector2 m_Center;
			[SerializeField, Min(0)] internal float m_Radius = 100;
			[SerializeField] internal float m_StartAngle;
			[SerializeField] internal float m_Angle = 360;

			internal Snapshot Capture()
			{
				ValidateDeltaTime(m_Duration);
				ValidateDeltaTime(m_Delay);
				if (!Enum.IsDefined(typeof(TweenProperty), m_Property) || !Enum.IsDefined(typeof(Ease), m_Ease)) { throw new InvalidOperationException("Invalid tween property or easing."); }
				var snapshot = new Snapshot
				{
					Property = m_Property, Rect = m_Rect, Group = m_Group, Graphic = m_Graphic,
					From = m_From, To = m_To, FromAlpha = m_FromAlpha, ToAlpha = m_ToAlpha,
					FromColor = m_FromColor, ToColor = m_ToColor, Center = m_Center, Radius = m_Radius,
					StartAngle = m_StartAngle, Angle = m_Angle, Duration = m_Duration, Delay = m_Delay,
					Ease = m_Ease, ApplyStart = m_ApplyStartImmediately
				};
				if (snapshot.Target == null) { throw new InvalidOperationException("Assign a live target for every tween."); }
				if (m_FromCurrent)
				{
					switch (m_Property)
					{
						case TweenProperty.AnchoredPosition: snapshot.From = m_Rect.anchoredPosition; break;
						case TweenProperty.LocalScale: snapshot.From = m_Rect.localScale; break;
						case TweenProperty.LocalRotation: snapshot.From = m_Rect.localEulerAngles; break;
						case TweenProperty.Alpha: snapshot.FromAlpha = m_Group.alpha; break;
						case TweenProperty.Color: snapshot.FromColor = m_Graphic.color; break;
						case TweenProperty.Circle:
							Vector2 offset = m_Rect.anchoredPosition - m_Center;
							snapshot.Radius = offset.magnitude;
							snapshot.StartAngle = Mathf.Atan2(offset.y, offset.x) * Mathf.Rad2Deg;
							break;
					}
				}
				snapshot.Validate();
				return snapshot;
			}
		}

		[SerializeField] private Tween[] m_Tweens = Array.Empty<Tween>();
		private Run m_Run;
		// Storage remains registered inside LitMotion. Reuse dispatchers by concurrent run count,
		// not by created Prefab count, and never update LitMotion's shared Default dispatcher.
		private static readonly Stack<ManualMotionDispatcher> s_Dispatchers = new Stack<ManualMotionDispatcher>();
		public override bool SupportsExternalTime => true;

		private sealed class Run
		{
			internal readonly TaskCompletionSource<bool> Completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
			internal readonly List<MotionHandle> Handles = new List<MotionHandle>();
			internal CancellationTokenRegistration Registration;
			internal ManualMotionDispatcher Dispatcher;
			internal int Remaining;
			internal bool Updating;
			internal bool Finished;
		}
		internal sealed class Snapshot
		{
			internal TweenProperty Property;
			internal RectTransform Rect;
			internal CanvasGroup Group;
			internal Graphic Graphic;
			internal Vector3 From, To;
			internal float FromAlpha, ToAlpha, Radius, StartAngle, Angle, Duration, Delay;
			internal Color FromColor, ToColor;
			internal Vector2 Center;
			internal Ease Ease;
			internal bool ApplyStart;
			internal UnityEngine.Object Target => Property == TweenProperty.Alpha ? Group : Property == TweenProperty.Color ? Graphic : Rect;
			internal int PropertyKey => Property == TweenProperty.Circle ? (int)TweenProperty.AnchoredPosition : (int)Property;
			internal void Validate()
			{
				void Finite(float value) { if (float.IsNaN(value) || float.IsInfinity(value)) { throw new InvalidOperationException("Tween values must be finite."); } }
				foreach (float value in new[] { From.x, From.y, From.z, To.x, To.y, To.z, FromAlpha, ToAlpha,
					FromColor.r, FromColor.g, FromColor.b, FromColor.a, ToColor.r, ToColor.g, ToColor.b, ToColor.a,
					Center.x, Center.y, Radius, StartAngle, Angle }) { Finite(value); }
				if (Radius < 0) { throw new InvalidOperationException("Circle radius must be nonnegative."); }
			}
			internal void Apply(float progress)
			{
				if (Target == null) { throw new InvalidOperationException("The tween target was destroyed."); }
				switch (Property)
				{
					case TweenProperty.AnchoredPosition: Rect.anchoredPosition = Vector3.LerpUnclamped(From, To, progress); break;
					case TweenProperty.LocalScale: Rect.localScale = Vector3.LerpUnclamped(From, To, progress); break;
					case TweenProperty.LocalRotation: Rect.localEulerAngles = Vector3.LerpUnclamped(From, To, progress); break;
					case TweenProperty.Alpha: Group.alpha = Mathf.LerpUnclamped(FromAlpha, ToAlpha, progress); break;
					case TweenProperty.Color: Graphic.color = UnityEngine.Color.LerpUnclamped(FromColor, ToColor, progress); break;
					case TweenProperty.Circle:
						float angle = (StartAngle + Angle * progress) * Mathf.Deg2Rad;
						Rect.anchoredPosition = Center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * Radius;
						break;
				}
			}
		}

		private List<Snapshot> Capture()
		{
			var snapshots = new List<Snapshot>(m_Tweens.Length);
			var properties = new HashSet<(UnityEngine.Object, int)>();
			foreach (Tween tween in m_Tweens)
			{
				if (tween == null) { throw new InvalidOperationException("Tween items cannot be null."); }
				Snapshot snapshot = tween.Capture();
				if (!properties.Add((snapshot.Target, snapshot.PropertyKey))) { throw new InvalidOperationException("Tween items write the same target property."); }
				snapshots.Add(snapshot);
			}
			return snapshots;
		}

		public override Task PlayAsync(AnimationTimeMode timeMode, CancellationToken cancellation)
		{
			ValidateTimeMode(timeMode);
			if (cancellation.IsCancellationRequested) { return Task.FromCanceled(cancellation); }
			if (!isActiveAndEnabled) { return Task.FromException(new InvalidOperationException("Enable the tween adapter before playback.")); }
			List<Snapshot> snapshots = Capture();
			Stop();
			Run run = new Run { Dispatcher = s_Dispatchers.Count != 0 ? s_Dispatchers.Pop() : new ManualMotionDispatcher(), Remaining = snapshots.Count };
			m_Run = run;
			try
			{
				foreach (Snapshot snapshot in snapshots)
				{
					if (m_Run != run) { break; }
					if (snapshot.ApplyStart) { snapshot.Apply(0); }
					if (m_Run != run) { break; }
					MotionHandle handle = LMotion.Create(0f, 1f, snapshot.Duration)
						.WithEase(snapshot.Ease).WithDelay(snapshot.Delay, skipValuesDuringDelay: true)
						.WithScheduler(run.Dispatcher.Scheduler)
						.WithOnComplete(() => { if (m_Run == run && --run.Remaining == 0) { Finish(run, null, false); } })
						.Bind(progress =>
						{
							if (m_Run != run) { return; }
							try { snapshot.Apply(progress); }
							catch (Exception exception) { Finish(run, exception, false); }
						});
					run.Handles.Add(handle);
				}
				if (m_Run == run)
				{
					run.Registration = RegisterCancellation(cancellation, () => { if (m_Run == run) { Stop(); } });
					if (run.Finished) { run.Registration.Dispose(); }
					if (snapshots.Count == 0) { Finish(run, null, false); }
				}
			}
			catch (Exception exception) { Finish(run, exception, false); }
			return run.Completion.Task;
		}

		public override void Advance(float deltaTime)
		{
			ValidateDeltaTime(deltaTime);
			Run run = m_Run;
			if (run == null) { return; }
			run.Updating = true;
			try { run.Dispatcher.Update(deltaTime); }
			catch (Exception exception) { Finish(run, exception, false); }
			finally { run.Updating = false; if (run.Finished) { ReturnDispatcher(run); } }
		}
		public override void Stop() { if (m_Run != null) { Finish(m_Run, null, true); } }
		public override void ApplyFinal()
		{
			List<Snapshot> snapshots = Capture();
			Stop();
			foreach (Snapshot snapshot in snapshots) { snapshot.Apply(1); }
		}
		private void Finish(Run run, Exception exception, bool cancelled)
		{
			if (run.Finished) { return; }
			run.Finished = true;
			if (m_Run == run) { m_Run = null; }
			run.Registration.Dispose();
			foreach (MotionHandle handle in run.Handles) { handle.TryCancel(); }
			if (!run.Updating) { ReturnDispatcher(run); }
			if (exception != null) { run.Completion.TrySetException(exception); }
			else if (cancelled) { run.Completion.TrySetCanceled(); }
			else { run.Completion.TrySetResult(true); }
		}
		private static void ReturnDispatcher(Run run)
		{
			ManualMotionDispatcher dispatcher = run.Dispatcher;
			run.Dispatcher = null;
			run.Handles.Clear();
			if (dispatcher == null) { return; }
			dispatcher.Reset();
			s_Dispatchers.Push(dispatcher);
		}
		private void OnDisable() { Stop(); }
		private void OnDestroy() { Stop(); }
	}
}
