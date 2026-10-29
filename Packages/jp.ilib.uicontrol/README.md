# UIControl

uGUIの画面をPrefabで組み立て、開閉・Padの選択・状態アニメーションを接続するライブラリ。`0.1.2`、Unity 6000.3。

画面管理を使わず、選択や入力の部品だけを使うこともできます。

## 導入

Package Managerの **Install package from git URL** に、リポジトリのURLへ`?path=Packages/jp.ilib.uicontrol`を付けて入力します。ローカルでは **Install package from disk** で`package.json`を指定します。

Runtimeは3つのassemblyで、依存は一方向です。通常のAPIは`using UIControl;`です。

| assembly | 内容 |
| --- | --- |
| `UIControl.Runtime` | `UIStack` / `Page` / ロード・準備 / `FocusScope` / `UIFocus` / Navigation |
| `UIControl.Animation` | `NamedStatePlayer`、各StateDriver、`ClipAnimationAdapter` |
| `UIControl.Input` | `InputButton`、`PointerSelection`、`PointerFollower`、Drag / Drop |

連携は`Integrations/*`にあり、依存パッケージがあるときだけVersion Definesでコンパイルされます。

| 連携 | 依存パッケージ | assembly |
| --- | --- | --- |
| Input System（方式判定、仮想マウス） | `com.unity.inputsystem` 1.4.0以上 | `UIControl.InputSystem` |
| LitMotion（PrefabだけのTween） | `com.annulusgames.lit-motion` 2.0.2以上 | `UIControl.LitMotion` |
| UVMBinding（IViewModelの接続、行演出） | `jp.ilib.uvmb` 0.1.0以上 | `UIControl.UVMBinding` |
| GameStateStructure（ステート終了で閉じる） | `jp.yazawa-ichio.game-state-structure` 0.4.0以上 | `UIControl.GSS` |

利用側のasmdefから連携の型を使う場合は、その連携assemblyへの参照を追加します。

## 画面を開く

Sceneに置くのは`UIStack`、Canvas、EventSystemだけです。Rootや登録用のComponentはありません。

```csharp
PageHandle page = stack.Push(UIParameter.WithoutData("UI/Reward"));
OpenResult opened = await page.Opened;
if (opened.Exception != null) Debug.LogException(opened.Exception);
CloseResult closed = await page.Close();
```

- 同じStack内の操作は受付順に1つずつ実行し、進行中の演出へ割り込みません。即時に消すのは`ForceRelease()`です。
- 背面・初期Focus・演出順序は **Options → Page Prefab → UIStack** の順に解決します。通常はPrefabとStackで決めます。
- 全画面の入力遮断や画面間の業務フローは利用側で書きます。描画順はuGUIのCanvasとHierarchyで決まります。

## ドキュメント

| 章 | 内容 |
| --- | --- |
| [画面](Documentation~/pages.md) | 最初の画面、Prefab構成、Push / Replace / Close、Backdrop、ロードと準備、遷移中の遮断 |
| [選択と入力](Documentation~/focus-and-input.md) | FocusScope、Navigation、タブとリスト、マウス・Pad・仮想マウス、InputButton、Drag / Drop |
| [アニメーション](Documentation~/animation.md) | NamedStatePlayer、画面・ボタン・行の演出、Clip / LitMotion |
| [連携](Documentation~/integrations.md) | UVMBinding、GameStateStructure |
| [トラブルシューティング](Documentation~/troubleshooting.md) | 症状別の確認箇所 |

## Sample

| Sample | 内容 |
| --- | --- |
| Queued Pages | 回答とCloseまで1件ずつ待つ要求Queueの参考実装 |
| Queued Dialogs | IViewModelを使う3連続ダイアログ。Queued Pages、UVMBinding、Input System、LitMotionが必要 |
