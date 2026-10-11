using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

namespace UIControl.InputSystem
{
	/// <summary>
	/// Mouse（実マウス・仮想Mouseデバイス）とNavigation（方向キー）の方式を判定し、方式に応じて
	/// EventSystem.sendNavigationEventsとカーソルの表示を切り替える。
	/// </summary>
	/// <remarks>
	/// Input Moduleへデバイスもポインタも足さず、設定も参照も読み書きしない。
	/// 書き換える外部は自身のAction、EventSystem.sendNavigationEvents、Cursor.visible、指定されたGraphicのenabledだけ。
	/// 仮想カーソルは <see cref="VirtualMouseCursor"/> がデバイスとして提供し、UIへの配送はInput Moduleの標準経路で行われる。
	/// </remarks>
	[DisallowMultipleComponent]
	// EventSystem (-1000) より前に方式と可否を決める。
	[DefaultExecutionOrder(-2000)]
	[RequireComponent(typeof(EventSystem))]
	public sealed class InputSystemUIInputRouter : MonoBehaviour, IUIInputSource
	{
		[SerializeField] private PointerSelection m_Selection;
		[SerializeField, Tooltip("Navigationへ切り替える方向入力。sendNavigationEventsを下ろしている間はEventSystemから観測できないため自前で持つ。")]
		private InputActionProperty m_NavigationMove;
		[SerializeField, Tooltip("仮想Mouseデバイス。任意。指定すると、その操作中はOSカーソルを隠してGraphicを出す。")]
		private VirtualMouseCursor m_VirtualMouse;
		[SerializeField, Tooltip("仮想マウス操作中に表示するGraphic。任意。位置はPointerFollower（Source = VirtualMouseCursor）で追従させる。")]
		private Graphic m_VirtualMouseGraphic;
		[SerializeField, Tooltip("実マウス操作中だけOSカーソルを表示し、仮想マウス操作中とNavigation中は隠す。")]
		private bool m_ManageOsCursor = true;
		[SerializeField, Tooltip("Navigationへ移る時、Mouse（実・仮想）の位置をデバイス状態だけ画面外へ置き、置き去りのHoverを解く。OSカーソルは動かさず、次のマウス移動で実位置へ戻る。")]
		private bool m_ParkPointerOnNavigation = true;
		[SerializeField, Tooltip("Navigation中は選択中のSelectableの中心をPositionとして報告する。PointerFollower（Source = Router）が選択へ追従し、仮想マウスもそこから始まる。")]
		private bool m_FollowSelectionInNavigation = true;
		[SerializeField, Tooltip("選択中のSelectableのどこをPointerの位置にするか。Navigation中の報告位置と、仮想マウスへ戻る時の起点に使う。要素にPointerAnchorがあればそちらを優先する。")]
		private SelectionAnchor m_SelectionAnchor = SelectionAnchor.Center;
		[SerializeField, Min(0), Tooltip("四隅を選んだ時に中心方向へ寄せる量（画面ピクセル）。0でも1pxは内側に置く。")]
		private float m_SelectionInset = 8;
		[SerializeField, Tooltip("上の位置に足す画面ピクセルのずらし。PointerAnchorの位置にも足す。")]
		private Vector2 m_SelectionOffset;
		private bool m_HasNavigationPosition;
		private static readonly Vector2 ParkedPosition = new Vector2(-10000, -10000);
		[SerializeField, Min(0)] private float m_MouseMoveThreshold = 2;
		[SerializeField, Range(0, 1)] private float m_StickThreshold = 0.2f;
		private EventSystem m_Events;
		private Mouse m_ActiveMouse;
		private bool m_OriginalNavigation;
		private bool m_OriginalCursorVisible;
		private bool m_OriginalGraphicEnabled;
		private bool m_ChangedVisibility;
		private bool m_Bound;
		private bool m_Focused = true;
		private bool m_Changing;
		private UIInputMode? m_RequestedMode;
		private bool m_WaitForNavigationRelease;
		private Vector2 m_Position;
		public UIInputMode Mode { get; private set; } = UIInputMode.Mouse;
		public Vector2 Position => m_Position;
		public bool IsInputActive => isActiveAndEnabled && m_Focused && m_Events != null && m_Events.isActiveAndEnabled;
		/// <summary>いま操作中のMouseが仮想Mouseデバイス。</summary>
		public bool IsVirtualMouseActive => m_VirtualMouse != null && m_ActiveMouse != null && ReferenceEquals(m_ActiveMouse, m_VirtualMouse.Device);
		public bool IsPointerPressed => IsInputActive && Mode == UIInputMode.Mouse && m_ActiveMouse != null && m_ActiveMouse.added && m_ActiveMouse.leftButton.isPressed;
		public event Action<UIInputMode> ModeChanged;

		/// <summary>設定したActionと検出中のMouseを見るための書き出し。入力が効かないときに原因を切り分ける。</summary>
		public string DescribeInputs()
		{
			InputAction action = m_NavigationMove.action;
			string nav = action == null ? "未設定" : !action.enabled ? "無効" : action.name + "=" + action.ReadValue<Vector2>().ToString("F1");
			return "bound=" + m_Bound + " nav=" + nav + " mouse=" + (m_ActiveMouse != null ? m_ActiveMouse.layout : "none") + " virtual=" + IsVirtualMouseActive;
		}

		/// <summary>Mouse方式では操作中のMouse（実・仮想）の位置。Navigation方式では選択中のSelectableの中心（Follow Selection In Navigation）。</summary>
		public bool TryGetPosition(out Vector2 screenPosition)
		{
			screenPosition = m_Position;
			return IsInputActive && (Mode == UIInputMode.Mouse || m_HasNavigationPosition);
		}

		private void OnEnable()
		{
			m_NavigationMove.action?.Enable();
			Bind();
		}

		private void OnDisable()
		{
			Unbind();
			m_NavigationMove.action?.Disable();
		}

		private void Bind()
		{
			// 自身のGameObjectのEventSystemだけを使う。EventSystem.currentからは探さない。
			EventSystem events = GetComponent<EventSystem>();
			if (events == null)
			{
				Debug.LogError("UI input routing requires an EventSystem on the same GameObject.", this);
				enabled = false;
				return;
			}
			m_Events = events;
			m_OriginalNavigation = events.sendNavigationEvents;
			m_OriginalCursorVisible = Cursor.visible;
			m_OriginalGraphicEnabled = m_VirtualMouseGraphic != null && m_VirtualMouseGraphic.enabled;
			m_Bound = true;
			m_ActiveMouse = Mouse.current;
			m_Position = m_ActiveMouse != null ? m_ActiveMouse.position.ReadValue() : new Vector2(Screen.width, Screen.height) * 0.5f;
			Mode = UIInputMode.Mouse;
			events.sendNavigationEvents = false;
			ModeChanged?.Invoke(Mode);
		}

		private void Unbind()
		{
			// 元の値へ戻すのは接続できていた場合だけ。書き込んだ状態はこれとカーソル表示だけ。
			if (m_Bound && m_Events != null) { m_Events.sendNavigationEvents = m_OriginalNavigation; }
			RestoreVisibility();
			m_Bound = false;
			m_Events = null;
			m_ActiveMouse = null;
			m_WaitForNavigationRelease = false;
			m_HasNavigationPosition = false;
		}

		private void Update()
		{
			if (!m_Bound || m_Events == null || !m_Events.isActiveAndEnabled || !m_Focused) { return; }
			Mouse activeMouse = FindActiveMouse();
			if (activeMouse != null) { m_ActiveMouse = activeMouse; }

			// 同一フレームの競合は Mouse > 明示 Navigation。
			if (m_RequestedMode.HasValue)
			{
				UIInputMode requested = m_RequestedMode.Value;
				m_RequestedMode = null;
				ApplyMode(requested);
			}
			else if (activeMouse != null) { ApplyMode(UIInputMode.Mouse); }
			// Pointerを押している間（Drag中など）は方式を変えない。離してから方向入力でNavigationへ移る。
			else if (Active(m_NavigationMove) && !IsEditingText() && !IsPointerPressed) { ApplyMode(UIInputMode.Navigation); }

			// ModeChangedの購読側がRouterを無効化した場合は、そのフレームも停止する。
			if (!isActiveAndEnabled || !m_Bound || m_Events == null) { return; }

			// 仮想マウスの位置は自己申告を使う。Editorでは更新種別（Player / Editor）の切り替えでデバイスの読み出しが
			// 書き込みに追いつかず、Moduleへは届いているのにここだけ古い値になることがある（操作の有無と同じ理由）。
			if (Mode == UIInputMode.Mouse && m_ActiveMouse != null && m_ActiveMouse.added)
			{
				m_Position = IsVirtualMouseActive ? m_VirtualMouse.Position : m_ActiveMouse.position.ReadValue();
			}
			// Navigation中は選択が「Pointerの居場所」。追従表示は選択に付いて動き、仮想マウスへ戻る時もそこから始まる。
			Vector2 selected = default;
			m_HasNavigationPosition = Mode == UIInputMode.Navigation && m_FollowSelectionInNavigation && ScreenPoint.TryGetSelectedPoint(m_SelectionAnchor, m_SelectionInset, m_SelectionOffset, out selected);
			if (m_HasNavigationPosition) { m_Position = selected; }
			if (m_WaitForNavigationRelease && Read(m_NavigationMove).sqrMagnitude <= m_StickThreshold * m_StickThreshold) { m_WaitForNavigationRelease = false; }
			// 方向入力をカーソル移動にも使う構成があるため、Navigation以外ではModuleのMove / Submit / Cancelを下ろす。
			m_Events.sendNavigationEvents = m_OriginalNavigation && Mode == UIInputMode.Navigation && !m_WaitForNavigationRelease;
			// 仮想マウスの決定は、仮想マウスで操作している間だけ左クリックにする。Navigation中はModuleのSubmit、実マウス中は何もしない。
			if (m_VirtualMouse != null) { m_VirtualMouse.ClickEnabled = Mode == UIInputMode.Mouse && IsVirtualMouseActive; }
			UpdateCursorVisibility();
		}

		public void SetMode(UIInputMode mode)
		{
			if (mode < UIInputMode.Mouse || mode > UIInputMode.Navigation) { throw new ArgumentOutOfRangeException(nameof(mode)); }
			// Button のイベント内から同じフレームの処理へ割り込まない。
			m_RequestedMode = mode;
		}

		private void ApplyMode(UIInputMode mode)
		{
			if (m_Events == null || Mode == mode || m_Changing) { return; }
			m_Changing = true;
			try
			{
				// 成立を確認してから状態を変える。拒否された場合は何も変えない。
				bool consumeInitialInput = false;
				if (mode == UIInputMode.Navigation && m_Selection != null && m_Selection.isActiveAndEnabled
					&& !m_Selection.TryBeginNavigation(m_Position, Direction(Read(m_NavigationMove)), out consumeInitialInput))
				{
					return;
				}
				UIInputMode previous = Mode;
				Mode = mode;
				m_WaitForNavigationRelease = consumeInitialInput;
				m_Events.sendNavigationEvents = m_OriginalNavigation && mode == UIInputMode.Navigation && !m_WaitForNavigationRelease;
				if (mode == UIInputMode.Navigation && m_ParkPointerOnNavigation) { ParkPointers(); }
				else if (mode == UIInputMode.Mouse && previous == UIInputMode.Navigation && IsVirtualMouseActive && m_VirtualMouse.StartFromSelection) { m_VirtualMouse.TryMoveToSelection(m_SelectionAnchor, m_SelectionInset, m_SelectionOffset); }
				ModeChanged?.Invoke(mode);
			}
			finally { m_Changing = false; }
		}

		/// <summary>
		/// Pointerが最後に居た場所のHoverはNavigation中も残る（Moduleは毎フレーム同じ位置でRaycastする）。
		/// デバイスの位置だけを画面外へ書き換えて解く。OSカーソルは動かさない。実マウスは次の移動イベントで実位置へ戻り、
		/// 仮想マウスは内部の位置を保ったまま次のスティック入力で戻る。
		/// </summary>
		private void ParkPointers()
		{
			foreach (InputDevice device in UnityEngine.InputSystem.InputSystem.devices)
			{
				if (!(device is Mouse mouse) || !mouse.enabled || mouse is UIControlVirtualMouse) { continue; }
				InputState.Change(mouse.position, ParkedPosition);
			}
			if (m_VirtualMouse != null) { m_VirtualMouse.Park(); }
		}

		private void UpdateCursorVisibility()
		{
			bool virtualActive = Mode == UIInputMode.Mouse && IsVirtualMouseActive;
			if (m_ManageOsCursor) { Cursor.visible = Mode == UIInputMode.Mouse && !virtualActive; m_ChangedVisibility = true; }
			if (m_VirtualMouseGraphic != null) { m_VirtualMouseGraphic.enabled = virtualActive; m_ChangedVisibility = true; }
		}

		private void RestoreVisibility()
		{
			if (!m_ChangedVisibility) { return; }
			m_ChangedVisibility = false;
			if (m_ManageOsCursor) { Cursor.visible = m_OriginalCursorVisible; }
			if (m_VirtualMouseGraphic != null) { m_VirtualMouseGraphic.enabled = m_OriginalGraphicEnabled; }
		}

		private void OnApplicationFocus(bool focused) { m_Focused = focused; }

		private bool IsEditingText() { return m_Selection != null && m_Selection.isActiveAndEnabled && m_Selection.IsEditingText; }

		/// <summary>有意な操作があったMouse。実マウスも仮想Mouseデバイスも同じ規則で扱う。</summary>
		private Mouse FindActiveMouse()
		{
			Mouse active = null;
			foreach (InputDevice device in UnityEngine.InputSystem.InputSystem.devices)
			{
				if (!(device is Mouse mouse) || !mouse.enabled) { continue; }
				// 仮想マウスは移動だけを操作とみなす（下の自己申告で拾う）。押下はNavigation中の決定と同じボタンなので方式を変えない。
				if (mouse is UIControlVirtualMouse) { continue; }
				Vector2 delta = mouse.delta.ReadValue();
				if (delta != Vector2.zero && delta.sqrMagnitude >= m_MouseMoveThreshold * m_MouseMoveThreshold
					|| mouse.scroll.ReadValue() != Vector2.zero
					|| mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame || mouse.middleButton.wasPressedThisFrame)
				{
					if (active == null || mouse.lastUpdateTime > active.lastUpdateTime) { active = mouse; }
				}
			}
			// 仮想マウスは自分の操作を知っている。状態バッファの読み出しに依存せず、操作したフレームは操作中として扱う。
			if (m_VirtualMouse != null && m_VirtualMouse.IsActiveThisFrame && m_VirtualMouse.Device != null && m_VirtualMouse.Device.added
				&& (active == null || m_VirtualMouse.Device.lastUpdateTime >= active.lastUpdateTime)) { active = m_VirtualMouse.Device; }
			return active;
		}

		private MoveDirection Direction(Vector2 value)
		{
			if (value.sqrMagnitude <= m_StickThreshold * m_StickThreshold) { return MoveDirection.None; }
			if (Mathf.Abs(value.x) > Mathf.Abs(value.y)) { return value.x > 0 ? MoveDirection.Right : MoveDirection.Left; }
			return value.y > 0 ? MoveDirection.Up : MoveDirection.Down;
		}

		private bool Active(InputActionProperty property) { return Read(property).sqrMagnitude > m_StickThreshold * m_StickThreshold; }
		private static Vector2 Read(InputActionProperty property) { return property.action != null && property.action.enabled ? property.action.ReadValue<Vector2>() : Vector2.zero; }
	}
}
