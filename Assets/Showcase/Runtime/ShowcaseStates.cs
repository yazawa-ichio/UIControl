using System;
using System.Threading;
using System.Threading.Tasks;
using GameStateStructure;

namespace UIControl.Showcase
{
	/// <summary>
	/// 見本の状態木。Root → Title → Home → 各画面。Pageの開閉はGSSのContextへ紐づけ、状態の終了で自動的に閉じる。
	/// 見せたい機能ごとに画面を1つ置く:
	/// Title（Replaceによるサブページ切替）、Home（独立ヘッダーとの共存、作り直しても選択が戻る）、
	/// Inventory（スクロールするリストとSelectionScroll）、Shop（Front StackのDialogで回答を待つ）、
	/// Settings（TabControllerとL / R、入力欄）、Play（Backdrop Hidden、フェード）、Result（Hidden + BackgroundFirstの報酬画面）。
	/// </summary>
	public abstract partial class ShowcaseState : GameState
	{
		internal ShowcaseApp App { get; set; }
		protected PageHandle Page { get; private set; }
		protected ShowcasePageModel Model { get; private set; }
		bool m_Working;
		protected CancellationToken Token => Context.DisposeCancellationToken;

		protected async Task Show(ShowcasePageModel model, string prefab = "MenuPage", bool replace = false,
			BackdropMode backdrop = BackdropMode.Covered, TransitionOrder order = TransitionOrder.Inherit)
		{
			Model = model;
			Page = App.Open(model, prefab, replace, backdrop: backdrop, order: order, state: Context);
			var result = await Page.Opened;
			// 状態の終了やアプリの破棄でOpenが取り消された場合は失敗ではない。呼び出し側（App.Run / GSS）は取消として扱う。
			if (result.Status == OpenStatus.Cancelled || result.Status == OpenStatus.Forced || result.Status == OpenStatus.ClosedBeforeOpen) { throw new OperationCanceledException(); }
			if (result.Status != OpenStatus.Opened) { throw result.Exception ?? new InvalidOperationException("Page open failed: " + result.Status); }
		}

		protected void Async(Func<Task> action)
		{
			if (m_Working) { return; }
			m_Working = true;
			App.Run(async () => { try { await action(); } finally { m_Working = false; } });
		}
		protected void Push<T>(ParameterHolder args = null) where T : GameState { Manager.Push<T>(this, args ?? new ParameterHolder()); }
		protected void GoTo<T>(ParameterHolder args = null) where T : GameState { Manager.GoTo<T>(this, args ?? new ParameterHolder()); }
		protected Task Process<T>(ParameterHolder args = null) where T : GameState, IProcess { return Manager.RunProcess<T>(this, args ?? new ParameterHolder(), null, Token); }
		protected static ParameterHolder Arg(string name, object value) { var args = new ParameterHolder(); args.Set(name, value); return args; }
		public virtual void OpenHeader(string name) { Manager.FindState<HeaderHostState>()?.OpenHeader(name); }

		/// <summary>設定画面の内容。4つのタブは独立したFocusScopeで、L / R（Q / E）はPreviousButton / NextButtonで切り替える。</summary>
		protected ShowcasePageModel Settings(string title, Action back)
		{
			var model = new ShowcasePageModel(App, title, "Four tabs, each an independent FocusScope. Q / E or LB / RB switch tabs through typed InputButtons.");
			model.Back(back, "APPLY & BACK");
			int volume = 50;
			bool mirror = false;
			string quality = "High";
			model.Set("Detail", "Volume 50% | Mirror OFF | Quality High | Player " + App.PlayerName);
			void Refresh() { model.Set("Detail", "Volume " + volume + "% | Mirror " + (mirror ? "ON" : "OFF") + " | Quality " + quality + " | Player " + App.PlayerName); }
			model.On("Volume", () => { volume = (volume + 10) % 110; Refresh(); });
			model.On("Mirror", () => { mirror = !mirror; Refresh(); });
			model.On("Quality", () => { quality = quality == "High" ? "Low" : "High"; Refresh(); });
			model.On("Help", () => Async(() => Process<HelpDialogState>()));
			model.On("Rebind", () => Async(() => App.Confirm("Key binding", "Rebinding is a placeholder dialog in this showcase.", Token)));
			model.On("Rename", () => Async(async () => { if (await App.Confirm("Player name", "Type a name. The input field keeps its selection while editing.", Token, input: true)) { Refresh(); } }));
			model.On("Defaults", () => Async(async () =>
			{
				if (await App.Confirm("Reset settings?", "Reset in-memory settings to defaults.", Token))
				{
					volume = 50; mirror = false; quality = "High"; Refresh();
				}
			}));
			return model;
		}
	}

	/// <summary>ヘッダー（Settings / Help / Title）からの遷移を受ける状態。別Stackからの要求をどう解決するかは利用側が決める。</summary>
	public abstract partial class HeaderHostState : ShowcaseState
	{
		public override void OpenHeader(string name)
		{
			if (this is TitleState title)
			{
				App.Run(async () =>
				{
					while (!IsLeaf) { Manager.Pop(Manager.FindState<ShowcaseState>()); while (Manager.IsBusy) { await Task.Yield(); } }
					title.Change(name);
				});
				return;
			}
			// GSS Push は同じ車線の既存の子を終了する。重ね開きのSettings → Helpは兄弟の置き換え（GoTo）にする。
			bool replace = this is HeaderOverlayState;
			switch (name)
			{
				case "Settings": if (replace) { GoTo<SettingsState>(); } else { Push<SettingsState>(); } break;
				case "Help": if (replace) { GoTo<HelpState>(); } else { Push<HelpState>(); } break;
			}
		}
	}

	[GoTo(typeof(SettingsState)), GoTo(typeof(HelpState))]
	public abstract partial class HeaderOverlayState : HeaderHostState { }

	[Push(typeof(TitleState))]
	public partial class RootState : ShowcaseState
	{
		protected override void OnEnter() { Push<TitleState>(); }
	}

	/// <summary>タイトル。サブページはGSSの状態を増やさず、同じStateの中でUIStack.Replaceで切り替える。</summary>
	[GoTo(typeof(HomeState)), Push(typeof(HelpDialogState))]
	[ScreenFade]
	public partial class TitleState : HeaderHostState
	{
		public string CurrentPage { get; private set; } = "Top";
		protected override Task OnPreEnter() { return ChangePage("Top", false); }
		public void Change(string page) { Async(() => ChangePage(page, true)); }

		Task ChangePage(string page, bool replace)
		{
			CurrentPage = page;
			ShowcasePageModel model;
			string prefab = "MenuPage";
			switch (page)
			{
				case "Load":
					model = new ShowcasePageModel(App, "TITLE / CONTINUE", "Choose a slot. Rows are IViewModels in a ScrollRect; SelectionScroll keeps the selected row visible.");
					model.Items(12, i => "Save slot " + (i + 1).ToString("00") + "   stage " + (i + 1), i => { App.Stage = i + 1; GoTo<HomeState>(); });
					model.Set("Detail", "Selecting a slot enters HomeState with a screen fade.");
					model.Back(() => Change("Top"));
					prefab = "ListPage";
					break;
				case "Settings":
					model = Settings("TITLE / SETTINGS", () => Change("Top"));
					prefab = "SettingsPage";
					break;
				case "Credits":
					model = new ShowcasePageModel(App, "TITLE / CREDITS", "UIControl showcase\n\nUIStack + FocusScope + InputButton + VirtualMouseCursor\nGameStateStructure + UVMBinding\nAnimationClip + LitMotion");
					model.Back(() => Change("Top"));
					break;
				default:
					CurrentPage = "Top";
					model = new ShowcasePageModel(App, "TITLE", "Every subpage below is a UIStack.Replace inside one GSS state.\nBack (East / Esc) reaches the visible Back button through BackButton.");
					model.Actions(("NEW GAME", () => { App.Stage = 1; GoTo<HomeState>(); }), ("CONTINUE", () => Change("Load")),
						("SETTINGS", () => Change("Settings")), ("CREDITS", () => Change("Credits")),
						("HOW TO PLAY", () => Async(() => Process<HelpDialogState>())),
						("QUIT", () => Async(() => App.Confirm("Quit?", "The showcase stays open so that you can keep exploring.", Token))));
					break;
			}
			return Show(model, prefab, replace);
		}
	}

	/// <summary>ホーム。独立ヘッダー（Priority 100）と共存し、作り直しても選択とヘッダーの操作権が保たれる。</summary>
	[GoTo(typeof(TitleState)), GoTo(typeof(ReloadHomeState))]
	[Push(typeof(InventoryState)), Push(typeof(ShopState)), Push(typeof(PlayState)), Push(typeof(ResultState))]
	[Push(typeof(SettingsState)), Push(typeof(HelpState)), Push(typeof(HelpDialogState))]
	[ScreenFade]
	public partial class HomeState : HeaderHostState
	{
		protected override async Task OnPreEnter()
		{
			await Task.Delay(150, Token);
			await Recreate();
		}
		public Task Recreate()
		{
			var model = new ShowcasePageModel(App, "HOME", "Pages below push onto the Main stack. F1 / Start moves focus to the independent header.");
			model.Actions(("INVENTORY", () => Push<InventoryState>()), ("SHOP", () => Push<ShopState>()), ("PLAY STAGE", () => Push<PlayState>()),
				("SETTINGS", () => Push<SettingsState>()), ("HELP", () => Push<HelpState>()), ("TUTORIAL", () => Async(() => Process<HelpDialogState>())),
				("RESULT DEMO", () => Push<ResultState>()), ("NEXT STAGE", () => { App.Stage++; GoTo<ReloadHomeState>(); }),
				("TITLE", () => Async(async () => { if (await App.Confirm("Return to title?", "Progress stays in memory for this session.", Token)) { GoTo<TitleState>(); } })));
			model.Set("Detail", Status());
			return Show(model, "HUDPage", Page != null && Page.Page != null);
		}
		string Status() { return "PLAYER " + App.PlayerName + "  /  STAGE " + App.Stage + "  /  COINS " + App.Coins + "  /  ITEM " + App.SelectedItem; }
		protected override void OnPopChild(GameState state, PopReason reason)
		{
			if (reason == PopReason.ParentExit) { return; }
			Model?.Set("Detail", Status());
		}
	}

	/// <summary>GoToで同じ画面を作り直す経由State。フェードの中でHomeが入れ替わる。</summary>
	[GoTo(typeof(HomeState))]
	[ScreenFade]
	public partial class ReloadHomeState : ShowcaseState
	{
		protected override void OnEnter() { GoTo<HomeState>(); }
	}

	/// <summary>スクロールするリスト。行を選ぶと詳細が変わり、閉じると元の画面の選択が戻る。</summary>
	public partial class InventoryState : ShowcaseState
	{
		protected override Task OnPreEnter()
		{
			var model = new ShowcasePageModel(App, "INVENTORY", "Select a row, then USE. Closing this page restores the previous selection on HOME.");
			model.Items(30, i => "Item " + (i + 1).ToString("00"), i => { App.SelectedItem = "Item " + (i + 1).ToString("00"); model.Set("Detail", App.SelectedItem + "\nDescription updated through the IViewModel."); });
			model.Set("Detail", "Choose an entry from the scrollable list.");
			model.Actions(("USE SELECTED", () => model.Set("Detail", App.SelectedItem + " used (placeholder).")), ("SORT", () => model.Set("Detail", "Sorted (placeholder).")));
			model.Back(Pop);
			return Show(model, "ListPage");
		}
	}

	/// <summary>購入確認をFront StackのDialogで行う。Dialogの間、Mainとヘッダーは操作できない。</summary>
	public partial class ShopState : ShowcaseState
	{
		protected override Task OnPreEnter()
		{
			var model = new ShowcasePageModel(App, "SHOP", "Buying opens a confirmation on the Front stack. The dialog's FocusScope has Priority 200.");
			model.Items(12, i => "Goods " + (i + 1).ToString("00") + "   " + (i + 1) * 25 + " coins", i => Buy(i, model));
			model.Set("Detail", "Coins " + App.Coins);
			model.Actions(("ADD 100 COINS", () => { App.Coins += 100; model.Set("Detail", "Coins " + App.Coins); }));
			model.Back(Pop);
			return Show(model, "ListPage");
		}
		void Buy(int index, ShowcasePageModel model)
		{
			int price = (index + 1) * 25;
			Async(async () =>
			{
				if (App.Coins < price) { model.Set("Detail", "Not enough coins for Goods " + (index + 1).ToString("00") + "."); return; }
				if (!await App.Confirm("Buy Goods " + (index + 1).ToString("00") + "?", price + " coins. Confirm or cancel; the list keeps its selection afterwards.", Token)) { return; }
				App.Coins -= price;
				App.SelectedItem = "Goods " + (index + 1).ToString("00");
				model.Set("Detail", "Bought " + App.SelectedItem + ". Coins " + App.Coins);
			});
		}
	}

	[Push(typeof(HelpDialogState))]
	[ScreenFade("OutQuick")]
	public partial class SettingsState : HeaderOverlayState
	{
		protected override Task OnPreEnter() { return Show(Settings("SETTINGS", Pop), "SettingsPage"); }
	}

	[Push(typeof(HelpDialogState))]
	[ScreenFade("OutQuick")]
	public partial class HelpState : HeaderOverlayState
	{
		protected override Task OnPreEnter()
		{
			var model = new ShowcasePageModel(App, "HELP", "Settings and Help opened from the header are sibling GSS states: one replaces the other.");
			model.Items(15, i => "Help topic " + (i + 1), i => model.Set("Detail", "Help topic " + (i + 1) + " (placeholder)."));
			model.Actions(("OPEN TUTORIAL", () => Async(() => Process<HelpDialogState>())));
			model.Back(Pop);
			return Show(model, "ListPage");
		}
	}

	/// <summary>数ページの説明Dialog。IProcessとして呼び出し側が完了を待つ。</summary>
	public partial class HelpDialogState : ShowcaseState, IProcess
	{
		readonly TaskCompletionSource<bool> m_Done = new TaskCompletionSource<bool>();
		protected override Task OnPreEnter()
		{
			int index = 1;
			var model = new ShowcasePageModel(App, "TUTORIAL", "Page 1 / 3: D-pad moves, South submits, East goes back.");
			model.Actions(("NEXT PAGE", () =>
			{
				if (++index > 3) { m_Done.TrySetResult(true); return; }
				model.Set("Description", index == 2 ? "Page 2 / 3: the left stick moves a virtual mouse; it starts from the selected button." : "Page 3 / 3: F1 / Start focuses the header; LB / RB switch tabs in Settings.");
			}));
			model.Back(() => m_Done.TrySetResult(true), "CLOSE");
			return Show(model, "DialogPage");
		}
		public async Task Run(CancellationToken ct) { using (ct.Register(() => m_Done.TrySetCanceled(ct))) { await m_Done.Task; } }
	}

	/// <summary>プレイ中の見立て。Backdrop Hiddenで後ろを隠し、終了で結果画面へ。</summary>
	[GoTo(typeof(PlayState)), Push(typeof(ResultState)), Push(typeof(SettingsState))]
	[ScreenFade]
	public partial class PlayState : ShowcaseState
	{
		protected override Task OnPreEnter()
		{
			var model = new ShowcasePageModel(App, "STAGE " + App.Stage, "Gameplay is represented by this dialog. The pages behind it are Hidden, and their ViewModels stay bound.");
			model.Actions(("FINISH STAGE", () => Push<ResultState>()), ("PAUSE / SETTINGS", () => Push<SettingsState>()),
				("RETRY", () => Async(async () => { if (await App.Confirm("Retry?", "Restart the stage placeholder.", Token)) { GoTo<PlayState>(); } })));
			model.Back(() => Async(async () => { if (await App.Confirm("Give up?", "Return to HOME.", Token)) { Pop(); } }), "GIVE UP");
			return Show(model, "DialogPage", backdrop: BackdropMode.Hidden);
		}
		protected override void OnPopChild(GameState state, PopReason reason)
		{
			if (reason == PopReason.ParentExit || reason == PopReason.GoTo) { return; }
			if (state is ResultState) { Pop(); }
		}
	}

	/// <summary>結果と報酬。報酬のCardsPageはBackdrop Hidden + BackgroundFirstで、結果が先に隠れてから開く。</summary>
	[Push(typeof(HelpDialogState))]
	public partial class ResultState : ShowcaseState
	{
		PageHandle m_Cards;
		bool m_RewardDone;
		protected override Task OnPreEnter()
		{
			var model = new ShowcasePageModel(App, "RESULT", "STAGE " + App.Stage + "  /  RANK A  /  SCORE 842000\nOpen the reward cards to see Hidden + BackgroundFirst.");
			model.Actions(("COLLECT REWARD", () => Async(Cards)), ("RESULT DETAIL", () => model.Set("Detail", "Perfect 180 / Great 42 / Miss 3 (placeholder)")),
				("REWARD HELP", () => Async(() => Process<HelpDialogState>())));
			model.Back(() => { if (m_RewardDone) { Pop(); } else { Async(Cards); } }, "CONTINUE");
			return Show(model, "MenuPage", backdrop: BackdropMode.Hidden);
		}
		async Task Cards()
		{
			if (m_RewardDone) { Model.Set("Detail", "Reward already collected. CONTINUE returns to HOME."); return; }
			var completion = new TaskCompletionSource<bool>();
			var selected = new System.Collections.Generic.HashSet<int>();
			var model = new ShowcasePageModel(App, "REWARD CARDS", "Pick three cards, then confirm. This page opens BackgroundFirst: the result hides before the cards appear.");
			model.Items(9, i => "Card " + (i + 1), i =>
			{
				if (!selected.Remove(i) && selected.Count < 3) { selected.Add(i); }
				model.Set("Detail", "Selected " + selected.Count + " / 3\nCards: " + string.Join(", ", selected));
			});
			model.Actions(("CONFIRM THREE CARDS", () => { if (selected.Count == 3) { completion.TrySetResult(true); } else { model.Set("Detail", "Select exactly three cards first."); } }));
			model.Back(() => completion.TrySetResult(false), "BACK TO RESULT");
			m_Cards = App.Open(model, "CardsPage", backdrop: BackdropMode.Hidden, order: TransitionOrder.BackgroundFirst, state: Context);
			using var cancel = Token.Register(() => completion.TrySetCanceled(Token));
			try
			{
				await m_Cards.Opened;
				if (await completion.Task)
				{
					m_RewardDone = true;
					App.Coins += 50;
					Model.Set("Detail", "Reward collected: +50 coins. CONTINUE returns to HOME.");
				}
			}
			finally { await m_Cards.Close(); m_Cards = null; }
		}
	}
}
