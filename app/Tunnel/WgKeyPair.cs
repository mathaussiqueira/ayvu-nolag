using NSec.Cryptography;

namespace AYVUNoLag.Tunnel;

/// <summary>
/// Gera e armazena um par de chaves Curve25519 (X25519) no formato raw base64
/// que o WireGuard espera.
/// </summary>
public sealed class WgKeyPair
{
    public string PrivateKeyBase64 { get; }
    public string PublicKeyBase64  { get; }

    private WgKeyPair(string priv, string pub)
    {
        PrivateKeyBase64 = priv;
        PublicKeyBase64  = pub;
    }

    /// <summary>Gera um novo par de chaves aleatório.</summary>
    public static WgKeyPair Generate()
    {
        var algo = KeyAgreementAlgorithm.X25519;
        using var key = Key.Create(algo, new KeyCreationParameters
        {
            ExportPolicy = KeyExportPolicies.AllowPlaintextExport,
        });

        var priv = key.Export(KeyBlobFormat.RawPrivateKey);
        var pub  = key.PublicKey.Export(KeyBlobFormat.RawPublicKey);

        return new WgKeyPair(
            Convert.ToBase64String(priv),
            Convert.ToBase64String(pub));
    }
}
