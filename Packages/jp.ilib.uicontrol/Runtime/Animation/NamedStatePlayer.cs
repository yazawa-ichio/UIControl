using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace UIControl
{
	[DisallowMultipleComponent]
	public sealed class NamedStatePlayer : MonoBehaviour
	{
		[SerializeField] private StateDefinition[] m_States = Array.Empty<StateDefinition>();
		private readonly Dictionary<string, Channel> m_Channels = new Dictionary<string, Channel>(StringComparer.Ordinal);
		private readonly Queue<Action> m_Operations = new Queue<Action>();
		private bool m_IsProcessing;
		private bool m_IsStoppingAll;
		[SerializeField] private AnimationTimeMode m_TimeMode;
		private readonly List<(Channel Channel, Run Run)> m_Advancing = new List<(Channel, Run)>();
		private bool m_IsAdvancing;
		public AnimationTimeMode TimeMode
		{
			get => m_TimeMode;
			set
			{
				AnimationAdapter.ValidateTimeMode(value);
				if (m_TimeMode == value) { return; }
				foreach (Channel slot in m_Channels.Values)
				{
					if (slot.Requested != null && !slot.Requested.Completion.Task.IsCompleted) { throw new InvalidOperationException("Stop playback before changing its time mode."); }
				}
				m_TimeMode = value;
			}
		}

		/// <summary>時間はPlayerだけが供給する。外部時間対応のAdapterへ毎フレームdeltaTimeを渡す。</summary>
		private void Update()
		{
			AdvanceActive(m_TimeMode == AnimationTimeMode.Scaled ? Time.deltaTime : Time.unscaledDeltaTime);
		}
		private void AdvanceActive(float deltaTime)
		{
			if (m_IsAdvancing) { throw new InvalidOperationException("Advance cannot be called recursively."); }
			m_IsAdvancing = true;
			try
			{
				// Snapshot runs, not just channels: a callback cannot tick a replacement twice.
				m_Advancing.Clear();
				foreach (Channel slot in m_Channels.Values)
				{
					Run run = slot.Active;
					if (run != null && !run.Completion.Task.IsCompleted && run.Definition.Animation != null && run.Definition.Animation.SupportsExternalTime)
					{
						m_Advancing.Add((slot, run));
					}
				}
				foreach (var active in m_Advancing)
				{
					if (active.Channel.Active != active.Run || active.Channel.Requested != active.Run || active.Run.Completion.Task.IsCompleted) { continue; }
					try { active.Run.Definition.Animation.Advance(deltaTime); }
					catch (Exception exception) { FailAdvance(active.Channel, active.Run, exception); }
				}
			}
			finally { m_Advancing.Clear(); m_IsAdvancing = false; }
		}

		private void FailAdvance(Channel channel, Run run, Exception exception)
		{
			Enqueue(() => Finish(channel, run, StateStatus.Faulted, exception));
		}

		public IReadOnlyList<StateDefinition> States => m_States;

		private sealed class Channel
		{
			public Run Requested;
			public Run Active;
		}

		private sealed class Run
		{
			public readonly StateDefinition Definition;
			public readonly TaskCompletionSource<StateResult> Completion = new TaskCompletionSource<StateResult>(TaskCreationOptions.RunContinuationsAsynchronously);
			public CancellationTokenSource Cancellation = new CancellationTokenSource();

			public Run(StateDefinition definition)
			{
				Definition = definition;
			}

			public void Finish(StateStatus status, Exception exception = null)
			{
				Completion.TrySetResult(new StateResult(status, exception));
			}
		}

		public Task<StateResult> Play(string state, string channel = "Default", bool restart = false)
		{
			StateDefinition definition;
			try
			{
				definition = FindState(state, channel);
			}
			catch (Exception exception)
			{
				return Task.FromResult(new StateResult(StateStatus.Faulted, exception));
			}
			if (definition == null)
			{
				return Task.FromResult(new StateResult(StateStatus.Missing));
			}
			if (!isActiveAndEnabled || m_IsStoppingAll)
			{
				return Task.FromResult(new StateResult(StateStatus.Cancelled));
			}
			Channel slot = GetChannel(definition.Channel);
			if (!restart && slot.Requested?.Definition == definition)
			{
				return slot.Requested.Completion.Task;
			}
			Run run = Replace(slot, definition);
			Enqueue(() => StartRun(slot, run));
			return run.Completion.Task;
		}

		public StateResult ApplyImmediately(string state, string channel = "Default")
		{
			if (m_IsProcessing || m_IsStoppingAll)
			{
				return new StateResult(StateStatus.Faulted, new InvalidOperationException("ApplyImmediately cannot run inside an adapter callback."));
			}
			try
			{
				StateDefinition definition = FindState(state, channel);
				if (definition == null)
				{
					return new StateResult(StateStatus.Missing);
				}
				Channel slot = GetChannel(definition.Channel);
				Run run = Replace(slot, definition);
				Enqueue(() => StartRun(slot, run, true));
				return run.Completion.Task.GetAwaiter().GetResult();
			}
			catch (Exception exception)
			{
				return new StateResult(StateStatus.Faulted, exception);
			}
		}

		public void Stop(string channel = "Default")
		{
			if (!m_Channels.TryGetValue(NormalizeChannel(channel), out Channel slot))
			{
				return;
			}
			Invalidate(slot, StateStatus.Cancelled);
			Enqueue(() => StopActive(slot));
		}

		public void StopAll()
		{
			m_IsStoppingAll = true;
			Channel[] slots = new List<Channel>(m_Channels.Values).ToArray();
			foreach (Channel slot in slots)
			{
				Invalidate(slot, StateStatus.Cancelled);
			}
			Enqueue(() =>
			{
				List<Exception> errors = new List<Exception>();
				try
				{
					foreach (Channel slot in slots)
					{
						try { StopActive(slot); }
						catch (Exception exception) { errors.Add(exception); }
					}
				}
				finally { m_IsStoppingAll = false; }
				if (errors.Count != 0)
				{
					throw new AggregateException(errors);
				}
			});
		}

		public StateDefinition FindState(string state, string channel = "Default")
		{
			if (string.IsNullOrEmpty(state))
			{
				throw new ArgumentException("State name is required.", nameof(state));
			}
			ValidateConfiguration();
			channel = NormalizeChannel(channel);
			foreach (StateDefinition definition in m_States)
			{
				if (definition.Channel == channel && definition.Name == state)
				{
					return definition;
				}
			}
			return null;
		}

		public void ValidateConfiguration()
		{
			HashSet<(string, string)> names = new HashSet<(string, string)>();
			Dictionary<AnimationAdapter, string> channels = new Dictionary<AnimationAdapter, string>();
			foreach (StateDefinition definition in m_States)
			{
				if (definition == null || string.IsNullOrEmpty(definition.Name) || definition.Animation == null)
				{
					throw new InvalidOperationException("Each state requires a name and an animation adapter.");
				}
				if (!names.Add((definition.Channel, definition.Name)))
				{
					throw new InvalidOperationException($"Duplicate state: {definition.Channel}/{definition.Name}.");
				}
				if (channels.TryGetValue(definition.Animation, out string previous) && previous != definition.Channel)
				{
					throw new InvalidOperationException("An adapter cannot be shared between channels.");
				}
				channels[definition.Animation] = definition.Channel;
			}
		}

		private static string NormalizeChannel(string channel)
		{
			return string.IsNullOrEmpty(channel) ? "Default" : channel;
		}

		private Channel GetChannel(string channel)
		{
			if (!m_Channels.TryGetValue(channel, out Channel slot))
			{
				slot = new Channel();
				m_Channels.Add(channel, slot);
			}
			return slot;
		}

		private static void Invalidate(Channel slot, StateStatus status)
		{
			Run previous = slot.Requested;
			slot.Requested = null;
			previous?.Finish(status);
			if (previous != null && previous != slot.Active)
			{
				previous.Cancellation?.Dispose();
				previous.Cancellation = null;
			}
		}

		private static Run Replace(Channel slot, StateDefinition definition)
		{
			Invalidate(slot, StateStatus.Superseded);
			Run run = new Run(definition);
			slot.Requested = run;
			return run;
		}

		private void StartRun(Channel slot, Run run, bool immediately = false)
		{
			if (slot.Requested != run)
			{
				return;
			}
			try
			{
				StopActive(slot);
				if (slot.Requested != run)
				{
					return;
				}
				slot.Active = run;
				if (immediately)
				{
					run.Definition.Animation.ApplyFinal();
					Finish(slot, run, StateStatus.Completed, null);
				}
				else
				{
					Task playback = run.Definition.Animation.PlayAsync(m_TimeMode, run.Cancellation.Token);
					Observe(slot, run, playback ?? throw new InvalidOperationException("The adapter returned a null task."));
				}
			}
			catch (Exception exception)
			{
				Finish(slot, run, StateStatus.Faulted, exception);
			}
		}

		private async void Observe(Channel slot, Run run, Task playback)
		{
			StateStatus status = StateStatus.Completed;
			Exception error = null;
			try { await playback; }
			catch (OperationCanceledException) { status = StateStatus.Cancelled; }
			catch (Exception exception) { status = StateStatus.Faulted; error = exception; }
			Enqueue(() => Finish(slot, run, status, error));
		}

		private void Finish(Channel slot, Run run, StateStatus status, Exception error)
		{
			if (slot.Requested != run)
			{
				return;
			}
			if (status != StateStatus.Completed && slot.Active == run)
			{
				try { StopActive(slot); }
				catch (Exception stopError)
				{
					status = StateStatus.Faulted;
					error = error == null ? stopError : new AggregateException(error, stopError);
				}
			}
			run.Cancellation?.Dispose();
			run.Cancellation = null;
			run.Finish(status, error);
		}

		private static void StopActive(Channel slot)
		{
			Run active = slot.Active;
			if (active == null)
			{
				return;
			}
			try
			{
				if (active.Definition.Animation != null)
				{
					active.Definition.Animation.Stop();
				}
				slot.Active = null;
			}
			finally
			{
				CancellationTokenSource cancellation = active.Cancellation;
				active.Cancellation = null;
				if (cancellation != null)
				{
					try { cancellation.Cancel(); }
					finally { cancellation.Dispose(); }
				}
			}
		}

		private void Enqueue(Action operation)
		{
			m_Operations.Enqueue(operation);
			if (m_IsProcessing)
			{
				return;
			}
			m_IsProcessing = true;
			List<Exception> errors = new List<Exception>();
			try
			{
				while (m_Operations.Count > 0)
				{
					try { m_Operations.Dequeue()(); }
					catch (Exception exception) { errors.Add(exception); }
				}
			}
			finally { m_IsProcessing = false; }
			if (errors.Count != 0)
			{
				throw new AggregateException(errors);
			}
		}

		private void OnDisable()
		{
			try { StopAll(); }
			catch (Exception exception) { Debug.LogException(exception, this); }
		}

		private void OnDestroy()
		{
			OnDisable();
		}
	}
}
