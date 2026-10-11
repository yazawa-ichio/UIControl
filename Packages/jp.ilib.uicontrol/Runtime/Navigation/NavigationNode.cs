using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace UIControl
{
	/// <summary>
	/// Selectableごとの方向候補・除外設定と、Move / Select / PointerDownの受け口。
	/// FocusScopeが配下のSelectableへ自動で付けるので、明示候補や除外が不要なら配置しなくてよい。
	/// 動的に生成する行では、Scopeが生成を検知できないため行Prefabに付けておく。
	/// </summary>
	[DisallowMultipleComponent]
	public sealed class NavigationNode : MonoBehaviour, IMoveHandler, IPointerDownHandler, ISelectHandler
	{
		[SerializeField]
		private UnityEngine.UI.Selectable m_Selectable;
		[SerializeField]
		private NavigationCandidate[] m_Up = Array.Empty<NavigationCandidate>();
		[SerializeField]
		private NavigationCandidate[] m_Down = Array.Empty<NavigationCandidate>();
		[SerializeField]
		private NavigationCandidate[] m_Left = Array.Empty<NavigationCandidate>();
		[SerializeField]
		private NavigationCandidate[] m_Right = Array.Empty<NavigationCandidate>();
		[SerializeField]
		private NavigationDirections m_Directions = NavigationDirections.All;
		[SerializeField, Tooltip("方向入力の自動探索で到達先にしない。カーソルと明示候補からは選べる。Drag対象のように、指せる必要はあるが方向キーで巡回させたくないものへ使う。")]
		private bool m_ExcludeFromAutomatic;
		private UnityEngine.UI.Selectable m_OwnedSelectable;
		private UnityEngine.UI.Navigation m_OriginalNavigation;
		internal bool OwnsNavigation => m_OwnedSelectable != null;
		public UnityEngine.UI.Selectable Selectable => m_Selectable != null ? m_Selectable : GetComponent<UnityEngine.UI.Selectable>();
		public FocusScope Scope { get; internal set; }
		/// <summary>Scopeが自動で付けたNode。Inspectorには出さない。</summary>
		public bool IsAuto { get; private set; }
		internal bool ExcludeFromAutomatic => m_ExcludeFromAutomatic;
		public RectTransform Rect => transform as RectTransform;

		internal void MarkAuto()
		{
			IsAuto = true;
			hideFlags |= HideFlags.HideInInspector | HideFlags.DontSave;
		}

		public IReadOnlyList<NavigationCandidate> Candidates(MoveDirection direction)
		{
			switch (direction)
			{
				case MoveDirection.Up: return m_Up;
				case MoveDirection.Down: return m_Down;
				case MoveDirection.Left: return m_Left;
				case MoveDirection.Right: return m_Right;
				default: return Array.Empty<NavigationCandidate>();
			}
		}

		/// <summary>この対象を選ぶ。Scopeも有効化する。</summary>
		public bool RequestSelect() { return Scope != null && UIFocus.Arbiter.RequestSelect(this); }
		/// <summary>選択は動かさず、この対象を復元先として記憶する。</summary>
		public bool Remember() { return Scope != null && UIFocus.Arbiter.Remember(this); }

		/// <summary>uGUIの自動Navigationを止め、移動をScopeの規則へ委ねる。解除時は元の設定へ戻す。</summary>
		internal void OwnNavigation(bool own)
		{
			UnityEngine.UI.Selectable selectable = Selectable;
			if (m_OwnedSelectable != null && (!own || m_OwnedSelectable != selectable))
			{
				m_OwnedSelectable.navigation = m_OriginalNavigation;
				m_OwnedSelectable = null;
			}
			if (own && selectable != null && m_OwnedSelectable == null)
			{
				m_OwnedSelectable = selectable;
				m_OriginalNavigation = selectable.navigation;
			}
			if (own && m_OwnedSelectable != null)
			{
				UnityEngine.UI.Navigation navigation = m_OwnedSelectable.navigation;
				navigation.mode = UnityEngine.UI.Navigation.Mode.None;
				m_OwnedSelectable.navigation = navigation;
			}
		}

		public void OnMove(AxisEventData eventData)
		{
			FocusArbiter arbiter = UIFocus.Arbiter;
			if (Scope == null || !arbiter.CanSelect(this) || NativeWidgetOwns(eventData.moveDir)) { return; }
			arbiter.NotifyInputMode(FocusInputMode.Navigation);
			NavigationNode next = arbiter.ResolveMove(this, eventData.moveDir);
			if (next != null && arbiter.RequestSelect(next, eventData.moveDir)) { eventData.Use(); }
		}

		public void OnPointerDown(PointerEventData eventData)
		{
			if (eventData.button == PointerEventData.InputButton.Left && Scope != null) { UIFocus.Arbiter.NotifyInputMode(FocusInputMode.Pointer); }
		}

		public void OnSelect(BaseEventData eventData)
		{
			if (Scope != null) { UIFocus.Arbiter.ObserveSelection(this); }
		}

		internal bool NativeWidgetOwns(MoveDirection direction)
		{
			NavigationDirections flag = direction == MoveDirection.Up ? NavigationDirections.Up : direction == MoveDirection.Down
				? NavigationDirections.Down : direction == MoveDirection.Left ? NavigationDirections.Left : direction == MoveDirection.Right
				? NavigationDirections.Right : NavigationDirections.None;
			if (flag == NavigationDirections.None || (m_Directions & flag) == 0) { return true; }
			bool horizontal = direction == MoveDirection.Left || direction == MoveDirection.Right;
			if (Selectable is UnityEngine.UI.Slider slider)
			{
				return horizontal == (slider.direction == UnityEngine.UI.Slider.Direction.LeftToRight || slider.direction == UnityEngine.UI.Slider.Direction.RightToLeft);
			}
			if (Selectable is UnityEngine.UI.Scrollbar scrollbar)
			{
				return horizontal == (scrollbar.direction == UnityEngine.UI.Scrollbar.Direction.LeftToRight || scrollbar.direction == UnityEngine.UI.Scrollbar.Direction.RightToLeft);
			}
			return Selectable is UnityEngine.UI.InputField input && input.isFocused
				|| Selectable is TMPro.TMP_InputField textInput && textInput.isFocused;
		}

		private FocusScope Owner() { return GetComponentInParent<FocusScope>(true); }
		private void OnEnable() { Owner()?.Invalidate(); }
		private void OnDisable() { Scope?.Invalidate(); }
		private void OnDestroy()
		{
			OwnNavigation(false);
			Scope?.Nodes.Remove(this);
			Scope?.Invalidate();
			Scope = null;
		}
		private void OnTransformParentChanged()
		{
			Scope?.Invalidate();
			Owner()?.Invalidate();
		}
		private void OnValidate() { if (Application.isPlaying) { Scope?.Invalidate(); } }
		private void Reset() { m_Selectable = GetComponent<UnityEngine.UI.Selectable>(); }
	}
}
