using AYVUNoLag.Config;
using Microsoft.Win32;
using System.Net.Http;
using System.Text.Json;

namespace AYVUNoLag.Services;

public enum LicenseStatus
{
    Valid,
    Expired,
    Paused,
    NotFound,
    NoInternet,
    ConfigError,
}

public sealed record LicenseResult(
    LicenseStatus Status,
    DateTime?     ExpiresAt   = null,
    string?       ClientName  = null,
    bool          IsPermanent = false);

/// <summary>
/// Valida um ID de licença contra o Firebase Realtime Database.
/// Salva/carrega o último ID utilizado no registry (HKCU).
/// </summary>
internal sealed class LicenseService
{
    // ── Registry ────────────────────────────────────────────────────────────────
    private const string RegPath  = @"Software\AYVU\NL\License\";
    private const string RegValue = "lastId";

    // ── HTTP ────────────────────────────────────────────────────────────────────
    private static readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromSeconds(8),
    };

    // ── Validação principal ──────────────────────────────────────────────────────

    public async Task<LicenseResult> ValidateAsync(string id)
    {
        if (string.IsNullOrWhiteSpace(FirebaseConfig.DatabaseUrl) ||
            FirebaseConfig.DatabaseUrl.Contains("SEU-PROJETO") ||
            string.IsNullOrWhiteSpace(FirebaseConfig.Secret) ||
            FirebaseConfig.Secret.Contains("SEU_DATABASE"))
        {
            return new LicenseResult(LicenseStatus.ConfigError);
        }

        var normalizedId = id.Trim().ToUpperInvariant();

        try
        {
            var response = await _http.GetStringAsync(FirebaseConfig.LicenseUrl(normalizedId));

            if (string.IsNullOrWhiteSpace(response) || response.Trim() == "null")
                return new LicenseResult(LicenseStatus.NotFound);

            using var doc = JsonDocument.Parse(response);
            var root = doc.RootElement;

            var clientName   = root.TryGetProperty("clientName", out var cn)  ? cn.GetString()  : null;
            var typeStr      = root.TryGetProperty("type",       out var tp)  ? tp.GetString()  : "trial";
            var isPaused     = root.TryGetProperty("paused",     out var pau) && pau.ValueKind == JsonValueKind.True;
            var expiresAtStr = root.TryGetProperty("expiresAt",  out var ea)  ? ea.GetString()  : null;

            var isPermanent = typeStr == "permanent";

            if (isPaused)
                return new LicenseResult(LicenseStatus.Paused, ClientName: clientName, IsPermanent: isPermanent);

            if (isPermanent)
                return new LicenseResult(LicenseStatus.Valid, ExpiresAt: null, ClientName: clientName, IsPermanent: true);

            if (string.IsNullOrWhiteSpace(expiresAtStr))
                return new LicenseResult(LicenseStatus.NotFound);

            if (!DateTime.TryParse(expiresAtStr, out var expiresAt))
                return new LicenseResult(LicenseStatus.NotFound);

            var expiryMoment = DateTime.SpecifyKind(expiresAt.Date.AddDays(1), DateTimeKind.Local);

            if (expiryMoment <= DateTime.Now)
                return new LicenseResult(LicenseStatus.Expired, expiryMoment, clientName);

            return new LicenseResult(LicenseStatus.Valid, expiryMoment, clientName, IsPermanent: false);
        }
        catch (HttpRequestException)  { return new LicenseResult(LicenseStatus.NoInternet); }
        catch (TaskCanceledException) { return new LicenseResult(LicenseStatus.NoInternet); }
        catch                         { return new LicenseResult(LicenseStatus.NoInternet); }
    }

    // ── Registry helpers ─────────────────────────────────────────────────────────

    public string LoadLastId()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegPath);
            return key?.GetValue(RegValue) as string ?? string.Empty;
        }
        catch { return string.Empty; }
    }

    public void SaveLastId(string id)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RegPath);
            key.SetValue(RegValue, id.Trim().ToUpperInvariant(), RegistryValueKind.String);
        }
        catch { /* best-effort */ }
    }
}
