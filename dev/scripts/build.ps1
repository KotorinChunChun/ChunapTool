param()
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path (Split-Path $PSScriptRoot)
dotnet build (Join-Path $projectRoot 'tests/ChunapTool.Tests/ChunapTool.Tests.csproj') -c Debug --nologo
if ($LASTEXITCODE) { throw 'ChunapToolのビルドに失敗しました。' }
dotnet build (Join-Path $projectRoot 'src/ChunapTool.Cli/ChunapTool.Cli.csproj') -c Debug --nologo
if ($LASTEXITCODE) { throw 'chunap（CLI）のビルドに失敗しました。' }
