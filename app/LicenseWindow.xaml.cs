using AYVUNoLag.Services;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace AYVUNoLag;

public partial class LicenseWindow : Window
{
    private readonly LicenseService _license = new();
    private DispatcherTimer?        _countdownTimer;
    private int                     _countdown;

    public LicenseWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var lastId = _license.LoadLastId();
        if (!string.IsNullOrWhiteSpace(lastId))
        {
            IdInput.Text = lastId;
            IdInput.SelectAll();
        }
        IdInput.Focus();
    }

    // ── Drag ────────────────────────────────────────────────────────────────────

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    // ── Close ────────────────────────────────────────────────────────────────────

    private void CloseBtn_Click(object sender, RoutedEventArgs e)
    {
        Application.Current.Shutdown();
    }

    // ── Input helpers ────────────────────────────────────────────────────────────

    private void IdInput_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        SetFeedback(null, isError: false);
        RetryBtn.Visibility    = Visibility.Collapsed;
        ValidateBtn.Visibility = Visibility.Visible;
    }

    private void IdInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            TriggerValidate();
    }

    private void ValidateBtn_Click(object sender, RoutedEventArgs e) => TriggerValidate();
    private void RetryBtn_Click(object sender, RoutedEventArgs e)    => TriggerValidate();

    // ── Validate ─────────────────────────────────────────────────────────────────

    private async void TriggerValidate()
    {
        var id = IdInput.Text.Trim().ToUpperInvariant();
        if (id.Length == 0) return;

        SetLoading(true);

        var result = await _license.ValidateAsync(id);

        SetLoading(false);

        switch (result.Status)
        {
            case LicenseStatus.Valid:
                OnValid(id, result);
                break;

            case LicenseStatus.Paused:
                OnInvalidOrExpired("Esta licença está suspensa.\nEntre em contato para reativá-la.");
                break;

            case LicenseStatus.Expired:
                var expStr = result.ExpiresAt.HasValue
                    ? result.ExpiresAt.Value.ToString("dd/MM/yyyy")
                    : "data desconhecida";
                OnInvalidOrExpired($"Licença expirada em {expStr}.\nEntre em contato para renovar.");
                break;

            case LicenseStatus.NotFound:
                OnInvalidOrExpired("ID não reconhecido.\nVerifique o código e tente novamente.");
                break;

            case LicenseStatus.NoInternet:
                OnNoInternet();
                break;

            case LicenseStatus.ConfigError:
                SetFeedback("Configuração do servidor incompleta.\nContate o suporte.", isError: true);
                break;
        }
    }

    private void OnValid(string id, LicenseResult result)
    {
        _license.SaveLastId(id);

        var main = new MainWindow();
        main.ApplyLicense(id, result);
        main.Show();
        Close();
    }

    private void OnInvalidOrExpired(string message)
    {
        IdInput.IsEnabled      = false;
        ValidateBtn.IsEnabled  = false;
        ValidateBtn.Visibility = Visibility.Collapsed;
        RetryBtn.Visibility    = Visibility.Collapsed;

        _countdown = 5;
        SetFeedback(BuildCountdownMessage(message, _countdown), isError: true);

        _countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _countdownTimer.Tick += (_, _) =>
        {
            _countdown--;
            if (_countdown <= 0)
            {
                _countdownTimer.Stop();
                Application.Current.Shutdown();
                return;
            }
            SetFeedback(BuildCountdownMessage(message, _countdown), isError: true);
        };
        _countdownTimer.Start();
    }

    private static string BuildCountdownMessage(string reason, int seconds) =>
        $"{reason}\n\nO programa será encerrado em {seconds}s...";

    private void OnNoInternet()
    {
        SetFeedback(
            "Sem conexão com a internet.\nConecte-se e tente novamente.",
            isError: true);

        RetryBtn.Visibility    = Visibility.Visible;
        ValidateBtn.Visibility = Visibility.Collapsed;
    }

    // ── UI helpers ───────────────────────────────────────────────────────────────

    private void SetLoading(bool loading)
    {
        ValidateBtn.IsEnabled   = !loading;
        IdInput.IsEnabled       = !loading;
        SpinnerPanel.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;

        if (loading)
        {
            SetFeedback(null, isError: false);
            RetryBtn.Visibility    = Visibility.Collapsed;
            ValidateBtn.Visibility = Visibility.Visible;
        }
    }

    private void SetFeedback(string? message, bool isError)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            FeedbackText.Visibility = Visibility.Collapsed;
            return;
        }

        FeedbackText.Text       = message;
        FeedbackText.Foreground = isError
            ? new SolidColorBrush(Color.FromRgb(0xFF, 0x44, 0x00))
            : new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
        FeedbackText.Visibility = Visibility.Visible;
    }
}
