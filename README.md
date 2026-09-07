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

## Publish a standalone Windows build

```powershell
dotnet publish PaperView.csproj -c Release -r win-x64 --self-contained true -o artifacts/PaperView
```

Launch `artifacts\PaperView\PaperView.exe`. Distribute the entire folder.

## Scope

Use **Dark mode / Light mode** in the reader or dashboard to switch the monochrome interface. The choice is saved locally. PDF page colors remain faithful to the document. Enable **Loupe**, then press and hold on a rendered page for a 2.5× circular magnifier. Move while holding to inspect nearby content; release to dismiss it without changing page zoom or position.

Opening a PDF switches to a focused reader with the dashboard, sidebar, and footer hidden. A compact toolbar provides Library, Open, navigation, zoom, and a sidebar toggle. Pages automatically fit the available width, preserve their original proportions, and adapt when the window is resized. Manual zoom overrides automatic fitting until Fit width is selected again.

Pages are rendered as images. Text selection, text search within PDFs, annotations, printing, and password entry are not implemented. Password-protected, damaged, or missing documents show an error without removing their history entry.

Rendering API reference: https://learn.microsoft.com/en-us/uwp/api/windows.data.pdf.pdfpage.rendertostreamasync

## Development

```powershell
dotnet run --project tests/Smoke.csproj -c Release
```

The smoke checks use generated PDFs and isolated data under the test output directory. They exercise rendering, continuous navigation, cached thumbnails, saved history, themes, and the loupe without opening a visible test window.

- `App.xaml`, `MainWindow.xaml`, `Theme.xaml`: startup, layout, and monochrome control styles.
- `UI/`: continuous reading, theme switching, and loupe interactions.
- `Services/`: local history, thumbnail caching, and Windows PDF rendering.
- `tests/`: executable integration checks and generated PDF fixtures.
- `artifacts/PaperView/`: standalone Windows build, excluded from Git.

Page lookup uses binary search. Scrolling only scans the nearby page range and cached images; distant bitmaps are released. Returning to the library releases the active document's page visuals. Build outputs, package caches, and local tools are excluded from Git.
