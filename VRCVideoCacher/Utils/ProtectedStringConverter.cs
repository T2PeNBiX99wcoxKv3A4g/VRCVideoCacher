using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;
using Newtonsoft.Json;
using Serilog;

namespace VRCVideoCacher.Utils;

public class ProtectedStringConverter : JsonConverter<string>
{
    private static readonly ILogger Log = Program.Logger.ForContext<ProtectedStringConverter>();
    private const string Prefix = "enc:";

    // Dynamic machine & user derived key
    private static readonly Lazy<byte[]> EncryptionKey = new(GetDerivedKey);

    // Fallback key for backward compatibility with v1 static encryption
    private static readonly Lazy<byte[]> LegacyStaticKey = new(() =>
        SHA256.HashData(Encoding.UTF8.GetBytes("VRCVideoCacher.SecretKey.ProtectedConfigValue.v1")));

    private static byte[] GetDerivedKey()
    {
        try
        {
            var machineId = GetMachineId();
            var userName = Environment.UserName;
            var rawEntropy = $"VRCVideoCacher:{machineId}:{userName}:ProtectedSalt.v2";
            return SHA256.HashData(Encoding.UTF8.GetBytes(rawEntropy));
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to derive dynamic encryption key, falling back to static entropy");
            return LegacyStaticKey.Value;
        }
    }

    private static string GetMachineId()
    {
        if (OperatingSystem.IsWindows())
            try
            {
                var guid = Registry.GetValue(
                    @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Cryptography",
                    "MachineGuid",
                    null) as string;
                if (!string.IsNullOrWhiteSpace(guid))
                    return guid.Trim();
            }
            catch
            {
                // ignored
            }
        else if (OperatingSystem.IsLinux())
            try
            {
                if (File.Exists("/etc/machine-id"))
                {
                    var id = File.ReadAllText("/etc/machine-id").Trim();
                    if (!string.IsNullOrEmpty(id))
                        return id;
                }

                if (File.Exists("/var/lib/dbus/machine-id"))
                {
                    var id = File.ReadAllText("/var/lib/dbus/machine-id").Trim();
                    if (!string.IsNullOrEmpty(id))
                        return id;
                }
            }
            catch
            {
                // ignored
            }

        return Environment.MachineName;
    }

    public static string Encrypt(string plainText)
    {
        if (string.IsNullOrEmpty(plainText))
            return string.Empty;

        try
        {
            using var aes = Aes.Create();
            aes.Key = EncryptionKey.Value;
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

            var iv = new byte[16];
            Buffer.BlockCopy(combinedBytes, 0, iv, 0, 16);
            var cipherBytes = new byte[combinedBytes.Length - 16];
            Buffer.BlockCopy(combinedBytes, 16, cipherBytes, 0, cipherBytes.Length);

            // 1. Try decrypting with dynamic derived key
            try
            {
                return DecryptWithKey(cipherBytes, iv, EncryptionKey.Value);
            }
            catch (CryptographicException)
            {
                // 2. Fallback to legacy static key if encrypted under previous version
                try
                {
                    return DecryptWithKey(cipherBytes, iv, LegacyStaticKey.Value);
                }
                catch
                {
                    Log.Warning("Failed to decrypt protected string: invalid key or payload mismatch");
                    return string.Empty;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to decrypt protected string, falling back to raw value");
            return cipherText;
        }
    }

    private static string DecryptWithKey(byte[] cipherBytes, byte[] iv, byte[] key)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;

        using var decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
        var plainBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);
        return Encoding.UTF8.GetString(plainBytes);
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