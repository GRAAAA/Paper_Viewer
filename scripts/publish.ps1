param([ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version = '1.0.0')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$output = Join-Path $projectRoot "artifacts\release-$Version"
dotnet publish (Join-Path $projectRoot 'PaperView.csproj') -c Release -r win-x64 --self-contained true -o $output `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false "-p:Version=$Version"
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
$asset = Join-Path $output 'PaperView-win-x64.exe'
Copy-Item -LiteralPath (Join-Path $output 'PaperView.exe') -Destination $asset -Force
$hash = (Get-FileHash -LiteralPath $asset -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath (Join-Path $output 'SHA256SUMS.txt') -Value "$hash  PaperView-win-x64.exe" -Encoding ASCII
Write-Output "Release ready: $asset"
Write-Output "Publish the executable and SHA256SUMS.txt on GitHub Releases with tag v$Version when approved."
