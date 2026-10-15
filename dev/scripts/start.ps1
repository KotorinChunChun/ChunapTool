param()
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path (Split-Path $PSScriptRoot)
$projectFile = Join-Path $projectRoot 'src/ChunapTool/ChunapTool.csproj'
dotnet build $projectFile -c Debug --nologo
if ($LASTEXITCODE) { throw 'ChunapToolのビルドに失敗しました。' }
$app = Join-Path $projectRoot 'src/ChunapTool/bin/Debug/net10.0-windows10.0.19041.0/ChunapTool.exe'
# 利用者が明示的に起動する対話画面を表示する。
Start-Process -FilePath $app -WorkingDirectory $projectRoot -WindowStyle Normal
