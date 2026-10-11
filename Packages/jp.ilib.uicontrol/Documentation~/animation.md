# アニメーション

NamedStatePlayerのStatesへ**Channel・Name・Adapter**の組を登録します。コードやDriverは状態名だけを伝え、対象・値・長さはPrefabに持たせます。Pageが無くても使えます。

| 役割 | コンポーネント |
| --- | --- |
| 状態名の再生と完了・停止 | NamedStatePlayer |
| 画面のOpen / Close / Covered / Hidden | PageStateDriver（Page.Visualへ指定） |
| ボタンのNormal / Highlighted / Selected / Pressed / Disabled | SelectableStateDriver |
| 行のShow / Hide / 再利用前の初期化 | ListItemStateDriver |
| 複数行の時間差表示 | ListStateDriver |
| 値を動かす | ClipAnimationAdapter / LitMotionAnimationAdapter |

## 画面のフェード

1. Input GroupのCanvasGroupのalphaを0→1にするOpen用、1→0にするClose用のClipを作ります。Loop TimeはOFFにします。
2. Clipの起点のGameObjectにAnimatorを置きます（Animator Controllerは不要）。
3. ClipAnimationAdapterをOpen / Close用に置き、ClipとRootを指定します。カーブのパスはRootからの相対です。
4. NamedStatePlayerへ`Page / Open`、`Page / Close`を登録し、PageStateDriver.PlayerとPage.Visualを設定します。

PageStateDriverはOpen / Close / Covered / Hidden / Restored（既定はOpenを再生）を状態名へ変換します。登録の無い状態は演出を省いて遷移を続けます。演出の取消は失敗ではなく、終値を即時適用します。

## LitMotion

LitMotion連携のLitMotionAnimationAdapterは、InspectorのTweensにProperty（AnchoredPosition / LocalScale / LocalRotation / Alpha / Color / Circle）、対象、開始・終了値、Duration、Delay、Easeを設定します。From Currentで現在値から始め、複数項目はすべての完了を待ちます。同じ対象の同じプロパティは重複できません。

## ボタンと行

- SelectableStateDriverはButtonと同じGameObjectへ置きます。Channelの既定は`Selection`です。Disabledは利用側のinteractableに従い、画面遷移中の入力禁止では変わりません。
- ListItemStateDriverは行Prefabへ置きます。Channelの既定は`Item`で、`Hidden`（Reset）/ `Show` / `Hide`を登録します。非表示の行は選択候補から外れます。`BeforeReuse()`は演出と選択履歴を止めてResetの終値を適用し、`Hide(token)`は完了を待てます。Hideは実体を破棄しません。
- ListStateDriverの`Show()`は子の行を時間差で表示します（既定の間隔0.05秒）。

## コードから再生する

```csharp
StateResult result = await player.Play("Attention", "Emphasis", restart: true);
```

| StateStatus | 意味 |
| --- | --- |
| Completed | 完了、または終値の適用完了 |
| Superseded | 同じChannelの新しい状態に置き換えられた |
| Cancelled | Stop、Disable、取消 |
| Missing | 状態が登録されていない |
| Faulted | 設定不正や再生失敗。Exceptionを確認 |

`ApplyImmediately(name, channel)`は終値を適用し、`Stop(channel)` / `StopAll()`は現在の姿勢で止めます。時間はTime Mode（Unscaled既定 / Scaled）に従ってPlayerが供給します。
