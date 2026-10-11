using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace UIControl
{
	public sealed class ClipAnimationAdapter : AnimationAdapter
	{
		[SerializeField] private AnimationClip m_Clip;
		[SerializeField] private GameObject m_Root;
		private TaskCompletionSource<bool> m_Completion;
		private CancellationTokenRegistration m_Registration;
		private AnimationClip m_ActiveClip;
		private GameObject m_ActiveRoot;
		private double m_Elapsed;
		public AnimationClip Clip { get => m_Clip; set => m_Clip = value; }
		public GameObject Root { get => m_Root; set => m_Root = value; }
		public override bool SupportsExternalTime => true;

		public override Task PlayAsync(AnimationTimeMode timeMode, CancellationToken cancellation)
		{
			ValidateTimeMode(timeMode);
			if (cancellation.IsCancellationRequested) { return Task.FromCanceled(cancellation); }
			if (!isActiveAndEnabled || m_Clip == null || m_Clip.isLooping)
			{
				return Task.FromException(new InvalidOperationException("An enabled adapter and a finite, non-looping clip are required."));
			}
			GameObject root = m_Root != null ? m_Root : gameObject;
			if (!m_Clip.legacy && root.GetComponent<Animator>() == null)
			{
				return Task.FromException(new InvalidOperationException("A non-Legacy clip requires an Animator on its target in Player builds. Add an Animator to the target Prefab; an Animator Controller is not required."));
			}
			Stop();
			var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
			m_Completion = completion;
			m_ActiveClip = m_Clip;
			m_ActiveRoot = root;
			m_Elapsed = 0;
			try
			{
				m_Registration = RegisterCancellation(cancellation, () => { if (m_Completion == completion) { Stop(); } });
				if (m_Completion == completion) { Sample(0); }
				if (m_Completion == completion && m_ActiveClip.length <= 0) { Complete(); }
				if (m_Completion != completion) { m_Registration.Dispose(); }
			}
			catch (Exception exception) { Fail(exception); }
			return completion.Task;
		}

		public override void Advance(float deltaTime)
		{
			ValidateDeltaTime(deltaTime);
			if (m_Completion == null) { return; }
			try
			{
				if (m_ActiveClip == null || m_ActiveRoot == null) { throw new InvalidOperationException("The active clip or target was destroyed."); }
				m_Elapsed = Math.Min(m_Elapsed + deltaTime, m_ActiveClip.length);
				bool finished = m_Elapsed >= m_ActiveClip.length;
				Sample((float)m_Elapsed);
				if (finished && m_Completion != null) { Complete(); }
			}
			catch (Exception exception) { Fail(exception); }
		}
		public override void Stop()
		{
			TaskCompletionSource<bool> completion = Detach();
			completion?.TrySetCanceled();
		}
		public override void ApplyFinal()
		{
			Stop();
			if (m_Clip == null || m_Clip.isLooping) { throw new InvalidOperationException("Assign a finite, non-looping clip."); }
			GameObject root = m_Root != null ? m_Root : gameObject;
			if (!m_Clip.legacy && root.GetComponent<Animator>() == null) { throw new InvalidOperationException("Add an Animator to the non-Legacy clip target Prefab. An Animator Controller is not required."); }
			m_Clip.SampleAnimation(root, m_Clip.length);
		}
		private void Sample(float time) { m_ActiveClip.SampleAnimation(m_ActiveRoot, time); }
		private TaskCompletionSource<bool> Detach()
		{
			TaskCompletionSource<bool> completion = m_Completion;
			m_Completion = null;
			m_ActiveClip = null;
			m_ActiveRoot = null;
			m_Registration.Dispose();
			m_Registration = default;
			return completion;
		}
		private void Complete() { Detach()?.TrySetResult(true); }
		private void Fail(Exception exception) { Detach()?.TrySetException(exception); }
		private void OnDisable() { Stop(); }
		private void OnDestroy() { Stop(); }
	}
}
