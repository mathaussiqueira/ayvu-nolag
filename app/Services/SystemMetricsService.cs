using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AYVUNoLag.Services;

/// <summary>
/// Lê métricas do hardware local: CPU, RAM e GPU.
/// </summary>
public sealed class SystemMetricsService : IDisposable
{
    // ── CPU ───────────────────────────────────────────────────────────────────
    private readonly PerformanceCounter? _cpuCounter;

    // ── Disco ─────────────────────────────────────────────────────────────────
    private readonly PerformanceCounter? _diskCounter;

    // ── GPU ───────────────────────────────────────────────────────────────────
    private readonly List<PerformanceCounter> _gpuCounters = [];
    private bool _gpuAvailable = false;

    // ── RAM (Win32) ───────────────────────────────────────────────────────────
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MEMORYSTATUSEX
    {
        public uint  dwLength;
        public uint  dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    // ─────────────────────────────────────────────────────────────────────────

    public SystemMetricsService()
    {
        // CPU counter — primeira leitura retorna 0, por isso fazemos aqui
        try
        {
            _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            _cpuCounter.NextValue(); // warm-up
        }
        catch { _cpuCounter = null; }

        // Disco — uso total de I/O
        try
        {
            _diskCounter = new PerformanceCounter("PhysicalDisk", "% Disk Time", "_Total");
            _diskCounter.NextValue(); // warm-up
        }
        catch { _diskCounter = null; }

        // GPU counters via PDH "GPU Engine"
        try
        {
            var cat = new PerformanceCounterCategory("GPU Engine");
            var instances = cat.GetInstanceNames()
                              .Where(n => n.Contains("engtype_3D", StringComparison.OrdinalIgnoreCase))
                              .ToArray();

            foreach (var inst in instances)
            {
                try
                {
                    var pc = new PerformanceCounter("GPU Engine", "Utilization Percentage", inst, readOnly: true);
                    pc.NextValue(); // warm-up
                    _gpuCounters.Add(pc);
                }
                catch { /* ignora instâncias inválidas */ }
            }

            _gpuAvailable = _gpuCounters.Count > 0;
        }
        catch { /* GPU Engine não disponível neste sistema */ }
    }

    // ── Leituras ──────────────────────────────────────────────────────────────

    /// <summary>Uso de CPU em % (0–100). Retorna -1 se não disponível.</summary>
    public float GetCpuUsage()
    {
        if (_cpuCounter is null) return -1f;
        try { return Math.Clamp(_cpuCounter.NextValue(), 0f, 100f); }
        catch { return -1f; }
    }

    /// <summary>Uso de GPU em % (0–100). Retorna -1 se não disponível.</summary>
    public float GetGpuUsage()
    {
        if (!_gpuAvailable) return -1f;
        try
        {
            var total = _gpuCounters.Sum(c => { try { return c.NextValue(); } catch { return 0f; } });
            return Math.Clamp(total, 0f, 100f);
        }
        catch { return -1f; }
    }

    /// <summary>Uso de disco em % (0–100). Retorna -1 se não disponível.</summary>
    public float GetDiskUsage()
    {
        if (_diskCounter is null) return -1f;
        try { return Math.Clamp(_diskCounter.NextValue(), 0f, 100f); }
        catch { return -1f; }
    }

    /// <summary>Retorna (usedGB, totalGB). Retorna (-1,-1) se falhar.</summary>
    public (double usedGB, double totalGB) GetRamUsage()
    {
        var mem = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (!GlobalMemoryStatusEx(ref mem)) return (-1, -1);

        var totalGB = mem.ullTotalPhys / 1024.0 / 1024.0 / 1024.0;
        var availGB = mem.ullAvailPhys / 1024.0 / 1024.0 / 1024.0;
        return (Math.Round(totalGB - availGB, 1), Math.Round(totalGB, 1));
    }

    // ─────────────────────────────────────────────────────────────────────────

    public void Dispose()
    {
        _cpuCounter?.Dispose();
        _diskCounter?.Dispose();
        foreach (var c in _gpuCounters) c.Dispose();
        _gpuCounters.Clear();
    }
}
