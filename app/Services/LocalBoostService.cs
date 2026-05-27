using System.Diagnostics;
using System.Runtime.InteropServices;
using AYVUNoLag.Models;

namespace AYVUNoLag.Services;

public sealed class LocalBoostService
{
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
        results.Add(new BoostActionResult("DNS seguro", true, "v1 apenas diagnostica DNS; troca automatica fica para etapa aprovada."));
        results.Add(new BoostActionResult("Trafego local", true, "Feche downloads, stream e sync em nuvem durante a partida."));
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

        return results;
    }

    private static BoostActionResult TrimCurrentProcessMemory()
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            var ok = EmptyWorkingSet(process.Handle);
            return ok
                ? new BoostActionResult("RAM do app", true, "Memoria de trabalho do NoLag aparada.")
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
