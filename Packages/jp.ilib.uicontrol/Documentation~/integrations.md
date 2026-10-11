# 連携

どちらも本体の`Integrations/*`にあり、依存パッケージがあるときだけコンパイルされます。

## UVMBinding

画面へ渡すのはIViewModelです。Pageの型ごとのModel登録やViewの派生クラスは要りません。

```text
Page Prefab root
  Page（Preparation: [0] ViewBindStep, [1] 追加取得Step, [2] ViewRefreshStep）
  ViewBindStep（View → 同じPage配下のView）
```

```csharp
using UIControl.UVMBinding;

PageHandle page = stack.Push(model.CreateUIParameter("UI/Settings"));
```

- ViewBindStepはPageと同じルートに置き、Page.Preparationへ登録します。準備中にView.Prepare → ViewModel設定 → TryUpdateを行い、回収時に接続を外します。VMはDisposeしません。
- VMと画面が一対一なら、VMの型に`[UIPageFromVM("UI/Settings")]`を付けて`model.CreateUIParameter()`と書けます。属性は継承されません。
- Bindingで決まったキーで翻訳やAtlasを取得するときは、ViewBindStepの後に取得Step、必要ならViewRefreshStepを置きます。

| Binder | 動作 |
| --- | --- |
| NamedStateBinder | stringの状態名をNamedStatePlayerへ反映する。準備中は終値、表示後は再生 |
| FocusSelectionBinder | boolで選択を要求し、選択・解除を書き戻す。NavigationNodeと同じGameObjectに置く。選べない時の要求は保留しない |
| AnimatedCollectionBinder | CollectionBinderの代わりに使い、新しい行のShowと、削除前のHide完了待ちを行う |

AnimatedCollectionBinderの行PrefabのルートにはView、ListItemStateDriver、NavigationNodeを置き、`Item / Hidden`・`Item / Show`・`Item / Hide`を登録します。LayoutGroup内で消える行の位置を保つには、行ルートにLayoutElementを置きます（Hide中だけignoreLayoutをtrueにします）。

## GameStateStructure

Push / Replaceの直後、Openedを待つ前に`AttachToState`を呼ぶと、ステートの終了で画面が閉じます。

```csharp
using UIControl.GSS;

PageHandle page = stack.Push(UIParameter.WithoutData("UI/Settings")).AttachToState(Context);
OpenResult opened = await page.Opened;
```

通常の終了ではCloseを待ち、Closeの失敗をGSSへ伝えます。終了の取消や強制終了ではForceReleaseします。GSSのManage対象はExitTaskより先に破棄されるので、Close中に必要なVMやAssetは利用側で保持します。UVMBindingと併用する場合も、`model.CreateUIParameter`で開いたHandleへAttachToStateを呼ぶだけです。

### ステートをまたいで画面を差し替える

前の画面を次の画面の準備が済むまで見せておくには、**次のステートの`OnInitialize`で`Replace`し、自分のContextへAttach**します。GSSのGoTo / Pushは「次のOnInitialize → 前のステートの終了 → 次のOnEnter」の順に進むためです。

```csharp
protected override async Task OnInitialize()
{
	await stack.Replace(UIParameter.WithoutData("UI/Day")).AttachToState(Context).Opened;
}
```

前のステートの終了で走るCloseは`Replaced`として正常に終わります。新しい画面が失敗した場合は、前の画面が通常どおり閉じます。`OnEnter`で開くと前の画面は閉じた後なので、一度画面が空になります。

親が`IProcess`の子を`RunXxx`で順に待つ流れでは、前の子が終了してから次の子が作られます。この場合は画面の終了を親のContextに持たせ、子は`Replace`だけを行います。

```csharp
// 親のステート。子はReplaceで得たHandleを渡す。
PageExitTask m_Current;

public void Show(PageHandle page)
{
	if (m_Current != null) { Context.UnregisterExitTask(m_Current); }
	m_Current = Context.RegisterExitTask(new PageExitTask(page));
}
```
