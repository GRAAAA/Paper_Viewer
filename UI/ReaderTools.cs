using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PaperView;

public partial class MainWindow
{
    private bool darkTheme;
    private bool loupeEnabled;
    private bool loupeHeld;

    private void InitializeReaderTools()
    {
        try { darkTheme = File.ReadAllText(Path.Combine(library.Root, "theme.txt")).Trim() == "dark"; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        ApplyTheme();
        Deactivated += (_, _) => HideLoupe();
    }

    private void Theme_Click(object sender, RoutedEventArgs e)
    {
        darkTheme = !darkTheme; ApplyTheme();
        try { File.WriteAllText(Path.Combine(library.Root, "theme.txt"), darkTheme ? "dark" : "light"); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { StatusLabel.Text = "Theme changed for this session; preference could not be saved."; }
    }

    private void ApplyTheme()
    {
        string[] keys = ["AppBackground", "PanelBackground", "Surface", "CanvasBackground", "Ink", "Muted", "Outline", "HoverBackground", "Accent", "AccentText"];
        string[] colors = darkTheme
            ? ["#181818", "#202020", "#282828", "#101010", "#F0F0F0", "#B5B5B5", "#484848", "#383838", "#F0F0F0", "#181818"]
            : ["#F7F7F7", "#EEEEEE", "#FFFFFF", "#DDDDDD", "#202020", "#666666", "#CCCCCC", "#E0E0E0", "#171717", "#FFFFFF"];
        for (var i = 0; i < keys.Length; i++)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[i]));
            brush.Freeze();
            Resources[keys[i]] = brush;
        }
        ReaderThemeButton.Content = DashboardThemeButton.Content = darkTheme ? "Light mode" : "Dark mode";
    }

    private void Loupe_Click(object sender, RoutedEventArgs e)
    {
        HideLoupe(); loupeEnabled = !loupeEnabled;
        LoupeButton.Content = loupeEnabled ? "Loupe: on" : "Loupe";
        LoupeButton.SetResourceReference(Control.BackgroundProperty, loupeEnabled ? "HoverBackground" : "Surface");
        PagesCanvas.Cursor = loupeEnabled ? Cursors.Cross : Cursors.Arrow;
    }

    private bool ShowLoupe(Point documentPoint, Point surfacePoint)
    {
        if (!loupeEnabled || document == null) return false;
        if (pageViews.Count == 0) return false;
        var page = pageViews[FindPageAtOffset(documentPoint.Y)];
        if (page.Image.Source == null || documentPoint.Y < page.Top || documentPoint.Y > page.Top + page.Frame.Height
            || documentPoint.X < Canvas.GetLeft(page.Frame) || documentPoint.X > Canvas.GetLeft(page.Frame) + page.Frame.Width) return false;
        const double diameter = 240, magnification = 2.5;
        var area = diameter / magnification;
        var brush = LoupeLens.Fill as VisualBrush ?? new VisualBrush(PagesCanvas) { ViewboxUnits = BrushMappingMode.Absolute, Stretch = Stretch.Fill };
        brush.Viewbox = new Rect(documentPoint.X - area / 2, documentPoint.Y - area / 2, area, area);
        LoupeLens.Fill = brush;
        Canvas.SetLeft(LoupeLens, Math.Clamp(surfacePoint.X - diameter / 2, 0, Math.Max(0, ReaderSurface.ActualWidth - diameter)));
        Canvas.SetTop(LoupeLens, Math.Clamp(surfacePoint.Y - diameter / 2, 0, Math.Max(0, ReaderSurface.ActualHeight - diameter)));
        LoupeLens.Visibility = Visibility.Visible;
        return true;
    }

    private void Reader_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (ShowLoupe(e.GetPosition(PagesCanvas), e.GetPosition(ReaderSurface)))
        {
            loupeHeld = true; ReaderSurface.CaptureMouse(); e.Handled = true;
        }
    }
    private void Reader_MouseMove(object sender, MouseEventArgs e)
    {
        if (!loupeHeld) return;
        if (e.LeftButton != MouseButtonState.Pressed) { HideLoupe(); return; }
        if (!ShowLoupe(e.GetPosition(PagesCanvas), e.GetPosition(ReaderSurface))) LoupeLens.Visibility = Visibility.Collapsed;
    }
    private void Reader_MouseUp(object sender, MouseButtonEventArgs e) { if (loupeHeld) { HideLoupe(); e.Handled = true; } }
    private void Reader_LostCapture(object sender, MouseEventArgs e) => HideLoupe();
    private void HideLoupe()
    {
        loupeHeld = false; LoupeLens.Visibility = Visibility.Collapsed; LoupeLens.Fill = null;
        if (ReaderSurface.IsMouseCaptured) ReaderSurface.ReleaseMouseCapture();
    }
}
