using Microsoft.Win32;

namespace AYVUNoLag.Services;

public enum GpuScalingMode
{
    FullPanel           = 0,
    Centered            = 1,
    PreserveAspectRatio = 2,
}

public sealed class GpuScalingService
{
    private const string DisplayClass =
        @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";

    public (bool success, string detail) Apply(GpuScalingMode mode)
    {
        try
        {
            var path = FindAmdAdapterPath();
            if (path is null)
                return (false, "GPU AMD Radeon não encontrada.");

            using var key = Registry.LocalMachine.OpenSubKey(path, writable: true);
            if (key is null)
                return (false, "Sem permissão. Execute o app como Administrador.");

            key.SetValue("KMD_GPUScaling",  1,          RegistryValueKind.DWord);
            key.SetValue("KMD_ScalingMode", (int)mode,  RegistryValueKind.DWord);

            return (true, $"GPU Scaling → {ModeName(mode)} aplicado com sucesso.");
        }
        catch (UnauthorizedAccessException)
        {
            return (false, "Permissão negada. Execute como Administrador.");
        }
        catch (Exception ex)
        {
            return (false, $"Erro: {ex.Message}");
        }
    }

    public GpuScalingMode ReadCurrent()
    {
        try
        {
            var path = FindAmdAdapterPath();
            if (path is null) return GpuScalingMode.FullPanel;

            using var key = Registry.LocalMachine.OpenSubKey(path);
            if (key is null) return GpuScalingMode.FullPanel;

            var raw = key.GetValue("KMD_ScalingMode");
            return raw is null ? GpuScalingMode.FullPanel : (GpuScalingMode)Convert.ToInt32(raw);
        }
        catch
        {
            return GpuScalingMode.FullPanel;
        }
    }

    public bool IsAmdPresent() => FindAmdAdapterPath() is not null;

    private static string? FindAmdAdapterPath()
    {
        using var classKey = Registry.LocalMachine.OpenSubKey(DisplayClass);
        if (classKey is null) return null;

        foreach (var name in classKey.GetSubKeyNames())
        {
            if (!name.All(char.IsDigit)) continue;

            var subPath = $@"{DisplayClass}\{name}";
            using var sub = Registry.LocalMachine.OpenSubKey(subPath);
            if (sub is null) continue;

            var desc = (sub.GetValue("DriverDesc") as string) ?? "";
            if (desc.Contains("AMD",    StringComparison.OrdinalIgnoreCase) ||
                desc.Contains("Radeon", StringComparison.OrdinalIgnoreCase))
                return subPath;
        }
        return null;
    }

    public static string ModeName(GpuScalingMode m) => m switch
    {
        GpuScalingMode.FullPanel           => "Full Panel",
        GpuScalingMode.Centered            => "Centered",
        GpuScalingMode.PreserveAspectRatio => "Preserve Aspect Ratio",
        _                                  => m.ToString(),
    };
}
