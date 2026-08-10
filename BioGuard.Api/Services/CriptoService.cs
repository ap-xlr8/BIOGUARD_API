using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace BioGuard.Api.Services;

public class CriptoService
{
    private readonly byte[] _key;
    // AES-GCM: nonce 12 bytes, tag 16 bytes (128 bits)
    private const int NonceSizeBytes = 12;
    private const int TagSizeBytes = 16;
    // Prefijo que identifica datos cifrados con AES-GCM v2
    private const string V2Prefix = "v2:";

    public CriptoService(IConfiguration config, ILogger<CriptoService>? logger = null)
    {
        var configuredKey = config["Cripto:Key"];
        if (!string.IsNullOrEmpty(configuredKey))
        {
            _key = SHA256.HashData(Encoding.UTF8.GetBytes(configuredKey));
        }
        else
        {
            // Fallback: derivar de Jwt:Key con dominio separado
            var jwtKey = !string.IsNullOrWhiteSpace(config["Jwt:Key"])
                ? config["Jwt:Key"]
                : Environment.GetEnvironmentVariable("JWT_SECRET_KEY");
            if (string.IsNullOrWhiteSpace(jwtKey))
            {
                throw new InvalidOperationException(
                    "CriptoService: no hay clave de cifrado configurada. Define Cripto:Key, " +
                    "CRIPTO_KEY o JWT_SECRET_KEY antes de arrancar.");
            }
            logger?.LogWarning(
                "CriptoService: usando JWT_SECRET_KEY como fuente de la clave de cifrado. " +
                "Configura CRIPTO_KEY para aislar la clave de cifrado de la clave de firma JWT.");
            _key = SHA256.HashData(Encoding.UTF8.GetBytes("bioguard-cripto-v1:" + jwtKey));
        }
    }

    /// <summary>
    /// Cifra usando AES-256-GCM (Authenticated Encryption). Genera un nonce aleatorio
    /// por operación. El resultado tiene prefijo "v2:" para distinguir del formato CBC legacy.
    /// </summary>
    public virtual string Encrypt(string plainText)
    {
        if (string.IsNullOrEmpty(plainText)) return plainText;

        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var nonce = new byte[NonceSizeBytes];
        var tag = new byte[TagSizeBytes];
        var cipherBytes = new byte[plainBytes.Length];

        RandomNumberGenerator.Fill(nonce);

        using var aesGcm = new AesGcm(_key, TagSizeBytes);
        aesGcm.Encrypt(nonce, plainBytes, cipherBytes, tag);

        // Formato: nonce (12) | cipherText (n) | tag (16)
        var combined = new byte[NonceSizeBytes + cipherBytes.Length + TagSizeBytes];
        Buffer.BlockCopy(nonce, 0, combined, 0, NonceSizeBytes);
        Buffer.BlockCopy(cipherBytes, 0, combined, NonceSizeBytes, cipherBytes.Length);
        Buffer.BlockCopy(tag, 0, combined, NonceSizeBytes + cipherBytes.Length, TagSizeBytes);

        return V2Prefix + Convert.ToBase64String(combined);
    }

    /// <summary>
    /// Descifra texto cifrado. Soporta el formato AES-GCM v2 (prefix "v2:") y el
    /// formato AES-CBC legacy (sin prefix) para retrocompatibilidad con datos existentes.
    /// </summary>
    public virtual string Decrypt(string cipherText)
    {
        if (string.IsNullOrEmpty(cipherText)) return cipherText;

        return cipherText.StartsWith(V2Prefix, StringComparison.Ordinal)
            ? DecryptGcm(cipherText[V2Prefix.Length..])
            : DecryptCbcLegacy(cipherText);
    }

    // ── AES-GCM (nuevo) ──────────────────────────────────────────────
    private string DecryptGcm(string base64)
    {
        try
        {
            var combined = Convert.FromBase64String(base64);
            if (combined.Length <= NonceSizeBytes + TagSizeBytes)
                return base64; // corrupto

            var nonce = combined[..NonceSizeBytes];
            var cipherBytes = combined[NonceSizeBytes..(combined.Length - TagSizeBytes)];
            var tag = combined[(combined.Length - TagSizeBytes)..];
            var plainBytes = new byte[cipherBytes.Length];

            using var aesGcm = new AesGcm(_key, TagSizeBytes);
            aesGcm.Decrypt(nonce, cipherBytes, tag, plainBytes);
            return Encoding.UTF8.GetString(plainBytes);
        }
        catch
        {
            return base64;
        }
    }

    // ── AES-CBC legacy (solo lectura, retrocompatibilidad) ──────────
    private string DecryptCbcLegacy(string cipherText)
    {
        try
        {
            var fullCipher = Convert.FromBase64String(cipherText);
            using var aes = Aes.Create();
            aes.Key = _key;

            var iv = new byte[aes.BlockSize / 8];
            var cipher = new byte[fullCipher.Length - iv.Length];
            Buffer.BlockCopy(fullCipher, 0, iv, 0, iv.Length);
            Buffer.BlockCopy(fullCipher, iv.Length, cipher, 0, cipher.Length);

            using var decryptor = aes.CreateDecryptor(aes.Key, iv);
            var plainBytes = decryptor.TransformFinalBlock(cipher, 0, cipher.Length);
            return Encoding.UTF8.GetString(plainBytes);
        }
        catch
        {
            return cipherText;
        }
    }
}
