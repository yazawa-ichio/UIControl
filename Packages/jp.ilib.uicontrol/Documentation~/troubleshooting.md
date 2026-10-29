# トラブルシューティング

まず`Opened` / `Closed`のStatusとExceptionを確認します。

## 画面

| 症状 | 確認する箇所 |
| --- | --- |
| Push直後に例外 | UIStackが有効か、UIParameterがnullでないか、UIStackのBackdrop等がInheritになっていないか |
| OpenedがFaulted | ResourcesのPath、PrefabルートのPage、Input Group、Preparationの参照 |
| 表示されない | Canvas、表示先の大きさ、`SetVisible(false)`になっていないか |
| Openが終わらない | PrepareStepやOpenのAdapterが完了するか |
| Open中に閉じても演出が続く | 仕様。Open完了後に閉じる。即時に消すならForceRelease |
| Hiddenでも更新される | HiddenはGameObjectを止めない。`RenderingChanged`で判断する |

## 選択と入力

| 症状 | 確認する箇所 |
| --- | --- |
| どのボタンも押せない | EventSystem、GraphicRaycaster、Raycast Target、前面の遮断Graphic |
| 画面だけ押せない | 上端のLiveなPageか、遷移中でないか（`Page.IsInputAllowed`） |
| マウスで押したボタンが選ばれない | 既定の動作。FocusScopeの`Select On Pointer`をONにする（PointerSelectionは`Clear Selection On Pointer Move`をOFF） |
| Padで選択が付かない | Scopeが`Activate()`されているか。Page.Focus、Priority、Collect Selectables / Exclude |
| 生成した行が選べない | 行にNavigationNodeを付けるか、生成後に`scope.Refresh()` |
| NavigationNodeの方向に動かない | 付けた対象は候補が空の方向へ動かない。Automaticを明示する |
| 選択を外しても戻ってくる | 既定の動作。FocusScopeの`Restore On Empty`をOFFにする |
| 未選択のまま方向入力しても選ばれない | 独自の入力元では`UIFocus.NotifyMove`を呼ぶ |
| Back / Menuが呼ばれない | 派生型と`TryInvoke<T>()`の型、Probeの位置が露出しているか |
| テストで選択が戻らない | 入力方式はstaticに残る。`UIFocus.NotifyInputMode`で宣言する |

## 仮想マウス

| 症状 | 確認する箇所 |
| --- | --- |
| カーソルが動かない | VirtualMouseCursorのMove、RouterのVirtual Mouse、PointerFollowerのSource |
| EditorでHoverもクリックも届かない | Editor Input Behaviorを`All Device Input Always Goes To Game View`にする |
| スティックでNavigationも動く | ModuleのMoveと仮想マウスのMoveに同じ入力を使っていないか |
| カーソル画像がクリックを遮る | Raycast TargetをOFFにする |
| Padに替えてもHoverが残る | RouterのPark Pointer On NavigationがONか |

## アニメーション・Binding

| 症状 | 確認する箇所 |
| --- | --- |
| Clipが動かない | Loopしていないか、Rootからのパス、RootのAnimator |
| フェードのalphaが戻る | Page.VisualにPageStateDriverを指定しているか |
| Missingになる | ChannelとNameの一致（PageStateDriverの既定ChannelはPage） |
| VMを渡しても開かない | PageルートのViewBindStepとPreparationへの登録 |
| 行の削除が即時になる | AnimatedCollectionBinder、行のListItemStateDriverとHideの登録 |
