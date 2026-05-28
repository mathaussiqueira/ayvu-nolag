using AYVUNoLag.Tunnel;

namespace AYVUNoLag.Services;

public enum WarpStatus { NotReady, Disconnected, Connecting, Connected }

/// <summary>
/// Fachada entre a UI e o WireGuardEngine.
/// Não depende mais de WARP instalado — usa wireguard.exe local + API Cloudflare.
/// </summary>
public sealed class WarpService
{
    private readonly WireGuardEngine _engine = new();

    public bool EngineReady  => _engine.WireGuardReady;
    public bool IsConnected  => _engine.IsConnected;

    public WarpStatus GetStatus()
    {
        if (!EngineReady)     return WarpStatus.NotReady;
        if (IsConnected)      return WarpStatus.Connected;
        return WarpStatus.Disconnected;
    }

    /// <summary>
    /// Garante que o WireGuard engine está disponível localmente.
    /// onProgress(0–100, mensagem)
    /// </summary>
    public Task<(bool ok, string detail)> EnsureEngineAsync(
        Action<int, string> onProgress,
        CancellationToken   ct = default)
        => _engine.EnsureWireGuardAsync(onProgress, ct);

    /// <summary>Conecta o túnel. onStatus = mensagem de progresso.</summary>
    public Task<(bool ok, string detail)> ConnectAsync(
        Action<string> onStatus,
        CancellationToken ct = default)
        => _engine.ConnectAsync(onStatus, ct);

    /// <summary>Desconecta o túnel.</summary>
    public Task<(bool ok, string detail)> DisconnectAsync()
        => _engine.DisconnectAsync();

    /// <summary>
    /// Remove serviços WireGuard presos que possam estar bloqueando a internet.
    /// Chamar ao iniciar o app.
    /// </summary>
    public Task CleanupStaleAsync() => _engine.CleanupStaleServiceAsync();
}
