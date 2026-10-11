# UIControl Showcase

`Scenes/Showcase.unity`を開いてPlayします。UIControlの機能を一通り触れる見本アプリで、UIControl、GameStateStructure、UVMBinding、LitMotion、Input Systemを使います。素材は色と文字だけで、データはメモリ上の仮の値です。

## 操作

| 入力 | 動作 |
| --- | --- |
| マウス | 通常のuGUIのボタンとスクロール |
| 矢印キー / D-pad | Navigation（選択の移動） |
| 左スティック | 仮想マウス。選択中のボタンの位置から始まる |
| 右スティック | 仮想マウスの下のScrollRectをスクロール |
| Enter / South | 決定。仮想マウス中は左クリック |
| Esc / East | 露出しているBackボタンを`BackButton`経由で押す |
| F1 / Start | 独立ヘッダー（Priority 100）へ焦点を出し入れ |
| Q, E / LB, RB | 設定画面のタブを`PreviousButton` / `NextButton`経由で切り替え |

右側のパネルにGSSの状態木、UIStackの内容、現在のScopeと選択、直近の操作が出ます。

## 見られる機能

| 画面 | 機能 |
| --- | --- |
| TITLE | 1つのGSS状態の中で`UIStack.Replace`によるサブページ切替（CONTINUE / SETTINGS / CREDITS） |
| HOME | 独立ヘッダーとの共存。`REOPEN HOME (FOCUS CHECK)` はヘッダーを操作中にしてから低Priorityの画面を作り直し、ヘッダーが操作対象のまま残ることを見る |
| INVENTORY / HELP | ScrollRectの行（IViewModel）、`SelectionScroll`、閉じた後に前の画面の選択が戻る（`Restore Last`） |
| SHOP | Front Stackの確認Dialog（FocusScope Priority 200）。Dialogの間はMainとヘッダーを操作できない |
| SETTINGS | `TabController`と4つの独立FocusScope、L / Rによる切替、入力欄の編集中は矢印が文字編集へ行く |
| PLAY STAGE | Backdrop Hiddenで後ろを隠す。ViewModelは接続されたまま |
| RESULT → REWARD CARDS | Hidden + `TransitionOrder.BackgroundFirst`。結果が先に隠れてからカードが開く |
| 画面遷移全般 | 全画面フェード（Raycast遮断 + `UIFocus.OccupyNavigation()`）、`PageStateDriver`によるOpen / Close / Covered / Hidden、ボタンの`SelectableStateDriver` |

## 構成

- `Prefabs/Showcase.prefab`: Canvas、2つのUIStack（Main / Front）、EventSystem、GameStateManager、ヘッダー、フェード。Rootに当たるComponentは無く、FocusScopeは自分で`UIFocus`へ登録します。
- `MenuPage` / `HUDPage` / `ListPage` / `SettingsPage` / `DialogPage` / `CardsPage`: PageのPrefab。`ViewBindStep`でIViewModelを受け取ります。
- `ActionRow`: 行のPrefab。`EventButton`と`TextMeshProBinder`、`SelectableStateDriver`。
- EventSystemに`InputSystemUIInputRouter`、`PointerSelection`、`VirtualMouseCursor`。カーソル画像は`PointerFollower`で追従します。
- 遷移の演出は`ClipAnimationAdapter`、ボタンは`LitMotionAnimationAdapter`。

`UIControl > Samples > Create Showcase`で再生成できます。このフォルダーのPrefab / Scene / Animation / InputActionsを上書きするので、保存済みのSceneから実行してください。

## 業務側に置いているもの

どの画面からどこへ行けるか（状態木）、ヘッダーのボタンが何を開くか、フェードの遮断、Dialog中の操作制限は`ShowcaseApp`と各Stateが決めます。ライブラリはPageの開閉・演出・選択の調停・入力部品だけを担当します。
