using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace UIControl.InputSystem.Tests
{
	/// <summary>
	/// Routerの方式判定を検証する。Routerが触る外部は自身のAction、EventSystem.sendNavigationEvents、
	/// カーソル表示だけであり、Input Moduleへデバイスもポインタも足さないことを前提にしている。
	/// 仮想マウスの検出は VirtualMouseDeviceTests で扱う。
	/// </summary>
	public sealed class InputRouterTests
	{
		private GameObject m_Host;
		private GameObject m_World;
		private EventSystem m_Events;
		private InputSystemUIInputRouter m_Router;
		private Gamepad m_Pad;
		private InputAction m_NavigationMove;

		[SetUp]
		public void SetUp()
		{
			m_Pad = UnityEngine.InputSystem.InputSystem.AddDevice<Gamepad>();
			m_NavigationMove = new InputAction("NavigationMove", InputActionType.Value, "<Gamepad>/dpad");
			UIFocus.NotifyInputMode(FocusInputMode.Navigation);
		}

		[TearDown]
		public void TearDown()
		{
			if (m_Host != null) { Object.DestroyImmediate(m_Host); }
			if (m_World != null) { Object.DestroyImmediate(m_World); }
			m_NavigationMove?.Dispose();
			if (m_Pad != null && m_Pad.added) { UnityEngine.InputSystem.InputSystem.RemoveDevice(m_Pad); }
		}

		/// <summary>直列化フィールドへテスト用のActionを差す。Routerは公開setterを持たない。</summary>
		private static void SetField(object target, string name, object value)
		{
			FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
			Assert.That(field, Is.Not.Null, name + " が見つからない");
			field.SetValue(target, value);
		}

		/// <summary>非activeで組み立ててからactivateする。RouterのOnEnableがActionを有効化するため。</summary>
		private void Build(bool withSelection = false, bool selectNearestOnNavigation = false)
		{
			m_Host = new GameObject("Router Host");
			m_Host.SetActive(false);
			m_Events = m_Host.AddComponent<EventSystem>();
			m_Events.sendNavigationEvents = true;
			m_Router = m_Host.AddComponent<InputSystemUIInputRouter>();
			if (withSelection)
			{
				PointerSelection selection = m_Host.AddComponent<PointerSelection>();
				SetField(selection, "m_Source", m_Router);
				SetField(selection, "m_SelectNearestOnNavigation", selectNearestOnNavigation);
				SetField(m_Router, "m_Selection", selection);
			}
			SetField(m_Router, "m_NavigationMove", new InputActionProperty(m_NavigationMove));
			m_Host.SetActive(true);
		}

		/// <summary>有効化済みのScopeとButtonを1つ用意する。TrySelectNearestの候補になる。</summary>
		private Button BuildScopeWithButton()
		{
			m_World = new GameObject("World");
			m_World.SetActive(false);
			GameObject canvasObject = new GameObject("Canvas", typeof(RectTransform));
			canvasObject.transform.SetParent(m_World.transform, false);
			Canvas canvas = canvasObject.AddComponent<Canvas>();
			canvas.renderMode = RenderMode.ScreenSpaceOverlay;
			GameObject scopeObject = new GameObject("Scope", typeof(RectTransform));
			scopeObject.transform.SetParent(canvasObject.transform, false);
			FocusScope scope = scopeObject.AddComponent<FocusScope>();
			GameObject buttonObject = new GameObject("Button", typeof(RectTransform));
			buttonObject.transform.SetParent(scopeObject.transform, false);
			((RectTransform)buttonObject.transform).sizeDelta = new Vector2(80, 40);
			buttonObject.AddComponent<Image>();
			Button button = buttonObject.AddComponent<Button>();
			m_World.SetActive(true);
			scope.Activate();
			return button;
		}

		private void Send(Vector2 dpad = default)
		{
			GamepadState state = new GamepadState();
			if (dpad.y > 0.5f) { state = state.WithButton(GamepadButton.DpadUp); }
			if (dpad.y < -0.5f) { state = state.WithButton(GamepadButton.DpadDown); }
			if (dpad.x < -0.5f) { state = state.WithButton(GamepadButton.DpadLeft); }
			if (dpad.x > 0.5f) { state = state.WithButton(GamepadButton.DpadRight); }
			UnityEngine.InputSystem.InputSystem.QueueStateEvent(m_Pad, state);
			UnityEngine.InputSystem.InputSystem.Update();
		}

		[UnityTest]
		public IEnumerator LowersNavigationEventsInMouseMode()
		{
			Build();
			yield return null;
			Assert.That(m_Router.Mode, Is.EqualTo(UIInputMode.Mouse));
			Assert.That(m_Events.sendNavigationEvents, Is.False, "Mouse中は下ろす");
		}

		[UnityTest]
		public IEnumerator RestoresNavigationEventsOnDisable()
		{
			Build();
			yield return null;
			Assert.That(m_Events.sendNavigationEvents, Is.False);
			m_Router.enabled = false;
			yield return null;
			Assert.That(m_Events.sendNavigationEvents, Is.True, "元の値へ戻す");
		}

		[UnityTest]
		public IEnumerator SetModeAppliesOnTheNextUpdate()
		{
			Build();
			yield return null;
			m_Router.SetMode(UIInputMode.Navigation);
			Assert.That(m_Router.Mode, Is.EqualTo(UIInputMode.Mouse), "同じフレームでは変えない");
			yield return null;
			Assert.That(m_Router.Mode, Is.EqualTo(UIInputMode.Navigation));
			Assert.That(m_Events.sendNavigationEvents, Is.True, "Navigation中だけ利用者の設定へ戻す");
		}

		[UnityTest]
		public IEnumerator NavigationMoveEntersNavigation()
		{
			Build();
			yield return null;
			Send(dpad: new Vector2(1, 0));
			yield return null;
			Assert.That(m_Router.Mode, Is.EqualTo(UIInputMode.Navigation));
			Assert.That(m_Events.sendNavigationEvents, Is.True);
		}

		/// <summary>切替先がNavigationで選択候補が見つからない場合は方式を変えない。</summary>
		[UnityTest]
		public IEnumerator RejectedNavigationSwitchChangesNothing()
		{
			// Scopeを一つも有効化しないのでTrySelectNearestは候補を見つけられない。
			Build(withSelection: true, selectNearestOnNavigation: true);
			yield return null;
			Assert.That(m_Router.Mode, Is.EqualTo(UIInputMode.Mouse));
			Send(dpad: new Vector2(1, 0));
			yield return null;
			Assert.That(m_Router.Mode, Is.EqualTo(UIInputMode.Mouse), "拒否されたら方式を変えない");
			Assert.That(m_Events.sendNavigationEvents, Is.False, "拒否されたら可否も変えない");
		}

		/// <summary>未選択からNavigationへ入る最初の入力は選択の開始に使い、解放するまでModuleへ渡さない。</summary>
		[UnityTest]
		public IEnumerator ConsumesTheInitialNavigationInputUntilRelease()
		{
			Build(withSelection: true, selectNearestOnNavigation: true);
			Button button = BuildScopeWithButton();
			yield return null;
			yield return null;
			Assert.That(UIFocus.Selected, Is.Null, "前提: Pointer中は選択を持たない");
			Send(dpad: new Vector2(1, 0));
			yield return null;
			Assert.That(m_Router.Mode, Is.EqualTo(UIInputMode.Navigation));
			Assert.That(UIFocus.Selected, Is.EqualTo(button.gameObject), "方向側の候補から選択を始める");
			Assert.That(m_Events.sendNavigationEvents, Is.False, "押している間は最初の入力を渡さない");
			Send();
			yield return null;
			Assert.That(m_Events.sendNavigationEvents, Is.True, "解放したら通常配送へ戻る");
		}
	}
}
