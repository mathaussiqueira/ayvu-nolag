using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using AYVUNoLag.Models;

namespace AYVUNoLag.Services;

/// <summary>
/// Aplica otimizações de input lag no nível do Windows:
///   1. Timer resolution       — reduz de 15,6 ms → 1 ms (winmm)
///   2. Power plan             — muda para Alto Desempenho
///   3. Nagle's algorithm      — desativa via registro (TcpAckFrequency / TCPNoDelay)
///   4. Game Mode              — ativa Game Mode do Windows
///   5. Foreground boost       — Win32PrioritySeparation = 38
///   6. Network Throttling     — desativa throttling de rede em jogos
///   7. System Responsiveness  — scheduler 100% para o processo em foco
///   8. Game DVR               — desativa captura automática em segundo plano
///   9. Animações do Windows   — desativa animações de minimize/maximize
/// </summary>
public sealed class InputLagService
{
    // ── Estado salvo para reversão ────────────────────────────────────────────
    private bool   _timerActive             = false;
    private string _previousPlanGuid        = "";
    private bool   _nagleDisabled           = false;
    private bool   _gameModeWasDisabled     = false;
    private int    _previousPrioritySep     = -1;
    private int    _previousNetThrottle     = -1;
    private int    _previousSysResponse     = -1;
    private int    _previousGameDvr         = -1;
    private int    _previousAppCapture      = -1;
    private int    _previousMinAnimate      = -1;
    private int    _previousGpuPriority     = -1;
    private int    _previousHagsMode        = -1;
    private bool   _tcpTimestampsDisabled   = false;
    private int    _previousVisualFx        = -1;
    private int    _previousTransparency    = -1;
    private int    _previousFseBehavior     = -1;

    public bool IsActive { get; private set; } = false;

    // ── Aplicar todas as otimizações ─────────────────────────────────────────
    public IReadOnlyList<BoostActionResult> Apply()
    {
        var r = new List<BoostActionResult>
        {
            ApplyTimerResolution(),
            ApplyHighPerformancePlan(),
            ApplyNagle(),
            ApplyGameMode(),
            ApplyForegroundBoost(),
            ApplyNetworkThrottling(),
            ApplySystemResponsiveness(),
            ApplyGameDvr(),
            ApplyWindowsAnimations(),
            ApplyGpuPriority(),
            ApplyHags(),
            ApplyTcpTimestamps(),
            ApplyFullscreenOptimizations(),
            ApplyVisualEffects(),
            ApplyProcessMemoryTrim(),
        };
        IsActive = true;
        return r;
    }

    // ── Reverter todas as otimizações ─────────────────────────────────────────
    public IReadOnlyList<BoostActionResult> Revert()
    {
        var r = new List<BoostActionResult>
        {
            RevertTimerResolution(),
            RevertPowerPlan(),
            RevertNagle(),
            RevertGameMode(),
            RevertForegroundBoost(),
            RevertNetworkThrottling(),
            RevertSystemResponsiveness(),
            RevertGameDvr(),
            RevertWindowsAnimations(),
            RevertGpuPriority(),
            RevertHags(),
            RevertTcpTimestamps(),
            RevertFullscreenOptimizations(),
            RevertVisualEffects(),
        };
        IsActive = false;
        return r;
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 1. TIMER RESOLUTION  (winmm.dll — timeBeginPeriod)
    //    Padrão Windows: 15,625 ms → com 1 ms os frames e inputs ficam mais
    //    uniformes; 0,5 ms é o mínimo suportado pela maioria do hardware.
    // ══════════════════════════════════════════════════════════════════════════
    private BoostActionResult ApplyTimerResolution()
    {
        try
        {
            var r = timeBeginPeriod(1);
            if (r == 0) { _timerActive = true; return Ok("Timer resolution", "Reduzido de 15,6 ms → 1 ms"); }
            return Fail("Timer resolution", $"timeBeginPeriod retornou {r}");
        }
        catch (Exception ex) { return Fail("Timer resolution", ex.Message); }
    }

    private BoostActionResult RevertTimerResolution()
    {
        if (!_timerActive) return Skip("Timer resolution");
        timeEndPeriod(1);
        _timerActive = false;
        return Ok("Timer resolution", "Restaurado para padrão Windows");
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 2. POWER PLAN — Alto Desempenho
    //    Evita que o CPU entre em estados C2/C3 (sleep) durante a partida.
    //    GUID padrão do Windows: 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c
    // ══════════════════════════════════════════════════════════════════════════
    private const string HighPerfGuid = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";

    private BoostActionResult ApplyHighPerformancePlan()
    {
        try
        {
            // Salva plano atual
            var current = RunCmd("powercfg", "/getactivescheme");
            var parts   = current.Split(' ');
            _previousPlanGuid = parts.Length >= 4 ? parts[3].Trim() : "";

            if (string.Equals(_previousPlanGuid, HighPerfGuid, StringComparison.OrdinalIgnoreCase))
                return Ok("Power plan", "Alto Desempenho já estava ativo");

            RunCmd("powercfg", $"/setactive {HighPerfGuid}");
            return Ok("Power plan", "Alterado para Alto Desempenho");
        }
        catch (Exception ex) { return Fail("Power plan", ex.Message); }
    }

    private BoostActionResult RevertPowerPlan()
    {
        if (string.IsNullOrWhiteSpace(_previousPlanGuid)) return Skip("Power plan");
        try
        {
            RunCmd("powercfg", $"/setactive {_previousPlanGuid}");
            return Ok("Power plan", "Plano de energia restaurado");
        }
        catch (Exception ex) { return Fail("Power plan", ex.Message); }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 3. NAGLE'S ALGORITHM — desativa por interface de rede
    //    TcpAckFrequency=1 e TCPNoDelay=1 fazem o TCP enviar cada pacote
    //    imediatamente sem aguardar acumulação de dados (ACK delay ~200 ms).
    // ══════════════════════════════════════════════════════════════════════════
    private const string TcpInterfaces =
        @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces";

    private BoostActionResult ApplyNagle()
    {
        try
        {
            using var root = Registry.LocalMachine.OpenSubKey(TcpInterfaces, writable: true);
            if (root is null) return Fail("Nagle (TCP)", "Chave de registro não encontrada");

            var changed = 0;
            foreach (var sub in root.GetSubKeyNames())
            {
                using var iface = root.OpenSubKey(sub, writable: true);
                if (iface is null) continue;
                // Só aplica em interfaces que têm endereço IP configurado
                var dhcp  = iface.GetValue("DhcpIPAddress") as string;
                var stat  = iface.GetValue("IPAddress")    as string[];
                var hasIp = !string.IsNullOrWhiteSpace(dhcp) ||
                            (stat is { Length: > 0 } && stat[0] != "0.0.0.0");
                if (!hasIp) continue;

                iface.SetValue("TcpAckFrequency", 1, RegistryValueKind.DWord);
                iface.SetValue("TCPNoDelay",      1, RegistryValueKind.DWord);
                changed++;
            }

            _nagleDisabled = changed > 0;
            return changed > 0
                ? Ok("Nagle (TCP)", $"Desativado em {changed} interface(s) — efeito imediato")
                : Ok("Nagle (TCP)", "Nenhuma interface ativa encontrada para ajustar");
        }
        catch (Exception ex) { return Fail("Nagle (TCP)", ex.Message); }
    }

    private BoostActionResult RevertNagle()
    {
        if (!_nagleDisabled) return Skip("Nagle (TCP)");
        try
        {
            using var root = Registry.LocalMachine.OpenSubKey(TcpInterfaces, writable: true);
            if (root is null) return Skip("Nagle (TCP)");

            foreach (var sub in root.GetSubKeyNames())
            {
                using var iface = root.OpenSubKey(sub, writable: true);
                iface?.DeleteValue("TcpAckFrequency", throwOnMissingValue: false);
                iface?.DeleteValue("TCPNoDelay",      throwOnMissingValue: false);
            }
            _nagleDisabled = false;
            return Ok("Nagle (TCP)", "Restaurado para padrão Windows");
        }
        catch (Exception ex) { return Fail("Nagle (TCP)", ex.Message); }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 4. WINDOWS GAME MODE
    // ══════════════════════════════════════════════════════════════════════════
    private const string GameBarKey = @"Software\Microsoft\GameBar";

    private BoostActionResult ApplyGameMode()
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(GameBarKey);
            var prev = key.GetValue("AutoGameModeEnabled");
            _gameModeWasDisabled = prev is int v && v == 0;

            key.SetValue("AutoGameModeEnabled", 1, RegistryValueKind.DWord);
            key.SetValue("AllowAutoGameMode",   1, RegistryValueKind.DWord);
            return Ok("Game Mode", "Ativado — Windows prioriza o jogo em foco");
        }
        catch (Exception ex) { return Fail("Game Mode", ex.Message); }
    }

    private BoostActionResult RevertGameMode()
    {
        if (!_gameModeWasDisabled) return Skip("Game Mode");
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(GameBarKey);
            key.SetValue("AutoGameModeEnabled", 0, RegistryValueKind.DWord);
            return Ok("Game Mode", "Restaurado para estado anterior");
        }
        catch (Exception ex) { return Fail("Game Mode", ex.Message); }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 5. FOREGROUND BOOST (Win32PrioritySeparation)
    //    Valor 38 (0x26) = quantum longo + boost máximo para o processo em foco.
    //    Padrão Windows: 2.
    // ══════════════════════════════════════════════════════════════════════════
    private const string PriorityKey =
        @"SYSTEM\CurrentControlSet\Control\PriorityControl";

    private BoostActionResult ApplyForegroundBoost()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(PriorityKey, writable: true);
            if (key is null) return Fail("Foreground boost", "Chave não encontrada");

            _previousPrioritySep = (int)(key.GetValue("Win32PrioritySeparation") ?? 2);
            key.SetValue("Win32PrioritySeparation", 38, RegistryValueKind.DWord);
            return Ok("Foreground boost", "CPU quantum máximo para o processo em foco");
        }
        catch (Exception ex) { return Fail("Foreground boost", ex.Message); }
    }

    private BoostActionResult RevertForegroundBoost()
    {
        if (_previousPrioritySep < 0) return Skip("Foreground boost");
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(PriorityKey, writable: true);
            key?.SetValue("Win32PrioritySeparation", _previousPrioritySep, RegistryValueKind.DWord);
            _previousPrioritySep = -1;
            return Ok("Foreground boost", "Restaurado para padrão Windows");
        }
        catch (Exception ex) { return Fail("Foreground boost", ex.Message); }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 6. NETWORK THROTTLING INDEX
    //    Desativa o throttle de rede que o Windows aplica em processos que
    //    detecta como "multimídia/jogo". Valor 0xFFFFFFFF = sem limite.
    // ══════════════════════════════════════════════════════════════════════════
    private const string MultimediaKey =
        @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile";

    private BoostActionResult ApplyNetworkThrottling()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(MultimediaKey, writable: true);
            if (key is null) return Fail("Network Throttling", "Chave não encontrada");

            _previousNetThrottle = (int)(key.GetValue("NetworkThrottlingIndex") ?? 10);
            key.SetValue("NetworkThrottlingIndex", unchecked((int)0xFFFFFFFF), RegistryValueKind.DWord);
            return Ok("Network Throttling", "Throttle de rede desativado — latência UDP reduzida");
        }
        catch (Exception ex) { return Fail("Network Throttling", ex.Message); }
    }

    private BoostActionResult RevertNetworkThrottling()
    {
        if (_previousNetThrottle < 0) return Skip("Network Throttling");
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(MultimediaKey, writable: true);
            key?.SetValue("NetworkThrottlingIndex", _previousNetThrottle, RegistryValueKind.DWord);
            _previousNetThrottle = -1;
            return Ok("Network Throttling", "Restaurado para padrão Windows");
        }
        catch (Exception ex) { return Fail("Network Throttling", ex.Message); }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 7. SYSTEM RESPONSIVENESS
    //    0 = 100% da CPU para o processo em foco (scheduler agressivo).
    //    Padrão Windows: 20 (reserva 20% para tarefas de sistema).
    // ══════════════════════════════════════════════════════════════════════════
    private BoostActionResult ApplySystemResponsiveness()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(MultimediaKey, writable: true);
            if (key is null) return Fail("System Responsiveness", "Chave não encontrada");

            _previousSysResponse = (int)(key.GetValue("SystemResponsiveness") ?? 20);
            key.SetValue("SystemResponsiveness", 0, RegistryValueKind.DWord);
            return Ok("System Responsiveness", "Scheduler 100% para o jogo em foco");
        }
        catch (Exception ex) { return Fail("System Responsiveness", ex.Message); }
    }

    private BoostActionResult RevertSystemResponsiveness()
    {
        if (_previousSysResponse < 0) return Skip("System Responsiveness");
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(MultimediaKey, writable: true);
            key?.SetValue("SystemResponsiveness", _previousSysResponse, RegistryValueKind.DWord);
            _previousSysResponse = -1;
            return Ok("System Responsiveness", "Restaurado para padrão Windows");
        }
        catch (Exception ex) { return Fail("System Responsiveness", ex.Message); }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 8. GAME DVR — desativa captura automática de gameplay
    //    GameDVR usa a GPU/CPU em segundo plano, causando stutters e spikes.
    // ══════════════════════════════════════════════════════════════════════════
    private const string GameConfigKey  = @"System\GameConfigStore";
    private const string GameDvrKey     = @"SOFTWARE\Microsoft\Windows\CurrentVersion\GameDVR";

    private BoostActionResult ApplyGameDvr()
    {
        try
        {
            using var cfg = Registry.CurrentUser.CreateSubKey(GameConfigKey);
            _previousGameDvr = (int)(cfg.GetValue("GameDVR_Enabled") ?? 1);
            cfg.SetValue("GameDVR_Enabled", 0, RegistryValueKind.DWord);

            using var dvr = Registry.CurrentUser.CreateSubKey(GameDvrKey);
            _previousAppCapture = (int)(dvr.GetValue("AppCaptureEnabled") ?? 1);
            dvr.SetValue("AppCaptureEnabled", 0, RegistryValueKind.DWord);

            return Ok("Game DVR", "Captura automática desativada — menos uso de GPU/CPU");
        }
        catch (Exception ex) { return Fail("Game DVR", ex.Message); }
    }

    private BoostActionResult RevertGameDvr()
    {
        if (_previousGameDvr < 0) return Skip("Game DVR");
        try
        {
            using var cfg = Registry.CurrentUser.CreateSubKey(GameConfigKey);
            cfg.SetValue("GameDVR_Enabled", _previousGameDvr, RegistryValueKind.DWord);

            using var dvr = Registry.CurrentUser.CreateSubKey(GameDvrKey);
            dvr.SetValue("AppCaptureEnabled", _previousAppCapture < 0 ? 1 : _previousAppCapture,
                         RegistryValueKind.DWord);

            _previousGameDvr = _previousAppCapture = -1;
            return Ok("Game DVR", "Restaurado para estado anterior");
        }
        catch (Exception ex) { return Fail("Game DVR", ex.Message); }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 9. ANIMAÇÕES DO WINDOWS
    //    Desativa animações de minimize/maximize — libera pequenas fatias
    //    de GPU e elimina jank visual que pode mascarar input real.
    // ══════════════════════════════════════════════════════════════════════════
    private const string WindowMetricsKey = @"Control Panel\Desktop\WindowMetrics";

    private BoostActionResult ApplyWindowsAnimations()
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(WindowMetricsKey);
            var prev = key.GetValue("MinAnimate") as string;
            _previousMinAnimate = prev == "0" ? 0 : 1;

            key.SetValue("MinAnimate", "0", RegistryValueKind.String);
            return Ok("Animações Windows", "Animações de janela desativadas");
        }
        catch (Exception ex) { return Fail("Animações Windows", ex.Message); }
    }

    private BoostActionResult RevertWindowsAnimations()
    {
        if (_previousMinAnimate < 0) return Skip("Animações Windows");
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(WindowMetricsKey);
            key.SetValue("MinAnimate", _previousMinAnimate == 0 ? "0" : "1", RegistryValueKind.String);
            _previousMinAnimate = -1;
            return Ok("Animações Windows", "Restaurado para estado anterior");
        }
        catch (Exception ex) { return Fail("Animações Windows", ex.Message); }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 10. GPU PRIORITY no SystemProfile\Tasks\Games
    //     GPU Priority=8 + Priority=6 + Scheduling Category=High
    //     Diz ao Windows Scheduler para dar máxima prioridade de GPU ao jogo.
    // ══════════════════════════════════════════════════════════════════════════
    private const string GamesTaskKey =
        @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games";

    private BoostActionResult ApplyGpuPriority()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(GamesTaskKey, writable: true);
            if (key is null) return Fail("GPU Priority", "Chave SystemProfile\\Tasks\\Games não encontrada");

            _previousGpuPriority = (int)(key.GetValue("GPU Priority") ?? 8);

            key.SetValue("GPU Priority",          8,      RegistryValueKind.DWord);
            key.SetValue("Priority",              6,      RegistryValueKind.DWord);
            key.SetValue("Scheduling Category",   "High", RegistryValueKind.String);
            key.SetValue("SFIO Priority",         "High", RegistryValueKind.String);
            key.SetValue("Background Only",       "False",RegistryValueKind.String);
            key.SetValue("Clock Rate",            10000,  RegistryValueKind.DWord);

            return Ok("GPU Priority", "GPU Priority=8, Scheduling=High — jogo recebe máxima prioridade de GPU");
        }
        catch (Exception ex) { return Fail("GPU Priority", ex.Message); }
    }

    private BoostActionResult RevertGpuPriority()
    {
        if (_previousGpuPriority < 0) return Skip("GPU Priority");
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(GamesTaskKey, writable: true);
            if (key is null) return Skip("GPU Priority");
            key.SetValue("GPU Priority",        _previousGpuPriority, RegistryValueKind.DWord);
            key.SetValue("Priority",            2,      RegistryValueKind.DWord);
            key.SetValue("Scheduling Category", "Medium", RegistryValueKind.String);
            key.SetValue("SFIO Priority",       "Normal", RegistryValueKind.String);
            key.SetValue("Background Only",     "True",  RegistryValueKind.String);
            _previousGpuPriority = -1;
            return Ok("GPU Priority", "Restaurado para padrão Windows");
        }
        catch (Exception ex) { return Fail("GPU Priority", ex.Message); }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 11. HAGS — Hardware-Accelerated GPU Scheduling
    //     HwSchMode=2: GPU gerencia sua própria fila de memória diretamente,
    //     reduzindo latência frame→tela em ~10-20% em GPUs modernas.
    //     Requer GPU compatível (NVIDIA Turing+ / AMD RDNA+).
    // ══════════════════════════════════════════════════════════════════════════
    private const string GraphicsDriversKey =
        @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers";

    private BoostActionResult ApplyHags()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(GraphicsDriversKey, writable: true);
            if (key is null) return Fail("HAGS", "Chave GraphicsDrivers não encontrada");

            _previousHagsMode = (int)(key.GetValue("HwSchMode") ?? 1);
            if (_previousHagsMode == 2)
                return Ok("HAGS", "Hardware-Accelerated GPU Scheduling já estava ativo");

            key.SetValue("HwSchMode", 2, RegistryValueKind.DWord);
            return Ok("HAGS", "Ativado — GPU gerencia fila de memória diretamente (efeito no próximo boot)");
        }
        catch (Exception ex) { return Fail("HAGS", ex.Message); }
    }

    private BoostActionResult RevertHags()
    {
        if (_previousHagsMode < 0) return Skip("HAGS");
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(GraphicsDriversKey, writable: true);
            if (key is null) return Skip("HAGS");
            key.SetValue("HwSchMode", _previousHagsMode, RegistryValueKind.DWord);
            _previousHagsMode = -1;
            return Ok("HAGS", "Restaurado para estado anterior");
        }
        catch (Exception ex) { return Fail("HAGS", ex.Message); }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 12. TCP TIMESTAMPS — desativa overhead de timestamp por pacote
    //     Timestamps TCP adicionam 12 bytes de overhead por pacote.
    //     Desativar melhora throughput e reduz CPU em conexões de alta taxa.
    // ══════════════════════════════════════════════════════════════════════════
    private BoostActionResult ApplyTcpTimestamps()
    {
        try
        {
            var output = RunCmd("netsh", "int tcp set global timestamps=disabled");
            _tcpTimestampsDisabled = true;
            return Ok("TCP Timestamps", "Desativado — menos overhead por pacote TCP");
        }
        catch (Exception ex) { return Fail("TCP Timestamps", ex.Message); }
    }

    private BoostActionResult RevertTcpTimestamps()
    {
        if (!_tcpTimestampsDisabled) return Skip("TCP Timestamps");
        try
        {
            RunCmd("netsh", "int tcp set global timestamps=enabled");
            _tcpTimestampsDisabled = false;
            return Ok("TCP Timestamps", "Restaurado para padrão Windows");
        }
        catch (Exception ex) { return Fail("TCP Timestamps", ex.Message); }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 13. FULLSCREEN OPTIMIZATIONS — desativa globalmente
    //     Windows aplica DWM compositor mesmo em jogos "fullscreen" por padrão.
    //     Desativar força Exclusive Fullscreen real — menos overhead de frame.
    // ══════════════════════════════════════════════════════════════════════════
    private BoostActionResult ApplyFullscreenOptimizations()
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(GameConfigKey);
            _previousFseBehavior = (int)(key.GetValue("GameDVR_FSEBehaviorMode") ?? 0);

            key.SetValue("GameDVR_DXGIHonorFSEWindowsCompatible", 1, RegistryValueKind.DWord);
            key.SetValue("GameDVR_EFSEFeatureFlags",               0, RegistryValueKind.DWord);
            key.SetValue("GameDVR_FSEBehaviorMode",                2, RegistryValueKind.DWord);
            key.SetValue("GameDVR_HonorUserFSEBehaviorMode",       1, RegistryValueKind.DWord);
            key.SetValue("GameDVR_FSEBehavior",                    2, RegistryValueKind.DWord);

            return Ok("Fullscreen Optimizations", "Desativadas — jogo usará Exclusive Fullscreen real (+FPS)");
        }
        catch (Exception ex) { return Fail("Fullscreen Optimizations", ex.Message); }
    }

    private BoostActionResult RevertFullscreenOptimizations()
    {
        if (_previousFseBehavior < 0) return Skip("Fullscreen Optimizations");
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(GameConfigKey);
            key.SetValue("GameDVR_DXGIHonorFSEWindowsCompatible", 0, RegistryValueKind.DWord);
            key.SetValue("GameDVR_EFSEFeatureFlags",               0, RegistryValueKind.DWord);
            key.SetValue("GameDVR_FSEBehaviorMode",  _previousFseBehavior, RegistryValueKind.DWord);
            key.SetValue("GameDVR_HonorUserFSEBehaviorMode", 0, RegistryValueKind.DWord);
            _previousFseBehavior = -1;
            return Ok("Fullscreen Optimizations", "Restaurado — Windows gerencia modo de tela novamente");
        }
        catch (Exception ex) { return Fail("Fullscreen Optimizations", ex.Message); }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 14. VISUAL EFFECTS — mínimo
    //     Desativa sombras, transparência, animações de UI e Aero Peek.
    //     Libera GPU compositor para o jogo.
    // ══════════════════════════════════════════════════════════════════════════
    private const string VisualEffectsKey  = @"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects";
    private const string PersonalizeKey    = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string DwmKey            = @"Software\Microsoft\Windows\DWM";

    private BoostActionResult ApplyVisualEffects()
    {
        try
        {
            using var vfx = Registry.CurrentUser.CreateSubKey(VisualEffectsKey);
            _previousVisualFx = (int)(vfx.GetValue("VisualFXSetting") ?? 0);
            vfx.SetValue("VisualFXSetting", 2, RegistryValueKind.DWord); // 2 = Best performance

            using var pers = Registry.CurrentUser.CreateSubKey(PersonalizeKey);
            _previousTransparency = (int)(pers.GetValue("EnableTransparency") ?? 1);
            pers.SetValue("EnableTransparency", 0, RegistryValueKind.DWord);

            using var dwm = Registry.CurrentUser.CreateSubKey(DwmKey);
            dwm.SetValue("EnableAeroPeek", 0, RegistryValueKind.DWord);

            return Ok("Visual Effects", "Mínimo ativo — GPU compositor liberado para o jogo (+FPS)");
        }
        catch (Exception ex) { return Fail("Visual Effects", ex.Message); }
    }

    private BoostActionResult RevertVisualEffects()
    {
        if (_previousVisualFx < 0) return Skip("Visual Effects");
        try
        {
            using var vfx = Registry.CurrentUser.CreateSubKey(VisualEffectsKey);
            vfx.SetValue("VisualFXSetting", _previousVisualFx, RegistryValueKind.DWord);

            using var pers = Registry.CurrentUser.CreateSubKey(PersonalizeKey);
            pers.SetValue("EnableTransparency", _previousTransparency < 0 ? 1 : _previousTransparency,
                          RegistryValueKind.DWord);

            using var dwm = Registry.CurrentUser.CreateSubKey(DwmKey);
            dwm.SetValue("EnableAeroPeek", 1, RegistryValueKind.DWord);

            _previousVisualFx = _previousTransparency = -1;
            return Ok("Visual Effects", "Restaurado para configuração anterior");
        }
        catch (Exception ex) { return Fail("Visual Effects", ex.Message); }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 15. TRIM MEMÓRIA DE PROCESSOS EM BACKGROUND
    //     EmptyWorkingSet em processos não críticos libera RAM física para o
    //     jogo. Processos recuperam memória naturalmente quando necessário.
    // ══════════════════════════════════════════════════════════════════════════
    private static readonly HashSet<string> _systemProcs = new(StringComparer.OrdinalIgnoreCase)
    {
        "system", "idle", "registry", "smss", "csrss", "wininit", "winlogon",
        "lsass", "lsm", "services", "svchost", "fontdrvhost", "dwm",
        "taskmgr", "conhost", "ayvu nolag", "AYVU NoLag"
    };

    private BoostActionResult ApplyProcessMemoryTrim()
    {
        var trimmed = 0;
        foreach (var proc in Process.GetProcesses())
        {
            if (_systemProcs.Contains(proc.ProcessName)) continue;
            try
            {
                EmptyWorkingSet(proc.Handle);
                trimmed++;
            }
            catch { /* processo sem permissão — ignorar */ }
        }
        return Ok("Trim RAM background", $"Memória aparada em {trimmed} processos — mais RAM para o jogo");
    }

    // NOTA: MSI Mode foi movido para MsiModeService (painel GPU Mode dedicado),
    // pois é uma configuração permanente de hardware — não faz parte do ciclo
    // de otimizações por sessão que o botão Otimizar aplica/reverte.

    // ── Factories de resultado ────────────────────────────────────────────────
    private static BoostActionResult Ok(string name, string detail)
        => new(name, true,  detail);
    private static BoostActionResult Fail(string name, string detail)
        => new(name, false, detail);
    private static BoostActionResult Skip(string name)
        => new(name, true,  "Não foi alterado — nada a reverter");

    // ── Helpers ───────────────────────────────────────────────────────────────
    private static string RunCmd(string exe, string args)
    {
        var psi = new ProcessStartInfo(exe, args)
        {
            UseShellExecute        = false,
            RedirectStandardOutput = true,
            CreateNoWindow         = true,
        };
        using var p = Process.Start(psi)!;
        var output = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return output;
    }

    // ── P/Invoke ──────────────────────────────────────────────────────────────
    [DllImport("winmm.dll")]  private static extern uint timeBeginPeriod(uint uPeriod);
    [DllImport("winmm.dll")]  private static extern uint timeEndPeriod(uint uPeriod);
    [DllImport("psapi.dll")]  private static extern bool EmptyWorkingSet(IntPtr processHandle);
}
