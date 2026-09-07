using System.Windows;
namespace PaperView;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            MainWindow = new MainWindow();
            MainWindow.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show("PaperView could not start.\n\n" + ex.GetBaseException().Message,
                "PaperView — startup error", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
}
