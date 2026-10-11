using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace UIControl.InputSystem
{
	/// <summary>
	/// 仮想Mouseデバイス方式の仮想カーソル（試作）。有効化時に専用Layoutのデバイスを1つ追加し、無効化時に除去する。
	/// スティックで位置を進め、決定ボタンを左ボタンとして書き込む。UI Input Moduleは通常の &lt;Mouse&gt; 束縛で
	/// このデバイスを読むため、Hover・Click・Drag・Scrollが標準Pointer経路で成立する。
	/// 描画は行わない。カーソル画像は <see cref="PointerFollower"/> のSourceにこのComponentを指定して追従させる。
	/// </summary>
	[DisallowMultipleComponent]
	public sealed class VirtualMouseCursor : MonoBehaviour, IPointerPositionSource
	{
		[SerializeField, Tooltip("カーソルを動かす方向入力（Vector2）。")]
		private InputActionProperty m_Move;
		[SerializeField, Tooltip("左ボタンとして書き込む決定入力（Button）。")]
		private InputActionProperty m_Click;
		[SerializeField, Min(0), Tooltip("画面ピクセル／秒。")]
		private float m_Speed = 900;
		[SerializeField, Tooltip("Navigationから仮想マウスへ移る時、現在選択中のSelectableの中心から始める。Routerが方式の切り替え時に適用する。")]
		private bool m_StartFromSelection = true;
		/// <summary>Hoverを解くために置く画面外の位置。</summary>
		private static readonly Vector2 ParkedPosition = new Vector2(-10000, -10000);
		private bool m_Parked;
		private UIControlVirtualMouse m_Device;
		private Action m_AfterUpdate;
		private Action<InputAction.CallbackContext> m_OnClick;
		private double m_LastTime;
		private Vector2 m_Position;
		private bool m_Moved;
		private int m_LastActiveFrame = -1;

		/// <summary>追加したデバイス。無効中はnull。</summary>
		public UIControlVirtualMouse Device => m_Device;
		public Vector2 Position => m_Position;
		/// <summary>有効化後にスティック入力で動いたことがある。</summary>
		public bool HasMoved => m_Moved;
		/// <summary>このフレームにスティックで動いた。デバイスの状態を読まずに操作中かを知るための値。押下は含めない（Navigation中の決定と同じボタンのため）。</summary>
		public bool IsActiveThisFrame => m_LastActiveFrame == Time.frameCount;
		private bool m_ClickEnabled = true;
		/// <summary>
		/// 新しい押下を左ボタンとして書くか。Navigation中は決定がModuleのSubmitになるので、Routerが偽にして二重に届くのを防ぐ。
		/// 押下中に偽になっても離すまでは押したまま（Drag中の方向入力で落ちない）。離す操作は常に書く。
		/// </summary>
		public bool ClickEnabled { get => m_ClickEnabled; set => m_ClickEnabled = value; }
		/// <summary>Navigationから戻る時に、選択中のSelectableの中心から始めるか。</summary>
		public bool StartFromSelection { get => m_StartFromSelection; set => m_StartFromSelection = value; }

		public bool TryGetPosition(out Vector2 screenPosition)
		{
			screenPosition = m_Position;
			return isActiveAndEnabled && m_Device != null && m_Device.added;
		}

		/// <summary>位置を直接置く。選択位置からの再開や、テストからの配置に使う。</summary>
		public void SetPosition(Vector2 screenPosition)
		{
			m_Position = Clamp(screenPosition);
			m_Parked = false;
			if (m_Device == null || !m_Device.added) { return; }
			InputState.Change(m_Device.position, m_Position);
			InputState.Change(m_Device.delta, Vector2.zero);
		}

		/// <summary>
		/// 現在選択中のSelectableの中心へ置く。選択が無い、RectTransformでない、非表示なら何もしないでfalse。
		/// Routerが「Navigation → 仮想マウス」の切り替え時に <see cref="StartFromSelection"/> に従って呼ぶ。手動でも呼べる。
		/// </summary>
		public bool TryMoveToSelection() { return TryMoveToSelection(SelectionAnchor.Center, 0, Vector2.zero); }

		/// <summary>現在選択中のSelectable内の指定位置（中心・四隅、四隅は inset だけ内側、+ 画面ピクセルのずらし）へ置く。PointerAnchorがあればそちら。</summary>
		public bool TryMoveToSelection(SelectionAnchor anchor, float inset, Vector2 offset)
		{
			if (!ScreenPoint.TryGetSelectedPoint(anchor, inset, offset, out Vector2 point)) { return false; }
			SetPosition(point);
			return true;
		}

		/// <summary>
		/// デバイス上の位置だけを画面外へ置き、置き去りのHoverを解く。内部の位置は保つので、次のスティック入力は元の位置から続く。
		/// Navigationへ移る時にRouterが呼ぶ。
		/// </summary>
		public void Park()
		{
			if (m_Parked || m_Device == null || !m_Device.added) { return; }
			m_Parked = true;
			InputState.Change(m_Device.position, ParkedPosition);
			InputState.Change(m_Device.delta, Vector2.zero);
		}

		private void OnEnable()
		{
			UIControlVirtualMouse.EnsureRegistered();
			m_Device = UnityEngine.InputSystem.InputSystem.AddDevice<UIControlVirtualMouse>();
			m_Position = Clamp(m_Position == Vector2.zero ? new Vector2(Screen.width, Screen.height) * 0.5f : m_Position);
			InputState.Change(m_Device.position, m_Position);
			m_LastTime = InputState.currentTime;
			m_Moved = false;
			m_Parked = false;
			m_AfterUpdate ??= Tick;
			m_OnClick ??= OnClick;
			UnityEngine.InputSystem.InputSystem.onAfterUpdate += m_AfterUpdate;
			if (m_Click.action != null) { m_Click.action.performed += m_OnClick; m_Click.action.canceled += m_OnClick; }
			m_Move.action?.Enable();
			m_Click.action?.Enable();
		}

		private void OnDisable()
		{
			UnityEngine.InputSystem.InputSystem.onAfterUpdate -= m_AfterUpdate;
			if (m_Click.action != null) { m_Click.action.performed -= m_OnClick; m_Click.action.canceled -= m_OnClick; }
			m_Move.action?.Disable();
			m_Click.action?.Disable();
			if (m_Device != null && m_Device.added) { UnityEngine.InputSystem.InputSystem.RemoveDevice(m_Device); }
			m_Device = null;
		}

		/// <summary>入力更新の直後に位置を進める。同じ更新内で書くと、Moduleは次の処理で新しい位置を読む。</summary>
		private void Tick()
		{
			if (m_Device == null || !m_Device.added) { return; }
			double now = InputState.currentTime;
			float deltaTime = Mathf.Clamp((float)(now - m_LastTime), 0, 0.1f);
			m_LastTime = now;
			Vector2 stick = m_Move.action != null && m_Move.action.enabled ? m_Move.action.ReadValue<Vector2>() : Vector2.zero;
			if (stick == Vector2.zero) { return; }
			Vector2 delta = stick * (m_Speed * deltaTime);
			if (delta == Vector2.zero) { return; }
			// 画面端で位置が変わらなくても、実マウスと同じくdeltaは報告する。押し続けている間は操作中として扱われる。
			m_Moved = true;
			m_LastActiveFrame = Time.frameCount;
			Vector2 next = Clamp(m_Position + delta);
			if (next != m_Position || m_Parked) { m_Position = next; m_Parked = false; InputState.Change(m_Device.position, m_Position); }
			InputState.Change(m_Device.delta, delta);
		}

		private void OnClick(InputAction.CallbackContext context)
		{
			// Button型のActionはperformedが押下、canceledが解放。デバイス除去時のcanceledではcontrolの値を読めないので、phaseで判断する。
			bool pressed = context.phase == InputActionPhase.Performed;
			if (pressed && !m_ClickEnabled) { return; }
			WriteButton(pressed);
		}

		private void WriteButton(bool pressed)
		{
			if (m_Device == null || !m_Device.added) { return; }
			// ボタンはビット制御なので、全状態を写して書き戻す（Unity の VirtualMouseInput と同じ手順）。
			m_Device.CopyState(out MouseState state);
			if (state.buttons == 0 && !pressed) { return; }
			state.WithButton(MouseButton.Left, pressed);
			InputState.Change(m_Device, state);
		}

		private static Vector2 Clamp(Vector2 position)
		{
			return new Vector2(Mathf.Clamp(position.x, 0, Screen.width), Mathf.Clamp(position.y, 0, Screen.height));
		}
	}
}
