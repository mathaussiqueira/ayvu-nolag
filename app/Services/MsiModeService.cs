using System.Management;
using Microsoft.Win32;

namespace AYVUNoLag.Services;

/// <summary>
/// Gerencia o MSI Mode (Message Signaled Interrupts) — uma configuração
/// PERMANENTE de hardware no registro do Windows. Diferente das otimizações
/// por sessão, MSI Mode é escrito uma vez e persiste em todo boot.
///
/// Aplica em dois tipos de dispositivo:
///   - GPU  → frametime mais estável, menos stutter
///   - Rede → jitter de pacotes reduzido, ping mais consistente
///
/// Requer reinicialização para o driver carregar a nova configuração.
/// </summary>
public sealed class MsiModeService
{
    public sealed record DeviceMsiInfo(
        string Name,
        string Category,   // "GPU" ou "Rede"
        string PnpId,
        bool   MsiEnabled);

    private const string MsiSubPath =
        @"Device Parameters\Interrupt Management\MessageSignaledInterruptProperties";

    private static string EnumPath(string pnpId) =>
        $@"SYSTEM\CurrentControlSet\Enum\{pnpId}\{MsiSubPath}";

    // ── Detecção ────────────────────────────────────────────────────────────────

    /// <summary>Lista GPUs e adaptadores de rede com seu estado MSI atual.</summary>
    public IReadOnlyList<DeviceMsiInfo> DetectDevices()
    {
        var result = new List<DeviceMsiInfo>();
        result.AddRange(QueryDevices(
            "SELECT Name, PNPDeviceID FROM Win32_VideoController WHERE PNPDeviceID IS NOT NULL",
            "GPU"));
        result.AddRange(QueryDevices(
            "SELECT Name, PNPDeviceID FROM Win32_NetworkAdapter WHERE PNPDeviceID IS NOT NULL AND PhysicalAdapter = TRUE",
            "Rede"));
        return result;
    }

    private static IEnumerable<DeviceMsiInfo> QueryDevices(string wql, string category)
    {
        var devices = new List<DeviceMsiInfo>();
        try
        {
            using var searcher = new ManagementObjectSearcher(wql);
            foreach (ManagementObject obj in searcher.Get())
            {
                var name  = obj["Name"]?.ToString()        ?? "Desconhecido";
                var pnpId = obj["PNPDeviceID"]?.ToString() ?? "";
                if (string.IsNullOrEmpty(pnpId)) continue;

                // Ignora adaptadores virtuais comuns (sem MSI físico)
                if (category == "Rede" && IsVirtualAdapter(name)) continue;

                var enabled = ReadMsiState(pnpId);
                devices.Add(new DeviceMsiInfo(name, category, pnpId, enabled));
            }
        }
        catch { /* sem acesso WMI */ }
        return devices;
    }

    private static bool IsVirtualAdapter(string name) =>
        name.Contains("Virtual",   StringComparison.OrdinalIgnoreCase) ||
        name.Contains("WAN Miniport", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Loopback",  StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Bluetooth", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("VMware",    StringComparison.OrdinalIgnoreCase) ||
        name.Contains("VirtualBox",StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Hyper-V",   StringComparison.OrdinalIgnoreCase) ||
        name.Contains("TAP",       StringComparison.OrdinalIgnoreCase);

    private static bool ReadMsiState(string pnpId)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(EnumPath(pnpId));
            return key is not null && Convert.ToInt32(key.GetValue("MSISupported") ?? 0) == 1;
        }
        catch { return false; }
    }

    // ── Aplicação ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Aplica MSI Mode em um dispositivo. Retorna (sucesso, mudouEstado, detalhe).
    /// mudouEstado=true só quando o valor realmente foi alterado (precisa restart).
    /// </summary>
    public (bool ok, bool changed, string detail) SetMsi(string pnpId, bool enable)
    {
        try
        {
            var currentlyEnabled = ReadMsiState(pnpId);
            if (currentlyEnabled == enable)
                return (true, false, enable
                    ? "MSI já estava ativo — nenhuma mudança."
                    : "MSI já estava inativo — nenhuma mudança.");

            using var key = Registry.LocalMachine.CreateSubKey(EnumPath(pnpId));
            if (key is null)
                return (false, false, "Sem permissão. Execute como Administrador.");

            key.SetValue("MSISupported", enable ? 1 : 0, RegistryValueKind.DWord);
            return (true, true, enable ? "MSI Mode ativado." : "MSI Mode desativado.");
        }
        catch (UnauthorizedAccessException)
        {
            return (false, false, "Permissão negada. Execute como Administrador.");
        }
        catch (Exception ex)
        {
            return (false, false, $"Erro: {ex.Message}");
        }
    }

    /// <summary>
    /// Aplica MSI em todos os dispositivos de uma categoria ("GPU", "Rede" ou null=todos).
    /// Retorna (totalAplicado, totalMudou, detalhe).
    /// </summary>
    public (int applied, int changed, string detail) SetMsiAll(bool enable, string? category = null)
    {
        var devices = DetectDevices()
            .Where(d => category is null || d.Category == category)
            .ToList();

        if (devices.Count == 0)
            return (0, 0, "Nenhum dispositivo compatível encontrado.");

        var applied = 0;
        var changed = 0;
        foreach (var d in devices)
        {
            var (ok, didChange, _) = SetMsi(d.PnpId, enable);
            if (ok) applied++;
            if (didChange) changed++;
        }

        var verb = enable ? "ativado" : "desativado";
        return (applied, changed,
            changed > 0
                ? $"MSI {verb} em {changed} dispositivo(s) — reinicie para efeito total."
                : $"MSI já estava no estado desejado em todos os {applied} dispositivo(s).");
    }
}
