param([string]$ReleaseExe = (Join-Path $PSScriptRoot '..\artifacts\release-1.0.0\PaperView-win-x64.exe'))
$ErrorActionPreference = 'Stop'
$project = Split-Path $PSScriptRoot -Parent
$root = Join-Path $project ('artifacts\packaging-test-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($root) | Out-Null
$install = Join-Path $root "installation with spaces and ' apostrophe"
$shortcut = Join-Path $root 'PaperView.lnk'
$log = Join-Path $root 'maintenance.log'
$manager = Join-Path $project 'Packaging\manage.ps1'
function Check([bool]$Condition, [string]$Name) {
    if (-not $Condition) { throw "FAIL: $Name" }
    Write-Host "PASS: $Name"
}
function Run-Management([string]$Action, [int]$Expected = 0, [string]$Script = $manager) {
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $Script -Command $Action -Source $ReleaseExe -InstallDirectory $install -ShortcutPath $shortcut -LogPath $log -SkipPath
    Check ($LASTEXITCODE -eq $Expected) "$Action exits $Expected"
}
function Run-Cli([string]$Argument, [int]$Expected) {
    $start = [Diagnostics.ProcessStartInfo]::new($ReleaseExe, $Argument)
    $start.UseShellExecute = $false
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $process = [Diagnostics.Process]::Start($start)
    $stdout = $process.StandardOutput.ReadToEnd()
    $stderr = $process.StandardError.ReadToEnd()
    $process.WaitForExit()
    Check ($process.ExitCode -eq $Expected) "CLI $Argument exits $Expected"
    Check (($stdout + $stderr).Length -gt 0) "CLI $Argument produces terminal output"
    return $stdout
}
Check ((Run-Cli '--version' 0) -match 'PaperView 1.0.0') 'Published executable reports its version'
Run-Cli '--help' 0
Run-Cli 'not-a-command' 2
Run-Management uninstall 1
Run-Management install
Check (Test-Path -LiteralPath (Join-Path $install 'PaperView.exe')) 'Install copies executable'
Check (Test-Path -LiteralPath $shortcut) 'Install creates shortcut'
$installedVersion = & (Join-Path $install 'bin\paperview.cmd') --version
Check ($LASTEXITCODE -eq 0 -and "$installedVersion" -match 'PaperView 1.0.0') 'Installed launcher waits and prints version'
Run-Management install

# Generate offline GitHub responses, exercising the real updater without publishing.
$fixture = Join-Path $root 'update-fixture'
dotnet publish (Join-Path $project 'PaperView.csproj') -c Release -r win-x64 --self-contained true -o $fixture -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:Version=1.0.1
Check ($LASTEXITCODE -eq 0) 'Build newer update fixture'
$newExe = Join-Path $fixture 'PaperView.exe'
$hash = (Get-FileHash -LiteralPath $newExe -Algorithm SHA256).Hash
function Make-Mock([string]$Tag, [string]$Digest, [string]$Name) {
    $mock = Join-Path $root "$Name.ps1"
    $quotedExe = $newExe.Replace("'", "''")
    $quotedManager = $manager.Replace("'", "''")
    @"
function Invoke-RestMethod {
    return @{ tag_name = '$Tag'; assets = @(@{ name = 'PaperView-win-x64.exe'; digest = 'sha256:$Digest'; browser_download_url = 'https://github.com/GRAAAA/Paper_Viewer/releases/download/$Tag/PaperView-win-x64.exe' }) }
}
function Invoke-WebRequest { param(`$Uri, `$OutFile, `$Headers, `$TimeoutSec, [switch]`$UseBasicParsing) Copy-Item -LiteralPath '$quotedExe' -Destination `$OutFile }
& '$quotedManager' @args
exit `$LASTEXITCODE
"@ | Set-Content -LiteralPath $mock
    return $mock
}
$original = (Get-FileHash -LiteralPath (Join-Path $install 'PaperView.exe')).Hash
Run-Management update 0 (Make-Mock 'v1.0.0' $hash 'current')
Run-Management update 1 (Make-Mock 'v1.0.1' ('0' * 64) 'bad-hash')
Check ((Get-FileHash -LiteralPath (Join-Path $install 'PaperView.exe')).Hash -eq $original) 'Bad checksum leaves installed executable intact'
Run-Management update 1 (Make-Mock 'v1.0.2' $hash 'bad-version')
Check ((Get-FileHash -LiteralPath (Join-Path $install 'PaperView.exe')).Hash -eq $original) 'Mismatched version leaves installed executable intact'
Run-Management update 0 (Make-Mock 'v1.0.1' $hash 'new-version')
Check ((Get-FileHash -LiteralPath (Join-Path $install 'PaperView.exe')).Hash -eq $hash) 'Update installs verified newer executable'
Check (@(Get-ChildItem -LiteralPath $install -Filter 'download-*').Count -eq 0) 'Update cleans temporary downloads'

$unrelated = Join-Path $install 'user-document.pdf'
Set-Content -LiteralPath $unrelated -Value 'preserve me'
# Redirect the installed manager's defaults into the fixture before exercising
# the launcher deleting itself. No user PATH or real Start menu changes occur.
$installedManager = Join-Path $install 'manage.ps1'
$defaults = @{ InstallDirectory = $install; ShortcutPath = $shortcut; LogPath = $log }
$isolatedScript = foreach ($line in Get-Content -LiteralPath $installedManager) {
    if ($line -match '^\s*\[string\]\$(InstallDirectory|ShortcutPath|LogPath) =') {
        $name = $Matches[1]
        '    [string]$' + $name + " = '" + $defaults[$name].Replace("'", "''") + "',"
    } elseif ($line -match '^\s*\[switch\]\$SkipPath,') {
        '    [switch]$SkipPath = $true,'
    } else { $line }
}
$isolatedScript | Set-Content -LiteralPath $installedManager
$previousPath = $env:Path
try {
    $env:Path = (Join-Path $install 'bin') + ';' + $env:Path
    Check ((Get-Command paperview).Source -eq (Join-Path $install 'bin\paperview.cmd')) 'PATH resolves to the terminal launcher'
    & paperview uninstall
    Check ($LASTEXITCODE -eq 0) 'Installed launcher uninstalls itself successfully'
} finally { $env:Path = $previousPath }
Check (Test-Path -LiteralPath $unrelated) 'Uninstall preserves unrelated files'
Check (-not (Test-Path -LiteralPath (Join-Path $install 'PaperView.exe'))) 'Uninstall removes executable'
Check (-not (Test-Path -LiteralPath $shortcut)) 'Uninstall removes owned shortcut'
Run-Management install 1
Write-Output "Packaging tests passed. Isolated fixtures: $root"
