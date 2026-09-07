using System.IO;
using System.Text;
using PaperView;

var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "smoke-data"));
Directory.CreateDirectory(root);
var pdfPath = Path.Combine(root, "two pages.pdf");
var pdf = new StringBuilder("%PDF-1.4\n");
var offsets = new List<int> { 0 };
string[] objects = [
    "<< /Type /Catalog /Pages 2 0 R >>",
    "<< /Type /Pages /Kids [3 0 R 4 0 R] /Count 2 >>",
    "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 5 0 R >> >> /Contents 6 0 R >>",
    "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 5 0 R >> >> /Contents 7 0 R >>",
    "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
    Stream("BT /F1 30 Tf 72 700 Td (PaperView - first page) Tj ET"),
    Stream("BT /F1 30 Tf 72 700 Td (PaperView - second page) Tj ET")
];
for (var i = 0; i < objects.Length; i++) { offsets.Add(pdf.Length); pdf.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n"); }
var xref = pdf.Length;
pdf.Append($"xref\n0 {offsets.Count}\n0000000000 65535 f \n");
foreach (var offset in offsets.Skip(1)) pdf.Append($"{offset:D10} 00000 n \n");
pdf.Append($"trailer\n<< /Size {offsets.Count} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
File.WriteAllText(pdfPath, pdf.ToString(), Encoding.ASCII);
var document = await PdfRenderer.OpenAsync(pdfPath);
Check(document.PageCount == 2, "Open real two-page PDF");
var first = await PdfRenderer.RenderAsync(document, 0, 360);
var second = await PdfRenderer.RenderAsync(document, 1, 1400);
Check(Library.ReadImage(first).PixelWidth == 360, "Generate first-page thumbnail");
Check(Library.ReadImage(second).PixelWidth == 1400, "Render second page at reading resolution");
Check(!first.SequenceEqual(second), "Distinct pages render distinctly");
var library = new Library(Path.Combine(root, "library"));
library.Items.Clear();
var entry = new RecentPdf { FilePath = pdfPath, LastPage = 1, PageCount = 2, LastOpened = DateTime.UtcNow, Size = new FileInfo(pdfPath).Length };
library.Items.Add(entry);
File.WriteAllBytes(library.ThumbnailPath(pdfPath), first);
library.Save();
var restored = new Library(library.Root);
Check(restored.Items.Count == 1 && restored.Items[0].LastPage == 1, "Restore recent file and reading position");
Check(restored.Items[0].Thumbnail?.PixelWidth == 360, "Restore cached thumbnail");
restored.Remove(restored.Items[0]);
Check(new Library(library.Root).Items.Count == 0 && File.Exists(pdfPath), "Remove history while preserving source PDF");
File.WriteAllText(Path.Combine(library.Root, "history.json"), "broken json");
Check(new Library(library.Root).LoadWarning != null, "Recover gracefully from damaged history");
var badPath = Path.Combine(root, "invalid.pdf");
File.WriteAllText(badPath, "not a PDF");
bool rejected = false;
try { await PdfRenderer.OpenAsync(badPath); } catch { rejected = true; }
Check(rejected, "Reject invalid PDF");
var unavailableRoot = Path.Combine(root, "storage-is-a-file");
File.WriteAllText(unavailableRoot, "This file prevents creating a history directory here.");
var unavailableLibrary = new Library(unavailableRoot);
Check(unavailableLibrary.LoadWarning != null && unavailableLibrary.Items.Count == 0,
    "Unavailable history storage does not crash startup");
Exception? uiError = null;
var uiThread = new Thread(() =>
{
    var dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
    SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext(dispatcher));
    dispatcher.InvokeAsync(async () =>
    {
        try
        {
            var app = new System.Windows.Application();
            app.Resources.MergedDictionaries.Add(new System.Windows.ResourceDictionary { Source = new Uri("/PaperView;component/Theme.xaml", UriKind.Relative) });
            var window = new MainWindow(Path.Combine(root, "ui-library"));
            window.Measure(new System.Windows.Size(1220, 820)); window.Arrange(new System.Windows.Rect(0, 0, 1220, 820));
            Check(window.FindName("Dashboard") is System.Windows.Controls.Grid, "Load dashboard XAML and layout");
            var open = typeof(MainWindow).GetMethod("OpenPdfAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            await (Task)open.Invoke(window, [pdfPath])!;
            var layoutRoot = (System.Windows.FrameworkElement)window.Content;
            layoutRoot.Measure(new System.Windows.Size(1220, 820));
            layoutRoot.Arrange(new System.Windows.Rect(0, 0, 1220, 820));
            layoutRoot.UpdateLayout();
            var canvas = (System.Windows.Controls.Canvas)window.FindName("PagesCanvas");
            Check(canvas.Children.Count == 2, "Lay out both PDF pages for continuous reading");
            Check(((System.Windows.Controls.Grid)window.FindName("Dashboard")).Visibility == System.Windows.Visibility.Collapsed,
                "Opening a PDF hides the dashboard");
            Check(((System.Windows.Controls.Border)window.FindName("Sidebar")).Visibility == System.Windows.Visibility.Collapsed,
                "Opening a PDF hides the sidebar automatically");
            Check(((System.Windows.Controls.DockPanel)window.FindName("DashboardFooter")).Visibility == System.Windows.Visibility.Collapsed,
                "Reading view hides dashboard footer");
            var frame = (System.Windows.Controls.Border)canvas.Children[0];
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
            var visibleRender = typeof(MainWindow).GetMethod("RenderVisiblePagesAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            await (Task)visibleRender.Invoke(window, null)!;
            Check(((System.Windows.Controls.Image)((System.Windows.Controls.Grid)frame.Child).Children[1]).Source != null, "Display PDF in reader");
            var render = typeof(MainWindow).GetMethod("RenderPageAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            await (Task)render.Invoke(window, [(uint)1, true])!;
            Check(((System.Windows.Controls.TextBox)window.FindName("PageBox")).Text == "2", "Navigate reader to page two");
            Check(new Library(Path.Combine(root, "ui-library")).Items[0].LastPage == 1, "Reader persists actual navigation");
            var scroller = (System.Windows.Controls.ScrollViewer)window.FindName("PageScroller");
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
            Check(Math.Abs(frame.Width - (scroller.ViewportWidth - 16)) < 2, "PDF page fills available reading width");
            Check(Math.Abs(frame.Width / frame.Height - 612d / 792) < .001, "Fitting preserves page proportions");
            scroller.ScrollToTop(); window.UpdateLayout();
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
            Check(((System.Windows.Controls.TextBox)window.FindName("PageBox")).Text == "1", "Scrolling upward tracks page one");
            scroller.ScrollToBottom(); window.UpdateLayout();
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
            Check(((System.Windows.Controls.TextBox)window.FindName("PageBox")).Text == "2", "Scrolling downward reaches page two");
            Check(new Library(Path.Combine(root, "ui-library")).Items[0].LastPage == 1, "Scrolling persists reading position");
            var toggle = (System.Windows.Controls.Button)window.FindName("SidebarToggle");
            toggle.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Check(((System.Windows.Controls.Border)window.FindName("Sidebar")).Visibility == System.Windows.Visibility.Visible,
                "Restore sidebar using the same toggle");
            toggle.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Check(((System.Windows.Controls.ColumnDefinition)window.FindName("SidebarColumn")).Width.Value == 0,
                "Hide sidebar and reclaim its width");
            var themeButton = (System.Windows.Controls.Button)window.FindName("ReaderThemeButton");
            themeButton.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            var themeColor = ((System.Windows.Media.SolidColorBrush)window.Resources["AppBackground"]).Color;
            Check(themeColor.R == themeColor.G && themeColor.G == themeColor.B, "Theme uses neutral monochrome colors");
            var savedTheme = File.ReadAllText(Path.Combine(root, "ui-library", "theme.txt"));
            Check(savedTheme == (themeColor.R < 128 ? "dark" : "light"), "Theme choice persists");
            var nextWindow = new MainWindow(Path.Combine(root, "ui-library"));
            Check(((System.Windows.Media.SolidColorBrush)nextWindow.Resources["AppBackground"]).Color == themeColor, "Restore selected theme on next launch");
            nextWindow.Close();
            themeButton.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Check(((System.Windows.Media.SolidColorBrush)window.Resources["AppBackground"]).Color != themeColor, "Switch between light and dark themes");
            var loupeButton = (System.Windows.Controls.Button)window.FindName("LoupeButton");
            loupeButton.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            await (Task)visibleRender.Invoke(window, null)!;
            var targetFrame = (System.Windows.Controls.Border)canvas.Children[1];
            var point = new System.Windows.Point(System.Windows.Controls.Canvas.GetLeft(targetFrame) + 120, System.Windows.Controls.Canvas.GetTop(targetFrame) + 120);
            var oldOffset = scroller.VerticalOffset;
            var showLoupe = typeof(MainWindow).GetMethod("ShowLoupe", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            Check((bool)showLoupe.Invoke(window, [point, new System.Windows.Point(200, 200)])!, "Loupe magnifies a rendered PDF area");
            var lens = (System.Windows.Shapes.Ellipse)window.FindName("LoupeLens");
            var brush = (System.Windows.Media.VisualBrush)lens.Fill;
            Check(brush.Viewbox.Width == 96 && brush.Viewbox.X + 48 == point.X, "Loupe centers 2.5x magnification on pressed location");
            typeof(MainWindow).GetMethod("HideLoupe", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(window, null);
            Check(lens.Visibility == System.Windows.Visibility.Collapsed && scroller.VerticalOffset == oldOffset, "Dismissing loupe preserves reading position");
            Check(((System.Windows.Controls.TextBlock)window.FindName("AuthorCredit")).Text == "by GRAAAA", "Author credit appears at the bottom");
            typeof(MainWindow).GetMethod("Home_Click", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(window, [window, new System.Windows.RoutedEventArgs()]);
            Check(canvas.Children.Count == 0, "Returning to the dashboard releases page visuals");
            layoutRoot.Measure(new System.Windows.Size(1220, 820));
            layoutRoot.Arrange(new System.Windows.Rect(0, 0, 1220, 820));
            layoutRoot.UpdateLayout();
            Capture(layoutRoot, Path.Combine(root, "dashboard-theme-1.png"));
            themeButton.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            layoutRoot.UpdateLayout();
            Capture(layoutRoot, Path.Combine(root, "dashboard-theme-2.png"));
            window.Close();
        }
        catch (Exception ex) { uiError = ex; }
        finally { dispatcher.BeginInvokeShutdown(System.Windows.Threading.DispatcherPriority.Background); }
    });
    System.Windows.Threading.Dispatcher.Run();
});
uiThread.SetApartmentState(ApartmentState.STA); uiThread.Start();
if (!uiThread.Join(TimeSpan.FromSeconds(45))) throw new Exception("UI smoke test timed out");
if (uiError != null) { Console.Error.WriteLine("FAIL: " + uiError.Message); Environment.ExitCode = 1; return; }
Console.WriteLine("All smoke checks passed.");
static string Stream(string text) => $"<< /Length {text.Length} >>\nstream\n{text}\nendstream";
static void Check(bool condition, string name) { if (!condition) throw new Exception(name); Console.WriteLine("PASS: " + name); }
static void Capture(System.Windows.FrameworkElement element, string path)
{
    var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(1220, 820, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
    bitmap.Render(element);
    var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
    encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
    using var output = File.Create(path); encoder.Save(output);
}
