using System.Diagnostics;
using AYVUNoLag.Models;

namespace AYVUNoLag.Services;

public static class GameProcessService
{
    private static readonly string[] KnownGameHints =
    [
        "steam",
        "riot",
        "valorant",
        "fortnite",
        "cs2",
        "counter",
        "league",
        "dota",
        "pubg",
        "roblox",
        "minecraft",
        "warzone",
        "battle",
        "epic",
        "fivem",
        "gta",
        "tibia",
        "albion"
    ];

    public static IReadOnlyList<GameProcess> FindLikelyGameProcesses()
    {
        return Process.GetProcesses()
            .Where(IsLikelyGameProcess)
            .OrderByDescending(process => SafeMemoryMb(process))
            .Take(12)
            .Select(process => new GameProcess(
                process.Id,
                process.ProcessName,
                SafeTitle(process),
                SafeMemoryMb(process)))
            .ToArray();
    }

    public static BoostActionResult PrioritizeProcess(int processId)
    {
        try
        {
            var process = Process.GetProcessById(processId);
            process.PriorityClass = ProcessPriorityClass.High;
            return new BoostActionResult("Prioridade do jogo", true, $"{process.ProcessName} definido como High.");
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return new BoostActionResult("Prioridade do jogo", false, ex.Message);
        }
    }

    public static BoostActionResult NormalizeProcess(int processId)
    {
        try
        {
            var process = Process.GetProcessById(processId);
            process.PriorityClass = ProcessPriorityClass.Normal;
            return new BoostActionResult("Prioridade do jogo", true, $"{process.ProcessName} restaurado para Normal.");
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return new BoostActionResult("Prioridade do jogo", false, ex.Message);
        }
    }

    private static bool IsLikelyGameProcess(Process process)
    {
        var name = process.ProcessName.ToLowerInvariant();
        var title = SafeTitle(process).ToLowerInvariant();
        var hasHint = KnownGameHints.Any(hint => name.Contains(hint) || title.Contains(hint));
        return hasHint || (SafeMemoryMb(process) > 700 && !string.IsNullOrWhiteSpace(title));
    }

    private static string SafeTitle(Process process)
    {
        try
        {
            return process.MainWindowTitle;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static long SafeMemoryMb(Process process)
    {
        try
        {
            return process.WorkingSet64 / 1024 / 1024;
        }
        catch
        {
            return 0;
        }
    }
}
