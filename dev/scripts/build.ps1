param()
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path (Split-Path $PSScriptRoot)
dotnet build (Join-Path $projectRoot 'tests/ChunapTool.Tests/ChunapTool.Tests.csproj') -c Debug --nologo
if ($LASTEXITCODE) { throw 'ChunapToolのビルドに失敗しました。' }
