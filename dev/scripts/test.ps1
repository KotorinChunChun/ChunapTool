param([switch]$Native, [switch]$Ui)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path (Split-Path $PSScriptRoot)
& (Join-Path $PSScriptRoot 'build.ps1')
$testApp = Join-Path $projectRoot 'tests/ChunapTool.Tests/bin/Debug/net10.0-windows10.0.19041.0/ChunapTool.Tests.exe'
if ($Native -and $Ui) { throw '-Nativeと-Uiは同時に指定しないでください。' }
if ($Native) { & $testApp --native } elseif ($Ui) { & $testApp --ui } else { & $testApp }
if ($LASTEXITCODE) { throw 'ChunapToolのテストに失敗しました。' }
