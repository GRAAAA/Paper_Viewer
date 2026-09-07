param(
    [Parameter(Mandatory = $true)][ValidateSet('install', 'update', 'uninstall')][string]$Command,
    [string]$Source,
    [string]$InstallDirectory = (Join-Path $env:LOCALAPPDATA 'Programs\PaperView'),
    [string]$ShortcutPath = (Join-Path ([Environment]::GetFolderPath('Programs')) 'PaperView.lnk'),
    [string]$LogPath = (Join-Path $env:LOCALAPPDATA 'PaperView\maintenance.log'),
    [switch]$SkipPath,
    [switch]$CleanupScript,
    [int]$WaitForProcess = 0
)
$ErrorActionPreference = 'Stop'
$installRoot = [IO.Path]::GetFullPath($InstallDirectory).TrimEnd('\')
$executable = Join-Path $installRoot 'PaperView.exe'
$manager = Join-Path $installRoot 'manage.ps1'
$binDirectory = Join-Path $installRoot 'bin'
$launcher = Join-Path $binDirectory 'paperview.cmd'
$marker = Join-Path $installRoot '.paperview-install'
$download = $null
$lock = $null
$script:lastPercent = -1
$script:lastStage = ''
$script:progressWidth = 0

function Show-Progress([int]$Percent, [string]$Stage) {
    $Percent = [Math]::Max(0, [Math]::Min(100, $Percent))
    if ($Percent -eq $script:lastPercent -and $Stage -eq $script:lastStage) { return }
    $script:lastPercent = $Percent
    $script:lastStage = $Stage
    $filled = [int][Math]::Floor($Percent / 5)
    $line = '[{0}{1}] {2,3}% {3}: {4}' -f ('#' * $filled), ('-' * (20 - $filled)), $Percent, $Command, $Stage
    if ([Console]::IsOutputRedirected) {
        [Console]::WriteLine($line)
    } else {
        [Console]::Write("`r" + $line.PadRight([Math]::Max($script:progressWidth, $line.Length)))
        $script:progressWidth = $line.Length
        if ($Percent -eq 100) { [Console]::WriteLine(); $script:progressWidth = 0 }
    }
}

function Report([string]$Message) {
    if ($script:progressWidth -gt 0) { [Console]::WriteLine(); $script:progressWidth = 0 }
    Write-Output $Message
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($LogPath)) | Out-Null
    Add-Content -LiteralPath $LogPath -Value "$(Get-Date -Format s) $Message"
}

function Change-UserPath([bool]$Add) {
    if ($SkipPath) { return }
    $current = [Environment]::GetEnvironmentVariable('Path', 'User')
    $parts = @($current -split ';' | Where-Object { $_ -and $_.TrimEnd('\') -ine $binDirectory })
    if ($Add) { $parts += $binDirectory }
    $next = $parts -join ';'
    if ($next -cne $current) { [Environment]::SetEnvironmentVariable('Path', $next, 'User') }
    # Notify Explorer so newly opened terminals inherit the updated user PATH.
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class PaperViewEnvironment {
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr SendMessageTimeout(IntPtr window, uint message, UIntPtr wParam, string lParam, uint flags, uint timeout, out UIntPtr result);
}
'@
    $result = [UIntPtr]::Zero
    [PaperViewEnvironment]::SendMessageTimeout([IntPtr]0xffff, 0x1a, [UIntPtr]::Zero, 'Environment', 2, 3000, [ref]$result) | Out-Null
}

function Assert-Closed {
    foreach ($running in @(Get-Process -Name PaperView -ErrorAction SilentlyContinue)) {
        if ($running.Path -ieq $executable) { throw 'Close PaperView before installing, updating, or uninstalling.' }
    }
}

function Write-Integration {
    if ([IO.Path]::GetFullPath($PSCommandPath) -ine $manager) {
        Copy-Item -LiteralPath $PSCommandPath -Destination $manager -Force
    }
    # A batch launcher waits for GUI executables and runs maintenance outside the app,
    # allowing Windows to release the executable before update/uninstall.
    # The final (goto) closes the batch context before maintenance can delete it;
    # the following PowerShell command supplies the actual maintenance exit code.
    [IO.Directory]::CreateDirectory($binDirectory) | Out-Null
    $batch = @'
@echo off
if /I "%~1"=="install" goto maintenance
if /I "%~1"=="update" goto maintenance
if /I "%~1"=="uninstall" goto maintenance
"%~dp0..\PaperView.exe" %*
exit /b %errorlevel%
:maintenance
if not "%~2"=="" exit /b 2
(goto) 2>nul & powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0..\manage.ps1" -Command "%~1" -Source "%~dp0..\PaperView.exe"
'@
    [IO.File]::WriteAllText($launcher, ($batch -replace '\r?\n', "`r`n") + "`r`n", [Text.Encoding]::ASCII)
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($ShortcutPath)) | Out-Null
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($ShortcutPath)
    $shortcut.TargetPath = $executable
    $shortcut.WorkingDirectory = $installRoot
    $shortcut.Description = 'PaperView PDF viewer'
    $shortcut.Save()
    Show-Progress 90 'Adding terminal command to PATH'
    Change-UserPath $true
}

try {
    Show-Progress 0 'Starting'
    if ($WaitForProcess -gt 0) { Wait-Process -Id $WaitForProcess -Timeout 30 -ErrorAction SilentlyContinue }
    # Serialize maintenance without leaving a locked file in the installation folder.
    $lockId = [BitConverter]::ToString([Security.Cryptography.SHA256]::Create().ComputeHash([Text.Encoding]::UTF8.GetBytes($installRoot.ToLowerInvariant()))).Replace('-', '')
    $lock = [Threading.Mutex]::new($false, "Local\PaperView-$lockId")
    if (-not $lock.WaitOne(0)) { throw 'Another PaperView maintenance command is running.' }
    $ownsLock = $true
    if ($Command -ne 'install' -and -not (Test-Path -LiteralPath $marker)) {
        throw 'PaperView is not installed. Run the release executable with install first.'
    }
    Assert-Closed
    Show-Progress 10 'Checking installation'
    switch ($Command) {
        'install' {
            if (-not $Source -or -not (Test-Path -LiteralPath $Source -PathType Leaf)) { throw 'An existing release executable is required.' }
            if ((Test-Path -LiteralPath $installRoot) -and -not (Test-Path -LiteralPath $marker) -and @(Get-ChildItem -LiteralPath $installRoot -Force).Count -gt 0) {
                throw 'The installation directory contains unrelated files. Installation stopped.'
            }
            [IO.Directory]::CreateDirectory($installRoot) | Out-Null
            Show-Progress 25 'Copying application'
            if ([IO.Path]::GetFullPath($Source) -ine $executable) {
                Copy-Item -LiteralPath $Source -Destination $executable -Force
            }
            Set-Content -LiteralPath $marker -Value 'PaperView per-user installation v1' -Encoding ASCII
            Show-Progress 75 'Creating launcher and shortcut'
            Write-Integration
            Report "Installed PaperView in $installRoot. Open a new terminal to use paperview."
            Show-Progress 100 'Complete'
        }
        'update' {
            Show-Progress 15 'Checking latest release'
            [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
            $headers = @{ 'User-Agent' = 'PaperView-Updater'; Accept = 'application/vnd.github+json' }
            try {
                $release = Invoke-RestMethod -Uri 'https://api.github.com/repos/GRAAAA/Paper_Viewer/releases/latest' -Headers $headers -TimeoutSec 30
            } catch {
                throw "Could not check GitHub Releases. A published release and internet access are required. $($_.Exception.Message)"
            }
            $latest = $null
            if (-not [Version]::TryParse(($release.tag_name -replace '^v', ''), [ref]$latest)) { throw 'The release tag must be a stable version such as v1.0.1.' }
            $current = [Version]([Diagnostics.FileVersionInfo]::GetVersionInfo($executable).FileVersion)
            # Compare three components, avoiding 1.0.0 versus 1.0.0.0 differences.
            if ([Version]$latest.ToString(3) -le [Version]$current.ToString(3)) { Report "PaperView $($current.ToString(3)) is up to date."; Show-Progress 100 'Already up to date'; break }
            $asset = @($release.assets | Where-Object name -eq 'PaperView-win-x64.exe')
            if ($asset.Count -ne 1 -or $asset[0].digest -notmatch '^sha256:[a-fA-F0-9]{64}$') { throw 'The release needs PaperView-win-x64.exe with a GitHub SHA-256 digest.' }
            $uri = [Uri]$asset[0].browser_download_url
            if ($uri.Scheme -ne 'https' -or $uri.Host -ne 'github.com' -or -not $uri.AbsolutePath.StartsWith('/GRAAAA/Paper_Viewer/releases/download/')) { throw 'Unexpected release download URL.' }
            $download = Join-Path $installRoot ('download-' + [Guid]::NewGuid().ToString('N') + '.exe')
            Show-Progress 20 'Downloading release'
            $client = New-Object Net.WebClient
            try {
                foreach ($key in $headers.Keys) { $client.Headers[$key] = $headers[$key] }
                $transfer = $client.DownloadFileTaskAsync($uri, $download)
                $timer = [Diagnostics.Stopwatch]::StartNew()
                while (-not $transfer.IsCompleted) {
                    if ($timer.Elapsed.TotalSeconds -ge 180) { $client.CancelAsync(); throw 'Release download timed out.' }
                    $received = if (Test-Path -LiteralPath $download) { (Get-Item -LiteralPath $download).Length } else { 0 }
                    if ($asset[0].size -gt 0) {
                        $fraction = [Math]::Min(1, $received / $asset[0].size)
                        Show-Progress (20 + [int][Math]::Floor(60 * $fraction)) 'Downloading release'
                    }
                    Start-Sleep -Milliseconds 100
                }
                [void]$transfer.GetAwaiter().GetResult()
            } finally { $client.Dispose() }
            Show-Progress 80 'Verifying checksum and version'
            if ((Get-FileHash -LiteralPath $download -Algorithm SHA256).Hash -ine ($asset[0].digest -replace '^sha256:', '')) { throw 'Downloaded release failed SHA-256 verification.' }
            $actual = [Version]([Diagnostics.FileVersionInfo]::GetVersionInfo($download).FileVersion)
            if ($actual.ToString(3) -ne $latest.ToString(3)) { throw 'Downloaded executable version does not match its release tag.' }
            Assert-Closed
            Show-Progress 90 'Replacing application'
            $backup = Join-Path $installRoot 'PaperView.previous.exe'
            # Atomic replacement leaves the previous executable available on failure.
            [IO.File]::Replace($download, $executable, $backup)
            Remove-Item -LiteralPath $backup -Force
            Report "Updated PaperView to $($latest.ToString(3))."
            Show-Progress 100 'Complete'
        }
        'uninstall' {
            Show-Progress 25 'Removing application'
            # Delete only files owned by PaperView; never recurse through user data.
            if (Test-Path -LiteralPath $executable) { Remove-Item -LiteralPath $executable -Force }
            Show-Progress 50 'Removing terminal PATH entry'
            Change-UserPath $false
            Show-Progress 65 'Removing shortcut'
            if (Test-Path -LiteralPath $ShortcutPath) {
                $shell = New-Object -ComObject WScript.Shell
                if ($shell.CreateShortcut($ShortcutPath).TargetPath -ieq $executable) { Remove-Item -LiteralPath $ShortcutPath -Force }
            }
            Show-Progress 80 'Removing launcher and installation files'
            foreach ($owned in @($manager, $launcher, $marker)) {
                if (Test-Path -LiteralPath $owned) { Remove-Item -LiteralPath $owned -Force }
            }
            if ((Test-Path -LiteralPath $binDirectory) -and @(Get-ChildItem -LiteralPath $binDirectory -Force).Count -eq 0) { [IO.Directory]::Delete($binDirectory) }
            if (@(Get-ChildItem -LiteralPath $installRoot -Force).Count -eq 0) { [IO.Directory]::Delete($installRoot) }
            Report 'Uninstalled PaperView. Reading history, preferences, and PDF files were preserved.'
            Show-Progress 100 'Complete'
        }
    }
    exit 0
} catch {
    Report "PaperView maintenance failed: $($_.Exception.Message)"
    exit 1
} finally {
    if ($download -and (Test-Path -LiteralPath $download)) { Remove-Item -LiteralPath $download -Force }
    if ($lock) { if ($ownsLock) { $lock.ReleaseMutex() }; $lock.Dispose() }
    if ($CleanupScript) {
        Remove-Item -LiteralPath $PSCommandPath -Force
        $scriptFolder = [IO.Path]::GetDirectoryName($PSCommandPath)
        if (@(Get-ChildItem -LiteralPath $scriptFolder -Force).Count -eq 0) { [IO.Directory]::Delete($scriptFolder) }
    }
}
