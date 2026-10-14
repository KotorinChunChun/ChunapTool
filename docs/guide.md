# ChunapTool 利用・開発ガイド

## 操作

- 起動時は対象未選択です。検索後に対象を明示的に選びます。自分自身は一覧に含みません。
- 座標とサイズは、見えるウィンドウ枠を基準にしたピクセル値です。透明なリサイズ枠を内部で補正します。
- 「同期」ONではスピン操作や入力確定から約250ms後に反映します。対象を外部操作した位置も表示へ反映します。
- 「同期」OFFでは未適用の入力値を保持します。「適用」またはEnterでまとめて反映します。ONへ戻すと実際の位置を読み直します。
- 「画面外可」OFFでは対象が現在属するモニターの作業領域（タスクバーを除く）へ収めます。別モニターへ数値指定で移す際はONにしてください。ONでも要求サイズの最小値は32pxです。
- 最大化中・最小化中は座標編集を停止します。対象アプリ自身の最小／最大サイズ制限が優先され、実際に反映された値を再取得します。
- 「戻す」は最大100手です。外部ドラッグを含み、別対象を選ぶと履歴をリセットします。操作に失敗した場合、戻す履歴は消費しません。
- 「最前面」はこのツールの表示順だけを切り替えます。対象を操作しても対象を手前へ持ってきません。
- 終了時に対象ウィンドウを自動復元しません。必要なら閉じる前に「戻す」を使用してください。

## プレビュー

WindowsのDWMサムネイルを使い、対象全体を縦横比を維持して表示します。対象が隠れていてもWindowsが提供する描画を表示します。最小化中は案内へ切り替えます。保護コンテンツ、特殊描画、DWMが提供しない画面は黒くなる・更新されない場合があります。

対象が終了したときは編集とプレビューを解除します。アクセス権や応答状態により操作できない場合は画面下へ理由を表示します。

## ビルドとテスト

```powershell
cd C:\develop\KotorinToys\tools\ChunapTool
pwsh -ExecutionPolicy Bypass -File dev/scripts/build.ps1
pwsh -ExecutionPolicy Bypass -File dev/scripts/test.ps1
pwsh -ExecutionPolicy Bypass -File dev/scripts/test.ps1 -Ui
pwsh -ExecutionPolicy Bypass -File dev/scripts/test.ps1 -Native
```

通常のテストは座標制約と履歴の検証です。`-Ui`は不可視・非アクティブな自フォームと偽サービスで画面状態と配置座標を確認し、他の画面へ入力しません。実描画やDWMの検証ではありません。`-Native`は専用のテストウィンドウを一時的に起動してWin32連携を検証するため、他の画面作業をしていない対話デスクトップで実行してください。手動確認用ウィンドウは次で起動できます。

```powershell
./tests/ChunapTool.Tests/bin/Debug/net10.0-windows/ChunapTool.Tests.exe --fixture
```

## 構成と移行元

- `src/ChunapTool/Core/`: 座標・履歴・共有型
- `src/ChunapTool/Native/`: Win32ウィンドウ操作、DWMプレビュー
- `src/ChunapTool/MainForm.cs`: 操作画面
- `tests/ChunapTool.Tests/`: 外部テストライブラリー不要の検証とテスト窓

元の`C:\develop\ChulipVideo\20251210_動画クリップ作成_Python\src\ChunapTool.py`と`window_utils.py`の関連機能から移植しました。アイコン画像は同プロジェクトの`ChulipVideo.png`を引き継いでいます。元ファイルは変更していません。
