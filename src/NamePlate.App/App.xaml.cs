using System.Windows;
using System.Windows.Threading;
using NamePlateApp.Services;
using NamePlateApp.ViewModels;

namespace NamePlateApp;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandled;
        // diagnostics: set NAMEPLATE_BINDING_LOG=<file> to record WPF binding errors (they are otherwise silent)
        if (Environment.GetEnvironmentVariable("NAMEPLATE_BINDING_LOG") is { Length: > 0 } log)
        {
            System.Diagnostics.PresentationTraceSources.Refresh();
            System.Diagnostics.PresentationTraceSources.DataBindingSource.Listeners.Add(new System.Diagnostics.TextWriterTraceListener(log));
            System.Diagnostics.PresentationTraceSources.DataBindingSource.Switch.Level = System.Diagnostics.SourceLevels.Warning;
            System.Diagnostics.Trace.AutoFlush = true;
        }
        var window = new MainWindow(new MainViewModel(new DialogService(), new FileUserDefaultsStore()));
        MainWindow = window;
        window.Show();
    }

    /// <summary>Last resort: report instead of closing the window and losing the user's work.</summary>
    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(MainWindow!, "Unexpected error:\n" + e.Exception.Message, "Name Plate Generator",
            MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
