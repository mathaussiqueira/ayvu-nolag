using System.Diagnostics;
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

    // ── P/Invoke — winmm.dll ──────────────────────────────────────────────────
    [DllImport("winmm.dll")] private static extern uint timeBeginPeriod(uint uPeriod);
    [DllImport("winmm.dll")] private static extern uint timeEndPeriod(uint uPeriod);
}
