# PaperView

A native Windows PDF viewer with a recent-document dashboard, real first-page thumbnails, filename search, drag and drop, and remembered reading positions.

by GRAAAA

## Run

Requires Windows 10 version 2004 or later, or Windows 11. Build with the .NET 10 SDK:

```powershell
dotnet run --project PaperView.csproj
```

Open a PDF with **Open PDF**, drop a file on the window, or pass its path on the command line. Scroll down to continue through all pages. Use the arrow buttons or Left/Right keys for pages, enter a page number and press Enter to jump, and use the zoom buttons or Fit width. Ctrl+O opens a file; Escape returns to the library. Click **Hide sidebar** / **Show sidebar**, or press Ctrl+B, to toggle the sidebar. The page number and saved reading position follow your scrolling; only nearby page images are kept in memory.

History and thumbnails are stored in `%LOCALAPPDATA%\PaperView`. Removing a recent entry or clearing history does not delete the original PDF. The library keeps up to 100 recent documents. PDFs are processed locally using Windows.Data.Pdf; no document upload or server is needed.

## Install and manage PaperView

Download `PaperView-win-x64.exe` from a published GitHub Release. You can double-click it to run the viewer without installing, or install from PowerShell:

```powershell
.\PaperView-win-x64.exe install | Out-Host
```

Installation is per-user, requires no administrator privileges, and bundles .NET. It installs into `%LOCALAPPDATA%\Programs\PaperView`, creates a Start menu shortcut, and adds its `bin` directory to your user PATH. Open a new terminal after installation:

```powershell
paperview --version
paperview --help
paperview "C:\Documents\example.pdf"
paperview update
paperview uninstall
```

Close the viewer before updating or uninstalling. Updates use the latest stable release in `GRAAAA/Paper_Viewer`, require a newer `vMAJOR.MINOR.PATCH` tag and a `PaperView-win-x64.exe` asset with a GitHub SHA-256 digest, and verify both the checksum and executable version before replacing the installed file. Network errors or invalid downloads leave the installed executable intact. A release must be published before online updating can work.

Install, update, and uninstall display a 0–100% progress bar with the current step. Installation and removal percentages represent completed stages; the download portion advances using bytes received and the release asset size. An unknown download size stays at the download stage until the transfer finishes. Failed operations stop before 100%. Version and help commands return immediately without a progress bar. Interactive terminals redraw the same line; redirected output emits progress lines for scripts and logs.

Uninstall preserves PDFs, history, thumbnails, and preferences. Maintenance results are recorded in `%LOCALAPPDATA%\PaperView\maintenance.log`. Use `paperview` for maintenance so the terminal waits for completion and returns the command's exit code. Calling the installed `.exe` directly with a maintenance command schedules the operation after that executable exits; check the log for its result. The `| Out-Host` above makes PowerShell wait for the downloaded GUI executable and display progress as it arrives; `Out-String` buffers output until completion.

## Build a distributable executable

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/publish.ps1 -Version 1.0.0
```

Distribute **only** `artifacts\release-1.0.0\PaperView-win-x64.exe`. It is a self-contained single-file Windows x64 build; users do not need the project files or a separate .NET installation. Native runtime components extract automatically at launch. `SHA256SUMS.txt` is also generated for download verification.

For a later release, rerun with a higher version, then manually publish the executable and checksum file in GitHub Releases with the matching tag (for example, `v1.0.1`). The script only creates local artifacts; it does not commit, push, tag, or publish. Builds are currently unsigned, so Windows may show a publisher warning when downloaded.

## Scope

Use **Dark mode / Light mode** in the reader or dashboard to switch the monochrome interface. The choice is saved locally. PDF page colors remain faithful to the document. Enable **Loupe**, then press and hold on a rendered page for a 2.5× circular magnifier. Move while holding to inspect nearby content; release to dismiss it without changing page zoom or position.

Opening a PDF switches to a focused reader with the dashboard, sidebar, and footer hidden. A compact toolbar provides Library, Open, navigation, zoom, and a sidebar toggle. Pages automatically fit the available width, preserve their original proportions, and adapt when the window is resized. Manual zoom overrides automatic fitting until Fit width is selected again.

Pages are rendered as images. Text selection, text search within PDFs, annotations, printing, and password entry are not implemented. Password-protected, damaged, or missing documents show an error without removing their history entry.

Rendering API reference: https://learn.microsoft.com/en-us/uwp/api/windows.data.pdf.pdfpage.rendertostreamasync

## Development

```powershell
dotnet run --project tests/Smoke.csproj -c Release
powershell -NoProfile -ExecutionPolicy Bypass -File tests/Packaging.ps1
```

The smoke checks use generated PDFs and isolated data under the test output directory. They exercise rendering, continuous navigation, cached thumbnails, saved history, themes, and the loupe without opening a visible test window.

Packaging checks require the local 1.0.0 release build. They use isolated installation folders and shortcuts under `artifacts`, skip user PATH changes, and simulate GitHub responses locally. They cover CLI output and exit codes, repeated installation, a successful update, checksum and version rejection, and uninstall file preservation. No release is uploaded or downloaded by these tests.

- `App.xaml`, `MainWindow.xaml`, `Theme.xaml`: startup, layout, and monochrome control styles.
- `UI/`: continuous reading, theme switching, and loupe interactions.
- `Services/`: local history, thumbnail caching, and Windows PDF rendering.
- `tests/`: executable integration checks and generated PDF fixtures.
- `artifacts/PaperView/`: standalone Windows build, excluded from Git.

Page lookup uses binary search. Scrolling only scans the nearby page range and cached images; distant bitmaps are released. Returning to the library releases the active document's page visuals. Build outputs, package caches, and local tools are excluded from Git.
