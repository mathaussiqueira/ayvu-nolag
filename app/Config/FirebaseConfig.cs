namespace AYVUNoLag.Config;

/// <summary>
/// Configuração de conexão com o Firebase Realtime Database.
/// Compartilha o mesmo banco do AYVU Windows Boost.
/// </summary>
internal static class FirebaseConfig
{
    // ─── CREDENCIAIS FIREBASE ───────────────────────────────────────────────────
    public const string DatabaseUrl = "https://ayvu-windows-boost-default-rtdb.firebaseio.com";
    public const string Secret      = "ZBseegh31UrI4hDUwkk1PzDT5rafOUo6DxnZiTyA";
    // ────────────────────────────────────────────────────────────────────────────

    /// <summary>URL base para acesso a uma licença individual (nó NoLag).</summary>
    public static string LicenseUrl(string id) =>
        $"{DatabaseUrl.TrimEnd('/')}/licenses/{id.ToUpperInvariant()}.json?auth={Secret}";
}
