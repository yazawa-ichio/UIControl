# Queued Dialogs

3連続の報酬画面を通常のIViewModel、EventButton、UIStackへ接続する導入例。

1. 本体UPMのSample **Queued Pages**を先にImportする。
2. UVMBinding、Input System、LitMotion 2.0.2を導入する。GSSと比較用OSSは不要。
3. このSampleをImportし、Scenes/QueuedDialogs.unityを開いてPlayする。
4. Launchから開始し、CONFIRM / CANCELで回答する。Back入力は`InputButton.TryInvoke<BackButton>()`から、CANCELボタンに付けたBackButtonを通して通常のonClickを呼ぶ。

画面はClipAnimationAdapter、ボタンと行はLitMotionAnimationAdapter。演出は各PrefabのPlayer / Adapterを編集する。Dialogの背面Pointer遮断はPrefabのMaskを使う。

RuntimeのQueue生成callbackがIViewModelをCreateUIParameterへ渡す。VMはQueue全体の結果を受け取ってアプリ側で回収する。Binding後の追加取得はPage.PreparationへPrepareStepとして配置できる。

すでにこの開発リポジトリのAssets/Adoptionを利用する場合、同じSampleを重複Importしない。

削除前の行HideはAnimatedCollectionBinderが待機する。UVMBindingのOnBeforeReleaseAsyncを含む版が必要。RewardRowのLayoutElementは待機中の位置維持に使う。差分・Pool・取消時の回収はUVMBindingが担当する。
