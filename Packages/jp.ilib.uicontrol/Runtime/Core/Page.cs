using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace UIControl
{
	[DisallowMultipleComponent]
	public sealed class Page : MonoBehaviour
	{
		[SerializeField]
		private CanvasGroup m_InputGroup;
		[SerializeField]
		private PageVisual m_Visual;
		[SerializeField]
		private FocusScope m_Focus;
		[SerializeField]
		private PrepareStep[] m_Preparation = Array.Empty<PrepareStep>();
		[SerializeField]
		private BackdropSettings m_Backdrop;
		internal Action Destroyed;
		public event Action<bool> RenderingChanged;
		/// <summary>IsInputAllowedの変化。上端のLiveなPageだけが真になる。</summary>
		public event Action<bool> InputAllowedChanged;
		public PageHandle Handle { get; private set; }
		public FocusScope Focus => m_Focus;
		[SerializeField] private FocusOnOpen m_FocusOnOpen = FocusOnOpen.Inherit;
		public FocusOnOpen FocusOnOpen => m_FocusOnOpen;
		[SerializeField, Tooltip("このPageの演出と背面の演出の順序。InheritはUIStackの既定。")]
		private TransitionOrder m_TransitionOrder = TransitionOrder.Inherit;
		public TransitionOrder TransitionOrder => m_TransitionOrder;

		internal void Attach(PageHandle handle) { Handle = handle; }
		internal void Detach() { Handle = null; }

		public bool IsPreparing { get; internal set; }
		public bool IsRendered { get; private set; }
		public bool IsInputAllowed { get; private set; }
		public Presentation Presentation { get; private set; } = Presentation.Hidden;
		public BackdropSettings Backdrop => m_Backdrop;
		public IReadOnlyList<PrepareStep> Preparation => m_Preparation;

		public void ValidateConfiguration()
		{
			if (m_Focus != null && m_Focus.GetComponentInParent<Page>(true) != this)
			{
				throw new InvalidOperationException("Page Focus must belong to this Page.");
			}
			if (m_InputGroup == null || !m_InputGroup.transform.IsChildOf(transform))
			{
				throw new InvalidOperationException("Assign a visibility and input CanvasGroup inside this Page.");
			}
			HashSet<PrepareStep> steps = new HashSet<PrepareStep>();
			foreach (PrepareStep step in m_Preparation)
			{
				if (step == null || !steps.Add(step) || !step.transform.IsChildOf(transform))
				{
					throw new InvalidOperationException("Prepare steps must be non-null, unique components in this Page.");
				}
			}
		}

		internal void SetRendering(bool enabled)
		{
			bool changed = IsRendered != enabled;
			IsRendered = enabled;
			// Visual（PageStateDriver）がある時は演出がalphaを持つ。Pageが上書きすると同じCanvasGroupでフェードを作れない。
			if (m_InputGroup != null && m_Visual == null) { m_InputGroup.alpha = enabled ? 1f : 0f; }
			if (changed && RenderingChanged != null)
			{
				foreach (Action<bool> listener in RenderingChanged.GetInvocationList())
				{
					try { listener(enabled); }
					catch (Exception exception) { Debug.LogException(exception, this); }
				}
			}
		}

		internal void SetInputAllowed(bool allowed)
		{
			bool changed = IsInputAllowed != allowed;
			IsInputAllowed = allowed;
			// 遷移中の入力制限で、子SelectableをDisabled表示へ変えない。blocksRaycastsだけを扱う。
			if (m_InputGroup != null) { m_InputGroup.blocksRaycasts = allowed; }
			if (!allowed)
			{
				GameObject selected = UIFocus.Selected;
				if (selected != null && selected.GetComponentInParent<Page>(true) == this) { UIFocus.Deselect(selected); }
			}
			if (changed)
			{
				if (InputAllowedChanged != null)
				{
					foreach (Action<bool> listener in InputAllowedChanged.GetInvocationList())
					{
						try { listener(allowed); }
						catch (Exception exception) { Debug.LogException(exception, this); }
					}
				}
				UIFocus.Reconcile();
			}
		}

		internal void SetPresentation(Presentation next)
		{
			bool becameHidden = next == Presentation.Hidden && Presentation != Presentation.Hidden;
			Presentation = next;
			SetRendering(next != Presentation.Hidden);
			if (becameHidden)
			{
				try { StopAnimations(); }
				catch (Exception exception) { Debug.LogException(exception, this); }
			}
		}

		internal Task Transition(Presentation next, ChangeReason reason)
		{
			if (next != Presentation.Hidden) { SetRendering(true); }
			return m_Visual == null ? Task.CompletedTask : m_Visual.Transition(next, reason);
		}

		internal void ApplyImmediately(Presentation next)
		{
			try { m_Visual?.ApplyImmediately(next, next == Presentation.Hidden ? ChangeReason.Hide : ChangeReason.Restore); }
			finally { SetPresentation(next); }
		}

		/// <summary>Pageの演出を止める。Page配下の個別の演出（Selectable / 行）はRenderingChanged(false)で自分で止まる。</summary>
		internal void StopAnimations() { m_Visual?.Stop(); }

		private void OnDestroy()
		{
			Destroyed?.Invoke();
		}

		private void Reset()
		{
			m_InputGroup = GetComponent<CanvasGroup>();
			m_Visual = GetComponent<PageVisual>();
		}
	}
}
