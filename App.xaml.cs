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
        MessageBox.Show($"Aetherlight encountered an error while starting or running.\n\n{e.Exception.GetType().Name}:\n{e.Exception.Message}\n\nDetails:\n{e.Exception.StackTrace}", "Aetherlight • Error", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
            MessageBox.Show($"Aetherlight stopped unexpectedly.\n\n{ex.GetType().Name}:\n{ex.Message}\n\nDetails:\n{ex.StackTrace}", "Aetherlight • Fatal Error", MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
