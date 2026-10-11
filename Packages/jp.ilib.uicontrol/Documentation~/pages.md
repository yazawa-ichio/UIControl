# 画面

## 最初の画面

```text
Scene
├─ EventSystem + UI Input Module
├─ Canvas
│  ├─ OpenSettingsButton
│  └─ PageDisplay (RectTransform、Stretch)
└─ MainStack (UIStack、Display Root → PageDisplay)

Assets/Resources/UI/Settings.prefab
Settings (RectTransform + Page + CanvasGroup)
└─ Panel
   └─ CloseButton (Button)
```

`Page.Input Group`へルートのCanvasGroupを指定します。既定LoaderはResourcesを読むので、Pathは`UI/Settings`です。Display Rootを省略するとUIStack自身が表示先になります。

```csharp
public sealed class SettingsLauncher : MonoBehaviour
{
	[SerializeField] private UIStack m_Stack;

	// OpenSettingsButtonのOn Click()へ接続する。
	public async void Open()
	{
		PageHandle page = m_Stack.Push(UIParameter.WithoutData("UI/Settings"));
		OpenResult result = await page.Opened;
		if (result.Exception != null) { Debug.LogException(result.Exception); }
	}
}

public sealed class SettingsActions : MonoBehaviour
{
	// Settingsに付け、CloseButtonのOn Click()へ接続する。
	public async void Close()
	{
		PageHandle handle = GetComponent<Page>().Handle;
		if (handle == null) { return; }
		CloseResult result = await handle.Close();
		if (result.Exception != null) { Debug.LogException(result.Exception); }
	}
}
```

## 選択と演出を足したPrefab

```text
Settings (Page + PageStateDriver + NamedStatePlayer)
├─ InputGroup (CanvasGroup)
│  └─ Content (FocusScope)
│     ├─ ApplyButton (Button)
│     └─ CloseButton (Button)
└─ Animations
   ├─ Open (ClipAnimationAdapter / LitMotionAnimationAdapter)
   └─ Close
```

| 設定先 | 参照 |
| --- | --- |
| Page / Input Group | InputGroupのCanvasGroup（表示・入力。フェードにも使える） |
| Page / Visual | PageStateDriver。設定するとPageはalphaを書かない |
| Page / Focus | Open時に有効化するFocusScope |
| Page / Preparation | 表示前に順に実行するPrepareStep |
| Page / Backdrop、Focus On Open、Transition Order | 既定はInherit（UIStackの値を使う） |

HUDやヘッダーはUIStackに載せず、FocusScopeだけを置いて`Activate()` / `Release()`します。Priorityを通常画面より大きくすれば、操作中に低優先度の画面が開いても選択を奪われません。別Stackの表示順はCanvasの順序で決まります。

## Push・Replace・Close

呼び出しはメインスレッドから行います。受付条件の不正（nullのパラメーター、停止中のStack）は例外になり、それ以外の結果は`Opened` / `Closed`のStatusで返ります。

| API | 動作 |
| --- | --- |
| `stack.Push(parameter, options, cancellation)` | 画面を積む |
| `stack.Replace(parameter, options, cancellation)` | 実行時の一番上を置き換える。新画面の準備が成功するまで旧画面を残し、失敗したら旧画面を保持する |
| `handle.Close()` | その要求を閉じる。上端以外も閉じられる |
| `stack.Pop()` / `stack.CloseAll()` | 上端を閉じる / すべて逆順に閉じる |
| `handle.ForceRelease()` / `stack.ForceReleaseAll()` | 演出を待たず同期で回収する |
| `stack.SetVisible(bool)` | 生成済み画面を保持したまま表示を切り替える |

- **進行中の演出へ割り込みません。** Open中の`Close()`はOpen完了後に閉じます。
- ForceRelease、Destroy、SetVisibleによる演出の取消は失敗ではなく、最終状態を即時適用します。
- PushへのCancellationTokenはOpen完了までの取消です。Opened後は`Close()`で閉じます。
- 表示中のPageを直接Destroyすると`Faulted`になります。UIStackのDestroyは全要求をForceReleaseします。

| Status | 意味 |
| --- | --- |
| `Opened` | Open完了（Open中にCloseされても、開き切ってから閉じる） |
| `Cancelled` / `ClosedBeforeOpen` | Open前に取消 / ロード・準備中にClose |
| `Closed` | 通常の回収 |
| `Replaced` / `Forced` / `Faulted` | 置き換え / 強制回収 / 失敗（`Exception`を確認） |
| `Empty` | Pop時に対象なし |

## Backdropと演出の順序

Backdropは開くPageが同じStackの後ろへ与える指定です。複数の指定はHidden > Covered > KeepVisibleで合成します。

| Mode | 背面 | Range | 対象 |
| --- | --- | --- | --- |
| `KeepVisible` | 表示を保つ | `Previous` | 直前の一枚 |
| `Covered` | 表示を保ち、PageStateDriverへCoveredを通知 | `AllBehind` | 後ろの全画面 |
| `Hidden` | Hidden演出後に非表示 | `None` | なし |

| TransitionOrder | Replace | Backdropが効くPush | Close |
| --- | --- | --- | --- |
| `ForegroundFirst`（既定） | 新Open → 旧Close | 新Open → 背面が退く | 自Close → 背面が戻る |
| `BackgroundFirst` | 旧Close → 新Open | 背面が退く → 新Open | 背面が戻る → 自Close |
| `Concurrent` | 同時 | 同時 | 同時 |

コードからの上書きは不変の`OpenOptions`で行います。

```csharp
OpenOptions options = OpenOptions.Default
	.WithBackdrop(BackdropMode.Hidden, BackdropRange.AllBehind)
	.WithTransitionOrder(TransitionOrder.BackgroundFirst);
stack.Push(UIParameter.WithoutData("UI/Reward"), options);
```

同じStackで入力できるのは上端のLiveなPageだけです。遷移中も入力できません。PageはInput Groupの`blocksRaycasts`だけを切り替え、`interactable`は触らないので、遷移でボタンがDisabled色になりません。HiddenでもGameObjectは無効化されないので、更新を止める処理は`Page.RenderingChanged`で判断します。

## ロードと準備

```text
LoadAsync（非アクティブな親の下へ生成）
→ UIParameterの初期接続 → Options.Setup
→ Page.Preparationを順にawait → Layout更新 → Open演出 → Opened
→ Close / 取消 / 強制回収 → OnReleaseの登録を逆順に実行 → PageInstanceを返却
```

PageのAwake / OnEnableの時点では初期接続や準備が済んでいません。データが要る初期化はUIParameterかPrepareStepへ置きます。

```csharp
// データ接続。コールバックはPreparationより前に一回呼ばれる。
stack.Push(UIParameter.Create("UI/Reward", (page, context) => page.GetComponent<RewardView>().Show(reward)));
```

取得元はパラメーターの第3引数`IPageLoader`で要求ごとに指定でき、省略時は`PageLoading.DefaultLoader`です。`DefaultPageLoader`は登録済みのPrefabを先に探し、無ければResourcesから読みます。

```csharp
var loader = new DefaultPageLoader();
loader.Register("UI/Settings", settingsPrefab);
PageLoading.DefaultLoader = loader;   // 最初のPushより前に。Play開始時に標準値へ戻る
```

独自Loaderは`LoadAsync(path, inactiveParent, cancellation)`で`inactiveParent`の下へPageを生成して`PageInstance(page, release)`を返し、`PreloadAsync`で保持を表すIDisposableを返します。返却処理は一度だけ呼ばれます。Poolは付属しません。

非同期の取得は`PrepareStep`を継承したComponentで行い、Page.Preparationへ登録します。

```csharp
public sealed class LoadCaptionStep : PrepareStep
{
	[SerializeField] private TMPro.TMP_Text m_Label;
	public string Key { get; set; }

	public override async Task ExecuteAsync(PrepareContext context)
	{
		ResourceRequest request = Resources.LoadAsync<TextAsset>(Key);
		while (!request.isDone) { context.ThrowIfEnded(); await Task.Yield(); }
		context.ThrowIfEnded();
		m_Label.text = ((TextAsset)request.asset).text;
	}
}
```

専用に取得した資源は`context.Own(resource)`、購読解除などは`context.OnRelease(...)`へ登録すると回収時に解放されます。渡されたViewModelや共有サービスはDisposeしません。

## 遷移中の遮断

全画面の入力遮断はアプリ側で行います。UIStackの通知を受けて、Raycastを受ける透明な板を最前面に出し、必要なら`UIFocus.OccupyNavigation()`でPadの選択移動も止めます。EventSystemやInput Moduleを無効にする必要はありません。

| event | 内容 |
| --- | --- |
| `TransitionChanged(bool)` | 遷移演出中か（`IsTransitioning`） |
| `ActivityChanged(bool)` | ロード・準備を含む要求が残るか（`HasPendingOperations`） |
| `Error(Exception)` | 内部の失敗。未購読ならログへ出す |

```csharp
public sealed class TransitionBlocker : MonoBehaviour
{
	[SerializeField] private UIStack m_Stack;
	[SerializeField] private GameObject m_Board;   // Raycast Target ONの透明なImage
	private IDisposable m_Occupancy;

	private void OnEnable() { m_Stack.TransitionChanged += Apply; Apply(m_Stack.IsTransitioning); }
	private void OnDisable() { m_Stack.TransitionChanged -= Apply; Apply(false); }

	private void Apply(bool block)
	{
		m_Board.SetActive(block);
		if (block && m_Occupancy == null) { m_Occupancy = UIFocus.OccupyNavigation(); }
		else if (!block && m_Occupancy != null) { m_Occupancy.Dispose(); m_Occupancy = null; UIFocus.Reconcile(); }
	}
}
```

## 回答を待つQueue

UIStackのFIFOはOpenが完了すると次の操作へ進むので、回答やCloseまでは待ちません。ダイアログを1件ずつ回答まで待つ場合は、Sample **Queued Pages**の`RequestQueue`を使うか参考にします。
