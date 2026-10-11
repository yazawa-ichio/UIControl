# 選択と入力

## FocusScope

1. 選択対象をまとめるGameObjectへFocusScopeを付けます。配下のSelectableは自動で候補になります（`Collect Selectables`、既定ON）。外したい配下は`Exclude`へ指定します。
2. 最初に選ぶ対象を`Initial`、選べない時の予備を`Fallbacks`へ指定します。未設定ならHierarchy順で最初の候補です。
3. Page内ならPage.Focusへ指定します。PageのOpenで有効化されます。独立したHUDでは`scope.Activate()`を呼びます。

FocusScopeは登録されただけでは操作対象になりません。`Activate()` / `RequestSelect(selectable)` / `RequestRestore()`で有効化し、`Release()`で外します。有効なScopeのうちPriorityが大きいもの、同値なら後から有効化したものが操作対象です。`Activate()`のtrueは受理の意味で、低Priorityなら現在の対象が残ります。

再び操作対象になった時は、`Restore Last`（既定ON）なら前回の選択、続いてInitial → Fallbacks → Hierarchy順の候補を選びます。Push型の画面を閉じると、前の画面の選択が戻ります。画面を作り直した場合は履歴を引き継ぎません。

遷移中など一時的に操作できない間も所属は失わず、操作できるようになると同じ選択へ戻ります。管理中のSelectableは標準のNavigationを一時的にNoneにし、管理を離れると戻します。

## UIFocus

選択の調停の入口（static）です。EventSystemの選択を書くのはここだけです。

| メンバー | 用途 |
| --- | --- |
| `Current` / `Selected` | 現在の操作対象Scopeと選択 |
| `NotifyInputMode` | Pointer / Navigationの通知。Pointer中は自動選択せず、Navigationへ戻ると復元する |
| `OccupyNavigation()` | 選択の移動と復元を止める。Drag中や遮断中に使う |
| `TrySelectNearest(position, direction)` | 未選択時、画面座標に近い候補から選択を始める |
| `Forget(rowRoot)` | 行の再利用前に、その配下の選択と履歴を忘れる |

毎フレームの再構築はしません。Scopeの有効化・解除、Pageの入力可否の変化、Selectableの有効化・無効化、`Refresh()`で整合を取ります。動的に生成する行にはNavigationNodeを付けるか、生成後に`scope.Refresh()`を呼びます。

## NavigationNode

方向の候補を決めたい対象だけに付けます。各方向の候補は上から最初に選択できるものを採用し、非アクティブやinteractable=falseなら次へ進みます。

| Kind | 移動先 |
| --- | --- |
| Node | 指定したSelectable |
| Scope | 指定したFocusScopeの選択履歴 |
| Automatic | 配置から進行方向の候補を探す（空欄は現在のScope） |
| Resolver | NavigationResolver（リスト順など独自の規則） |

NavigationNodeを付けない対象は全方向がAutomaticです。付けた対象は**候補が空の方向には移動しない**ので、Automaticも明示して追加します。`Exclude From Automatic`をONにした対象は自動探索の到達先になりません。SliderとScrollbarは自身の軸を、編集中の入力欄は矢印を優先します。

## タブとリスト

TabController.TabsへAnchor（タブのSelectable）とContent（FocusScope）の組を設定し、タブのonClickから`Select(index)`を呼びます。Tab FocusはContextual（Content内を操作中なら次のContentへ）、EnterContent、Anchorから選びます。

ListNavigationResolverはScope内のNodeをHierarchy順に並べ、Vertical / Horizontal / Grid / First / Lastで解決します。SelectionScrollをScrollRectと同じGameObjectへ置くと、選択した行が見える位置までスクロールします。

## 入力の構成

| やりたいこと | 構成 |
| --- | --- |
| マウスだけ | EventSystemとUI Input Module |
| 入力方式をUIFocusへ伝えるだけ | InputSystemFocusInput |
| マウス・Pad・仮想マウスの切り替え | InputSystemUIInputRouter + PointerSelection（+ VirtualMouseCursor + PointerFollower） |
| 独自の入力元 | `IUIInputSource`を実装してPointerSelectionへ接続 |

```text
EventSystem
  EventSystem / InputSystemUIInputModule
  InputSystemUIInputRouter（Selection、Navigation Move、Virtual Mouse、Virtual Mouse Graphic）
  PointerSelection（Source = Router）
  VirtualMouseCursor（Move = 左スティック、Click = South）
Canvas
  CursorImage（Raycast Target OFF）+ PointerFollower（Source = VirtualMouseCursor）
```

Moduleには通常どおりUIのInput Actionを指定し、Send Navigation EventsはONにします。RouterのNavigation MoveにはModuleのMoveと同じ方向入力を割り当てます。

### 方式の切り替え

| 方式 | 入る条件 |
| --- | --- |
| Mouse | 実マウスの移動・ボタン、仮想マウスの移動 |
| Navigation | Navigation Moveの入力 |

同じフレームではMouseを優先し、Pointerを押している間は方式を変えません。Routerが変更するのは`EventSystem.sendNavigationEvents`、`Cursor.visible`（Manage Os Cursor）、仮想マウスのGraphic、Navigationへ移る時のMouse位置だけで、UI Input Moduleの設定は変えません。

- **Hoverの解除**（`Park Pointer On Navigation`、既定ON）：Navigationへ移る時、実マウスと仮想マウスの位置をデバイスの状態だけ画面外へ置き、最後に居たボタンのHoverを消します。OSカーソルは動きません。
- **位置の報告**（`Follow Selection In Navigation`、既定ON）：Navigation中は選択中のSelectable内の点を`Position`として報告します。位置は`Selection Anchor`（中心か四隅）、`Selection Inset`（四隅を中心へ寄せる画面ピクセル、既定8）、`Selection Offset`で決めます。ボタンごとに決めたい時は、そのボタンか配下に`PointerAnchor`を置くと、そのRectTransformの中心が優先されます。Source = RouterのPointerFollowerは、Pad操作中は選択に付いて動きます。

PointerSelectionの`Clear Selection On Pointer Move`（既定ON）はPointer中に通常ボタンの選択を外します。`Select Nearest On Navigation`（既定OFF）は、未選択でNavigationへ戻る時に入力方向側の近い候補から選択を始めます。

### 仮想マウス

VirtualMouseCursorは有効化中だけMouse派生のデバイスを1つ追加し、スティックで位置を、決定を左ボタンとして書きます。UI Input Moduleは通常の`<Mouse>`束縛で読むので、Hover・Click・Drag & Drop・Scrollが標準の経路で動きます。

- `Mouse.current`を奪わず、PlayerInputの制御スキームも切り替えません。
- Navigationから戻る時は、上記の選択位置から始まります（`Start From Selection`、既定ON）。選択が無ければ前回の位置から続きます。
- 決定を左クリックにするのは仮想マウスで操作している間だけです。Navigation中の決定はModuleのSubmitです。
- 描画は持ちません。カーソル画像はPointerFollowerで追従させます。
- Editorでは、Input Settingsの Editor Input Behavior を `All Device Input Always Goes To Game View` にしないとGame Viewへ届きません。

## Back・Menu・L・Rを通常ボタンへ届ける

用途ごとに`InputButton`の派生型を作り、通常のButtonと同じGameObjectへ付けます。Backには`BackButton`があります。

```csharp
public sealed class MenuButton : InputButton { }

// Input Actionの押下などから呼ぶ。
InputButton.TryInvoke<BackButton>();
InputButton.TryInvoke<MenuButton>();
```

ProbeがRaycastの最前面に露出しているボタンを1つ選んでonClickを呼びます。マウスで押せないボタンは押せません。同じ型が複数露出していればPriorityが大きい方です。World Space Canvasは対象外です。派生型でOnEnable / OnDisableを使う場合は`OnEnabled` / `OnDisabled`をoverrideします。

## Drag / Drop・追従

UIDraggableは標準のPointerイベントで自身（またはTarget）を動かし、UIDropTargetが受理します。仮想マウスでも同じ経路で動きます。受理されなければ開始位置へ戻ります。

PointerFollowerは`IPointerPositionSource`（VirtualMouseCursorやRouter）の座標へ追従し、Boundsで範囲を制限できます。選択中の要素の画面座標は`ScreenPoint.TryGetSelectedPoint`でも取得できます。
