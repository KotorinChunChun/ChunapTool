# ChunapTool

Windowsのウィンドウを選び、位置・サイズ・最大化を調整するツールです。ドラッグやサムネイルでの対象選択、折りたためるライブプレビュー、最大100手の「戻す」を備えます。

## 起動

[Releases](https://github.com/KotorinChunChun/ChunapTool/releases)の`ChunapTool-0.1.2-setup.exe`で利用者単位に導入できます。KotorinToys 0.1.2以降の本体からの導入・更新・削除にも対応します。[導入と保持・削除の手順](docs/install.md)を確認してください。ランタイム同梱の未署名開発版です。ChunapToolは設定・Undo履歴をファイルへ保存しないため（起動ごとに既定値）、更新・削除で失われる利用者データはありません。

Windows 11 x64向けの[ポータブル版](https://github.com/KotorinChunChun/ChunapTool/releases/tag/v0.1.1)はZIPをすべて展開し、`ChunapTool.exe`を起動します。.NET 10ランタイムを同梱しています。配布ZIPは未署名です。同じリリースの`SHA256SUMS.txt`でハッシュを確認できます。

ソースから起動する場合は.NET 10 SDKとPowerShell 7を使用します。

```powershell
cd <ChunapToolのクローン先>
pwsh -ExecutionPolicy Bypass -File dev/scripts/start.ps1
```

Python、OpenCV、FFmpegの準備は不要です。録画や動画編集機能は含みません。

## 使い方

1. タイトルまたは実行ファイル名で検索し、「対象」からウィンドウを選びます。右の「⌖」を対象ウィンドウへドラッグする方法でも選択できます。
2. 「自動読み取り」をONにすると、対象の位置・サイズの変化をX/Y/W/Hへ表示します。
3. 「自動反映」がONなら入力変更を自動反映します。OFFなら入力値を編集し、「適用」を押して反映します。
4. 「最大化」で最大化するとボタンが「通常化」に変わります。「戻す」で直前の位置・サイズ・最大化状態へ戻します。
5. 適用ボタンの右の「画面外可」で、モニターの作業領域を超える位置・サイズを許可するか切り替えます。
6. Xの左の「▼」でプレビューを開き、「▲」で閉じます。通常は折りたたんだ状態で起動します。

画像から探す場合は「サムネイル選択」を押します。作業領域いっぱいの選択画面で画像とウィンドウ名を確認し、クリックして確定します。検索は入力直後に絞り込み、画像は1枚ずつ数秒おきに更新します。選択・取消で閉じると撮影も停止します。

現在の公開版はv0.1.2です。ソースは[GitHubリポジトリ](https://github.com/KotorinChunChun/ChunapTool)、詳しくは[利用・開発ガイド](docs/guide.md)、変更と検証状況は[開発資料](dev/README.md)、移植時の抽出範囲は[v0.1.0実装記録](dev/history/v0.1.0/v0.1.0-imp.md)を参照してください。v0.1.2公開後の修正（管理pipeの受付継続、スナップ中の位置へのUndo）は[コードレビュー修正記録](dev/research/code-review-2026-10-25/report.md)にあり、未リリースです。

KotorinToys配下の独立リポジトリです。本体からの開発版起動・停止・状態確認に対応しています。

## KotorinToys管理連携（2026-10-15追加）

本体の一覧からの開発版起動・既存画面表示・状態確認・正常停止に対応しました。v0.1.2からは本体からの導入・更新・削除にも対応しています（[導入と削除](docs/install.md)）。[管理連携と検証用起動](docs/kotorintoys-integration.md)を参照してください。
