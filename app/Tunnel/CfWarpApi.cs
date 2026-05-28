using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AYVUNoLag.Tunnel;

/// <summary>
/// Registra um par de chaves WireGuard na API pública do Cloudflare WARP
/// e retorna as credenciais necessárias para montar o config do WireGuard.
/// Não requer conta nem instalação do WARP.
/// </summary>
public static class CfWarpApi
{
    private const string BaseUrl     = "https://api.cloudflareclient.com/v0a2223/reg";
    private const string ClientVer   = "a-6.36-3672";
    private const string UserAgent   = "okhttp/3.12.1";

    private static readonly HttpClient _http = new();

    // ── Resultado da resgistração ─────────────────────────────────────────────
    public sealed class Credentials
    {
        public required string PrivateKey    { get; init; }
        public required string PublicKey     { get; init; }
        public required string ServerPubKey  { get; init; }
        public required string Endpoint      { get; init; }   // "IP:port"
        public required string ClientIpV4    { get; init; }   // "172.x.x.x"
        public required string ClientIpV6    { get; init; }
    }

    // ── Registro ──────────────────────────────────────────────────────────────
    public static async Task<Credentials> RegisterAsync(CancellationToken ct = default)
    {
        var keyPair  = WgKeyPair.Generate();
        var installId = Guid.NewGuid().ToString();
        var tos       = DateTime.UtcNow.ToString("o");

        var payload = new
        {
            install_id = installId,
            tos        = tos,
            key        = keyPair.PublicKeyBase64,
            fcm_token  = "",
            type       = "Android",
            locale     = "en_US",
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, BaseUrl);
        req.Headers.Add("CF-Client-Version", ClientVer);
        req.Headers.Add("User-Agent", UserAgent);
        req.Content = JsonContent.Create(payload);

        using var resp = await _http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();

        var body = await resp.Content.ReadAsStringAsync(ct);
        using var doc  = JsonDocument.Parse(body);
        var root        = doc.RootElement;

        // ── Extrair campos da resposta ────────────────────────────────────────
        var serverPub = root
            .GetProperty("config")
            .GetProperty("peers")[0]
            .GetProperty("public_key")
            .GetString() ?? throw new InvalidOperationException("server public_key ausente");

        var endpointV4 = root
            .GetProperty("config")
            .GetProperty("peers")[0]
            .GetProperty("endpoint")
            .GetProperty("v4")
            .GetString() ?? "162.159.193.1:2408";

        var ipv4 = root
            .GetProperty("config")
            .GetProperty("interface")
            .GetProperty("addresses")
            .GetProperty("v4")
            .GetString() ?? "172.16.0.2";

        var ipv6 = root
            .TryGetProperty("config", out var cfg2) &&
              cfg2.TryGetProperty("interface", out var iface) &&
              iface.TryGetProperty("addresses", out var addrs) &&
              addrs.TryGetProperty("v6", out var v6el)
            ? v6el.GetString() ?? ""
            : "";

        return new Credentials
        {
            PrivateKey   = keyPair.PrivateKeyBase64,
            PublicKey    = keyPair.PublicKeyBase64,
            ServerPubKey = serverPub,
            Endpoint     = endpointV4,
            ClientIpV4   = ipv4,
            ClientIpV6   = ipv6,
        };
    }
}
