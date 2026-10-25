param(
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9_-]*$')]
    [string]$RunId = (Get-Date -Format 'yyyyMMdd-HHmmss')
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path (Split-Path $PSScriptRoot)
$projectFile = Join-Path $projectRoot 'src/ChunapTool/ChunapTool.csproj'
[xml]$project = Get-Content -LiteralPath $projectFile -Raw
$version = [string]$project.Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw '配布するバージョンが不正です。' }
$runDirectory = Join-Path $projectRoot "dev/history/v$version/logs/$RunId"
$packageDirectory = Join-Path $runDirectory 'package'
if (Test-Path -LiteralPath $packageDirectory) { throw '既存の配布物は上書きしません。別のRunIdを指定してください。' }
$publishDirectory = Join-Path $packageDirectory 'ChunapTool'
New-Item -ItemType Directory -Path $publishDirectory -Force | Out-Null
dotnet publish $projectFile -c Release -r win-x64 --self-contained true `
    --artifacts-path (Join-Path $runDirectory 'build') -p:DebugType=None -p:DebugSymbols=false `
    -o $publishDirectory --nologo 2>&1 | Tee-Object -FilePath (Join-Path $runDirectory 'publish.log') | Out-Host
if ($LASTEXITCODE) { throw '配布用ビルドに失敗しました。' }
foreach ($name in @('ChunapTool.exe', 'ChunapTool.dll', 'coreclr.dll', 'hostfxr.dll', 'System.Windows.Forms.dll')) {
    if (-not (Test-Path -LiteralPath (Join-Path $publishDirectory $name))) { throw "配布ファイルが不足しています: $name" }
}
$exeVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $publishDirectory 'ChunapTool.exe')).FileVersion
if ($exeVersion -ne "$version.0") { throw '配布EXEのバージョンが一致しません。' }
$runtimeConfig = Get-Content -LiteralPath (Join-Path $publishDirectory 'ChunapTool.runtimeconfig.json') -Raw | ConvertFrom-Json
$assets = Get-Content -LiteralPath (Join-Path $runDirectory 'build/obj/ChunapTool/project.assets.json') -Raw | ConvertFrom-Json
foreach ($framework in $runtimeConfig.runtimeOptions.includedFrameworks) {
    $packageId = $framework.name.ToLowerInvariant() + '.runtime.win-x64'
    $runtimePackage = $assets.packageFolders.PSObject.Properties.Name |
        ForEach-Object { Join-Path $_ "$packageId/$($framework.version)" } |
        Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if (-not $runtimePackage) { throw "ランタイムのライセンスが見つかりません: $packageId" }
    $noticesDirectory = Join-Path $publishDirectory "licenses/$($framework.name)"
    New-Item -ItemType Directory -Path $noticesDirectory -Force | Out-Null
    $licenses = @(Get-ChildItem -LiteralPath $runtimePackage -File | Where-Object Name -Match '^LICENSE(\.TXT)?$')
    if (-not $licenses.Count) { throw "ランタイムのライセンスが不足しています: $packageId" }
    foreach ($license in $licenses) { Copy-Item -LiteralPath $license.FullName -Destination $noticesDirectory }
    $notices = Join-Path $runtimePackage 'THIRD-PARTY-NOTICES.TXT'
    if (Test-Path -LiteralPath $notices) { Copy-Item -LiteralPath $notices -Destination $noticesDirectory }
}
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs/guide.md') -Destination (Join-Path $publishDirectory '利用ガイド.md')
@"
ChunapTool v$version ポータブル版

Windows 11 x64向け。.NET 10ランタイムを同梱しています。
ZIPをすべて展開し、ChunapTool.exeを起動してください。
既に起動している場合は、終了してから新版を起動してください。
インストールやPython・FFmpegの導入は不要です。

詳しい操作は「利用ガイド.md」を参照してください。
未署名の配布物です。SHA256SUMS.txtでZIPのハッシュを照合できます。
インストーラー版とKotorinToys本体からの導入はGitHub Releasesのsetupを使用してください。
"@ | Set-Content -LiteralPath (Join-Path $publishDirectory 'はじめに.txt') -Encoding utf8
$archive = Join-Path $runDirectory "ChunapTool-$version-win-x64.zip"
Compress-Archive -LiteralPath $publishDirectory -DestinationPath $archive -CompressionLevel Optimal
$digest = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
"$digest  $([IO.Path]::GetFileName($archive))" | Set-Content -LiteralPath (Join-Path $runDirectory 'SHA256SUMS.txt') -Encoding utf8
Write-Output "配布ZIP: $archive"
Write-Output "SHA-256: $digest"
