# Pointer Input Example

`PointerInput.unity`を開いてPlay。`PointerInput.prefab`に設定をまとめています。元ゲームの素材は使わず、色と文字だけで構成しています。Input System 1.19.0で確認する利用例です。

## 操作

- マウス: 移動、クリック、青いカードを緑の領域へDrag／Drop。
- Pad左スティック: 仮想マウス。黄色い点がカーソルで、A／Southがクリック。保持してカードを移動し、緑の領域で放す。
- D-pad: 未選択なら仮想マウスの位置から入力方向側の近いボタンを選択して始める。候補がない方向ではマウス方式を維持。一度放し、次の押下から通常の移動。切り替え時にマウスや仮想マウスが乗っていたボタンのHoverは解ける。
- D-padで選んだ後に左スティックを倒すと、仮想マウスは選択中のボタンの中心から始まる。
- 矢印キー／Enter: 選択移動／決定。
- 既定ではポインター操作中にボタンの選択表示を残さない。
- カードのDrag中にD-padを押しても方式は変わらず、掴んだまま。放して落とせる。

画面下に入力方式、選択中のボタン、接続Pad、左スティック値、Drop結果を表示します。追従ラベルと仮想マウスのGraphicはRaycast TargetをOFFにしています。

Editorで仮想マウスを試すときは、Input Settingsの Editor Input Behavior を `All Device Input Always Goes To Game View` にします。Game Viewにフォーカスが無いと届きません。

## Prefabの構成

- ルート: 利用例の結果表示（PointerInputExample）。Start時に`FocusScope.Activate()`を呼ぶ。
- EventSystem: InputSystemUIInputModule、InputSystemUIInputRouter、PointerSelection、VirtualMouseCursor。RouterのSelectionにPointerSelection、Virtual MouseにVirtualMouseCursor、Virtual Mouse GraphicにVirtual CursorのImageを指定。PointerSelectionはSourceにRouter、Select Nearest On NavigationをON。VirtualMouseCursorのMoveは`Cursor/Move`（左スティック）、Clickは`Cursor/Click`（South）。
- Canvas / Buttons: FocusScopeと3つのButton。NavigationNodeで各方向にAutomatic候補。
- Canvas / Blue Card: UIDraggable。子のLabelも含めてドラッグ中のRaycastを透過。
- Canvas / Drop Area: ImageとUIDropTarget。Droppedイベントで結果表示を呼ぶ。
- Canvas / Pointer Label: PointerFollower。SourceにRouter、BoundsにCanvas、Offsetを設定。マウス・仮想マウス中はカーソルに、D-pad中は選択中のボタンに付いて動く。
- Canvas / Virtual Cursor: ImageとPointerFollower。SourceにVirtualMouseCursor。表示のON / OFFはRouterが行う。

UI Input Moduleの設定は通常どおりで、仮想マウス用の変更はありません。UIStack、Page、GSS、UVMBinding、LitMotionはこの例には必要ありません。

`UIControl > Samples > Build Pointer Input Example`で利用例を再生成できます。再生成はこのフォルダのPrefab／Scene／InputActionsを上書きします。編集を保存したSceneから実行してください。

実Padの操作感は人が操作して確認します。設定の詳細は[選択と入力](../../Packages/jp.ilib.uicontrol/Documentation~/focus-and-input.md)。
