using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using Windows.Data.Pdf;

namespace PaperView;

public partial class MainWindow : Window
{
    private readonly Library library;
    private PdfDocument? document;
    private RecentPdf? current;
    private uint pageIndex;
    private double zoom = 1;
    private bool opening;
    private int renderVersion;
    public MainWindow() : this(null) { }
    public MainWindow(string? dataDirectory)
    {
        InitializeComponent();
        library = new Library(dataDirectory);
        InitializeReaderTools();
        RefreshLibrary();
        if (library.LoadWarning != null) StatusLabel.Text = library.LoadWarning;
        Loaded += async (_, _) =>
        {
            var path = Environment.GetCommandLineArgs().Skip(1).FirstOrDefault(File.Exists);
            if (path != null) await OpenPdfAsync(path);
        };
    }
    private void RefreshLibrary()
    {
        if (library == null) return;
        var filtered = library.Items.Where(x => x.Name.Contains(SearchBox.Text.Trim(), StringComparison.OrdinalIgnoreCase)).OrderByDescending(x => x.LastOpened).ToList();
        RecentItems.ItemsSource = filtered;
        CountLabel.Text = library.Items.Count.ToString();
        EmptyState.Visibility = filtered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyLabel.Text = library.Items.Count == 0 ? "Open a PDF to see its preview in your library." : "No PDFs match your search. Try another name.";
    }
    private void SaveHistory()
    {
        try { library.Save(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { StatusLabel.Text = "The PDF is open, but history could not be saved: " + ex.Message; }
    }
    private async Task OpenPdfAsync(string path)
    {
        if (opening) return;
        if (!string.Equals(Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase)) { StatusLabel.Text = "Please choose a PDF file."; return; }
        opening = true;
        HideLoupe();
        StatusLabel.Text = "Opening " + Path.GetFileName(path) + "…";
        try
        {
            path = Path.GetFullPath(path);
            var loaded = await PdfRenderer.OpenAsync(path);
            if (loaded.PageCount == 0) throw new InvalidDataException("This PDF has no pages.");
            var entry = library.Items.FirstOrDefault(x => string.Equals(x.FilePath, path, StringComparison.OrdinalIgnoreCase)) ?? new RecentPdf { FilePath = path };
            var initialPage = Math.Min(entry.LastPage, loaded.PageCount - 1);
            var firstRender = await PdfRenderer.RenderAsync(loaded, initialPage, 1400);
            var thumbnail = await PdfRenderer.RenderAsync(loaded, 0, 360);
            ++renderVersion;
            document = loaded; current = entry; pageIndex = initialPage; zoom = 1;
            entry.PageCount = loaded.PageCount; entry.Size = new FileInfo(path).Length; entry.LastOpened = DateTime.UtcNow; entry.LastPage = pageIndex;
            entry.Thumbnail = Library.ReadImage(thumbnail);
            library.Items.Remove(entry); library.Items.Insert(0, entry);
            while (library.Items.Count > 100) library.Remove(library.Items[^1]);
            Dashboard.Visibility = Visibility.Collapsed; Reader.Visibility = Visibility.Visible;
            SetReadingView(true);
            fitWidth = true;
            Title = entry.Name + " — PaperView";
            BuildPages();
            pageViews[(int)initialPage].Image.Source = Library.ReadImage(firstRender);
            cachedPages.Add(pageViews[(int)initialPage]);
            UpdateReaderControls();
            PageScroller.UpdateLayout();
            LayoutPages();
            PageScroller.UpdateLayout();
            PageScroller.ScrollToVerticalOffset(pageViews[(int)initialPage].Top);
            StatusLabel.Text = "Ready  ·  Use ← / → to turn pages  ·  Ctrl+O to open another PDF";
            try { await File.WriteAllBytesAsync(library.ThumbnailPath(path), thumbnail); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { StatusLabel.Text = "PDF opened. The thumbnail could not be cached."; }
            SaveHistory(); RefreshLibrary();
        }
        catch (Exception ex)
        {
            StatusLabel.Text = "Unable to open PDF.";
            MessageBox.Show(this, "This PDF could not be opened. It may have moved, be password-protected, or be damaged.\n\n" + ex.Message, "Could not open PDF", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { opening = false; }
        if (Reader.Visibility == Visibility.Visible) await RenderVisiblePagesAsync();
    }
    private void ApplyImageWidth()
    {
        if (document == null) return;
        LayoutPages();
    }
    private void UpdateReaderControls()
    {
        PageBox.Text = (pageIndex + 1).ToString();
        PageCountLabel.Text = $"of {document?.PageCount}";
        PreviousButton.IsEnabled = pageIndex > 0;
        NextButton.IsEnabled = document != null && pageIndex + 1 < document.PageCount;
    }
    private async Task RenderPageAsync(uint target, bool resetScroll = true)
    {
        if (document == null || target >= document.PageCount || opening) return;
        var oldPage = pageViews[(int)pageIndex];
        var fraction = Math.Clamp((PageScroller.VerticalOffset - oldPage.Top) / Math.Max(1, oldPage.Frame.Height), 0, 1);
        ++renderVersion;
        if (!resetScroll) foreach (var view in pageViews) view.Image.Source = null;
        pageIndex = target;
        ApplyImageWidth(); UpdateReaderControls();
        PageScroller.UpdateLayout();
        var targetPage = pageViews[(int)target];
        PageScroller.ScrollToVerticalOffset(targetPage.Top + (resetScroll ? 0 : fraction * targetPage.Frame.Height));
        if (current != null) { current.LastPage = pageIndex; SaveHistory(); }
        await RenderVisiblePagesAsync();
    }
    private async void Open_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "PDF documents (*.pdf)|*.pdf", Title = "Open a PDF" };
        if (dialog.ShowDialog(this) == true) await OpenPdfAsync(dialog.FileName);
    }
    private void Home_Click(object sender, RoutedEventArgs e)
    {
        if (opening) return;
        HideLoupe();
        ++renderVersion; Dashboard.Visibility = Visibility.Visible; Reader.Visibility = Visibility.Collapsed;
        SetReadingView(false);
        document = null; current = null;
        PagesCanvas.Children.Clear(); pageViews.Clear(); cachedPages.Clear();
        Title = "PaperView"; RefreshLibrary(); StatusLabel.Text = "Ready  ·  Ctrl+O to open a PDF";
    }
    private async void Recent_Click(object sender, RoutedEventArgs e) { if (sender is Button { Tag: RecentPdf item }) await OpenPdfAsync(item.FilePath); }
    private void Search_Changed(object sender, TextChangedEventArgs e) => RefreshLibrary();
    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: RecentPdf item }) return;
        try { library.Remove(item); RefreshLibrary(); }
        catch (Exception ex) { StatusLabel.Text = "Could not update history: " + ex.Message; }
    }
    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        try { library.Clear(); RefreshLibrary(); StatusLabel.Text = "History cleared. Your PDF files are unchanged."; }
        catch (Exception ex) { StatusLabel.Text = "Could not clear history: " + ex.Message; }
    }
    private async void Previous_Click(object sender, RoutedEventArgs e) { if (pageIndex > 0) await RenderPageAsync(pageIndex - 1); }
    private async void Next_Click(object sender, RoutedEventArgs e) => await RenderPageAsync(pageIndex + 1);
    private async void ZoomOut_Click(object sender, RoutedEventArgs e) { fitWidth = false; zoom = Math.Max(.25, zoom - .25); await RenderPageAsync(pageIndex, false); }
    private async void ZoomIn_Click(object sender, RoutedEventArgs e) { fitWidth = false; zoom = Math.Min(4, zoom + .25); await RenderPageAsync(pageIndex, false); }
    private async void Fit_Click(object sender, RoutedEventArgs e)
    {
        if (document == null) return;
        fitWidth = true;
        await RenderPageAsync(pageIndex, false);
    }
    private async void PageBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        if (uint.TryParse(PageBox.Text, out var number) && number > 0 && number <= document?.PageCount) await RenderPageAsync(number - 1);
        else { UpdateReaderControls(); StatusLabel.Text = "Enter a page number within this document."; }
    }
    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths && paths.Length > 0) await OpenPdfAsync(paths[0]);
    }
    private async void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.O && Keyboard.Modifiers == ModifierKeys.Control) { e.Handled = true; Open_Click(sender, e); }
        else if (e.Key == Key.B && Keyboard.Modifiers == ModifierKeys.Control) { e.Handled = true; SidebarToggle_Click(sender, e); }
        else if (Reader.Visibility == Visibility.Visible && e.OriginalSource is not TextBox)
        {
            if (e.Key == Key.Left && pageIndex > 0) { e.Handled = true; await RenderPageAsync(pageIndex - 1); }
            else if (e.Key == Key.Right) { e.Handled = true; await RenderPageAsync(pageIndex + 1); }
            else if (e.Key == Key.Escape) Home_Click(sender, e);
        }
    }
}
