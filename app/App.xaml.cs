using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace AYVUNoLag;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    public App()
    {
        // ── Global crash handlers ─────────────────────────────────────────
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    // ── Handlers ──────────────────────────────────────────────────────────

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        WriteCrashLog("UI thread", e.Exception);
        e.Handled = true;   // evita que o WPF mate o processo sem avisar
        ShowCrashDialog(e.Exception);
    }

    private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
            WriteCrashLog("background thread", ex);
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        WriteCrashLog("async task", e.Exception);
        e.SetObserved();
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static void WriteCrashLog(string origin, Exception ex)
    {
        try
        {
            var desktop  = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            var logPath  = Path.Combine(desktop, "ayvu_crash.log");
            var content  =
                $"=== AYVU NoLag crash — {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===\n" +
                $"Origin : {origin}\n" +
                $"Type   : {ex.GetType().FullName}\n" +
                $"Message: {ex.Message}\n" +
                $"Stack  :\n{ex.StackTrace}\n\n";

            File.AppendAllText(logPath, content, System.Text.Encoding.UTF8);
        }
        catch { /* nunca deixar o logger travar o app */ }
    }

    private static void ShowCrashDialog(Exception ex)
    {
        try
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            MessageBox.Show(
                $"Erro inesperado:\n{ex.Message}\n\nDetalhes salvos em:\n{Path.Combine(desktop, "ayvu_crash.log")}",
                "AYVU NoLag — Erro",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch { }
    }
}

