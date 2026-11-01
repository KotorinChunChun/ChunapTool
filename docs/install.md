# インストールと削除

Windows 11 x64向け未署名開発版です。[ChunapTool Releases](https://github.com/KotorinChunChun/ChunapTool/releases)の`ChunapTool-0.1.2-setup.exe`を使用します。.NET Desktop Runtimeを同梱しています。KotorinToys 0.1.2以降からも導入・更新・削除できます。Python・FFmpegは不要です。

利用者単位へ導入し、スタートメニューへ登録します。更新・削除時は正常停止と製品識別を確認し、操作中に終了を拒否した場合は中止します。既存のポータブルZIPや利用者データは移動・削除しません。通常操作は[操作ガイド](guide.md)を参照してください。

Windowsの「インストールされているアプリ」またはKotorinToysから削除できます。v0.1.2は設定・Undo履歴をファイルへ保存しないため、更新・削除で失われる利用者データはありません。開発中のv0.2.0では設定とプリセットを`%LOCALAPPDATA%\KotorinToys\ChunapTool\settings.json`へ保存します。削除時の扱いはv0.2.0の配布時に決めます。ビルドと自動試験の成功を、全ウィンドウ・全DPIでの実操作受入完了とは扱いません。
