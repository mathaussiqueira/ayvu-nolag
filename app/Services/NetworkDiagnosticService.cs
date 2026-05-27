using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using AYVUNoLag.Models;

namespace AYVUNoLag.Services;

public sealed class NetworkDiagnosticService
{
    private static readonly string[] DefaultPingTargets = ["1.1.1.1", "8.8.8.8", "9.9.9.9"];

    private static readonly string[] DefaultDnsHosts = ["google.com", "cloudflare.com", "steampowered.com"];

    /// <summary>Pings todos os targets padrão em paralelo — uma rodada rápida (~1-2s).</summary>
    public async Task<IReadOnlyList<PingSample>> QuickPingAsync(CancellationToken ct)
    {
        var tasks = DefaultPingTargets.Select(t => PingAsync(t, ct)).ToArray();
        return await Task.WhenAll(tasks);
    }

    /// <summary>Resolve todos os hosts DNS padrão em paralelo.</summary>
    public async Task<IReadOnlyList<DnsProbeResult>> QuickDnsAsync(CancellationToken ct)
    {
        var tasks = DefaultDnsHosts.Select(h => ProbeDnsAsync(h, ct)).ToArray();
        return await Task.WhenAll(tasks);
    }

    public async Task<NoLagSnapshot> RunAsync(CancellationToken cancellationToken)
    {
        var snapshot = new NoLagSnapshot();

        foreach (var process in GameProcessService.FindLikelyGameProcesses())
        {
            snapshot.GameProcesses.Add(process);
        }

        foreach (var target in DefaultPingTargets)
        {
            for (var i = 0; i < 4; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                snapshot.PingSamples.Add(await PingAsync(target, cancellationToken));
            }
        }

        foreach (var host in DefaultDnsHosts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            snapshot.DnsResults.Add(await ProbeDnsAsync(host, cancellationToken));
        }

        ApplySummary(snapshot);
        return snapshot;
    }

    private static async Task<PingSample> PingAsync(string target, CancellationToken cancellationToken)
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(target, 1200).WaitAsync(cancellationToken);
            var success = reply.Status == IPStatus.Success;
            return new PingSample(target, success ? reply.RoundtripTime : null, success, reply.Status.ToString());
        }
        catch (Exception ex) when (ex is PingException or TimeoutException or OperationCanceledException)
        {
            if (ex is OperationCanceledException)
            {
                throw;
            }

            return new PingSample(target, null, false, ex.Message);
        }
    }

    private static async Task<DnsProbeResult> ProbeDnsAsync(string host, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(host, cancellationToken);
            sw.Stop();
            return new DnsProbeResult(
                host,
                addresses.Select(address => address.ToString()).ToArray(),
                sw.ElapsedMilliseconds,
                addresses.Length > 0,
                addresses.Length > 0 ? "Resolvido" : "Sem resposta");
        }
        catch (Exception ex) when (ex is System.Net.Sockets.SocketException or OperationCanceledException)
        {
            if (ex is OperationCanceledException)
            {
                throw;
            }

            sw.Stop();
            return new DnsProbeResult(host, [], sw.ElapsedMilliseconds, false, ex.Message);
        }
    }

    private static void ApplySummary(NoLagSnapshot snapshot)
    {
        var successful = snapshot.PingSamples
            .Where(sample => sample.Success && sample.LatencyMs.HasValue)
            .Select(sample => sample.LatencyMs!.Value)
            .ToArray();

        var total = snapshot.PingSamples.Count;
        var failed = total - successful.Length;
        snapshot.PacketLossPercent = total == 0 ? 0 : Math.Round(failed * 100d / total, 1);

        if (successful.Length == 0)
        {
            snapshot.Verdict = "Sem resposta de rede. Verifique conexao, firewall ou DNS.";
            return;
        }

        snapshot.AveragePingMs = Math.Round(successful.Average(), 1);
        snapshot.BestPingMs = successful.Min();
        snapshot.WorstPingMs = successful.Max();
        snapshot.JitterMs = Math.Round(successful.Select(value => Math.Abs(value - snapshot.AveragePingMs.Value)).Average(), 1);

        snapshot.Verdict = snapshot.PacketLossPercent switch
        {
            >= 20 => "Perda alta. Priorize cabo, reinicie roteador e teste outro DNS.",
            >= 5 => "Perda moderada. Ha instabilidade na rota ou rede local.",
            _ when snapshot.JitterMs >= 30 => "Jitter alto. Pode haver Wi-Fi instavel ou rota congestionada.",
            _ when snapshot.AveragePingMs >= 120 => "Ping alto. Otimizacao local ajuda pouco se o servidor estiver longe.",
            _ => "Conexao saudavel para jogo online."
        };
    }
}
