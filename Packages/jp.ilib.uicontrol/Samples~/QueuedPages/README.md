# Queued Pages

IUIStackとPageHandleを使うアプリ側Queueの参考実装。Coreの必須APIには含めない。

```csharp
using UIControl.Samples;
var queue = new RequestQueue(stack);
var result = await queue.Enqueue<bool>(responder =>
    UIParameter.Create("UI/Confirm", (page, context) =>
    {
        // Button等からresponder.TryAccept(true) / TryDismiss()へ接続する。
        // 接続解除はcontext.OnReleaseへ登録する。
    }));
queue.Dispose();
```

待機取消は生成を省略し、実行中の取消・Disposeは自分のPageだけを終了する。個々の回答・外部Close・例外の後もClosedを確認して次へ進む。業務上の回答型、連続処理のマスク、別Stackとの協調はアプリに合わせて変更する。
