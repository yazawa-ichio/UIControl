# Queued Dialogs 採用用Sample

`Scenes/QueuedDialogs.unity`を開いてPlayする。START REWARD FLOWから、9件の報酬行を含む3つのDialogが順番に開く。Confirm / Cancelの結果がHomeへ表示され、最後に元の選択へ戻る。Escape / Cancel Actionは通常Cancelボタンへ配送する。物理Padの操作感は実機で確認する。

`Runtime/QueuedDialogsSample.cs`はQueueの所有、VM作成、結果待機、VM解放を扱う。`DialogModel.cs`はデータと通常EventButtonのAccept / Dismiss購読を持つ。新しいVM型ごとのViewや型登録は不要。Queueは本体Sampleの利用例であり、Coreの画面管理APIではない。

## Prefabでの演出設定

- `QueueDialog.prefab`: PageのPreparationにViewBindStep。画面演出はPageStateDriver → NamedStatePlayer → ClipAnimationAdapter。Clipでalpha・scaleを編集する。
- Accept / Dismiss: SelectableStateDriverとNamedStatePlayer、LitMotionAnimationAdapter。Duration・Ease・対象PropertyをInspectorで設定する。
- `RewardRow.prefab`: 通常View、ListItemStateDriver、Player、Hidden / Show / Hide用LitMotion Adapter。LayoutElementは削除待機中の位置維持に使う。

AnimatedCollectionBinderが新規Bind後のShowと、削除前のHide待機を扱う。同じVMの並べ替えでは再Showしない。待機中の行は再利用せず、BinderのDisable / Unbindで取消して回収する。全行の自動staggerや残存行の移動補間は含めない。演出の制作Preview・非破壊Bakeは持たない。

## Binding後のAsset取得例

`PostLoadDialog.prefab`はViewBindStepの後に`LocalizedAssetsStep`を置く。TitleのBinding結果をキーとしてTextAssetを読み、VMで指定されたSpriteAtlasからアイコンを取得してから表示する。Resources内のテキストと色だけの画像はこの例のために新規作成したもの。

```csharp
var model = new UVMBinding.GeneralViewModel();
model.Set("Title", "Greeting");
model.Set("Message", "PostLoad example");
model.Set("LocaleTable", "Adoption/PostLoad/Locale");
model.Set("IconAtlas", "Adoption/PostLoad/Icons");
model.Set("IconName", "MarkerSprite");
model.Set<System.Collections.Generic.IList<UVMBinding.Core.IViewModel>>(
    "Items", new System.Collections.Generic.List<UVMBinding.Core.IViewModel>());
PageHandle handle = stack.Push(model.CreateUIParameter("Adoption/PostLoadDialog"));
OpenResult result = await handle.Opened;
// 利用側の通常ボタン処理などからhandle.Close()を呼ぶ。
await handle.Closed;
model.Dispose();
```

`using UIControl; using UIControl.UVMBinding;`を使用する。生成したSpriteだけをContextで回収し、共有Resources AssetをUnloadしない。失敗・取消ではOpen結果を確認する。これは初回表示の例であり、表示後にTitleキーや言語を変更する場合の再取得はアプリ側で扱う。

## 依存と再生成

通常Clipの再生対象PanelにはAnimatorをPrefabで明示配置する。Animator Controllerは不要。QueueDialogとPostLoadDialogへ設定済み。

Unity 6000.3.20f1、UIControl本体とUVMBinding連携、Input System、LitMotion 2.0.2が必要。UVMBindingは0.1.0（`OnBeforeReleaseAsync`を含む）を使う。GSSは不要。

`UIControl > Samples > Create Queued Dialogs`は基本Sampleの初期データ生成器。作者が編集したPrefabを再生成して上書きしないよう注意する。PostLoad用の追加PrefabとResourcesは保存済みの利用例で、この生成メニューの対象外。
