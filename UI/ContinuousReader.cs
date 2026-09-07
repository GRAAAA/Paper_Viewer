using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PaperView;

public partial class MainWindow
{
    private sealed class PageView
    {
        public required Image Image { get; init; }
        public required Border Frame { get; init; }
        public double Width { get; init; }
        public double Height { get; init; }
        public double Top { get; set; }
    }
    private readonly List<PageView> pageViews = [];
    private readonly SemaphoreSlim renderGate = new(1, 1);
    private bool fitWidth = true;
    private double fittedViewportWidth;
    private bool dashboardSidebarVisible = true;
    private readonly HashSet<PageView> cachedPages = [];

    private int FindPageAtOffset(double offset)
    {
        var low = 0; var high = pageViews.Count - 1;
        while (low < high)
        {
            var mid = (low + high + 1) / 2;
            if (pageViews[mid].Top <= offset) low = mid;
            else high = mid - 1;
        }
        return low;
    }

    private void SetReadingView(bool reading)
    {
        if (reading && DashboardFooter.Visibility == Visibility.Visible) dashboardSidebarVisible = Sidebar.Visibility == Visibility.Visible;
        SetSidebarVisible(!reading && dashboardSidebarVisible);
        ContentArea.Margin = reading ? new Thickness(0) : new Thickness(32, 28, 32, 28);
        DashboardFooter.Visibility = reading ? Visibility.Collapsed : Visibility.Visible;
    }

    private void SetSidebarVisible(bool visible)
    {
        Sidebar.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        SidebarColumn.Width = new GridLength(visible ? 220 : 0);
        SidebarToggle.Content = visible ? "Hide sidebar" : "Show sidebar";
    }

    private void SidebarToggle_Click(object sender, RoutedEventArgs e)
    {
        SetSidebarVisible(Sidebar.Visibility != Visibility.Visible);
    }

    private void BuildPages()
    {
        PagesCanvas.Children.Clear(); pageViews.Clear(); cachedPages.Clear();
        if (document == null) return;
        for (uint i = 0; i < document.PageCount; i++)
        {
            using var page = document.GetPage(i);
            var image = new Image { Stretch = Stretch.Fill };
            var content = new Grid();
            content.Children.Add(new TextBlock
            {
                Text = $"Page {i + 1}",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Brushes.Gray
            });
            content.Children.Add(image);
            var frame = new Border { Background = Brushes.White, Child = content };
            pageViews.Add(new PageView { Image = image, Frame = frame, Width = page.Size.Width, Height = page.Size.Height });
            PagesCanvas.Children.Add(frame);
        }
        LayoutPages();
    }

    private void LayoutPages()
    {
        fittedViewportWidth = PageScroller.ViewportWidth;
        var availableWidth = Math.Max(100, fittedViewportWidth > 0 ? fittedViewportWidth - 16 : ActualWidth - 34);
        var width = fitWidth ? availableWidth + 16 : pageViews.Count == 0 ? 0 : pageViews.Max(p => p.Width) * zoom + 16;
        double top = 8;
        foreach (var view in pageViews)
        {
            view.Top = top;
            var scale = fitWidth ? availableWidth / view.Width : zoom;
            view.Frame.Width = view.Width * scale; view.Frame.Height = view.Height * scale;
            Canvas.SetTop(view.Frame, top); Canvas.SetLeft(view.Frame, (width - view.Frame.Width) / 2);
            top += view.Frame.Height + 8;
        }
        PagesCanvas.Width = width; PagesCanvas.Height = top;
        if (fitWidth && pageViews.Count > 0) zoom = pageViews[(int)pageIndex].Frame.Width / pageViews[(int)pageIndex].Width;
        ZoomLabel.Text = fitWidth ? "Fit width" : $"{zoom:P0}";
    }

    private async void PageScroller_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (opening || document == null || Reader.Visibility != Visibility.Visible || pageViews.Count == 0) return;
        HideLoupe();
        if (fitWidth && Math.Abs(PageScroller.ViewportWidth - fittedViewportWidth) > 1)
        {
            await RenderPageAsync(pageIndex, false);
            return;
        }
        // Track the page at the upper part of the viewport, including at the bottom of the document.
        var marker = PageScroller.VerticalOffset + Math.Min(100, PageScroller.ViewportHeight / 3);
        var visiblePage = (uint)FindPageAtOffset(marker);
        if (pageIndex != visiblePage)
        {
            pageIndex = visiblePage; UpdateReaderControls();
            if (fitWidth) zoom = pageViews[(int)pageIndex].Frame.Width / pageViews[(int)pageIndex].Width;
            if (current != null) { current.LastPage = pageIndex; SaveHistory(); }
        }
        await RenderVisiblePagesAsync();
    }

    private async Task RenderVisiblePagesAsync()
    {
        var version = ++renderVersion;
        await renderGate.WaitAsync();
        try
        {
            if (version != renderVersion || document == null || Reader.Visibility != Visibility.Visible) return;
            var renderingDocument = document;
            var top = PageScroller.VerticalOffset;
            var bottom = top + Math.Max(PageScroller.ViewportHeight, 600);
            // Keep only nearby bitmaps. Placeholder geometry preserves smooth scrolling in large PDFs.
            var first = FindPageAtOffset(top - 900);
            var last = FindPageAtOffset(bottom + 900);
            var nearby = Enumerable.Range(first, last - first + 1).Select(index => (view: pageViews[index], index)).ToList();
            foreach (var view in cachedPages.ToArray())
                if (view.Top + view.Frame.Height < top - 900 || view.Top > bottom + 900)
                { view.Image.Source = null; cachedPages.Remove(view); }
            var dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
            foreach (var (view, index) in nearby.OrderBy(p => Math.Abs(p.view.Top - top)))
            {
                if (version != renderVersion) return;
                if (view.Image.Source != null) continue;
                var bytes = await PdfRenderer.RenderAsync(renderingDocument, (uint)index, (uint)Math.Clamp(view.Frame.Width * dpi, 300, 4096));
                if (version != renderVersion) return;
                view.Image.Source = Library.ReadImage(bytes);
                cachedPages.Add(view);
            }
            StatusLabel.Text = "Ready · Scroll to read more pages · Ctrl+B toggles the sidebar";
        }
        catch (Exception ex) { if (version == renderVersion) StatusLabel.Text = "Could not render a page: " + ex.Message; }
        finally { renderGate.Release(); }
    }
}
