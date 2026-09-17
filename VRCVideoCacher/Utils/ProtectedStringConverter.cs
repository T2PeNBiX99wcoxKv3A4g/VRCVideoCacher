using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Serilog;

namespace VRCVideoCacher.Utils;

public class ProtectedStringConverter : JsonConverter<string>
{
    private static readonly ILogger Log = Program.Logger.ForContext<ProtectedStringConverter>();
    private const string Prefix = "enc:";

    // Fixed application entropy key derived from SHA256 to ensure cross-platform reproducibility
    private static readonly byte[] EncryptionKey =
        SHA256.HashData(Encoding.UTF8.GetBytes("VRCVideoCacher.SecretKey.ProtectedConfigValue.v1"));

    public static string Encrypt(string plainText)
    {
        if (string.IsNullOrEmpty(plainText))
            return string.Empty;

        try
        {
            using var aes = Aes.Create();
            aes.Key = EncryptionKey;
            aes.GenerateIV();

            using var encryptor = aes.CreateEncryptor(aes.Key, aes.IV);
            var plainBytes = Encoding.UTF8.GetBytes(plainText);
            var cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

            // Combine IV + ciphertext
            var result = new byte[aes.IV.Length + cipherBytes.Length];
            Buffer.BlockCopy(aes.IV, 0, result, 0, aes.IV.Length);
            Buffer.BlockCopy(cipherBytes, 0, result, aes.IV.Length, cipherBytes.Length);

            return Prefix + Convert.ToBase64String(result);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to encrypt protected string");
            return plainText;
        }
    }

    public static string Decrypt(string cipherText)
    {
        if (string.IsNullOrEmpty(cipherText))
            return string.Empty;

        if (!cipherText.StartsWith(Prefix, StringComparison.Ordinal))
            return cipherText; // Plain text backward compatibility

        try
        {
            var rawBase64 = cipherText[Prefix.Length..];
            var combinedBytes = Convert.FromBase64String(rawBase64);

            if (combinedBytes.Length < 16)
                return cipherText;

            using var aes = Aes.Create();
            aes.Key = EncryptionKey;

            var iv = new byte[16];
            Buffer.BlockCopy(combinedBytes, 0, iv, 0, 16);
            aes.IV = iv;

            using var decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
            var cipherBytes = new byte[combinedBytes.Length - 16];
            Buffer.BlockCopy(combinedBytes, 16, cipherBytes, 0, cipherBytes.Length);

            var plainBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);
            return Encoding.UTF8.GetString(plainBytes);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to decrypt protected string, falling back to raw value");
            return cipherText;
        }
    }

    public override void WriteJson(JsonWriter writer, string? value, JsonSerializer serializer)
    {
        if (string.IsNullOrEmpty(value))
        {
            writer.WriteValue(string.Empty);
            return;
        }

        writer.WriteValue(Encrypt(value));
    }

    public override string? ReadJson(JsonReader reader, Type objectType, string? existingValue, bool hasExistingValue,
        JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.Null)
            return string.Empty;

        var str = reader.Value?.ToString();
        if (string.IsNullOrEmpty(str))
            return string.Empty;

        return Decrypt(str);
    }
}
