using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;

namespace UIControl
{
	[DisallowMultipleComponent]
	[RequireComponent(typeof(UnityEngine.UI.Selectable))]
	[AddComponentMenu("UIControl/Animation/Selectable State Driver")]
	public sealed class SelectableStateDriver : UIBehaviour, IPointerEnterHandler, IPointerExitHandler,
		IPointerDownHandler, IPointerUpHandler, ISelectHandler, IDeselectHandler, ISubmitHandler
	{
		[SerializeField]
		private NamedStatePlayer m_Player;
		[SerializeField]
		private string m_Channel = "Selection";
		[SerializeField]
		private string m_Normal = "Normal";
		[SerializeField]
		private string m_Highlighted = "Highlighted";
		[SerializeField]
		private string m_Selected = "Selected";
		[SerializeField]
		private string m_Pressed = "Pressed";
		[SerializeField]
		private string m_Disabled = "Disabled";
		[SerializeField, Min(0)]
		private float m_SubmitDuration = 0.1f;
		private UnityEngine.UI.Selectable m_Selectable;
		private Page m_Screen;
		private ListItemStateDriver m_Row;
		private bool m_IsSelected;
		private bool m_IsPointerInside;
		private bool m_IsPointerDown;
		private int m_PointerId;
		private double m_SubmitUntil;
		private string m_State;
		private bool m_IsReady;

		protected override void Awake()
		{
			base.Awake();
			m_Selectable = GetComponent<UnityEngine.UI.Selectable>();
		}

		protected override void OnEnable()
		{
			base.OnEnable();
			m_IsReady = false;
			Connect();
			ReadSelection();
			Refresh();
		}

		protected override void OnDisable()
		{
			Disconnect();
			Suspend();
			base.OnDisable();
		}

		protected override void OnTransformParentChanged()
		{
			base.OnTransformParentChanged();
			if (isActiveAndEnabled)
			{
				Disconnect();
				Connect();
				Refresh();
			}
		}

		private void Connect()
		{
			m_Screen = GetComponentInParent<Page>(true);
			m_Row = GetComponentInParent<ListItemStateDriver>(true);
			if (m_Screen != null) { m_Screen.RenderingChanged += OnRenderingChanged; }
			if (m_Row != null) { m_Row.VisibilityChanged += OnRowVisibilityChanged; }
		}

		private void Disconnect()
		{
			if (m_Screen != null) { m_Screen.RenderingChanged -= OnRenderingChanged; }
			if (m_Row != null) { m_Row.VisibilityChanged -= OnRowVisibilityChanged; }
			m_Screen = null;
			m_Row = null;
		}

		private void LateUpdate()
		{
			// 親のOnEnableからは、まだ有効化されていない子Adapterを開始しない。
			m_IsReady = true;
			ReadSelection();
			Refresh();
		}

		protected override void OnCanvasGroupChanged()
		{
			base.OnCanvasGroupChanged();
			// 利用側が設定したCanvasGroupの操作可否を演出へ反映する。
			if (m_Selectable != null) { Refresh(); }
		}

		private void ReadSelection()
		{
			m_IsSelected = EventSystem.current != null && EventSystem.current.currentSelectedGameObject == gameObject;
		}

		private bool IsInteractable()
		{
			return m_Selectable != null && m_Selectable.isActiveAndEnabled && m_Selectable.IsInteractable();
		}

		private void Refresh()
		{
			if (!isActiveAndEnabled || m_Player == null) { return; }
			if (!m_Player.isActiveAndEnabled || m_Screen != null && !m_Screen.IsRendered || m_Row != null && !m_Row.IsShown)
			{
				Suspend();
				return;
			}
			bool interactable = IsInteractable();
			// 可否判定ではなく、取り上げられた入力の後始末。blocksRaycastsが落ちるとUpが届かずPressedのまま残る。
			if (!interactable || m_Screen != null && !m_Screen.IsInputAllowed)
			{
				m_IsPointerDown = false;
				m_SubmitUntil = 0;
			}
			if (!m_IsReady) { return; }
			string state = !interactable ? m_Disabled : m_IsPointerDown || Time.unscaledTimeAsDouble < m_SubmitUntil ? m_Pressed
				: m_IsSelected ? m_Selected : m_IsPointerInside ? m_Highlighted : m_Normal;
			if (m_State == state) { return; }
			m_State = state;
			// 空のmappingはその状態の演出を使わない指定。旧状態のloopは停止する。
			if (string.IsNullOrEmpty(state))
			{
				StopChannel();
				return;
			}
			Observe(m_Player.Play(state, m_Channel));
		}

		private void Suspend()
		{
			m_IsPointerInside = false;
			m_IsPointerDown = false;
			m_SubmitUntil = 0;
			if (m_State != null) { StopChannel(); }
			m_State = null;
		}

		private void StopChannel()
		{
			try { if (m_Player != null) { m_Player.Stop(m_Channel); } }
			catch (Exception exception) { Diagnose(exception); }
		}

		private async void Observe(Task<StateResult> task)
		{
			try { Report(await task); }
			catch (Exception exception) { Diagnose(exception); }
		}

		private void Report(StateResult result)
		{
			if (result.Status == StateStatus.Faulted)
			{
				Diagnose(result.Exception ?? new InvalidOperationException("Selectable animation failed."));
			}
			else if (result.Status == StateStatus.Missing)
			{
				// 空の指定は問い合わせ前にRefreshが弾く。ここへ来るのは名前を書いたのに対応する状態が無い場合。
				Diagnose(new InvalidOperationException("Selectable animation state is missing on " + name + ": " + m_Channel + "/" + m_State));
			}
		}

		/// <summary>表示の装飾は例外で止めず、ログへ出して残りの表示を続ける。</summary>
		private void Diagnose(Exception exception) { Debug.LogException(exception, this); }

		private void OnRenderingChanged(bool rendered)
		{
			if (!rendered) { Suspend(); }
			else { ReadSelection(); Refresh(); }
		}

		private void OnRowVisibilityChanged()
		{
			ReadSelection();
			Refresh();
		}

		public void OnPointerEnter(PointerEventData eventData) { m_IsPointerInside = true; Refresh(); }
		public void OnPointerExit(PointerEventData eventData) { m_IsPointerInside = false; Refresh(); }
		public void OnSelect(BaseEventData eventData) { m_IsSelected = true; Refresh(); }
		public void OnDeselect(BaseEventData eventData) { m_IsSelected = false; Refresh(); }

		public void OnPointerDown(PointerEventData eventData)
		{
			// レイキャストを通る経路。入力不可のPageはblocksRaycastsで候補から外れるため、ここでPage状態を見ない。
			if (!isActiveAndEnabled || eventData.button != PointerEventData.InputButton.Left || !IsInteractable()) { return; }
			m_PointerId = eventData.pointerId;
			m_IsPointerDown = true;
			Refresh();
		}

		public void OnPointerUp(PointerEventData eventData)
		{
			if (eventData.button != PointerEventData.InputButton.Left || eventData.pointerId != m_PointerId) { return; }
			m_IsPointerDown = false;
			Refresh();
		}

		public void OnSubmit(BaseEventData eventData)
		{
			// 装飾だけを止める検査は置かない。Submitの可否は選択の制御で反映する。
			if (!isActiveAndEnabled || !IsInteractable()) { return; }
			// 装飾だけの短い押下。標準WidgetのSubmit/clickや入力の消費は行わない。
			float duration = float.IsNaN(m_SubmitDuration) || float.IsInfinity(m_SubmitDuration) ? 0.1f : Mathf.Max(0, m_SubmitDuration);
			m_SubmitUntil = Time.unscaledTimeAsDouble + duration;
			Refresh();
		}

#if UNITY_EDITOR
		protected override void Reset()
		{
			base.Reset();
			m_Player = GetComponent<NamedStatePlayer>();
		}
#endif
	}
}
