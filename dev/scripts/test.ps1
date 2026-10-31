param([switch]$Native, [switch]$Ui, [switch]$Cli, [switch]$CliNative)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path (Split-Path $PSScriptRoot)
& (Join-Path $PSScriptRoot 'build.ps1')
$testApp = Join-Path $projectRoot 'tests/ChunapTool.Tests/bin/Debug/net10.0-windows10.0.19041.0/ChunapTool.Tests.exe'
if (@($Native, $Ui, $Cli, $CliNative).Where({ $_ }).Count -gt 1) { throw '-Native、-Ui、-Cli、-CliNativeは1つだけ指定してください。' }
if ($Native) { & $testApp --native } elseif ($Ui) { & $testApp --ui } elseif ($Cli) { & $testApp --cli } elseif ($CliNative) { & $testApp --cli-native } else { & $testApp }
if ($LASTEXITCODE) { throw 'ChunapToolのテストに失敗しました。' }
