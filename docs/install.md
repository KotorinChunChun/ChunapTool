# インストールと削除

Windows 11 x64向けの未署名開発版です。[ChunapTool Releases](https://github.com/KotorinChunChun/ChunapTool/releases)の`ChunapTool-0.1.2-setup.exe`を使用します。.NET Desktop Runtimeを同梱しており、Python・FFmpegは不要です。KotorinToys 0.1.2以降からも導入・更新・削除できます。

インストーラーはChunapToolを利用者単位で導入し、スタートメニューに登録します。更新・削除の際は正常に停止したことと、対象の製品が一致することを確認します。操作中のアプリが終了を拒否した場合は、更新・削除を中止します。既存のポータブルZIPや利用者データは移動・削除しません。通常の操作は[操作ガイド](guide.md)を参照してください。

Windowsの「インストールされているアプリ」またはKotorinToysから削除できます。

v0.1.2は設定・Undo履歴をファイルへ保存しません。v0.1.3からは設定とプリセットを`%LOCALAPPDATA%\KotorinToys\ChunapTool\settings.json`へ保存します。保存先は導入先とは別なので、更新しても設定とプリセットは失われません。Undo履歴は保存しません。削除後に設定が不要な場合は、`%LOCALAPPDATA%\KotorinToys\ChunapTool\`フォルダーを削除してください。

ビルドと自動試験が成功しても、すべてのウィンドウとDPIでの実操作の受け入れが完了したとはみなしません。
