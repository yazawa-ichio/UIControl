# UIControl

uGUIの画面をPrefabで組み立て、開閉・Padの選択・状態アニメーションを接続するUnityライブラリ `jp.ilib.uicontrol` の開発リポジトリです。

使い方は[パッケージのREADME](Packages/jp.ilib.uicontrol/README.md)と`Packages/jp.ilib.uicontrol/Documentation~`を参照してください。

## 開発

Unity **6000.3.20f1** でこのフォルダーを開きます。依存パッケージ（LitMotion、Input System、UVMBinding、GameStateStructure、Unity Pipeline）はPackage ManagerがGitとUnity Registryから取得します。

| 場所 | 内容 |
| --- | --- |
| `Packages/jp.ilib.uicontrol` | 配布するパッケージ |
| `Assets/Showcase` | 機能を一通り触れる見本アプリ |
| `Assets/InputExamples` | マウス・仮想マウスのDrag / Drop、追従、選択開始の例 |
| `Assets/Adoption` | Sample **Queued Dialogs** の編集元 |
| `Assets/Testing` | パッケージのテスト（Core、InputSystem）、Sampleのスモークテスト、コンパイル確認。パッケージには含めない |

## テスト

テストはすべてPlayModeです。開いているEditorではUnity CLIで実行します。

```bash
unity command --project-path . run_tests --mode PlayMode --async_tests
unity command --project-path . test_status
```

Editorを閉じた状態ではバッチで実行します。結果は`Logs/UnityTests/`に保存され、XMLの件数と全件成功を確認してから成功を返します。

```bash
tools/run-tests.sh                      # Compile → PlayMode
tools/run-tests.sh --mode PlayMode --filter UIControl.Tests.PageLifecycleTests
```

WindowsではGit Bashから実行します。Python 3が必要です。
