using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using AYVUNoLag.Models;

namespace AYVUNoLag.Services;

public sealed class LocalBoostService
{
    // DNS salvo para reversão
    private readonly Dictionary<string, string[]> _previousDns = new();
    private bool _dnsChanged = false;

    public IReadOnlyList<BoostActionResult> RunBoost(int? processId)
    {
        var results = new List<BoostActionResult>
        {
            new("Modo AYVU Boost", true, "Perfil local aplicado sem alterar arquivos do sistema.")
        };

        results.Add(processId.HasValue
            ? GameProcessService.PrioritizeProcess(processId.Value)
            : new BoostActionResult("Prioridade do jogo", false, "Selecione um processo detectado antes de aplicar."));

        results.Add(TrimCurrentProcessMemory());
        results.Add(ApplyFastDns());
        results.Add(new BoostActionResult("Tráfego local", true,
            "Dica: feche downloads, OBS, sincronização em nuvem e Discord video durante a partida para liberar banda."));
        return results;
    }

    public IReadOnlyList<BoostActionResult> PauseBoost(int? processId)
    {
        var results = new List<BoostActionResult>
        {
            new("Modo AYVU Boost", true, "Boost pausado. Prioridades revertidas.")
        };

        results.Add(processId.HasValue
            ? GameProcessService.NormalizeProcess(processId.Value)
            : new BoostActionResult("Prioridade do jogo", false, "Nenhum processo estava priorizado."));

        if (_dnsChanged) results.Add(RevertDns());

        return results;
    }

    // ── DNS Rápido (Cloudflare 1.1.1.1 + Google 8.8.8.8) ─────────────────────
    private BoostActionResult ApplyFastDns()
    {
        try
        {
            _previousDns.Clear();
            var changed = 0;

            using var searcher = new ManagementObjectSearcher(
                "SELECT * FROM Win32_NetworkAdapterConfiguration WHERE IPEnabled = TRUE");

            foreach (ManagementObject obj in searcher.Get())
            {
                var adapterIndex = obj["Index"]?.ToString() ?? "?";
                var currentDns   = obj["DNSServerSearchOrder"] as string[] ?? [];
                _previousDns[adapterIndex] = currentDns;

                var result = obj.InvokeMethod("SetDNSServerSearchOrder",
                    new object[] { new string[] { "1.1.1.1", "8.8.8.8", "1.0.0.1" } });

                if (result is uint code && (code == 0 || code == 1))
                    changed++;
            }

            if (changed == 0) return new BoostActionResult("DNS rápido", false, "Nenhuma interface ativa para alterar DNS");

            _dnsChanged = true;
            return new BoostActionResult("DNS rápido", true,
                $"DNS → 1.1.1.1 (Cloudflare) em {changed} interface(s) — resolução mais rápida");
        }
        catch (Exception ex)
        {
            return new BoostActionResult("DNS rápido", false, $"Erro ao alterar DNS: {ex.Message}");
        }
    }

    private BoostActionResult RevertDns()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT * FROM Win32_NetworkAdapterConfiguration WHERE IPEnabled = TRUE");

            foreach (ManagementObject obj in searcher.Get())
            {
                var adapterIndex = obj["Index"]?.ToString() ?? "?";
                if (!_previousDns.TryGetValue(adapterIndex, out var prev)) continue;

                obj.InvokeMethod("SetDNSServerSearchOrder",
                    new object[] { prev.Length > 0 ? prev : null! });
            }

            _previousDns.Clear();
            _dnsChanged = false;
            return new BoostActionResult("DNS rápido", true, "DNS restaurado para configuração anterior");
        }
        catch (Exception ex)
        {
            return new BoostActionResult("DNS rápido", false, $"Erro ao reverter DNS: {ex.Message}");
        }
    }

    // ── Trim de memória do processo AYVU NoLag ────────────────────────────────
    private static BoostActionResult TrimCurrentProcessMemory()
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            var ok = EmptyWorkingSet(process.Handle);
            return ok
                ? new BoostActionResult("RAM do app", true, "Memória de trabalho do NoLag aparada.")
                : new BoostActionResult("RAM do app", false, "Windows recusou a limpeza leve.");
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return new BoostActionResult("RAM do app", false, ex.Message);
        }
    }

    [DllImport("psapi.dll")]
    private static extern bool EmptyWorkingSet(IntPtr processHandle);
}
