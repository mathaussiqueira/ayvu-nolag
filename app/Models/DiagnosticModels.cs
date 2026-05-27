using System.Collections.ObjectModel;

namespace AYVUNoLag.Models;

public sealed record GameProcess(int ProcessId, string Name, string WindowTitle, long MemoryMb);

public sealed record PingSample(string Target, long? LatencyMs, bool Success, string Status);

public sealed record DnsProbeResult(string Host, string[] Addresses, long ElapsedMs, bool Success, string Status);

public sealed record BoostActionResult(string Name, bool Success, string Detail);

public sealed class NoLagSnapshot
{
    public ObservableCollection<GameProcess> GameProcesses { get; } = [];

    public ObservableCollection<PingSample> PingSamples { get; } = [];

    public ObservableCollection<DnsProbeResult> DnsResults { get; } = [];

    public ObservableCollection<BoostActionResult> BoostResults { get; } = [];

    public double? AveragePingMs { get; set; }

    public long? BestPingMs { get; set; }

    public long? WorstPingMs { get; set; }

    public double? JitterMs { get; set; }

    public double PacketLossPercent { get; set; }

    public DateTimeOffset LastUpdated { get; set; } = DateTimeOffset.Now;

    public string Verdict { get; set; } = "Aguardando diagnostico";
}
