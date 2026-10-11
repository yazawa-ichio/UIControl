#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.TestTools;
using UIControl.InputSystem;

namespace UIControl.Testing.PlayMode
{
	/// <summary>
	/// プロジェクト内のサンプルを実際にPlayModeで動かす。テストランナーは想定外の
	/// LogError／LogExceptionでテストを落とすため、フレームを送るだけで
	/// 「起動して数秒動かしたときに壊れていないか」を見られる。
	/// 直列化が通ることと動くことは別なので、Prefabの再生成だけでは代わりにならない。
	/// </summary>
	public sealed class SampleSceneSmokeTests
	{
		private const int Frames = 90;

		private Scene m_Loaded;
		private Scene m_Previous;
		private InputSettings.EditorInputBehaviorInPlayMode m_EditorBehavior;
		private InputSettings.BackgroundBehavior m_BackgroundBehavior;

		[SetUp]
		public void SetUp()
		{
			// フォーカスの無いEditorではPointer入力がGame Viewへ届かない既定なので、テスト中だけ全入力を通す。仮想Mouseデバイスの配送に必要。
			InputSettings settings = UnityEngine.InputSystem.InputSystem.settings;
			m_EditorBehavior = settings.editorInputBehaviorInPlayMode;
			m_BackgroundBehavior = settings.backgroundBehavior;
			settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
			settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
		}

		[UnityTearDown]
		public IEnumerator TearDown()
		{
			InputSettings settings = UnityEngine.InputSystem.InputSystem.settings;
			settings.editorInputBehaviorInPlayMode = m_EditorBehavior;
			settings.backgroundBehavior = m_BackgroundBehavior;
			if (!m_Loaded.IsValid() || !m_Loaded.isLoaded) { yield break; }
			// 読み込んだサンプルを残すと、後続テストのEventSystemやUIControlRootと衝突する。
			// 空シーンを作ると二度目に同名で失敗するので、元のActiveへ戻してから降ろす。
			if (m_Previous.IsValid() && m_Previous.isLoaded) { SceneManager.SetActiveScene(m_Previous); }
			yield return SceneManager.UnloadSceneAsync(m_Loaded);
			m_Loaded = default;
		}

		private IEnumerator Run(string path, int frames = Frames)
		{
			m_Previous = SceneManager.GetActiveScene();
			UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(
				path, new LoadSceneParameters(LoadSceneMode.Additive));
			yield return null;
			m_Loaded = SceneManager.GetSceneByPath(path);
			Assert.That(m_Loaded.IsValid() && m_Loaded.isLoaded, Is.True, "読み込めなかった: " + path);
			SceneManager.SetActiveScene(m_Loaded);
			for (int i = 0; i < frames; i++) { yield return null; }
			Assert.That(m_Loaded.isLoaded, Is.True, "動作中に破棄された: " + path);
		}

		[UnityTest]
		public IEnumerator PointerInputSample() { yield return Run("Assets/InputExamples/PointerInput.unity"); }

		/// <summary>
		/// サンプル資産そのものをPadで操作する。スティックで仮想マウスが動いてMouse方式になり、
		/// 決定が左クリックとして標準経路でButtonのonClickまで届くところを見る。
		/// </summary>
		[UnityTest]
		public IEnumerator PointerInputSampleRespondsToPad()
		{
			Gamepad pad = UnityEngine.InputSystem.InputSystem.AddDevice<Gamepad>();
			try
			{
				yield return Run("Assets/InputExamples/PointerInput.unity", frames: 5);
				var router = Object.FindFirstObjectByType<UIControl.InputSystem.InputSystemUIInputRouter>();
				var cursor = Object.FindFirstObjectByType<VirtualMouseCursor>();
				Assert.That(router, Is.Not.Null, "サンプルにRouterが居ない");
				Assert.That(cursor, Is.Not.Null, "サンプルにVirtualMouseCursorが居ない");

				// 左スティックで仮想マウスが動き、Mouse方式になる。サンプルのActionは<Gamepad>/leftStickに張ってある。
				yield return EnterVirtualMouse(pad, router);
				Assert.That(router.IsVirtualMouseActive, Is.True, "操作中のMouseが仮想マウスではない");

				Button target = FindButton("Two");
				yield return PlaceOver(cursor, (RectTransform)target.transform);
				yield return Press(pad, GamepadButton.South);
				yield return null;
				Text status = Object.FindObjectsByType<Text>(FindObjectsSortMode.None).FirstOrDefault(text => text.name == "Status");
				Assert.That(status, Is.Not.Null, "サンプルのStatusが居ない");
				yield return TestUntil(() => status.text.Contains("Pressed: Two"), "決定がサンプルのonClickまで届かない: " + status.text, 60);
			}
			finally { if (pad != null && pad.added) { UnityEngine.InputSystem.InputSystem.RemoveDevice(pad); } }
		}

		/// <summary>
		/// 仮想マウスで操作している間、カーソル画像（Source = VirtualMouseCursor）と追従ラベル（Source = Router）の
		/// 両方のPointerFollowerがカーソル位置へ追従する。Routerの位置は仮想マウスの自己申告と一致する。
		/// </summary>
		[UnityTest]
		public IEnumerator PointerInputSampleFollowersTrackTheVirtualMouse()
		{
			Gamepad pad = UnityEngine.InputSystem.InputSystem.AddDevice<Gamepad>();
			try
			{
				yield return Run("Assets/InputExamples/PointerInput.unity", frames: 5);
				var router = Object.FindFirstObjectByType<UIControl.InputSystem.InputSystemUIInputRouter>();
				var cursor = Object.FindFirstObjectByType<VirtualMouseCursor>();
				PointerFollower[] followers = Object.FindObjectsByType<PointerFollower>(FindObjectsSortMode.None);
				PointerFollower image = followers.FirstOrDefault(f => f.name == "Virtual Cursor");
				PointerFollower label = followers.FirstOrDefault(f => f.name == "Pointer Label");
				Assert.That(image, Is.Not.Null, "Virtual CursorのPointerFollowerが居ない");
				Assert.That(label, Is.Not.Null, "Pointer LabelのPointerFollowerが居ない");

				// 方向キーでNavigationへ入り、Buttonを選んでからスティックで仮想マウスへ戻る。
				yield return Press(pad, GamepadButton.DpadRight);
				Assert.That(router.Mode, Is.EqualTo(UIInputMode.Navigation), "方向キーでNavigationへ入れない");
				EventSystem events = Object.FindFirstObjectByType<EventSystem>();
				// Navigation中は、Source = RouterのFollowerが選択中のButtonへ追従する。
				events.SetSelectedGameObject(FindButton("One").gameObject);
				yield return null;
				yield return null;
				Vector3 labelAtOne = label.transform.position;
				Button target = FindButton("Three");
				events.SetSelectedGameObject(target.gameObject);
				yield return null;
				yield return null;
				Assert.That(Vector3.Distance(label.transform.position, labelAtOne), Is.GreaterThan(1f), "Navigation中にPointer Labelが選択へ追従しない");
				Assert.That(Vector2.Distance(router.Position, Center((RectTransform)target.transform)), Is.LessThan(1f), "Navigation中のRouterの位置が選択中のButtonの中心でない");
				Vector3 imageBefore = image.transform.position;
				Vector3 labelBefore = label.transform.position;

				for (int i = 0; i < 20; i++) { Send(pad, new Vector2(0, 1)); yield return null; }
				Send(pad, Vector2.zero);
				yield return null;
				yield return null;
				string state = " mode=" + router.Mode + " virtual=" + router.IsVirtualMouseActive + " cursor=" + cursor.Position
					+ " routerPos=" + router.Position + " device=" + (cursor.Device != null ? cursor.Device.position.ReadValue().ToString() : "none")
					+ " image=" + image.transform.position + " label=" + label.transform.position
					+ " imageEnabled=" + image.GetComponent<UnityEngine.UI.Graphic>().enabled;
				Assert.That(router.Mode, Is.EqualTo(UIInputMode.Mouse), "スティックでMouse方式へ戻れない" + state);
				Assert.That(router.IsVirtualMouseActive, Is.True, "仮想マウスが操作中にならない" + state);
				Vector2 center = Center((RectTransform)target.transform);
				Assert.That(cursor.Position.x, Is.EqualTo(center.x).Within(2f), "選択中のButtonの中心（X）から始まらない" + state);
				Assert.That(cursor.Position.y, Is.GreaterThan(center.y), "上へ倒したのにカーソルが上へ進まない" + state);
				Assert.That(Vector3.Distance(image.transform.position, imageBefore), Is.GreaterThan(1f), "カーソル画像が追従しない" + state);
				Assert.That(Vector3.Distance(label.transform.position, labelBefore), Is.GreaterThan(1f), "Pointer Labelが追従しない" + state);
				Assert.That(Vector2.Distance(router.Position, cursor.Position), Is.LessThan(1f), "Routerの位置が仮想マウスの位置と一致しない" + state);
			}
			finally { if (pad != null && pad.added) { UnityEngine.InputSystem.InputSystem.RemoveDevice(pad); } }
		}

		/// <summary>サンプルのカードを仮想マウスで掴み、Drop Areaまで運んで落とす。標準のDrag経路で成立する。</summary>
		[UnityTest]
		public IEnumerator PointerInputSampleDragsWithPad()
		{
			Gamepad pad = UnityEngine.InputSystem.InputSystem.AddDevice<Gamepad>();
			try
			{
				yield return Run("Assets/InputExamples/PointerInput.unity", frames: 10);
				var router = Object.FindFirstObjectByType<UIControl.InputSystem.InputSystemUIInputRouter>();
				var cursor = Object.FindFirstObjectByType<VirtualMouseCursor>();
				var drag = Object.FindFirstObjectByType<UIDraggable>();
				var dropTarget = Object.FindFirstObjectByType<UIDropTarget>();
				Assert.That(drag, Is.Not.Null, "サンプルにUIDraggableが居ない");
				Assert.That(dropTarget, Is.Not.Null, "サンプルにUIDropTargetが居ない");
				yield return EnterVirtualMouse(pad, router);

				yield return PlaceOver(cursor, (RectTransform)drag.transform);
				Send(pad, Vector2.zero, GamepadButton.South);
				yield return null;
				RectTransform drop = (RectTransform)dropTarget.transform;
				Vector2 from = cursor.Position;
				Vector2 to = Center(drop);
				for (int i = 1; i <= 8; i++)
				{
					cursor.SetPosition(Vector2.Lerp(from, to, i / 8f));
					Send(pad, Vector2.zero, GamepadButton.South);
					yield return null;
				}
				Assert.That(drag.IsDragging, Is.True, "押したまま動かしてもDragが始まらない");

				Send(pad, Vector2.zero);
				yield return null;
				yield return null;
				Assert.That(drag.IsDragging, Is.False, "離しても落とせない");
				Text status = Object.FindObjectsByType<Text>(FindObjectsSortMode.None).FirstOrDefault(text => text.name == "Status");
				Assert.That(status, Is.Not.Null);
				yield return TestUntil(() => status.text.Contains("Accepted"), "Dropが受理されない: " + status.text, 60);
			}
			finally { if (pad != null && pad.added) { UnityEngine.InputSystem.InputSystem.RemoveDevice(pad); } }
		}

		/// <summary>掴んでいる間に方向キーを入れても、方式は変わらず掴みも維持され、そのまま離して落とせる。</summary>
		[UnityTest]
		public IEnumerator PointerInputSampleKeepsDragWhenDirectionPressed()
		{
			Gamepad pad = UnityEngine.InputSystem.InputSystem.AddDevice<Gamepad>();
			try
			{
				yield return Run("Assets/InputExamples/PointerInput.unity", frames: 10);
				var router = Object.FindFirstObjectByType<UIControl.InputSystem.InputSystemUIInputRouter>();
				var cursor = Object.FindFirstObjectByType<VirtualMouseCursor>();
				var drag = Object.FindFirstObjectByType<UIDraggable>();
				yield return EnterVirtualMouse(pad, router);
				yield return PlaceOver(cursor, (RectTransform)drag.transform);
				Send(pad, Vector2.zero, GamepadButton.South);
				yield return null;
				Vector2 from = cursor.Position;
				for (int i = 1; i <= 4; i++)
				{
					cursor.SetPosition(from + new Vector2(30 * i, 0));
					Send(pad, Vector2.zero, GamepadButton.South);
					yield return null;
				}
				Assert.That(drag.IsDragging, Is.True, "前提: 掴めている");

				// 押したまま方向キーを入れる。
				for (int i = 0; i < 6; i++)
				{
					GamepadState state = new GamepadState().WithButton(GamepadButton.South).WithButton(GamepadButton.DpadRight);
					UnityEngine.InputSystem.InputSystem.QueueStateEvent(pad, state);
					UnityEngine.InputSystem.InputSystem.Update();
					yield return null;
				}
				Assert.That(router.Mode, Is.EqualTo(UIInputMode.Mouse), "押している間に方向キーで方式が変わった");
				Assert.That(drag.IsDragging, Is.True, "方向キーで掴みが落ちた");

				Send(pad, Vector2.zero);
				yield return null;
				yield return null;
				Assert.That(drag.IsDragging, Is.False, "離しても落とせない");
			}
			finally { if (pad != null && pad.added) { UnityEngine.InputSystem.InputSystem.RemoveDevice(pad); } }
		}

		/// <summary>掴んだままDrag対象が破棄されても、離した後に方向キーでNavigationへ移れる。押下状態が残らない。</summary>
		[UnityTest]
		public IEnumerator PointerInputSampleRecoversWhenDraggedObjectIsDestroyed()
		{
			Gamepad pad = UnityEngine.InputSystem.InputSystem.AddDevice<Gamepad>();
			try
			{
				yield return Run("Assets/InputExamples/PointerInput.unity", frames: 10);
				var router = Object.FindFirstObjectByType<UIControl.InputSystem.InputSystemUIInputRouter>();
				var cursor = Object.FindFirstObjectByType<VirtualMouseCursor>();
				var drag = Object.FindFirstObjectByType<UIDraggable>();
				yield return EnterVirtualMouse(pad, router);
				yield return PlaceOver(cursor, (RectTransform)drag.transform);
				Send(pad, Vector2.zero, GamepadButton.South);
				yield return null;
				Vector2 from = cursor.Position;
				for (int i = 1; i <= 4; i++)
				{
					cursor.SetPosition(from + new Vector2(30 * i, 0));
					Send(pad, Vector2.zero, GamepadButton.South);
					yield return null;
				}
				Assert.That(drag.IsDragging, Is.True, "前提: 掴めている");

				Object.Destroy(drag.gameObject);
				yield return null;
				yield return null;
				Send(pad, Vector2.zero);
				yield return null;
				Assert.That(router.IsPointerPressed, Is.False, "離した後に押下が残っている");

				yield return Press(pad, GamepadButton.DpadRight);
				Assert.That(router.Mode, Is.EqualTo(UIInputMode.Navigation), "破棄後にNavigationへ移れない");
			}
			finally { if (pad != null && pad.added) { UnityEngine.InputSystem.InputSystem.RemoveDevice(pad); } }
		}

		/// <summary>
		/// ShowcaseをPadで一通り操作する。方式の切替、方向キーでの移動、決定までを通す。
		/// </summary>
		[UnityTest]
		public IEnumerator ShowcasePadOperation()
		{
			Gamepad pad = UnityEngine.InputSystem.InputSystem.AddDevice<Gamepad>();
			try
			{
				yield return Run("Assets/Showcase/Scenes/Showcase.unity", frames: 60);
				var app = Object.FindFirstObjectByType<UIControl.Showcase.ShowcaseApp>();
				yield return WaitForBoot(app);
				var router = app.InputRouter;
				var events = Object.FindFirstObjectByType<EventSystem>();

				// 方向キーでNavigationへ入り、選択が付く。
				Send(pad, Vector2.zero, GamepadButton.DpadRight);
				yield return null;
				Send(pad, Vector2.zero);
				yield return null;
				Assert.That(router.Mode, Is.EqualTo(UIInputMode.Navigation), "方向キーでNavigationへ入れない");
				yield return TestUntil(() => events.currentSelectedGameObject != null, "Navigationで選択が付かない", 120);
				GameObject first = events.currentSelectedGameObject;

				// 方向キーで選択が移る。候補の無い向きがあるので四方向を試す。
				bool moved = false;
				GamepadButton[] directions = { GamepadButton.DpadDown, GamepadButton.DpadUp, GamepadButton.DpadRight, GamepadButton.DpadLeft };
				foreach (GamepadButton direction in directions)
				{
					for (int i = 0; i < 4 && !moved; i++)
					{
						Send(pad, Vector2.zero, direction);
						yield return null;
						Send(pad, Vector2.zero);
						yield return null;
						yield return null;
						moved = events.currentSelectedGameObject != null && events.currentSelectedGameObject != first;
					}
					if (moved) { break; }
				}
				Assert.That(moved, Is.True, "どの方向キーでも選択が移らない。selected="
					+ (events.currentSelectedGameObject == null ? "(なし)" : events.currentSelectedGameObject.name));

				// 決定で何かが起きる。
				int before = app.History.Count;
				Send(pad, Vector2.zero, GamepadButton.South);
				yield return null;
				Send(pad, Vector2.zero);
				yield return null;
				yield return TestUntil(() => app.History.Count > before, "決定で何も起きない: " + Describe(app, router, events), 180);

				// スティックでVirtualCursorへ移れる。
				Send(pad, new Vector2(1, 0));
				yield return null;
				Assert.That(router.Mode, Is.EqualTo(UIInputMode.Mouse), "スティックでカーソルへ移れない");
				Assert.That(app.ErrorCount, Is.EqualTo(0), "操作中にサンプルがエラーを記録した");
			}
			finally { if (pad != null && pad.added) { UnityEngine.InputSystem.InputSystem.RemoveDevice(pad); } }
		}

		private static Vector2 Center(RectTransform rect)
		{
			return RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
		}

		/// <summary>仮想マウスを対象の中央へ置く。レイアウト確定前は座標がずれるので、数フレーム置き直す。</summary>
		private static IEnumerator PlaceOver(VirtualMouseCursor cursor, RectTransform target)
		{
			for (int i = 0; i < 3; i++)
			{
				cursor.SetPosition(Center(target));
				UnityEngine.InputSystem.InputSystem.Update();
				yield return null;
			}
		}

		/// <summary>スティックで仮想マウスを動かしてMouse方式へ入る。バッチではフレームが速く1回では拾えないことがあるので数回動かす。</summary>
		private static IEnumerator EnterVirtualMouse(Gamepad pad, UIControl.InputSystem.InputSystemUIInputRouter router)
		{
			// RouterはMouse方式で始まるので、方式だけでは仮想マウスの操作中とは言えない。スティックを倒して仮想マウスが操作中になるまで進める。
			for (int i = 0; i < 10 && !router.IsVirtualMouseActive; i++)
			{
				Send(pad, new Vector2(1, 0));
				yield return null;
			}
			Send(pad, Vector2.zero);
			yield return null;
			Assert.That(router.Mode, Is.EqualTo(UIInputMode.Mouse), "スティックでMouse方式へ入れない");
			Assert.That(router.IsVirtualMouseActive, Is.True, "スティックを倒しても仮想マウスが操作中にならない: " + router.DescribeInputs());
		}

		/// <summary>
		/// QueuedDialogsサンプルへ実マウス相当の入力を流し、押下がUI Input Module経由で
		/// Buttonへ届くことを見る。
		/// 確認できるのはここまで。バッチのヘッドレス実行では、手で積んだ入力イベントから
		/// モジュールのクリック確定（onClick）まで進められなかった。押下は届き選択も動くが
		/// onClickが走らないため、Dialogを開くところから先はEditorでの手動確認に委ねる。
		/// </summary>
		[UnityTest]
		public IEnumerator QueuedDialogsSampleReceivesMousePress()
		{
			Mouse mouse = UnityEngine.InputSystem.InputSystem.AddDevice<Mouse>();
			try
			{
				yield return Run("Assets/Adoption/Scenes/QueuedDialogs.unity", frames: 20);
				var sample = Object.FindFirstObjectByType<UIControl.Adoption.QueuedDialogsSample>();
				Assert.That(sample, Is.Not.Null, "サンプルのコンポーネントが居ない");
				yield return Click(mouse, FindButton("Launch"));
			}
			finally { if (mouse != null && mouse.added) { UnityEngine.InputSystem.InputSystem.RemoveDevice(mouse); } }
		}

		/// <summary>
		/// ShowcaseサンプルのRouterをPadで動かす。スティックでMouse方式になり、仮想マウスの左クリックが
		/// 有効なScope内のButtonへ標準経路で届き、サンプルがエラーを記録しないことを見る。
		/// </summary>
		[UnityTest]
		public IEnumerator ShowcaseRespondsToPad()
		{
			Gamepad pad = UnityEngine.InputSystem.InputSystem.AddDevice<Gamepad>();
			try
			{
				yield return Run("Assets/Showcase/Scenes/Showcase.unity", frames: 60);
				var app = Object.FindFirstObjectByType<UIControl.Showcase.ShowcaseApp>();
				Assert.That(app, Is.Not.Null, "サンプルのコンポーネントが居ない");
				Assert.That(app.InputRouter, Is.Not.Null, "Routerが結線されていない");
				var cursor = Object.FindFirstObjectByType<VirtualMouseCursor>();
				Assert.That(cursor, Is.Not.Null, "サンプルにVirtualMouseCursorが居ない");
				// タイトルから画面が立ち上がるまで待つ。固定フレームでは間に合わない。
				yield return WaitForBoot(app);
				yield return EnterVirtualMouse(pad, app.InputRouter);
				FocusScope scope = UIFocus.Current;
				Assert.That(scope, Is.Not.Null, "Mouse方式に移っても操作対象のScopeは残る");
				Button target = Object.FindObjectsByType<Button>(FindObjectsSortMode.None)
					.FirstOrDefault(item => item.IsInteractable() && scope.Contains(item.gameObject));
				Assert.That(target, Is.Not.Null, "有効なScope内に操作可能なButtonが居ない");
				yield return PlaceOver(cursor, (RectTransform)target.transform);
				int before = app.History.Count;
				yield return Press(pad, GamepadButton.South);
				yield return TestUntil(() => app.History.Count > before, "仮想マウスのクリックが届かない", 180);
				Assert.That(app.ErrorCount, Is.EqualTo(0), "サンプルがエラーを記録した");
			}
			finally { if (pad != null && pad.added) { UnityEngine.InputSystem.InputSystem.RemoveDevice(pad); } }
		}

		/// <summary>
		/// Showcaseサンプルの遷移を網羅する。画面に出ているButtonのonClickを順に起動し、
		/// 打ち止めになったらヘッダ開閉と戻るで別の枝へ進む。到達した状態を数え、
		/// エラーが出ないことと踏破数が基準を下回らないことを見る。
		/// 入力がButtonへ届くこと自体はShowcaseRespondsToPadで担保しているため、
		/// ここは枝の広さを優先してonClickを直接起動する。カーソル選択は有効なScope内
		/// だけに限られ、網羅には向かない。
		/// </summary>
		[UnityTest]
		public IEnumerator ShowcaseTransitionCoverage()
		{
			Gamepad pad = UnityEngine.InputSystem.InputSystem.AddDevice<Gamepad>();
			try
			{
				yield return Run("Assets/Showcase/Scenes/Showcase.unity", frames: 30);
				var app = Object.FindFirstObjectByType<UIControl.Showcase.ShowcaseApp>();
				Assert.That(app, Is.Not.Null, "サンプルのコンポーネントが居ない");
				yield return WaitForBoot(app);

				HashSet<string> visited = new HashSet<string>();
				List<string> violations = new List<string>();
				HashSet<string> tried = new HashSet<string>();
				Dictionary<string, int> rejected = new Dictionary<string, int>();
				List<string> trace = new List<string>();
				string lastSignature = null;
				bool headerOpen = false;
				for (int step = 0; step < 500; step++)
				{
					// 待ちでは失敗させない。IProcess系の画面はIsBusyのまま応答を待つので、
					// 落ち着かなければ出ているものを押して解決する。
					yield return Settle(app);
					string signature = string.Join(",", ActiveStates(app).OrderBy(name => name));
					foreach (string name in ActiveStates(app)) { visited.Add(name); }
					// 画面が変わったら、拒否された分はもう一度試す価値がある。
					if (signature != lastSignature) { rejected.Clear(); lastSignature = signature; }
					Inspect(app, signature, violations);

					Button[] onScreen = Object.FindObjectsByType<Button>(FindObjectsSortMode.None)
						.Where(item => item.IsInteractable()).ToArray();
					Button target = onScreen.FirstOrDefault(item =>
					{
						string key = signature + "|" + Label(item);
						return !tried.Contains(key) && (!rejected.TryGetValue(key, out int count) || count < 3);
					});
					if (target == null)
					{
						// 打ち止め。応答待ちなら出ているものを押し、そうでなければヘッダ開閉と戻るで別の枝へ。
						if (app.IsBusy && onScreen.Length > 0) { onScreen[0].onClick.Invoke(); yield return null; continue; }
						trace.Add((headerOpen ? "BACK" : "HEADER") + " @" + signature);
						yield return Press(pad, headerOpen ? GamepadButton.East : GamepadButton.Start);
						headerOpen = !headerOpen;
						continue;
					}
					string targetKey = signature + "|" + Label(target);
					// サンプルは受理したクリックだけHistoryへ記録する。無視されたものを試行済みにしない。
					int before = app.History.Count;
					target.onClick.Invoke();
					yield return null;
					if (app.History.Count > before)
					{
						tried.Add(targetKey);
						trace.Add(Label(target) + " @" + signature);
					}
					else
					{
						rejected.TryGetValue(targetKey, out int count);
						rejected[targetKey] = count + 1;
					}
				}

				Inspect(app, "final", violations);
				if (violations.Count != 0)
				{
					Debug.LogWarning("Showcase 不変条件の違反 " + violations.Count + "件 :: " + string.Join(" :: ", violations.Take(40)));
				}
				Assert.That(app.ErrorCount, Is.EqualTo(0), "遷移中にサンプルがエラーを記録した");
				Debug.Log("Showcase 踏破 " + visited.Count + "状態: " + string.Join(", ", visited.OrderBy(name => name))
					+ "  |  Showcase 経路: " + string.Join(" / ", trace));
				// 探索は非決定的なので踏破数では判定しない。結果は上のログで確認する。
			}
			finally { if (pad != null && pad.added) { UnityEngine.InputSystem.InputSystem.RemoveDevice(pad); } }
		}

		/// <summary>
		/// ライブラリ側の不備を探す。手作業の操作中に成立しているべき条件を、落ち着いた時点で見る。
		/// 失敗させず記録だけして探索を続け、最後にまとめて報告する。
		/// </summary>
		private static void Inspect(UIControl.Showcase.ShowcaseApp app, string where, List<string> violations)
		{
			if (app.IsBusy) { return; }
			void Add(string message) { if (violations.Count < 200 && !violations.Contains(message)) { violations.Add(message); } }

			// 占有が残ると選択も方向移動も決定も止まる。誰も占有していない場面で立っていないか。
			if (UIFocus.IsNavigationOccupied) { Add(where + ": ナビゲーションが占有されたまま"); }

			// 入力可否そのものは検査しない。BlockInputとSetVisibleで利用者が意図的に変えられるため、
			// 外から意図と不備を区別できない（実際にDemoScreenFadeのBlockInputで誤検知した）。

			// 回収済み・非表示のPageが選択を握っていないか。
			GameObject selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
			if (selected != null)
			{
				if (!selected.activeInHierarchy) { Add(where + ": 非activeなGameObjectが選択されている " + selected.name); }
				var selectable = selected.GetComponent<Selectable>();
				if (selectable != null && !selectable.IsInteractable()) { Add(where + ": 操作できないSelectableが選択されている " + selected.name); }
				Page owner = selected.GetComponentInParent<Page>(true);
				if (owner != null && !owner.IsInputAllowed) { Add(where + ": 入力不可のPage内が選択されている " + owner.name + "/" + selected.name); }
			}

			// どのStackにも属さないまま描画され続けているPageが無いか。
			var tracked = new HashSet<Page>();
			foreach (UIStack stack in new[] { app.Main, app.Front })
			{
				foreach (var handle in stack.GetStackSnapshot()) { if (handle.Page != null) { tracked.Add(handle.Page); } }
			}
			foreach (Page page in Object.FindObjectsByType<Page>(FindObjectsInactive.Include, FindObjectsSortMode.None))
			{
				if (page.IsRendered && !tracked.Contains(page)) { Add(where + ": Stackに無いPageが描画されたまま " + page.name); }
			}
		}

		/// <summary>Buttonに限らずSelectableをラベルで探して押す。リスト行がButtonでない場合に使う。</summary>
		private static IEnumerator ClickSelectableLabel(UIControl.Showcase.ShowcaseApp app, string label)
		{
			for (int i = 0; i < 600; i++)
			{
				yield return Settle(app);
				foreach (Selectable item in Object.FindObjectsByType<Selectable>(FindObjectsSortMode.None))
				{
					if (!item.IsInteractable()) { continue; }
					var text = item.GetComponentInChildren<TMPro.TMP_Text>(true);
					string name = text != null ? text.text : item.name;
					if (!name.ToUpperInvariant().Contains(label)) { continue; }
					int before = app.History.Count;
					if (item is Button button) { button.onClick.Invoke(); }
					else { ExecuteEvents.Execute(item.gameObject, new BaseEventData(EventSystem.current), ExecuteEvents.submitHandler); }
					yield return null;
					yield return TestUntil(() => app.History.Count > before, label + " を押しても何も起きない", 300);
					yield break;
				}
				yield return null;
			}
			Assert.Fail(label + " が見つからない。now=" + string.Join(",", ActiveStates(app))
				+ " selectables=[" + string.Join(" | ", Object.FindObjectsByType<Selectable>(FindObjectsSortMode.None)
					.Where(item => item.IsInteractable())
					.Select(item => { var x = item.GetComponentInChildren<TMPro.TMP_Text>(true); return x != null ? x.text : item.name; })) + "]");
		}

		/// <summary>
		/// マウスが存在する環境で方向キーがNavigationへ切り替わるか。
		/// バッチには実マウスが無く、利用者の環境にはある。唯一見つかった差なので再現を試みる。
		/// </summary>
		[UnityTest]
		public IEnumerator ShowcaseNavigationWorksWithMousePresent()
		{
			Mouse mouse = UnityEngine.InputSystem.InputSystem.AddDevice<Mouse>();
			Gamepad pad = UnityEngine.InputSystem.InputSystem.AddDevice<Gamepad>();
			try
			{
				yield return Run("Assets/Showcase/Scenes/Showcase.unity", frames: 30);
				var app = Object.FindFirstObjectByType<UIControl.Showcase.ShowcaseApp>();
				yield return WaitForBoot(app);
				var router = app.InputRouter;
				var events = Object.FindFirstObjectByType<EventSystem>();

				// マウスを動かしてMouse方式にする。利用者の初期状態に合わせる。
				Move(mouse, new Vector2(Screen.width * 0.5f, Screen.height * 0.5f), false);
				yield return null;
				Move(mouse, new Vector2(Screen.width * 0.5f + 40, Screen.height * 0.5f + 40), false);
				yield return null;
				yield return null;
				Assert.That(router.Mode, Is.EqualTo(UIInputMode.Mouse), "マウスを動かしてもMouse方式にならない");

				// この状態で方向キーを押す。
				for (int i = 0; i < 6 && router.Mode != UIInputMode.Navigation; i++)
				{
					Send(pad, Vector2.zero, GamepadButton.DpadDown);
					yield return null;
					Send(pad, Vector2.zero);
					yield return null;
				}
				Assert.That(router.Mode, Is.EqualTo(UIInputMode.Navigation),
					"マウスがあると方向キーでNavigationへ切り替わらない。" + DescribeScopes(app));
				yield return TestUntil(() => events.currentSelectedGameObject != null,
					"Navigationへ入っても選択が付かない。" + DescribeScopes(app), 180);
			}
			finally
			{
				if (pad != null && pad.added) { UnityEngine.InputSystem.InputSystem.RemoveDevice(pad); }
				if (mouse != null && mouse.added) { UnityEngine.InputSystem.InputSystem.RemoveDevice(mouse); }
			}
		}

		private static void Move(Mouse mouse, Vector2 position, bool pressed)
		{
			MouseState state = new MouseState { position = position };
			if (pressed) { state = state.WithButton(MouseButton.Left); }
			UnityEngine.InputSystem.InputSystem.QueueStateEvent(mouse, state);
			UnityEngine.InputSystem.InputSystem.Update();
		}

		/// <summary>FocusScopeが有効にならない理由を見るための書き出し。</summary>
		private static string DescribeScopes(UIControl.Showcase.ShowcaseApp app)
		{
			var parts = new List<string>();
			foreach (FocusScope scope in Object.FindObjectsByType<FocusScope>(FindObjectsInactive.Include, FindObjectsSortMode.None))
			{
				Page page = scope.GetComponentInParent<Page>(true);
				parts.Add(scope.name
					+ " enabled=" + scope.isActiveAndEnabled
					+ " canFocus=" + scope.CanFocus
					+ " active=" + (scope.IsActive ? "あり" : "なし")
					+ (page == null ? " page=なし"
						: " page=" + page.name + " preparing=" + page.IsPreparing + " rendered=" + page.IsRendered + " input=" + page.IsInputAllowed));
			}
			return "scopes=[" + string.Join(" || ", parts) + "]";
		}

		/// <summary>目的の状態へ着くまで探索する。自動操縦と同じ受理判定とヘッダ開閉を使う。</summary>
		private static IEnumerator CrawlUntil(UIControl.Showcase.ShowcaseApp app, Gamepad pad, string state)
		{
			HashSet<string> tried = new HashSet<string>();
			Dictionary<string, int> rejected = new Dictionary<string, int>();
			string lastSignature = null;
			bool headerOpen = false;
			for (int step = 0; step < 300; step++)
			{
				yield return Settle(app);
				if (ActiveStates(app).Contains(state)) { yield break; }
				string signature = string.Join(",", ActiveStates(app).OrderBy(name => name));
				if (signature != lastSignature) { rejected.Clear(); lastSignature = signature; }
				Button[] onScreen = Object.FindObjectsByType<Button>(FindObjectsSortMode.None).Where(item => item.IsInteractable()).ToArray();
				Button target = onScreen.FirstOrDefault(item =>
				{
					string key = signature + "|" + Label(item);
					return !tried.Contains(key) && (!rejected.TryGetValue(key, out int count) || count < 3);
				});
				if (target == null)
				{
					if (app.IsBusy && onScreen.Length > 0) { onScreen[0].onClick.Invoke(); yield return null; continue; }
					yield return Press(pad, headerOpen ? GamepadButton.East : GamepadButton.Start);
					headerOpen = !headerOpen;
					continue;
				}
				string targetKey = signature + "|" + Label(target);
				int before = app.History.Count;
				target.onClick.Invoke();
				yield return null;
				if (app.History.Count > before) { tried.Add(targetKey); }
				else { rejected.TryGetValue(targetKey, out int count); rejected[targetKey] = count + 1; }
			}
			Assert.Fail(state + " へ到達できない。now=" + string.Join(",", ActiveStates(app)));
		}

		/// <summary>表示中のButtonをラベルで探して押す。見つかるまで待つ。</summary>
		private static IEnumerator ClickLabel(UIControl.Showcase.ShowcaseApp app, string label)
		{
			Button target = null;
			for (int i = 0; i < 600; i++)
			{
				yield return Settle(app);
				target = Object.FindObjectsByType<Button>(FindObjectsSortMode.None)
					.FirstOrDefault(item => item.IsInteractable() && Label(item).ToUpperInvariant().Contains(label));
				if (target != null) { break; }
				yield return null;
			}
			Assert.That(target, Is.Not.Null, label + " が見つからない。now=" + string.Join(",", ActiveStates(app))
				+ " labels=[" + string.Join(" | ", Object.FindObjectsByType<Button>(FindObjectsSortMode.None)
					.Where(item => item.IsInteractable()).Select(Label)) + "]");
			int before = app.History.Count;
			target.onClick.Invoke();
			yield return null;
			yield return TestUntil(() => app.History.Count > before, label + " を押しても何も起きない", 300);
		}

		/// <summary>落ち着くまで待つ。落ち着かなくても失敗させず、呼び出し側の判断へ返す。</summary>
		private static IEnumerator Settle(UIControl.Showcase.ShowcaseApp app)
		{
			for (int i = 0; i < 180 && app.IsBusy; i++) { yield return null; }
		}

		/// <summary>テスト専用の内観。活性中の状態名を取る公開APIが無いため私有メソッドを呼ぶ。</summary>
		private static IEnumerable<string> ActiveStates(UIControl.Showcase.ShowcaseApp app)
		{
			var method = app.Manager.GetType().GetMethod("GetAllStates",
				System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
			if (method == null) { return System.Linq.Enumerable.Empty<string>(); }
			var list = new List<GameStateStructure.GameState>();
			method.Invoke(app.Manager, new object[] { list });
			return list.Where(state => state != null).Select(state => state.GetType().Name).ToList();
		}

		private static string Label(Button button)
		{
			var text = button.GetComponentInChildren<TMPro.TMP_Text>(true);
			return text != null ? text.text : button.name;
		}

		/// <summary>カーソルをButtonへ置いて選択させ、決定で起動する。</summary>
		private static IEnumerator ActivateWithPad(Gamepad pad, Button target)
		{
			var cursor = Object.FindFirstObjectByType<VirtualMouseCursor>();
			if (cursor == null) { yield break; }
			Send(pad, new Vector2(1, 0));
			yield return null;
			// スティックを倒したままだとカーソルが流れるので離してから置く。
			Send(pad, Vector2.zero);
			yield return null;
			yield return PlaceOver(cursor, (RectTransform)target.transform);
			yield return Press(pad, GamepadButton.South);
		}

		private static string Describe(UIControl.Showcase.ShowcaseApp app, UIControl.InputSystem.InputSystemUIInputRouter router, EventSystem events)
		{
			return "mode=" + router.Mode + " nav=" + events.sendNavigationEvents + " busy=" + app.IsBusy
				+ " selected=" + (events.currentSelectedGameObject == null ? "(なし)" : events.currentSelectedGameObject.name)
				+ " router=" + router.DescribeInputs()
				+ " last=" + (app.History.Count > 0 ? app.History[app.History.Count - 1] : "(なし)");
		}

		/// <summary>
		/// 起動（フェード → タイトルのPage → FocusScopeの有効化）を待つ。フェードは実時間で進むが、バッチのフレームは
		/// 極端に速いことがあるので、フレーム数ではなく実時間で上限を切る。
		/// </summary>
		private static IEnumerator WaitForBoot(UIControl.Showcase.ShowcaseApp app)
		{
			float deadline = Time.realtimeSinceStartup + 30f;
			while (Time.realtimeSinceStartup < deadline)
			{
				if (UIFocus.Current != null && !app.IsBusy) { yield break; }
				yield return null;
			}
			Assert.Fail("30秒待っても起動が終わらない: busy=" + app.IsBusy + " scope=" + (UIFocus.Current != null ? UIFocus.Current.name : "(なし)")
				+ " errors=" + app.ErrorCount + " last=" + (app.History.Count > 0 ? app.History[app.History.Count - 1] : "(なし)"));
		}

		private static IEnumerator Press(Gamepad pad, GamepadButton button)
		{
			Send(pad, Vector2.zero, button);
			yield return null;
			Send(pad, Vector2.zero);
			yield return null;
		}

		private static IEnumerator TestUntil(System.Func<bool> condition, string message, int frames = 300)
		{
			for (int i = 0; i < frames; i++)
			{
				if (condition()) { yield break; }
				yield return null;
			}
			Assert.Fail(frames + "フレーム待っても成立しなかった: " + message);
		}

		/// <summary>UI Input Moduleへ実マウス相当の移動と押下を流す。</summary>
		private static IEnumerator Click(Mouse mouse, Button button)
		{
			RectTransform rect = (RectTransform)button.transform;
			Vector2 position = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
			// Pageの入力が開くまで数フレームかかる。押せる状態になるまで動かし続ける。
			// 判定はレイキャストの実結果で行う。IsPointerOverGameObjectはPointerIdの取り方に左右される。
			List<RaycastResult> hits = new List<RaycastResult>();
			bool over = false;
			for (int i = 0; i < 300 && !over; i++)
			{
				UnityEngine.InputSystem.InputSystem.QueueStateEvent(mouse, new MouseState { position = position });
				UnityEngine.InputSystem.InputSystem.Update();
				yield return null;
				hits.Clear();
				if (EventSystem.current == null) { continue; }
				EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = position }, hits);
				over = hits.Any(hit => hit.gameObject == button.gameObject || hit.gameObject.transform.IsChildOf(button.transform));
			}
			Assert.That(over, Is.True, button.name + " へレイキャストが当たらない。position=" + position
				+ " screen=" + Screen.width + "x" + Screen.height
				+ " hits=[" + string.Join(", ", hits.Select(hit => hit.gameObject.name)) + "]");
			// 押下は数フレーム保持する。1フレームだと押下と解放が同じ入力更新へ寄ることがある。
			for (int i = 0; i < 3; i++)
			{
				UnityEngine.InputSystem.InputSystem.QueueStateEvent(mouse, new MouseState { position = position }.WithButton(MouseButton.Left));
				UnityEngine.InputSystem.InputSystem.Update();
				yield return null;
			}
			// モジュールが合成マウスを処理していれば、押下でSelectableが選択される。
			var module = Object.FindFirstObjectByType<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
			Assert.That(EventSystem.current.currentSelectedGameObject, Is.EqualTo(button.gameObject),
				"押下がモジュールへ届いていない。module=" + (module == null ? "なし" : module.isActiveAndEnabled.ToString())
				+ " point=" + (module?.point?.action?.enabled) + " click=" + (module?.leftClick?.action?.enabled)
				+ " overGO=" + EventSystem.current.IsPointerOverGameObject(mouse.deviceId));
			for (int i = 0; i < 3; i++)
			{
				UnityEngine.InputSystem.InputSystem.QueueStateEvent(mouse, new MouseState { position = position });
				UnityEngine.InputSystem.InputSystem.Update();
				yield return null;
			}
		}

		private static Button FindButton(string name)
		{
			Button button = Object.FindObjectsByType<Button>(FindObjectsSortMode.None).FirstOrDefault(item => item.name == name);
			Assert.That(button, Is.Not.Null, "サンプルにButton " + name + " が居ない");
			return button;
		}

		/// <summary>
		/// Padの状態を積むだけで、処理は次フレーム冒頭のInput System更新に任せる。
		/// ここで InputSystem.Update() を呼ぶと、ModuleのSubmit / Cancel（フレーム番号で押下を見る）が
		/// 次フレームのProcessで「このフレームの押下」にならず、決定が届かない。
		/// </summary>
		private static void Send(Gamepad pad, Vector2 leftStick, GamepadButton? button = null)
		{
			GamepadState state = new GamepadState { leftStick = leftStick };
			if (button.HasValue) { state = state.WithButton(button.Value); }
			UnityEngine.InputSystem.InputSystem.QueueStateEvent(pad, state);
		}

		[UnityTest]
		public IEnumerator QueuedDialogsSample() { yield return Run("Assets/Adoption/Scenes/QueuedDialogs.unity"); }

		[UnityTest]
		public IEnumerator ShowcaseSample() { yield return Run("Assets/Showcase/Scenes/Showcase.unity"); }
	}
}
#endif
