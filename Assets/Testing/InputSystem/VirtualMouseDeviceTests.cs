using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.UI;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace UIControl.InputSystem.Tests
{
	/// <summary>
	/// 仮想Mouseデバイス方式（改修計画E1）の試作を実測する。
	/// 確認すること: デバイスが1つだけ増え、Mouse.current / Pointer.currentを奪わず、無効化で消えること。
	/// Input Moduleの既定Action（&lt;Mouse&gt;束縛）を書き換えずに、Hover・Click・Dragが標準Pointer経路で届くこと。
	/// 現行Routerが仮想マウスを実マウスと同一視すること（併存時の設計判断の材料）。
	/// </summary>
	public sealed class VirtualMouseDeviceTests
	{
		private GameObject m_World;
		private EventSystem m_Events;
		private VirtualMouseCursor m_Cursor;
		private Button m_Button;
		private HoverCounter m_Hover;
		private int m_Clicks;
		private Gamepad m_Pad;
		private InputAction m_Move;
		private InputAction m_Click;

		private sealed class HoverCounter : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
		{
			public int Enter;
			public int Exit;
			public void OnPointerEnter(PointerEventData eventData) { Enter++; }
			public void OnPointerExit(PointerEventData eventData) { Exit++; }
		}

		private static void SetField(object target, string name, object value)
		{
			FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
			Assert.That(field, Is.Not.Null, name + " が見つからない");
			field.SetValue(target, value);
		}

		private static int CountVirtualMice()
		{
			int count = 0;
			foreach (InputDevice device in UnityEngine.InputSystem.InputSystem.devices) { if (device is UIControlVirtualMouse) { count++; } }
			return count;
		}

		private InputSettings.EditorInputBehaviorInPlayMode m_EditorBehavior;
		private InputSettings.BackgroundBehavior m_BackgroundBehavior;

		[SetUp]
		public void SetUp()
		{
			// バッチ・非フォーカスのEditorではPointer / Keyboardの入力がGame Viewへ届かない既定なので、テスト中だけ全入力を通す。
			InputSettings settings = UnityEngine.InputSystem.InputSystem.settings;
			m_EditorBehavior = settings.editorInputBehaviorInPlayMode;
			m_BackgroundBehavior = settings.backgroundBehavior;
			settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
			settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
			m_Pad = UnityEngine.InputSystem.InputSystem.AddDevice<Gamepad>();
			m_Move = new InputAction("CursorMove", InputActionType.Value, "<Gamepad>/leftStick");
			m_Click = new InputAction("Decide", InputActionType.Button, "<Gamepad>/buttonSouth");

			m_World = new GameObject("World");
			m_World.SetActive(false);

			GameObject canvasObject = new GameObject("Canvas", typeof(RectTransform));
			canvasObject.transform.SetParent(m_World.transform, false);
			Canvas canvas = canvasObject.AddComponent<Canvas>();
			canvas.renderMode = RenderMode.ScreenSpaceOverlay;
			canvasObject.AddComponent<GraphicRaycaster>();

			GameObject buttonObject = new GameObject("Button", typeof(RectTransform));
			buttonObject.transform.SetParent(canvasObject.transform, false);
			((RectTransform)buttonObject.transform).sizeDelta = new Vector2(120, 120);
			Image image = buttonObject.AddComponent<Image>();
			m_Button = buttonObject.AddComponent<Button>();
			m_Button.targetGraphic = image;
			m_Clicks = 0;
			m_Button.onClick.AddListener(() => m_Clicks++);
			m_Hover = buttonObject.AddComponent<HoverCounter>();

			GameObject eventsObject = new GameObject("EventSystem");
			eventsObject.transform.SetParent(m_World.transform, false);
			m_Events = eventsObject.AddComponent<EventSystem>();
			// 既定のUI Action（Point = <Mouse>/position、Click = <Mouse>/leftButton 等）。書き換えない。
			InputSystemUIInputModule module = eventsObject.AddComponent<InputSystemUIInputModule>();
			module.AssignDefaultActions();
			m_Cursor = eventsObject.AddComponent<VirtualMouseCursor>();
			SetField(m_Cursor, "m_Move", new InputActionProperty(m_Move));
			SetField(m_Cursor, "m_Click", new InputActionProperty(m_Click));

			m_World.SetActive(true);
		}

		[TearDown]
		public void TearDown()
		{
			if (m_World != null) { Object.DestroyImmediate(m_World); }
			m_Move?.Dispose();
			m_Click?.Dispose();
			if (m_Pad != null && m_Pad.added) { UnityEngine.InputSystem.InputSystem.RemoveDevice(m_Pad); }
			InputSettings settings = UnityEngine.InputSystem.InputSystem.settings;
			settings.editorInputBehaviorInPlayMode = m_EditorBehavior;
			settings.backgroundBehavior = m_BackgroundBehavior;
			Assert.That(CountVirtualMice(), Is.EqualTo(0), "テスト後に仮想マウスが残っている");
		}

		private void Send(Vector2 leftStick = default, bool south = false)
		{
			GamepadState state = new GamepadState { leftStick = leftStick };
			if (south) { state = state.WithButton(GamepadButton.South); }
			UnityEngine.InputSystem.InputSystem.QueueStateEvent(m_Pad, state);
			UnityEngine.InputSystem.InputSystem.Update();
		}

		private static Vector2 ScreenCenterOf(Component component)
		{
			RectTransform rect = (RectTransform)component.transform;
			return RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
		}

		/// <summary>カーソルを対象の中央へ置き、Hoverが届くまで待つ。生成直後はレイアウト未確定で座標がずれる。</summary>
		private IEnumerator PlaceOver(Component target)
		{
			for (int i = 0; i < 120; i++)
			{
				m_Cursor.SetPosition(ScreenCenterOf(target));
				UnityEngine.InputSystem.InputSystem.Update();
				yield return null;
				if (m_Hover.Enter > 0) { yield break; }
			}
			Assert.Fail("仮想マウスのHoverがButtonへ届かない。device.enabled=" + m_Cursor.Device.enabled
				+ " devicePos=" + m_Cursor.Device.position.ReadValue() + " target=" + ScreenCenterOf(target)
				+ " screen=" + Screen.width + "x" + Screen.height + " pointerOver=" + m_Events.IsPointerOverGameObject());
		}

		[UnityTest]
		public IEnumerator AddsOneDeviceWithoutTakingCurrent()
		{
			yield return null;
			Assert.That(CountVirtualMice(), Is.EqualTo(1), "追加されるデバイスは1つ");
			Assert.That(m_Cursor.Device, Is.Not.Null.And.Property("added").True);
			Assert.That(Mouse.current, Is.Not.SameAs(m_Cursor.Device), "Mouse.currentを奪わない");
			Assert.That(UnityEngine.InputSystem.Pointer.current, Is.Not.SameAs(m_Cursor.Device), "Pointer.currentを奪わない");
			m_Cursor.enabled = false;
			yield return null;
			Assert.That(CountVirtualMice(), Is.EqualTo(0), "無効化で除去する");
			m_Cursor.enabled = true;
			yield return null;
			Assert.That(CountVirtualMice(), Is.EqualTo(1), "再有効化で1つだけ戻る");
		}

		[UnityTest]
		public IEnumerator HoverAndClickReachButtonThroughDefaultModuleActions()
		{
			yield return PlaceOver(m_Button);
			Assert.That(m_Hover.Enter, Is.GreaterThan(0), "Hoverは標準経路で届く");
			Send(south: true);
			yield return null;
			Send(south: false);
			yield return null;
			yield return null;
			Assert.That(m_Clicks, Is.EqualTo(1), "決定が左クリックとして届く");
			m_Cursor.SetPosition(Vector2.zero);
			UnityEngine.InputSystem.InputSystem.Update();
			yield return null;
			yield return null;
			Assert.That(m_Hover.Exit, Is.GreaterThan(0), "離れるとExitが届く");
		}

		[UnityTest]
		public IEnumerator StickMovesThePosition()
		{
			yield return null;
			Vector2 start = m_Cursor.Position;
			for (int i = 0; i < 10; i++) { Send(leftStick: new Vector2(1, 0)); yield return null; }
			Send();
			Assert.That(m_Cursor.Position.x, Is.GreaterThan(start.x), "右へ進む");
			Assert.That(m_Cursor.Position.y, Is.EqualTo(start.y).Within(0.01f));
			Assert.That(m_Cursor.HasMoved, Is.True);
			Assert.That(m_Cursor.Device.position.ReadValue(), Is.EqualTo(m_Cursor.Position), "デバイスの位置と一致する");
		}

		[UnityTest]
		public IEnumerator DragWorksThroughStandardPointerPath()
		{
			UIDraggable draggable = m_Button.gameObject.AddComponent<UIDraggable>();
			int dragEvents = 0;
			draggable.Drag.AddListener(_ => dragEvents++);
			yield return PlaceOver(m_Button);
			Vector2 origin = m_Cursor.Position;
			Vector3 before = m_Button.transform.localPosition;
			Send(south: true);
			yield return null;
			for (int i = 1; i <= 5; i++)
			{
				m_Cursor.SetPosition(origin + new Vector2(20 * i, 0));
				Send(south: true);
				yield return null;
			}
			Assert.That(draggable.IsDragging, Is.True, "閾値を超えた移動でBeginDragが届く");
			Assert.That(dragEvents, Is.GreaterThan(0));
			Assert.That(m_Button.transform.localPosition.x, Is.Not.EqualTo(before.x).Within(0.5f), "Drag中は対象が動く");
			Send(south: false);
			yield return null;
			yield return null;
			Assert.That(draggable.IsDragging, Is.False, "離すとEndDrag");
			Assert.That(m_Button.transform.localPosition.x, Is.EqualTo(before.x).Within(0.5f), "未受理なので元位置へ戻る");
		}

		/// <summary>
		/// PlayerInputの制御スキーム自動切替は「未ペアのデバイスからのイベント」で起きる。
		/// 仮想マウスはInputState.Changeで状態を書き、イベントを発生させないので、Gamepadスキームのまま変わらないことを確認する。
		/// </summary>
		[UnityTest]
		public IEnumerator PlayerInputKeepsGamepadSchemeWhileVirtualMouseMoves()
		{
			InputActionAsset asset = ScriptableObject.CreateInstance<InputActionAsset>();
			InputActionMap map = asset.AddActionMap("Player");
			InputAction fire = map.AddAction("Fire", InputActionType.Button);
			fire.AddBinding("<Gamepad>/buttonSouth").WithGroup("Gamepad");
			InputAction look = map.AddAction("Look", InputActionType.Value);
			look.AddBinding("<Mouse>/delta").WithGroup("KeyboardMouse");
			InputAction point = map.AddAction("Point", InputActionType.Value);
			point.AddBinding("<Mouse>/position").WithGroup("KeyboardMouse");
			asset.AddControlScheme("Gamepad").WithRequiredDevice("<Gamepad>");
			asset.AddControlScheme("KeyboardMouse").WithRequiredDevice("<Mouse>");
			GameObject playerObject = new GameObject("Player");
			playerObject.transform.SetParent(m_World.transform, false);
			playerObject.SetActive(false);
			PlayerInput player = playerObject.AddComponent<PlayerInput>();
			player.actions = asset;
			player.defaultControlScheme = "Gamepad";
			player.neverAutoSwitchControlSchemes = false;
			playerObject.SetActive(true);
			try
			{
				yield return null;
				Assert.That(player.currentControlScheme, Is.EqualTo("Gamepad"));
				SetField(m_Cursor, "m_Speed", 90000f);
				for (int i = 0; i < 10; i++) { Send(leftStick: new Vector2(1, 0)); yield return null; }
				Send(south: true); yield return null; Send(south: false); yield return null;
				Assert.That(m_Cursor.HasMoved, Is.True);
				Assert.That(player.currentControlScheme, Is.EqualTo("Gamepad"), "仮想マウスの移動とクリックでスキームが切り替わらない");
			}
			finally
			{
				Object.DestroyImmediate(playerObject);
				Object.DestroyImmediate(asset);
			}
		}

		/// <summary>Navigationへ移ると、仮想マウスが乗っていたButtonのHoverが解け、戻る時は選択中のButtonの中心から始まる。</summary>
		[UnityTest]
		public IEnumerator ParksOnNavigationAndStartsFromTheSelection()
		{
			m_World.SetActive(false);
			InputSystemUIInputRouter router = m_Events.gameObject.AddComponent<InputSystemUIInputRouter>();
			InputAction navigation = new InputAction("NavigationMove", InputActionType.Value, "<Gamepad>/dpad");
			SetField(router, "m_NavigationMove", new InputActionProperty(navigation));
			SetField(router, "m_VirtualMouse", m_Cursor);
			m_World.SetActive(true);
			try
			{
				yield return null;
				// 仮想マウスをButtonへ乗せ、Mouse方式で操作中にする。
				SetField(m_Cursor, "m_Speed", 60000f);
				for (int i = 0; i < 30 && !router.IsVirtualMouseActive; i++)
				{
					m_Cursor.SetPosition(new Vector2(20, Screen.height * 0.5f));
					Send(leftStick: new Vector2(1, 0));
					yield return null;
				}
				Assert.That(router.IsVirtualMouseActive, Is.True);
				// スティックを戻す。倒したままだと仮想マウスが操作中のままで、Navigationへ移ってもすぐMouseへ戻る。
				Send();
				yield return null;
				yield return PlaceOver(m_Button);
				int exits = m_Hover.Exit;

				// Navigationへ移るとHoverが解ける。カーソルの内部位置は保たれる。
				Vector2 kept = m_Cursor.Position;
				router.SetMode(UIInputMode.Navigation);
				for (int i = 0; i < 30 && m_Hover.Exit == exits; i++) { yield return null; }
				Assert.That(router.Mode, Is.EqualTo(UIInputMode.Navigation));
				Assert.That(m_Hover.Exit, Is.GreaterThan(exits), "Navigationへ移っても仮想マウスのHoverが残る");
				Assert.That(m_Cursor.Position, Is.EqualTo(kept), "退避で内部の位置は変わらない");

				// Buttonを選択してから仮想マウスへ戻ると、Buttonの中心から始まる。
				m_Events.SetSelectedGameObject(m_Button.gameObject);
				m_Cursor.SetPosition(new Vector2(20, Screen.height * 0.5f));
				router.SetMode(UIInputMode.Navigation);
				yield return null;
				for (int i = 0; i < 30 && router.Mode != UIInputMode.Mouse; i++) { Send(leftStick: new Vector2(1, 0)); yield return null; }
				Send();
				yield return null;
				Assert.That(router.Mode, Is.EqualTo(UIInputMode.Mouse));
				Assert.That(router.IsVirtualMouseActive, Is.True);
				Vector2 center = ScreenCenterOf(m_Button);
				Assert.That(Vector2.Distance(m_Cursor.Position, center), Is.LessThan(m_Cursor.Device.delta.ReadValue().magnitude + 5f),
					"選択中のButtonの中心から始まらない: cursor=" + m_Cursor.Position + " center=" + center);
			}
			finally { navigation.Dispose(); }
		}

		/// <summary>Selection Anchorを右下にすると、仮想マウスは選択中のButtonの右下から Inset だけ内側 + Offset から始まり、その点はButtonに当たる。PointerAnchorがあればその位置。</summary>
		[UnityTest]
		public IEnumerator StartsFromTheConfiguredCornerOfTheSelection()
		{
			m_World.SetActive(false);
			InputSystemUIInputRouter router = m_Events.gameObject.AddComponent<InputSystemUIInputRouter>();
			InputAction navigation = new InputAction("NavigationMove", InputActionType.Value, "<Gamepad>/dpad");
			SetField(router, "m_NavigationMove", new InputActionProperty(navigation));
			SetField(router, "m_VirtualMouse", m_Cursor);
			SetField(router, "m_SelectionAnchor", SelectionAnchor.BottomRight);
			SetField(router, "m_SelectionInset", 6f);
			SetField(router, "m_SelectionOffset", new Vector2(-4, 4));
			m_World.SetActive(true);
			try
			{
				yield return null;
				router.SetMode(UIInputMode.Navigation);
				m_Events.SetSelectedGameObject(m_Button.gameObject);
				yield return null;
				yield return null;
				// Navigation中の報告位置も同じ点。
				RectTransform rect = (RectTransform)m_Button.transform;
				Vector2 corner = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(new Vector3(rect.rect.xMax, rect.rect.yMin, 0)));
				Vector2 expected = corner + new Vector2(-6, 6) + new Vector2(-4, 4);
				Assert.That(router.TryGetPosition(out Vector2 reported), Is.True, "Navigation中に位置を報告しない");
				Assert.That(Vector2.Distance(reported, expected), Is.LessThan(1.5f), "Navigation中の位置が右下でない: " + reported + " expected=" + expected);
				Assert.That(RectTransformUtility.RectangleContainsScreenPoint(rect, reported, null), Is.True, "右下の点がButtonの内側にない");

				SetField(m_Cursor, "m_Speed", 1f);
				for (int i = 0; i < 30 && router.Mode != UIInputMode.Mouse; i++) { Send(leftStick: new Vector2(1, 0)); yield return null; }
				Send();
				yield return null;
				Assert.That(router.IsVirtualMouseActive, Is.True);
				Assert.That(Vector2.Distance(m_Cursor.Position, expected), Is.LessThan(2f), "仮想マウスが右下から始まらない: " + m_Cursor.Position + " expected=" + expected);

				// PointerAnchorを置くと、AnchorとInsetの代わりにそのRectTransformの中心（+ Offset）になる。
				GameObject markerObject = new GameObject("Marker", typeof(RectTransform), typeof(PointerAnchor));
				markerObject.transform.SetParent(m_Button.transform, false);
				RectTransform marker = (RectTransform)markerObject.transform;
				marker.sizeDelta = new Vector2(10, 10);
				marker.anchoredPosition = new Vector2(-30, 20);
				router.SetMode(UIInputMode.Navigation);
				yield return null;
				yield return null;
				Vector2 markerCenter = RectTransformUtility.WorldToScreenPoint(null, marker.TransformPoint(marker.rect.center)) + new Vector2(-4, 4);
				Assert.That(router.TryGetPosition(out Vector2 anchored), Is.True);
				Assert.That(Vector2.Distance(anchored, markerCenter), Is.LessThan(1.5f), "PointerAnchorの位置にならない: " + anchored + " expected=" + markerCenter);
			}
			finally { navigation.Dispose(); }
		}

		/// <summary>Routerは仮想マウスの移動もMouse方式として扱い、IsVirtualMouseActiveで実マウスと区別できる。</summary>
		[UnityTest]
		public IEnumerator RouterTreatsTheVirtualMouseAsAMouse()
		{
			m_World.SetActive(false);
			InputSystemUIInputRouter router = m_Events.gameObject.AddComponent<InputSystemUIInputRouter>();
			InputAction navigation = new InputAction("NavigationMove", InputActionType.Value, "<Gamepad>/dpad");
			SetField(router, "m_NavigationMove", new InputActionProperty(navigation));
			SetField(router, "m_VirtualMouse", m_Cursor);
			m_World.SetActive(true);
			try
			{
				yield return null;
				router.SetMode(UIInputMode.Navigation);
				yield return null;
				Assert.That(router.Mode, Is.EqualTo(UIInputMode.Navigation));
				// バッチ実行はフレームが極端に速く、1更新あたりの移動がRouterのMouse Move Threshold（2px）を下回るので速度を上げる。
				// 画面端でもdeltaは報告されるので、位置は毎回左寄りへ戻すだけでよい。
				SetField(m_Cursor, "m_Speed", 60000f);
				for (int i = 0; i < 30 && router.Mode != UIInputMode.Mouse; i++)
				{
					m_Cursor.SetPosition(new Vector2(20, Screen.height * 0.5f));
					Send(leftStick: new Vector2(1, 0));
					yield return null;
				}
				Assert.That(router.IsVirtualMouseActive, Is.True, "操作中のMouseが仮想マウスだと分かる");
				Assert.That(router.Mode, Is.EqualTo(UIInputMode.Mouse), "仮想マウスの移動を実マウスの操作と判定する。 device.enabled=" + m_Cursor.Device.enabled
					+ " delta=" + m_Cursor.Device.delta.ReadValue() + " position=" + m_Cursor.Device.position.ReadValue() + " cursor=" + m_Cursor.Position
					+ " updateMode=" + UnityEngine.InputSystem.InputSystem.settings.updateMode + " Mouse.current=" + (Mouse.current != null ? Mouse.current.layout : "null")
					+ " router=" + router.DescribeInputs());
			}
			finally { navigation.Dispose(); }
		}
	}
}
