# ChunapTool

Windowsのウィンドウを選び、位置・サイズ・最大化を調整するツールです。選択対象のライブプレビューと、最大100手の「戻す」を備えます。

## 起動

Windows 11と.NET 10 SDKを使用します。PowerShell 7で実行してください。

```powershell
cd C:\develop\KotorinToys\tools\ChunapTool
pwsh -ExecutionPolicy Bypass -File dev/scripts/start.ps1
```

Python、OpenCV、FFmpegの準備は不要です。録画や動画編集機能は含みません。

## 使い方

1. タイトルまたは実行ファイル名で検索し、「対象」からウィンドウを選びます。
2. X/Y/W/Hを変更します。「同期」がONなら入力確定後に反映します。
3. まとめて変更する場合は「同期」をOFFにし、「適用」またはEnterで反映します。
4. 「最大化」で最大化／通常表示を切り替え、「戻す」で直前の位置・サイズ・最大化状態へ戻します。

詳しくは[利用・開発ガイド](docs/guide.md)、抽出範囲と検証状況は[v0.1.0実装記録](dev/history/v0.1.0/v0.1.0-imp.md)を参照してください。

KotorinToys配下の独立リポジトリです。本体からの起動・停止・導入管理は未登録です。

## KotorinToys管理連携（2026-10-15追加）

本体の一覧からの開発版起動・既存画面表示・状態確認・正常停止に対応しました。上記移植時点の未登録範囲を、この追加で更新します。配布・正式インストール・外部公開は未実施です。[管理連携と検証用起動](docs/kotorintoys-integration.md)を参照してください。
