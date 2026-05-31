using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using AYVUNoLag.Config;
using AYVUNoLag.Models;
using AYVUNoLag.Services;

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
    private readonly NetworkDiagnosticService _diagnosticService   = new();
    private readonly LocalBoostService        _boostService        = new();
    private readonly WarpService              _warpService         = new();
    private readonly InputLagService          _inputLagService     = new();
    private readonly SystemMetricsService     _systemMetrics       = new();
    private readonly MsiModeService           _msiService          = new();

    // ── State ─────────────────────────────────────────────────────────────────
    private CancellationTokenSource?                    _monitorCts;
    private CancellationTokenSource?                    _scanCts;
    private readonly List<PingSample>                   _rollingWindow  = [];
    private readonly ObservableCollection<PingSample>   _livePingItems  = [];
    private readonly ObservableCollection<LogEntry>     _log            = [];
    private readonly ObservableCollection<GameProcess>  _gameComboItems = [];
    private int  _liveCycle        = 0;
    private int? _boostedProcessId = null;

    // ── Sparkline history (Monitor em tempo real) ─────────────────────────────
    private const int SparkPoints = 40;
    private readonly Queue<double> _cpuHistory  = new();
    private readonly Queue<double> _ramHistory  = new();
    private readonly Queue<double> _gpuHistory  = new();
    private readonly Queue<double> _diskHistory = new();

    // ── Color constants ───────────────────────────────────────────────────────
    private static readonly SolidColorBrush BrushSuccess = new(Color.FromRgb(0x22, 0xC5, 0x5E));
    private static readonly SolidColorBrush BrushWarning = new(Color.FromRgb(0xF5, 0xA6, 0x23));
    private static readonly SolidColorBrush BrushDanger  = new(Color.FromRgb(0xEF, 0x44, 0x44));
    private static readonly SolidColorBrush BrushMuted   = new(Color.FromRgb(0xA3, 0xA3, 0xA3));
    private static readonly SolidColorBrush BrushAccent  = new(Color.FromRgb(0xFF, 0x44, 0x00));

    // ── Auto-update ───────────────────────────────────────────────────────────
    private const string CurrentVersion = "1.7.7";
    private string _updateDownloadUrl   = "";
    private static readonly HttpClient _downloadHttp = new() { Timeout = TimeSpan.FromMinutes(30) };
    private static readonly HttpClient _licenseHttp  = new() { Timeout = TimeSpan.FromSeconds(8) };

    // ── WARP state ────────────────────────────────────────────────────────────
    private WarpStatus _warpStatus = WarpStatus.NotReady;
    private bool       _warpBusy   = false;


    // ── Constructor ───────────────────────────────────────────────────────────
    public MainWindow()
    {
        InitializeComponent();
        LogList.ItemsSource      = _log;
        PingList.ItemsSource     = _livePingItems;
        GameComboBox.ItemsSource = _gameComboItems;
        LoadGames();
        LoadGpuModePanel();
        AddLog("App iniciado. Monitoramento em tempo real ativado.", "OK", BrushSuccess);

        StartLiveMonitor();
        _ = Task.Run(CheckForUpdateAsync);
        _ = Task.Run(async () =>
        {
            // Remove túnel preso que possa estar causando 100% de packet loss
            await _warpService.CleanupStaleAsync();
            Dispatcher.Invoke(RefreshWarpStatus);
        });
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
    // OTIMIZAR (Boost + Input Lag unificados)
    // ══════════════════════════════════════════════════════════════════════════

    private void OtimizarButton_Click(object sender, RoutedEventArgs e)
    {
        // Processo selecionado no ComboBox (ou no JogosList como fallback)
        var selected = GameComboBox.SelectedItem as GameProcess
                    ?? JogosList.SelectedItem   as GameProcess;

        // Aplica Boost + Input Lag
        var boostResults    = _boostService.RunBoost(selected?.ProcessId);
        var inputLagResults = _inputLagService.Apply();
        var allResults      = boostResults.Concat(inputLagResults).ToList();

        OtimizacoesList.ItemsSource = allResults;
        _boostedProcessId = selected?.ProcessId;

        // Atualiza UI
        var failCount = allResults.Count(r => !r.Success);
        OtimizarButton.Visibility        = Visibility.Collapsed;
        PararOtimizacaoButton.Visibility = Visibility.Visible;
        OtimizacoesSubtitle.Text         = "  —  ativo";

        InputLagBadge.Visibility     = Visibility.Visible;
        InputLagBadgeText.Text       = "✓ Otimizado";
        InputLagBadgeText.Foreground = BrushSuccess;

        StatusPillText.Text       = "● Otimizado";
        StatusPillText.Foreground = BrushAccent;

        var msg = selected is null
            ? "Sistema otimizado para jogo."
            : $"Sistema otimizado — {selected.Name} priorizado.";

        if (failCount > 0) msg += $" ({failCount} item(s) com erro)";
        StatusBarText.Text = msg;
        AddLog(msg, failCount == 0 ? "OK" : "!", failCount == 0 ? BrushSuccess : BrushWarning);
    }

    private void PararOtimizacaoButton_Click(object sender, RoutedEventArgs e)
    {
        var boostResults    = _boostService.PauseBoost(_boostedProcessId);
        var inputLagResults = _inputLagService.IsActive ? _inputLagService.Revert() : [];
        var allResults      = boostResults.Concat(inputLagResults).ToList();

        OtimizacoesList.ItemsSource = allResults;
        _boostedProcessId = null;

        OtimizarButton.Visibility        = Visibility.Visible;
        PararOtimizacaoButton.Visibility = Visibility.Collapsed;
        OtimizacoesSubtitle.Text         = "  —  clique em Otimizar para ativar";

        InputLagBadge.Visibility = Visibility.Collapsed;
        SetPillIdle();

        const string msg = "Otimizações revertidas.";
        StatusBarText.Text = msg;
        AddLog(msg, "OK", BrushMuted);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // WARP
    // ══════════════════════════════════════════════════════════════════════════

    private void RefreshWarpStatus()
    {
        _warpStatus = _warpService.GetStatus();
        Dispatcher.Invoke(() => ApplyWarpStatus(_warpStatus));
    }

    private void ApplyWarpStatus(WarpStatus status)
    {
        switch (status)
        {
            case WarpStatus.NotReady:
                WarpButton.Content      = "◎  WARP";
                WarpBadge.Visibility    = Visibility.Collapsed;
                break;

            case WarpStatus.Disconnected:
                WarpButton.Content      = "◎  Conectar WARP";
                WarpBadge.Visibility    = Visibility.Collapsed;
                break;

            case WarpStatus.Connecting:
                WarpButton.Content       = "◎  Conectando...";
                WarpBadge.Visibility     = Visibility.Visible;
                WarpBadgeText.Text       = "◎ WARP conectando";
                WarpBadgeText.Foreground = new SolidColorBrush(Color.FromRgb(0xF5, 0xA6, 0x23));
                break;

            case WarpStatus.Connected:
                WarpButton.Content       = "◉  Desconectar WARP";
                WarpBadge.Visibility     = Visibility.Visible;
                WarpBadgeText.Text       = "◉ WARP ativo";
                WarpBadgeText.Foreground = BrushSuccess;
                break;
        }

        WarpButton.IsEnabled = !_warpBusy;
    }

    private async void WarpButton_Click(object sender, RoutedEventArgs e)
    {
        if (_warpBusy) return;

        var status = _warpService.GetStatus();

        // Engine não baixado ainda → abre modal de setup
        if (status == WarpStatus.NotReady)
        {
            WarpInstallBanner.Visibility = Visibility.Visible;
            return;
        }

        _warpBusy = true;
        WarpButton.IsEnabled = false;

        if (status == WarpStatus.Connected)
        {
            WarpButton.Content = "◎  Desconectando...";
            AddLog("Desconectando túnel WireGuard...", "...", BrushWarning);
            var (ok, detail) = await _warpService.DisconnectAsync();
            AddLog(detail, ok ? "OK" : "✕", ok ? BrushMuted : BrushDanger);
        }
        else
        {
            WarpButton.Content = "◎  Conectando...";
            AddLog("Iniciando túnel Cloudflare WARP...", "...", BrushWarning);
            var (ok, detail) = await _warpService.ConnectAsync(
                msg => Dispatcher.Invoke(() => { WarpButton.Content = $"◎  {msg}"; AddLog(msg, "...", BrushWarning); }));
            AddLog(detail, ok ? "OK" : "✕", ok ? BrushSuccess : BrushDanger);
            if (ok) AddLog("Tráfego passando pela rede Cloudflare. Sem apps externos.", "↑", BrushSuccess);
        }

        _warpBusy = false;
        RefreshWarpStatus();
    }

    private CancellationTokenSource? _warpInstallCts;

    private void WarpInstallDismiss_Click(object sender, RoutedEventArgs e)
    {
        _warpInstallCts?.Cancel();
        WarpInstallBanner.Visibility = Visibility.Collapsed;
        ResetWarpInstallModal();
    }

    private async void WarpInstallConfirm_Click(object sender, RoutedEventArgs e)
    {
        WarpInstallDesc.Visibility     = Visibility.Collapsed;
        WarpInstallProgress.Visibility = Visibility.Visible;
        WarpInstallButtons.Visibility  = Visibility.Collapsed;
        WarpInstallTitle.Text          = "Configurando WireGuard Engine...";

        _warpInstallCts = new CancellationTokenSource();
        AddLog("Baixando WireGuard Engine (~10 MB)...", "...", BrushWarning);

        var (ok, detail) = await _warpService.EnsureEngineAsync(
            (pct, msg) => Dispatcher.Invoke(() =>
            {
                WarpInstallBar.Value   = pct;
                WarpInstallPct.Text    = $"{pct}%";
                WarpInstallStatus.Text = msg;
            }),
            _warpInstallCts.Token);

        if (ok)
        {
            WarpInstallBanner.Visibility = Visibility.Collapsed;
            ResetWarpInstallModal();
            AddLog("WireGuard Engine pronto. Conectando...", "OK", BrushSuccess);

            _warpBusy = true;
            WarpButton.IsEnabled = false;
            WarpButton.Content   = "◎  Conectando...";

            var (cOk, cDetail) = await _warpService.ConnectAsync(
                msg => Dispatcher.Invoke(() => AddLog(msg, "...", BrushWarning)));

            _warpBusy = false;
            AddLog(cDetail, cOk ? "OK" : "✕", cOk ? BrushSuccess : BrushDanger);
            RefreshWarpStatus();
        }
        else
        {
            WarpInstallTitle.Text          = "Erro ao configurar WireGuard";
            WarpInstallDesc.Text           = detail;
            WarpInstallDesc.Visibility     = Visibility.Visible;
            WarpInstallProgress.Visibility = Visibility.Collapsed;
            WarpInstallButtons.Visibility  = Visibility.Visible;
            WarpInstallConfirmBtn.Content  = "Tentar novamente";
            AddLog($"WireGuard Engine: {detail}", "✕", BrushDanger);
        }
    }

    private void ResetWarpInstallModal()
    {
        WarpInstallTitle.Text          = "Configurar WireGuard Tunnel";
        WarpInstallDesc.Text           = "O AYVU NoLag irá baixar o WireGuard Engine (~10 MB) e conectar diretamente à rede Cloudflare. Nenhum app adicional será instalado.";
        WarpInstallDesc.Visibility     = Visibility.Visible;
        WarpInstallProgress.Visibility = Visibility.Collapsed;
        WarpInstallButtons.Visibility  = Visibility.Visible;
        WarpInstallConfirmBtn.Content  = "Configurar agora";
        WarpInstallBar.Value           = 0;
        WarpInstallPct.Text            = "0%";
    }

    // ══════════════════════════════════════════════════════════════════════════
    // AUTO-UPDATE
    // ══════════════════════════════════════════════════════════════════════════

    private async Task CheckForUpdateAsync()
    {
        try
        {
            var json = await _licenseHttp.GetStringAsync(FirebaseConfig.ConfigUrl);
            if (string.IsNullOrWhiteSpace(json) || json.Trim() == "null") return;

            using var doc = JsonDocument.Parse(json);
            var root      = doc.RootElement;

            if (!root.TryGetProperty("latestVersion", out var latestProp)) return;
            if (!root.TryGetProperty("downloadUrl",   out var urlProp))    return;

            var latest      = latestProp.GetString() ?? "";
            var downloadUrl = urlProp.GetString()    ?? "";

            if (string.IsNullOrWhiteSpace(latest) || string.IsNullOrWhiteSpace(downloadUrl)) return;
            if (!IsNewerVersion(latest, CurrentVersion)) return;

            _updateDownloadUrl = downloadUrl;
            Dispatcher.Invoke(() =>
            {
                UpdateBannerText.Text   = $"Nova versão v{latest} disponível";
                UpdateBanner.Visibility = Visibility.Visible;
            });
        }
        catch { /* silencioso */ }
    }

    private static bool IsNewerVersion(string remote, string local)
    {
        try   { return Version.Parse(remote) > Version.Parse(local); }
        catch { return string.Compare(remote, local, StringComparison.Ordinal) > 0; }
    }

    private async void UpdateInstall_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_updateDownloadUrl)) return;

        UpdateInstallButton.IsEnabled  = false;
        UpdateTextPanel.Visibility     = Visibility.Collapsed;
        UpdateProgressPanel.Visibility = Visibility.Visible;

        try
        {
            var tempExe = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ayvu_nolag_update.exe");

            using var response = await _downloadHttp.GetAsync(
                _updateDownloadUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var total = response.Content.Headers.ContentLength ?? -1L;
            await using var stream = await response.Content.ReadAsStreamAsync();
            await using var file   = System.IO.File.Create(tempExe);

            var buffer    = new byte[81920];
            long received = 0;
            int  read;

            while ((read = await stream.ReadAsync(buffer)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, read));
                received += read;
                if (total > 0)
                {
                    var pct = (int)(received * 100 / total);
                    Dispatcher.Invoke(() =>
                    {
                        UpdateProgressBar.Value = pct;
                        UpdateProgressText.Text = $"{pct}%";
                    });
                }
            }

            file.Close();
            LaunchUpdaterScript(tempExe);
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            UpdateInstallButton.IsEnabled  = true;
            UpdateTextPanel.Visibility     = Visibility.Visible;
            UpdateProgressPanel.Visibility = Visibility.Collapsed;
            UpdateBannerText.Text          = $"Erro ao baixar: {ex.Message}";
        }
    }

    private void UpdateDismiss_Click(object sender, RoutedEventArgs e)
    {
        UpdateBanner.Visibility = Visibility.Collapsed;
    }

    // ── Restart Banner (MSI Mode) ─────────────────────────────────────────────
    private void RestartDismiss_Click(object sender, RoutedEventArgs e)
    {
        RestartBanner.Visibility = Visibility.Collapsed;
        AddLog("MSI Mode ativo — reinicie o computador para efeito total.", "⚠", BrushWarning);
    }

    private void RestartNow_Click(object sender, RoutedEventArgs e)
    {
        RestartBanner.Visibility = Visibility.Collapsed;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName        = "shutdown",
                Arguments       = "/r /t 15 /c \"AYVU NoLag — Reiniciando para aplicar MSI Mode da GPU\"",
                UseShellExecute = false,
                CreateNoWindow  = true,
            });
            AddLog("Reinicialização agendada em 15 segundos.", "OK", BrushSuccess);
            StatusBarText.Text = "Reiniciando em 15s para aplicar MSI Mode...";
        }
        catch (Exception ex)
        {
            AddLog($"Erro ao agendar reinicialização: {ex.Message}", "✕", BrushDanger);
        }
    }

    private static void LaunchUpdaterScript(string tempExe)
    {
        var currentExe = Environment.ProcessPath ?? "";
        if (string.IsNullOrWhiteSpace(currentExe)) return;

        var scriptPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ayvu_nolag_updater.cmd");

        // Usa aspas duplas nos SET para lidar com espaços no caminho
        var script =
            "@echo off\r\n" +
            $"set \"SRC={tempExe}\"\r\n" +
            $"set \"DST={currentExe}\"\r\n" +
            "timeout /t 8 /nobreak >nul\r\n" +
            "set /a TRIES=0\r\n" +
            ":retry\r\n" +
            "copy /Y \"%SRC%\" \"%DST%\"\r\n" +
            "if %errorlevel% equ 0 goto ok\r\n" +
            "set /a TRIES+=1\r\n" +
            "if %TRIES% lss 15 (\r\n" +
            "    timeout /t 2 /nobreak >nul\r\n" +
            "    goto retry\r\n" +
            ")\r\n" +
            // Falhou após 15 tentativas — avisa o usuário e abre a pasta de download
            "msg * \"A atualização do AYVU NoLag falhou. Baixe manualmente em: github.com/mathaussiqueira/ayvu-nolag/releases\" 2>nul\r\n" +
            "start \"\" \"https://github.com/mathaussiqueira/ayvu-nolag/releases\"\r\n" +
            "goto done\r\n" +
            ":ok\r\n" +
            "start \"\" \"%DST%\"\r\n" +
            ":done\r\n" +
            "del \"%SRC%\" 2>nul\r\n" +
            "(goto) 2>nul & del \"%~f0\"\r\n";

        System.IO.File.WriteAllText(scriptPath, script, System.Text.Encoding.ASCII);

        Process.Start(new ProcessStartInfo
        {
            FileName        = "cmd.exe",
            Arguments       = $"/c \"{scriptPath}\"",
            UseShellExecute = true,
            WindowStyle     = ProcessWindowStyle.Hidden,
        });
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
        if (_inputLagService.IsActive) _inputLagService.Revert();
        _systemMetrics.Dispose();
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
                await Task.Delay(500, ct);
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

        // Always: quick ping + system metrics (paralelo)
        var pingTask = _diagnosticService.QuickPingAsync(ct);
        var sysMetrics = await Task.Run(() => (
            cpu:  _systemMetrics.GetCpuUsage(),
            gpu:  _systemMetrics.GetGpuUsage(),
            ram:  _systemMetrics.GetRamUsage(),
            disk: _systemMetrics.GetDiskUsage()
        ), ct);
        var pings = await pingTask;

        // Every 5 cycles (or forced): refresh game list
        IReadOnlyList<GameProcess>? games = null;
        if (force || _liveCycle % 5 == 0)
            games = GameProcessService.FindLikelyGameProcesses();

        // Every 15 cycles (or forced): DNS probe
        IReadOnlyList<DnsProbeResult>? dns = null;
        if (force || _liveCycle % 15 == 0)
            dns = await _diagnosticService.QuickDnsAsync(ct);

        await Dispatcher.InvokeAsync(() => ApplyLiveTick(pings, games, dns, sysMetrics.cpu, sysMetrics.gpu, sysMetrics.ram, sysMetrics.disk));
    }

    private void ApplyLiveTick(
        IReadOnlyList<PingSample>      pings,
        IReadOnlyList<GameProcess>?    games,
        IReadOnlyList<DnsProbeResult>? dns,
        float cpu = -1f, float gpu = -1f,
        (double usedGB, double totalGB) ram = default,
        float disk = -1f)
    {
        // ── Monitor em tempo real (sparklines estilo Windows Boost) ────────
        var ramPct = ram.totalGB > 0 ? ram.usedGB / ram.totalGB * 100 : 0;

        Enqueue(_cpuHistory,  cpu  >= 0 ? cpu  : 0);
        Enqueue(_ramHistory,  ramPct);
        Enqueue(_gpuHistory,  gpu  >= 0 ? gpu  : 0);
        Enqueue(_diskHistory, disk >= 0 ? disk : 0);

        var sparkColor = Color.FromRgb(0xFF, 0x44, 0x00);
        DrawSparkline(CpuSparkline,  _cpuHistory,  100, sparkColor);
        DrawSparkline(RamSparkline,  _ramHistory,  100, sparkColor);
        DrawSparkline(GpuSparkline,  _gpuHistory,  100, sparkColor);
        DrawSparkline(DiskSparkline, _diskHistory, 100, sparkColor);

        CardCpuValue.Text  = cpu >= 0 ? $"{cpu:0}%" : "--%";
        CardCpuLabel.Text  = "de uso";
        CardGpuValue.Text  = gpu >= 0 ? $"{gpu:0}%" : "--%";
        CardGpuLabel.Text  = "de uso";
        CardDiskValue.Text = disk >= 0 ? $"{disk:0}%" : "--%";
        CardDiskLabel.Text = "de uso";

        if (ram.totalGB > 0)
        {
            CardRamValue.Text = $"{ram.usedGB:0.0} GB";
            CardRamLabel.Text = $"de {ram.totalGB:0.0} GB";
        }

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
        {
            JogosList.ItemsSource = games;

            // Sincroniza ComboBox preservando selecao atual
            var previousSelection = GameComboBox.SelectedItem as GameProcess;
            _gameComboItems.Clear();
            foreach (var g in games) _gameComboItems.Add(g);

            if (previousSelection is not null)
            {
                var match = _gameComboItems.FirstOrDefault(g => g.ProcessId == previousSelection.ProcessId);
                if (match is not null) GameComboBox.SelectedItem = match;
            }
        }

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

            var cpu  = _systemMetrics.GetCpuUsage();
            var gpu  = _systemMetrics.GetGpuUsage();
            var ram  = _systemMetrics.GetRamUsage();
            var disk = _systemMetrics.GetDiskUsage();
            ApplyLiveTick(pings, games, dns, cpu, gpu, ram, disk);
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

    // ══════════════════════════════════════════════════════════════════════════
    // MSI MODE — painel dedicado (GPU + Rede)
    // ══════════════════════════════════════════════════════════════════════════

    // View-model para binding na lista
    public sealed class MsiDeviceVm : System.ComponentModel.INotifyPropertyChanged
    {
        private bool _msiEnabled;
        public string Name     { get; init; } = "";
        public string Category { get; init; } = "";
        public string PnpId    { get; init; } = "";
        public bool   MsiEnabled
        {
            get => _msiEnabled;
            set { _msiEnabled = value; OnChanged(nameof(MsiEnabled)); OnChanged(nameof(StatusLabel)); OnChanged(nameof(StatusBrush)); }
        }
        public string StatusLabel => _msiEnabled ? "● MSI ativo" : "● MSI inativo";
        public SolidColorBrush StatusBrush => _msiEnabled
            ? new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E))
            : new SolidColorBrush(Color.FromRgb(0xA3, 0xA3, 0xA3));

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        private void OnChanged(string n) => PropertyChanged?.Invoke(this, new(n));
    }

    private readonly ObservableCollection<MsiDeviceVm> _msiDevices = [];

    private void LoadGpuModePanel()
    {
        MsiDeviceList.ItemsSource = _msiDevices;
        RefreshMsiDevices();
    }

    private void RefreshMsiDevices()
    {
        _msiDevices.Clear();
        foreach (var d in _msiService.DetectDevices())
        {
            _msiDevices.Add(new MsiDeviceVm
            {
                Name       = d.Name,
                Category   = d.Category,
                PnpId      = d.PnpId,
                MsiEnabled = d.MsiEnabled,
            });
        }
        if (_msiDevices.Count == 0)
            MsiApplyStatus.Text = "Nenhum dispositivo compatível detectado (execute como Administrador).";
    }

    // Toggle individual de um dispositivo
    private void MsiDeviceToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.CheckBox cb || cb.Tag is not string pnpId) return;
        var enable = cb.IsChecked == true;

        var (ok, changed, detail) = _msiService.SetMsi(pnpId, enable);
        SetMsiStatus(ok, detail);

        if (ok && changed)
        {
            AddLog($"MSI Mode: {detail}", "OK", BrushSuccess);
            RestartBanner.Visibility = Visibility.Visible;
        }
        else if (!ok)
        {
            // Reverte o checkbox visual se falhou
            cb.IsChecked = !enable;
        }
    }

    private void MsiApplyDevice_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: MsiDeviceVm vm }) return;

        var (ok, changed, detail) = _msiService.SetMsi(vm.PnpId, vm.MsiEnabled);
        SetMsiStatus(ok, $"{vm.Category}: {detail}");
        RefreshMsiDevices();

        if (ok && changed)
        {
            AddLog($"MSI Mode aplicado em {vm.Name}: {detail}", "OK", BrushSuccess);
            RestartBanner.Visibility = Visibility.Visible;
        }
        else if (ok)
        {
            AddLog($"MSI Mode sem mudança em {vm.Name}.", "OK", BrushMuted);
        }
        else
        {
            AddLog($"MSI Mode: {detail}", "!", BrushDanger);
        }
    }

    private void MsiEnableAll_Click(object sender, RoutedEventArgs e)
    {
        var (applied, changed, detail) = _msiService.SetMsiAll(enable: true);
        SetMsiStatus(applied > 0, detail);
        RefreshMsiDevices();
        if (changed > 0)
        {
            AddLog($"MSI Mode ativado em {changed} dispositivo(s).", "OK", BrushSuccess);
            RestartBanner.Visibility = Visibility.Visible;
        }
    }

    private void MsiDisableAll_Click(object sender, RoutedEventArgs e)
    {
        var (applied, changed, detail) = _msiService.SetMsiAll(enable: false);
        SetMsiStatus(applied > 0, detail);
        RefreshMsiDevices();
        if (changed > 0)
        {
            AddLog($"MSI Mode desativado em {changed} dispositivo(s).", "OK", BrushMuted);
            RestartBanner.Visibility = Visibility.Visible;
        }
    }

    private void SetMsiStatus(bool ok, string detail)
    {
        MsiApplyStatus.Text       = (ok ? "✓ " : "✗ ") + detail;
        MsiApplyStatus.Foreground = ok ? BrushSuccess : BrushDanger;
    }

    private static void Enqueue(Queue<double> queue, double value)
    {
        queue.Enqueue(Math.Clamp(value, 0, 100));
        while (queue.Count > SparkPoints)
            queue.Dequeue();
    }

    private static void DrawSparkline(Canvas canvas, IEnumerable<double> values, double maxValue, Color color)
    {
        canvas.Children.Clear();

        var points = values.ToList();
        if (points.Count < 2) return;

        var width = canvas.ActualWidth;
        var height = canvas.ActualHeight;
        if (width <= 0 || height <= 0)
        {
            width = canvas.Width > 0 ? canvas.Width : 180;
            height = canvas.Height > 0 ? canvas.Height : 58;
        }

        var safeMax = maxValue <= 0 ? 100 : maxValue;
        var step = width / Math.Max(1, points.Count - 1);
        var polyline = new Polyline
        {
            Stroke = new SolidColorBrush(color),
            StrokeThickness = 2,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Opacity = 0.95,
        };

        for (var i = 0; i < points.Count; i++)
        {
            var normalized = Math.Clamp(points[i] / safeMax, 0, 1);
            polyline.Points.Add(new Point(i * step, height - normalized * height));
        }

        canvas.Children.Add(polyline);
    }

    private void LoadGames()
    {
        var games = GameProcessService.FindLikelyGameProcesses();
        JogosList.ItemsSource = games;
        _gameComboItems.Clear();
        foreach (var g in games) _gameComboItems.Add(g);
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
        OtimizarButton.IsEnabled     = !busy;
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
