using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GameStateStructure;
using TMPro;
using UIControl.GSS;
using UIControl.InputSystem;
using UIControl.UVMBinding;
using UnityEngine;
using UnityEngine.InputSystem;

namespace UIControl.Showcase
{
	/// <summary>
	/// UIControlの機能を一通り触れる見本アプリ。2つのUIStack（Main / Front）、独立したヘッダーのFocusScope、
	/// 全画面フェードによる遮断、GSSの状態木、IViewModelによるPage内容、Padと仮想マウスの切り替えを1つのSceneに置く。
	/// 業務フロー（どの画面からどこへ行けるか）はこのクラスと各Stateが決め、ライブラリは画面の開閉と選択だけを扱う。
	/// </summary>
	public sealed class ShowcaseApp : MonoBehaviour, IStateObserver
	{
		[SerializeField] UIStack m_Main;
		[SerializeField] UIStack m_Front;
		[SerializeField] InputSystemUIInputRouter m_InputRouter;
		[SerializeField] FocusScope m_Header;
		[SerializeField] CanvasGroup m_HeaderGroup;
		[SerializeField] TMP_Text m_Status;
		[SerializeField] GameObject[] m_PagePrefabs;
		[SerializeField] GameStateManager m_Manager;
		[SerializeField] ScreenFade m_ScreenFade;
		readonly DefaultPageLoader m_Loader = new DefaultPageLoader();
		readonly List<string> m_Log = new List<string>();
		readonly List<StateTreeNode> m_Tree = new List<StateTreeNode>();
		float m_NextStatus;
		bool m_DialogOpen;
		bool m_Ready;
		bool m_Destroyed;
		public UIStack Main => m_Main;
		public UIStack Front => m_Front;
		public InputSystemUIInputRouter InputRouter => m_InputRouter;
		public GameStateManager Manager => m_Manager;
		public IReadOnlyList<string> History => m_Log;
		public int ErrorCount { get; private set; }
		// 仮のゲーム内データ。メモリ上だけで保存しない。
		public int Coins { get; set; } = 300;
		public int Stage { get; set; } = 1;
		public string PlayerName { get; set; } = "Player";
		public string SelectedItem { get; set; } = "(none)";
		public bool IsBusy => m_Manager.IsBusy || m_Main.HasPendingOperations || m_Front.HasPendingOperations || m_ScreenFade.IsBusy;

		async void Start()
		{
			m_Main.Error += Report;
			m_Front.Error += Report;
			UIFocus.Error += Report;
			m_Manager.AddObserver(this);
			foreach (var prefab in m_PagePrefabs) { m_Loader.Register(prefab.name, prefab); }
			try
			{
				var config = new Config { Factory = new ShowcaseStateFactory(this) };
				config.Decorator.Register(new ScreenFadeDecorator(m_ScreenFade));
				await m_Manager.Entry<RootState>(config);
				m_Ready = true;
			}
			catch (Exception error) { Report(error); }
		}

		void OnDestroy()
		{
			m_Destroyed = true;
			if (m_Manager != null) { m_Manager.RemoveObserver(this); }
			if (m_Main != null) { m_Main.Error -= Report; m_Main.ForceReleaseAll(); }
			if (m_Front != null) { m_Front.Error -= Report; m_Front.ForceReleaseAll(); }
			UIFocus.Error -= Report;
		}

		void Update()
		{
			if (!m_Ready) { return; }
			var keyboard = Keyboard.current;
			var pad = Gamepad.current;
			if (keyboard?.escapeKey.wasPressedThisFrame == true || pad?.buttonEast.wasPressedThisFrame == true) { InvokeInput("Back"); }
			if (keyboard?.f1Key.wasPressedThisFrame == true || pad?.startButton.wasPressedThisFrame == true) { ToggleHeader(); }
			if (keyboard?.qKey.wasPressedThisFrame == true || pad?.leftShoulder.wasPressedThisFrame == true) { m_InputRouter.SetMode(UIInputMode.Navigation); InvokeInput("Previous"); }
			if (keyboard?.eKey.wasPressedThisFrame == true || pad?.rightShoulder.wasPressedThisFrame == true) { m_InputRouter.SetMode(UIInputMode.Navigation); InvokeInput("Next"); }
			// 観測パネルの文字列生成はサンプルのみ。ライブラリの定常負荷に含めない。
			if (Time.unscaledTime >= m_NextStatus)
			{
				m_NextStatus = Time.unscaledTime + 0.2f;
				m_Status.text = Describe();
				bool headerAllowed = !m_DialogOpen && m_Manager.FindState<HeaderHostState>() != null && m_Manager.FindState<PlayState>() == null;
				m_HeaderGroup.interactable = headerAllowed;
				m_HeaderGroup.blocksRaycasts = headerAllowed && !IsBusy;
			}
		}

		/// <summary>Back / L / R を、Raycastで露出している通常Buttonへ型で届ける。ヘッダー操作中のBackはヘッダーを閉じる。</summary>
		public bool InvokeInput(string action)
		{
			if (IsBusy) { return false; }
			if (action == "Back" && m_Header.IsCurrent) { ToggleHeader(); return true; }
			switch (action)
			{
				case "Back": return InputButton.TryInvoke<BackButton>();
				case "Previous": return InputButton.TryInvoke<PreviousButton>();
				case "Next": return InputButton.TryInvoke<NextButton>();
				default: return false;
			}
		}

		public void Dispatch(string name, Action action)
		{
			if (!m_Ready || IsBusy) { return; }
			Log("CLICK " + name);
			try { action(); }
			catch (Exception error) { Report(error); }
		}

		public async void Run(Func<Task> action)
		{
			try { await action(); }
			catch (OperationCanceledException) { }
			catch (Exception error) { Report(error); }
		}

		/// <summary>
		/// IViewModelをPageへ渡して開く。Backdropと演出順序は呼び出し側の指定で上書きできる（通常はPrefab / Stackの既定）。
		/// GSSのContextを渡すと、状態の終了で自動的に閉じる。
		/// </summary>
		public PageHandle Open(ShowcasePageModel model, string prefab = "MenuPage", bool replace = false, bool front = false,
			BackdropMode backdrop = BackdropMode.Covered, TransitionOrder order = TransitionOrder.Inherit, Context state = null)
		{
			ViewModelUIParameter parameter = model.CreateUIParameter(prefab, m_Loader);
			OpenOptions options = OpenOptions.Default.WithBackdrop(backdrop, BackdropRange.AllBehind).WithTransitionOrder(order)
				.WithSetup((page, context) =>
				{
					page.name = model.Title;
					context.Own(model);
				});
			UIStack stack = front ? m_Front : m_Main;
			PageHandle handle = replace ? stack.Replace(parameter, options) : stack.Push(parameter, options);
			if (state != null) { handle.AttachToState(state); }
			Log((replace ? "REPLACE " : "PUSH ") + model.Title);
			return handle;
		}

		/// <summary>Front StackのDialogで回答を待つ。inputがtrueならテキスト欄を出し、確定でPlayerNameへ入れる。</summary>
		public async Task<bool> Confirm(string title, string message, CancellationToken token, bool input = false)
		{
			if (m_DialogOpen) { return false; }
			m_DialogOpen = true;
			PageHandle handle = null;
			// DialogPageは全面のDimがRaycastを受け、FocusScopeのPriorityが高いので、MainのPageは操作できない。
			m_HeaderGroup.interactable = false;
			var completion = new TaskCompletionSource<bool>();
			using var cancel = token.Register(() => completion.TrySetCanceled(token));
			try
			{
				var model = new ShowcasePageModel(this, title, message);
				model.Set("InputVisible", input);
				model.Actions((input ? "APPLY NAME" : "CONFIRM", () =>
				{
					if (input)
					{
						string text = handle.Page.GetComponentInChildren<TMP_InputField>().text;
						if (string.IsNullOrWhiteSpace(text)) { model.Set("Detail", "Type a name first. Arrow keys edit the text while the field is focused."); return; }
						PlayerName = text.Trim();
					}
					completion.TrySetResult(true);
				}));
				model.Back(() => completion.TrySetResult(false), "CANCEL");
				handle = Open(model, "DialogPage", front: true);
				await handle.Opened;
				return await completion.Task;
			}
			finally
			{
				if (handle != null) { await handle.Close(); }
				m_DialogOpen = false;
				m_HeaderGroup.interactable = m_Manager.FindState<PlayState>() == null;
			}
		}

		/// <summary>独立したヘッダーScope（Priority 100）へ入る / 出る。Pageのscope（0）より優先される。</summary>
		public void ToggleHeader()
		{
			if (IsBusy || m_DialogOpen || !m_HeaderGroup.interactable) { return; }
			m_InputRouter.SetMode(UIInputMode.Navigation);
			UIFocus.NotifyInputMode(FocusInputMode.Navigation);
			if (m_Header.IsCurrent) { m_Header.Release(); }
			else { m_Header.Activate(); }
		}

		/// <summary>ヘッダーのボタン。どの状態から何を開くかは現在の葉Stateが決める。</summary>
		public void OpenHeader(string name)
		{
			Dispatch("Header/" + name, () =>
			{
				if (m_DialogOpen || !m_HeaderGroup.interactable) { return; }
				m_Header.Release();
				if (name == "Title")
				{
					Run(async () =>
					{
						if (await Confirm("Return to title?", "Progress stays in memory for this session.", destroyCancellationToken))
						{
							m_Tree.Clear(); m_Manager.GetTree(m_Tree);
							if (m_Tree.Count > 1) { m_Manager.GoTo<TitleState>(m_Tree[1].State, new ParameterHolder()); }
						}
					});
					return;
				}
				m_Manager.FindState<ShowcaseState>()?.OpenHeader(name);
			});
		}

		/// <summary>
		/// 焦点の優先度を見る診断。ヘッダーを操作中にした後、低Priorityの画面を作り直しても、ヘッダーが操作対象のまま残る。
		/// </summary>
		public void RecreateHome()
		{
			var home = m_Manager.FindState<HomeState>();
			if (home == null || IsBusy || m_DialogOpen || !home.IsLeaf) { return; }
			m_InputRouter.SetMode(UIInputMode.Navigation);
			UIFocus.NotifyInputMode(FocusInputMode.Navigation);
			m_Header.Activate();
			Run(async () =>
			{
				await Task.Delay(350, destroyCancellationToken);
				await home.Recreate();
				Log("FOCUS CHECK " + (m_Header.IsCurrent ? "header retained" : "header lost"));
			});
		}

		public string Describe()
		{
			// 切り分け用の値は先頭へ置く。末尾だと表示枠からはみ出て見えない。
			var text = new StringBuilder("DIAGNOSTICS");
			text.Append("\nSCOPE: ").Append(UIFocus.Current is FocusScope scope ? scope.name + (scope.CanFocus ? "" : " (canFocus=false)") : "none");
			text.Append("\nOCCUPIED: ").Append(UIFocus.IsNavigationOccupied ? "yes" : "no");
			text.Append("\nACTIONS: ").Append(m_InputRouter != null ? m_InputRouter.DescribeInputs() : "none");
			text.Append("\n\nGSS / LIVE TREE\n\n");
			m_Tree.Clear(); m_Manager.GetTree(m_Tree);
			foreach (var node in m_Tree) { text.AppendLine(node.ToString()); }
			text.Append("\nMAIN STACK\n");
			foreach (var handle in m_Main.GetStackSnapshot()) { text.AppendLine((handle.Page != null ? handle.Page.name : "Loading") + " / " + handle.State); }
			text.Append("\nFRONT: ").Append(m_Front.GetStackSnapshot().Count);
			text.Append("\nINPUT: ").Append(m_InputRouter != null ? m_InputRouter.Mode.ToString() : "none");
			text.Append("\nFADE: ").Append(m_ScreenFade.IsBusy ? "active" : "clear");
			text.Append("\nFOCUS: ").Append(UIFocus.Selected != null ? UIFocus.Selected.GetComponentInChildren<TMP_Text>()?.text ?? UIFocus.Selected.name : "none");
			text.Append("\nDATA: ").Append(PlayerName).Append(" / coins ").Append(Coins).Append(" / stage ").Append(Stage);
			text.Append("\nERRORS: ").Append(ErrorCount).Append("\n\nRECENT ACTIVITY\n");
			for (int i = Math.Max(0, m_Log.Count - 6); i < m_Log.Count; i++) { text.AppendLine(m_Log[i]); }
			return text.ToString();
		}

		public void OnStateEvent(in StateEvent stateEvent)
		{
			Log(stateEvent.Type + " " + stateEvent.State?.GetType().Name);
			if (stateEvent.Error != null && !(stateEvent.Error is OperationCanceledException)) { m_ScreenFade.Clear(); Report(stateEvent.Error); }
		}
		public void Log(string message) { m_Log.Add(message); if (m_Log.Count > 300) { m_Log.RemoveAt(0); } }
		void Report(Exception error)
		{
			// 破棄中の取消（Sceneのアンロードなど）は記録しない。
			if (m_Destroyed || error is OperationCanceledException) { return; }
			ErrorCount++; Log("ERROR " + error.Message); Debug.LogException(error);
		}
	}

	sealed class ShowcaseStateFactory : IStateFactory
	{
		readonly ShowcaseApp m_App;
		public ShowcaseStateFactory(ShowcaseApp app) { m_App = app; }
		public (T State, IDisposable Value) Create<T>(in StateCreationContext context, IDisposable parent) where T : GameState
		{
			T state = Activator.CreateInstance<T>();
			((ShowcaseState)(object)state).App = m_App;
			return (state, null);
		}
	}
}
