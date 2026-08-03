using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace BioGuard.Api.Services;

public class CriptoService
{
    private readonly byte[] _key;

    public CriptoService(IConfiguration config)
    {
        var configuredKey = config["Cripto:Key"]
            ?? Environment.GetEnvironmentVariable("CRIPTO_KEY");
        if (!string.IsNullOrEmpty(configuredKey))
        {
            _key = SHA256.HashData(Encoding.UTF8.GetBytes(configuredKey));
        }
        else
        {
            // Fallback: derivar de Jwt:Key (o su variable de entorno JWT_SECRET_KEY, igual que
            // Program.cs) con dominio separado para no reusar la clave de firma JWT
            // directamente como clave de cifrado.
            var jwtKey = !string.IsNullOrWhiteSpace(config["Jwt:Key"])
                ? config["Jwt:Key"]
                : Environment.GetEnvironmentVariable("JWT_SECRET_KEY");
            if (string.IsNullOrWhiteSpace(jwtKey))
            {
                throw new InvalidOperationException(
                    "CriptoService: no hay clave de cifrado configurada. Define Cripto:Key, " +
                    "CRIPTO_KEY o JWT_SECRET_KEY antes de arrancar.");
            }
            _key = SHA256.HashData(Encoding.UTF8.GetBytes("bioguard-cripto-v1:" + jwtKey));
        }
    }

    public virtual string Encrypt(string plainText)
    {
        if (string.IsNullOrEmpty(plainText)) return plainText;

        using var aes = Aes.Create();
        aes.Key = _key;
        aes.GenerateIV();
        var iv = aes.IV;

        using var encryptor = aes.CreateEncryptor(aes.Key, iv);
        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

        var result = new byte[iv.Length + cipherBytes.Length];
        Buffer.BlockCopy(iv, 0, result, 0, iv.Length);
        Buffer.BlockCopy(cipherBytes, 0, result, iv.Length, cipherBytes.Length);

        return Convert.ToBase64String(result);
    }

    public virtual string Decrypt(string cipherText)
    {
        if (string.IsNullOrEmpty(cipherText)) return cipherText;

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
