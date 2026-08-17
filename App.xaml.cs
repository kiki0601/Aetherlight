using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace Aetherlight;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        base.OnStartup(e);
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        ShowException("Aetherlight • Startup/Runtime Error", e.Exception);
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex) ShowException("Aetherlight • Fatal Error", ex);
    }

    private static void ShowException(string title, Exception ex)
    {
        var sb = new StringBuilder();
        int level = 0;
        for (Exception? current = ex; current != null && level < 8; current = current.InnerException, level++)
        {
            sb.AppendLine($"[{level}] {current.GetType().FullName}");
            sb.AppendLine(current.Message);
            if (!string.IsNullOrWhiteSpace(current.StackTrace)) sb.AppendLine(current.StackTrace);
            sb.AppendLine();
        }
        MessageBox.Show("Aetherlight could not start.\n\n" + sb, title, MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
