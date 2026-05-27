using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using AYVUNoLag.Models;
using AYVUNoLag.Services;
using System.Windows.Controls;

namespace AYVUNoLag;

// ── Log entry ────────────────────────────────────────────────────────────────
public sealed class LogEntry
{
    public string Message    { get; init; } = "";
    public string Status     { get; init; } = "";
    public string Time       { get; init; } = "";
    public SolidColorBrush StatusBrush { get; init; } = _brushMuted;
    private static readonly SolidColorBrush _brushMuted = new(Color.FromRgb(0xA3, 0xA3, 0xA3));
}

public partial class MainWindow : Window
{
    // ── Services ─────────────────────────────────────────────────────────────
    private readonly NetworkDiagnosticService _diagnosticService = new();
    private readonly LocalBoostService        _boostService      = new();

    // ── State ─────────────────────────────────────────────────────────────────
    private CancellationTokenSource?                _monitorCts;
    private CancellationTokenSource?                _scanCts;
    private readonly List<PingSample>               _rollingWindow  = [];
    private readonly ObservableCollection<PingSample> _livePingItems = [];
    private readonly ObservableCollection<LogEntry> _log            = [];
    private int  _liveCycle       = 0;
    private int  _boostCount      = 0;
    private int? _boostedProcessId = null;

    // ── Color constants ───────────────────────────────────────────────────────
    private static readonly SolidColorBrush BrushSuccess = new(Color.FromRgb(0x22, 0xC5, 0x5E));
    private static readonly SolidColorBrush BrushWarning = new(Color.FromRgb(0xF5, 0xA6, 0x23));
    private static readonly SolidColorBrush BrushDanger  = new(Color.FromRgb(0xEF, 0x44, 0x44));
    private static readonly SolidColorBrush BrushMuted   = new(Color.FromRgb(0xA3, 0xA3, 0xA3));
    private static readonly SolidColorBrush BrushAccent  = new(Color.FromRgb(0xFF, 0x44, 0x00));

    // ── Constructor ───────────────────────────────────────────────────────────
    public MainWindow()
    {
        InitializeComponent();
        LogList.ItemsSource  = _log;
        PingList.ItemsSource = _livePingItems;
        LoadGames();
        AddLog("App iniciado. Monitoramento em tempo real ativado.", "OK", BrushSuccess);
        StartLiveMonitor();
    }

    // ── License ───────────────────────────────────────────────────────────────
    /// <summary>
    /// Chamado pela LicenseWindow após validação bem-sucedida.
    /// Exibe o ID e o tipo de licença no badge da barra de título.
    /// </summary>
    public void ApplyLicense(string id, LicenseResult result)
    {
        var label = result.IsPermanent
            ? $"🔑 {id}  ·  Permanente"
            : result.ExpiresAt.HasValue
                ? $"🔑 {id}  ·  expira {result.ExpiresAt.Value:dd/MM/yy}"
                : $"🔑 {id}";

        if (!string.IsNullOrWhiteSpace(result.ClientName))
            label = $"🔑 {result.ClientName}  ·  {id}";

        LicenseBadgeText.Text       = label;
        LicenseBadge.Visibility     = Visibility.Visible;
        LicenseBadge.BorderBrush    = new SolidColorBrush(Color.FromRgb(0x24, 0x24, 0x24));
        LicenseBadgeText.Foreground = BrushAccent;
    }

    // ══════════════════════════════════════════════════════════════════════════
    // TASKBAR FIX — impede que a janela cubra a barra de tarefas ao maximizar
    // ══════════════════════════════════════════════════════════════════════════
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var source = (HwndSource)PresentationSource.FromVisual(this);
        source.AddHook(HwndHook);
    }

    protected override void OnClosed(EventArgs e)
    {
        _monitorCts?.Cancel();
        _scanCts?.Cancel();
        base.OnClosed(e);
    }

    private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_GETMINMAXINFO = 0x0024;
        if (msg != WM_GETMINMAXINFO) return IntPtr.Zero;

        var mmi     = Marshal.PtrToStructure<MINMAXINFO>(lParam);
        var monitor = MonitorFromWindow(hwnd, 2 /* MONITOR_DEFAULTTONEAREST */);
        var info    = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };

        if (GetMonitorInfo(monitor, ref info))
        {
            mmi.ptMaxPosition.X = info.rcWork.Left - info.rcMonitor.Left;
            mmi.ptMaxPosition.Y = info.rcWork.Top  - info.rcMonitor.Top;
            mmi.ptMaxSize.X     = info.rcWork.Right  - info.rcWork.Left;
            mmi.ptMaxSize.Y     = info.rcWork.Bottom - info.rcWork.Top;
            Marshal.StructureToPtr(mmi, lParam, true);
            handled = true;
        }
        return IntPtr.Zero;
    }

    // ── Win32 P/Invoke ────────────────────────────────────────────────────────
    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int  cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    // ══════════════════════════════════════════════════════════════════════════
    // CHROME BUTTONS
    // ══════════════════════════════════════════════════════════════════════════
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) ToggleMaximize();
        else                   DragMove();
    }

    private void MinimizeWindow_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private void MaximizeWindow_Click(object sender, RoutedEventArgs e)
        => ToggleMaximize();

    private void CloseWindow_Click(object sender, RoutedEventArgs e)
        => Close();

    private void ToggleMaximize()
        => WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal : WindowState.Maximized;

    // ══════════════════════════════════════════════════════════════════════════
    // LIVE MONITOR
    // ══════════════════════════════════════════════════════════════════════════
    private void StartLiveMonitor()
    {
        _monitorCts?.Cancel();
        _monitorCts = new CancellationTokenSource();
        _ = LiveMonitorLoop(_monitorCts.Token);
    }

    private async Task LiveMonitorLoop(CancellationToken ct)
    {
        // Force a full tick immediately on start
        await DoLiveTick(ct, force: true);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(2000, ct);
                await DoLiveTick(ct, force: false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                // Don't crash the loop — log and keep going
                await Dispatcher.InvokeAsync(() =>
                    AddLog($"Monitor: {ex.Message}", "!", BrushWarning));
                await Task.Delay(3000, CancellationToken.None);
            }
        }
    }

    private async Task DoLiveTick(CancellationToken ct, bool force)
    {
        _liveCycle++;

        // Always: quick ping (all 3 targets in parallel)
        var pings = await _diagnosticService.QuickPingAsync(ct);

        // Every 5 cycles (or forced): refresh game list
        IReadOnlyList<GameProcess>? games = null;
        if (force || _liveCycle % 5 == 0)
            games = GameProcessService.FindLikelyGameProcesses();

        // Every 15 cycles (or forced): DNS probe
        IReadOnlyList<DnsProbeResult>? dns = null;
        if (force || _liveCycle % 15 == 0)
            dns = await _diagnosticService.QuickDnsAsync(ct);

        await Dispatcher.InvokeAsync(() => ApplyLiveTick(pings, games, dns));
    }

    private void ApplyLiveTick(
        IReadOnlyList<PingSample>    pings,
        IReadOnlyList<GameProcess>?  games,
        IReadOnlyList<DnsProbeResult>? dns)
    {
        // ── Rolling window (max 30) ────────────────────────────────────────
        foreach (var p in pings)
            _rollingWindow.Add(p);
        while (_rollingWindow.Count > 30)
            _rollingWindow.RemoveAt(0);

        // ── Live ping list (newest on top, max 15 rows) ───────────────────
        foreach (var p in pings.Reverse())
            _livePingItems.Insert(0, p);
        while (_livePingItems.Count > 15)
            _livePingItems.RemoveAt(_livePingItems.Count - 1);

        // ── DNS list ──────────────────────────────────────────────────────
        if (dns is not null)
            DnsList.ItemsSource = dns;

        // ── Game list ─────────────────────────────────────────────────────
        if (games is not null)
            JogosList.ItemsSource = games;

        // ── Metrics ───────────────────────────────────────────────────────
        var successful = _rollingWindow
            .Where(s => s.Success && s.LatencyMs.HasValue)
            .Select(s => s.LatencyMs!.Value)
            .ToArray();

        var total  = _rollingWindow.Count;
        var failed = total - successful.Length;
        var loss   = total == 0 ? 0d : Math.Round(failed * 100d / total, 1);

        SetCard(CardLossValue, CardLossLabel, $"{loss:0.0}%", LossQuality(loss));

        if (successful.Length > 0)
        {
            var avg  = Math.Round(successful.Average(), 1);
            var best = successful.Min();
            var jitter = Math.Round(successful.Select(v => Math.Abs(v - avg)).Average(), 1);

            SetCard(CardPingValue,   CardPingLabel,   $"{avg:0.0} ms",  PingQuality(avg));
            SetCard(CardJitterValue, CardJitterLabel, $"{jitter:0.0} ms", JitterQuality(jitter));
            SetCard(CardBestValue,   CardBestLabel,   $"{best} ms",     PingQuality(best));

            var verdict = loss switch
            {
                >= 20 => "Perda alta. Priorize cabo, reinicie roteador e teste outro DNS.",
                >= 5  => "Perda moderada. Há instabilidade na rota ou rede local.",
                _ when jitter >= 30 => "Jitter alto. Pode haver Wi-Fi instável ou rota congestionada.",
                _ when avg    >= 120 => "Ping alto. Otimização local ajuda pouco se o servidor estiver longe.",
                _                   => "Conexão saudável para jogo online."
            };

            VerdictText.Text      = verdict;
            StatusBarText.Text    = verdict;
        }

        // ── Timestamps ────────────────────────────────────────────────────
        var now = DateTime.Now.ToString("HH:mm:ss");
        VerdictTimestamp.Text = $"Última análise: {now}";
        LastScanLabel.Text    = $"Última análise: {now}";

        // ── Status pill ───────────────────────────────────────────────────
        if (StatusPillText.Text != "● Boost Ativo")
        {
            StatusPillText.Text       = "● Monitorando";
            StatusPillText.Foreground = BrushSuccess;
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // SCAN (Analisar — força atualização imediata)
    // ══════════════════════════════════════════════════════════════════════════
    private async void ScanButton_Click(object sender, RoutedEventArgs e)
    {
        _scanCts?.Cancel();
        _scanCts = new CancellationTokenSource();

        SetBusy(true);
        AddLog("Análise manual iniciada — ping, jitter, packet loss e DNS...", "...", BrushWarning);

        try
        {
            // Full parallel probe
            var pingTask = _diagnosticService.QuickPingAsync(_scanCts.Token);
            var dnsTask  = _diagnosticService.QuickDnsAsync(_scanCts.Token);
            var games    = GameProcessService.FindLikelyGameProcesses();

            await Task.WhenAll(pingTask, dnsTask);

            var pings = pingTask.Result;
            var dns   = dnsTask.Result;

            // Reset rolling window for fresh metrics
            _rollingWindow.Clear();

            ApplyLiveTick(pings, games, dns);
            AddLog("Análise concluída.", "OK", BrushSuccess);
        }
        catch (OperationCanceledException)
        {
            AddLog("Análise cancelada.", "✕", BrushDanger);
            SetPillIdle();
        }
        finally
        {
            SetBusy(false);
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // BOOST
    // ══════════════════════════════════════════════════════════════════════════
    private void BoostButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = JogosList.SelectedItem as GameProcess;
        var results  = _boostService.RunBoost(selected?.ProcessId);
        BoostList.ItemsSource = results;

        _boostCount++;
        _boostedProcessId = selected?.ProcessId;

        var msg = selected is null
            ? "Boost aplicado. Selecione um jogo em Jogos Detectados para priorizar o processo."
            : $"Boost aplicado — {selected.Name} priorizado como High.";

        AddLog(msg, "OK", BrushSuccess);
        StatusPillText.Text       = "● Boost Ativo";
        StatusPillText.Foreground = BrushAccent;
        StatusBarText.Text        = msg;

        PauseBoostButton.Visibility = Visibility.Visible;
    }

    // ══════════════════════════════════════════════════════════════════════════
    // PAUSAR BOOST
    // ══════════════════════════════════════════════════════════════════════════
    private void PauseBoostButton_Click(object sender, RoutedEventArgs e)
    {
        var results = _boostService.PauseBoost(_boostedProcessId);
        BoostList.ItemsSource = results;

        _boostedProcessId = null;

        var msg = "Boost pausado. Prioridades revertidas para Normal.";
        AddLog(msg, "OK", BrushMuted);

        StatusPillText.Text       = "● Monitorando";
        StatusPillText.Foreground = BrushSuccess;
        StatusBarText.Text        = msg;

        PauseBoostButton.Visibility = Visibility.Collapsed;
    }

    // ══════════════════════════════════════════════════════════════════════════
    // PRIORIZAR PROCESSO
    // ══════════════════════════════════════════════════════════════════════════
    private void PrioritizeButton_Click(object sender, RoutedEventArgs e)
    {
        if (JogosList.SelectedItem is not GameProcess game)
        {
            JogosStatusText.Text = "⚠  Selecione um processo na lista primeiro.";
            return;
        }

        var result = GameProcessService.PrioritizeProcess(game.ProcessId);
        JogosStatusText.Text = result.Success
            ? $"✓  {result.Detail}"
            : $"✗  {result.Detail}";

        AddLog(result.Detail, result.Success ? "OK" : "✕",
               result.Success ? BrushSuccess : BrushDanger);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // HELPERS
    // ══════════════════════════════════════════════════════════════════════════
    private static void SetCard(System.Windows.Controls.TextBlock valueBlock,
                                System.Windows.Controls.TextBlock labelBlock,
                                string value,
                                (string label, SolidColorBrush brush) quality)
    {
        valueBlock.Text       = value;
        labelBlock.Text       = quality.label;
        labelBlock.Foreground = quality.brush;
    }

    private static (string label, SolidColorBrush brush) PingQuality(double ms) => ms switch
    {
        <= 60  => ("● Ótimo", BrushSuccess),
        <= 120 => ("● Médio", BrushWarning),
        _      => ("● Alto",  BrushDanger),
    };

    private static (string label, SolidColorBrush brush) JitterQuality(double ms) => ms switch
    {
        <= 10 => ("● Ótimo",  BrushSuccess),
        <= 30 => ("● Médio",  BrushWarning),
        _     => ("● Alto",   BrushDanger),
    };

    private static (string label, SolidColorBrush brush) LossQuality(double pct) => pct switch
    {
        0     => ("● Zero",     BrushSuccess),
        <= 2  => ("● Estável",  BrushSuccess),
        <= 10 => ("● Moderada", BrushWarning),
        _     => ("● Alta",     BrushDanger),
    };

    private void LoadGames()
    {
        var games = GameProcessService.FindLikelyGameProcesses();
        JogosList.ItemsSource = games;
    }

    private void AddLog(string message, string status, SolidColorBrush brush)
    {
        _log.Insert(0, new LogEntry
        {
            Message     = message,
            Status      = status,
            Time        = DateTime.Now.ToString("HH:mm:ss"),
            StatusBrush = brush,
        });
        while (_log.Count > 60) _log.RemoveAt(_log.Count - 1);
    }

    private void SetPillIdle()
    {
        StatusPillText.Text       = "● Monitorando";
        StatusPillText.Foreground = BrushSuccess;
    }

    private void SetBusy(bool busy)
    {
        ScanButton.IsEnabled         = !busy;
        BoostButton.IsEnabled        = !busy;
        PrioritizeButton.IsEnabled   = !busy;
        ExecutionProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;

        if (busy)
        {
            StatusPillText.Text       = "● Analisando...";
            StatusPillText.Foreground = BrushWarning;
            StatusBarText.Text        = "Analisando ping, jitter, packet loss e DNS...";
        }
    }
}
