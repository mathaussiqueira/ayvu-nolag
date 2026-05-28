using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace AYVUNoLag.Tunnel;

/// <summary>
/// Gerencia o WireGuard engine localmente — sem instalar WARP.
///
/// Fluxo:
///   1. EnsureWireGuardAsync  — baixa wireguard.exe do site oficial (uma vez)
///   2. ConnectAsync          — registra chaves na API CF, grava .conf, instala serviço
///   3. DisconnectAsync       — desinstala o serviço de túnel
///   4. GetStatusAsync        — verifica se o serviço está ativo
/// </summary>
public sealed class WireGuardEngine
{
    // ── Caminhos ──────────────────────────────────────────────────────────────
    private static readonly string AppDataDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                     "AYVU NoLag", "wireguard");

    private static string WgExe  => Path.Combine(AppDataDir, "wireguard.exe");
    private static string ConfDir => Path.Combine(AppDataDir, "configs");

    private const string TunnelName   = "AYVUNoLag";
    private const string ConfFileName = "AYVUNoLag.conf";

    // WireGuard Windows MSI — fonte oficial (MIT license, redistribuível)
    private const string WgMsiUrl =
        "https://download.wireguard.com/windows-client/wireguard-amd64-0.5.3.msi";

    // Credenciais salvas para reconectar sem re-registrar
    private static readonly string CredsPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                     "AYVU NoLag", "warp-creds.json");

    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(10) };

    // ── Status ────────────────────────────────────────────────────────────────
    public bool WireGuardReady => File.Exists(WgExe);

    public bool IsConnected
    {
        get
        {
            try
            {
                using var p = Process.Start(new ProcessStartInfo
                {
                    FileName               = "sc.exe",
                    Arguments              = $"query \"WireGuardTunnel${TunnelName}\"",
                    UseShellExecute        = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow         = true,
                })!;
                var output = p.StandardOutput.ReadToEnd();
                p.WaitForExit();
                return output.Contains("RUNNING", StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }
    }

    // ── 1. Garantir wireguard.exe ─────────────────────────────────────────────
    /// <summary>
    /// Baixa o MSI oficial do WireGuard e extrai wireguard.exe para AppData.
    /// Reporta progresso via onProgress (0–100). Idempotente.
    /// </summary>
    public async Task<(bool ok, string detail)> EnsureWireGuardAsync(
        Action<int, string> onProgress,
        CancellationToken   ct = default)
    {
        if (WireGuardReady) return (true, "WireGuard já disponível.");

        Directory.CreateDirectory(AppDataDir);
        Directory.CreateDirectory(ConfDir);

        var msiPath = Path.Combine(Path.GetTempPath(), "wireguard-setup.msi");

        try
        {
            // ── Download MSI ──────────────────────────────────────────────────
            onProgress(0, "Baixando WireGuard Engine...");

            using var resp = await _http.GetAsync(
                WgMsiUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();

            var total     = resp.Content.Headers.ContentLength ?? -1L;
            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            await using var file   = File.Create(msiPath);

            var buffer    = new byte[81920];
            long received = 0;
            int  read;

            while ((read = await stream.ReadAsync(buffer, ct)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, read), ct);
                received += read;
                if (total > 0) onProgress((int)(received * 70 / total), "Baixando WireGuard Engine...");
            }
            file.Close();

            // ── Extrair via msiexec /a (sem instalar no sistema) ──────────────
            onProgress(72, "Extraindo arquivos...");
            var extractDir = Path.Combine(Path.GetTempPath(), "wg-extract");
            if (Directory.Exists(extractDir)) Directory.Delete(extractDir, true);
            Directory.CreateDirectory(extractDir);

            using var msi = Process.Start(new ProcessStartInfo
            {
                FileName        = "msiexec.exe",
                Arguments       = $"/a \"{msiPath}\" /qn TARGETDIR=\"{extractDir}\"",
                UseShellExecute = true,
                Verb            = "runas",
                CreateNoWindow  = true,
            })!;
            await msi.WaitForExitAsync(ct);

            onProgress(88, "Configurando...");

            // ── Localizar wireguard.exe na extração ───────────────────────────
            var wgFound = Directory.GetFiles(extractDir, "wireguard.exe", SearchOption.AllDirectories)
                                   .FirstOrDefault();
            if (wgFound is null)
                return (false, "wireguard.exe não encontrado após extração do MSI.");

            File.Copy(wgFound, WgExe, overwrite: true);

            // wintun.dll — junto com wireguard.exe
            var wintunFound = Directory.GetFiles(extractDir, "wintun.dll", SearchOption.AllDirectories)
                                       .FirstOrDefault();
            if (wintunFound is not null)
                File.Copy(wintunFound, Path.Combine(AppDataDir, "wintun.dll"), overwrite: true);

            onProgress(100, "WireGuard pronto.");
            return (true, "WireGuard Engine instalado.");
        }
        catch (OperationCanceledException) { return (false, "Cancelado."); }
        catch (Exception ex)               { return (false, ex.Message);    }
        finally
        {
            try { File.Delete(msiPath); } catch { }
        }
    }

    // ── 2. Conectar ───────────────────────────────────────────────────────────
    public async Task<(bool ok, string detail)> ConnectAsync(
        Action<string> onStatus,
        CancellationToken ct = default)
    {
        if (!WireGuardReady) return (false, "WireGuard Engine não disponível.");

        // Limpa túnel preso antes de tentar conectar
        if (IsConnected) await DisconnectAsync();

        try
        {
            // Carrega ou registra credenciais
            onStatus("Obtendo credenciais Cloudflare...");
            var creds = await LoadOrRegisterAsync(ct);

            // Grava config
            onStatus("Configurando túnel...");
            Directory.CreateDirectory(ConfDir);
            var confPath = Path.Combine(ConfDir, ConfFileName);
            WriteConfig(confPath, creds);

            // Instala serviço
            onStatus("Ativando túnel WireGuard...");
            using var p = Process.Start(new ProcessStartInfo
            {
                FileName        = WgExe,
                Arguments       = $"/installtunnelservice \"{confPath}\"",
                UseShellExecute = true,
                Verb            = "runas",
                CreateNoWindow  = true,
            })!;
            await p.WaitForExitAsync(ct);

            if (p.ExitCode != 0)
                return (false, $"wireguard.exe retornou código {p.ExitCode}.");

            // Aguarda serviço subir
            for (var i = 0; i < 10; i++)
            {
                await Task.Delay(800, ct);
                if (IsConnected) break;
            }

            if (!IsConnected)
                return (false, "Serviço WireGuard não subiu.");

            // ── Verificação de conectividade (5 tentativas, 1 s cada) ─────────
            onStatus("Verificando conectividade...");
            var reachable = await TestConnectivityAsync(ct);

            if (!reachable)
            {
                // Túnel ativo mas sem tráfego — desconecta para não bloquear internet
                await DisconnectAsync();
                // Limpa credenciais para forçar novo registro na próxima tentativa
                try { File.Delete(CredsPath); } catch { }
                return (false,
                    "Túnel iniciado mas sem resposta do servidor Cloudflare. " +
                    "Credenciais renovadas — tente conectar novamente.");
            }

            return (true, "Túnel ativo. Tráfego passando pela rede Cloudflare.");
        }
        catch (OperationCanceledException) { return (false, "Cancelado."); }
        catch (Exception ex)               { return (false, ex.Message);    }
    }

    /// <summary>
    /// Tenta atingir 1.1.1.1 via HTTP para confirmar que o túnel está passando tráfego.
    /// </summary>
    private static async Task<bool> TestConnectivityAsync(CancellationToken ct)
    {
        using var testHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
        for (var i = 0; i < 3; i++)
        {
            try
            {
                using var r = await testHttp.GetAsync("https://1.1.1.1/cdn-cgi/trace", ct);
                if (r.IsSuccessStatusCode) return true;
            }
            catch { /* tenta de novo */ }
            await Task.Delay(1000, ct);
        }
        return false;
    }

    /// <summary>
    /// Deve ser chamado ao iniciar o app — remove serviços WireGuard presos
    /// que possam estar bloqueando o tráfego.
    /// </summary>
    public async Task CleanupStaleServiceAsync()
    {
        if (!WireGuardReady) return;
        if (!IsConnected)    return;

        // Serviço está rodando — testa se está passando tráfego
        var ok = await TestConnectivityAsync(CancellationToken.None);
        if (!ok)
        {
            await DisconnectAsync();
        }
    }

    // ── 3. Desconectar ────────────────────────────────────────────────────────
    public async Task<(bool ok, string detail)> DisconnectAsync()
    {
        if (!WireGuardReady) return (false, "WireGuard Engine não disponível.");

        try
        {
            using var p = Process.Start(new ProcessStartInfo
            {
                FileName        = WgExe,
                Arguments       = $"/uninstalltunnelservice {TunnelName}",
                UseShellExecute = true,
                Verb            = "runas",
                CreateNoWindow  = true,
            })!;
            await p.WaitForExitAsync();
            return (true, "Túnel WireGuard desativado.");
        }
        catch (Exception ex) { return (false, ex.Message); }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    private static async Task<CfWarpApi.Credentials> LoadOrRegisterAsync(CancellationToken ct)
    {
        // Se já registrou antes, reutiliza
        if (File.Exists(CredsPath))
        {
            var json = await File.ReadAllTextAsync(CredsPath, ct);
            var saved = JsonSerializer.Deserialize<CfWarpApi.Credentials>(json);
            if (saved is not null) return saved;
        }

        // Registra novo par de chaves
        var creds = await CfWarpApi.RegisterAsync(ct);

        // Persiste
        Directory.CreateDirectory(Path.GetDirectoryName(CredsPath)!);
        await File.WriteAllTextAsync(CredsPath,
            JsonSerializer.Serialize(creds, new JsonSerializerOptions { WriteIndented = true }), ct);

        return creds;
    }

    private static void WriteConfig(string path, CfWarpApi.Credentials c)
    {
        var ipLine = string.IsNullOrWhiteSpace(c.ClientIpV6)
            ? $"Address = {c.ClientIpV4}/32"
            : $"Address = {c.ClientIpV4}/32, {c.ClientIpV6}/128";

        File.WriteAllText(path,
            $"[Interface]\n" +
            $"PrivateKey = {c.PrivateKey}\n" +
            $"{ipLine}\n" +
            $"DNS = 1.1.1.1, 1.0.0.1\n" +
            $"\n" +
            $"[Peer]\n" +
            $"PublicKey = {c.ServerPubKey}\n" +
            $"AllowedIPs = 0.0.0.0/0, ::/0\n" +
            $"Endpoint = {c.Endpoint}\n" +
            $"PersistentKeepalive = 25\n");
    }
}
