using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using JetBrains.Annotations;
using Microsoft.Win32;
using Newtonsoft.Json;
using Serilog;
using VRCVideoCacher.Extensions;

namespace VRCVideoCacher.Utils;

public class ProtectedStringConverter : JsonConverter<string>
{
    private static readonly ILogger Log = Program.Logger.ForContext<ProtectedStringConverter>();

    private const int KeySize = 32; // AES-256
    private const int NonceSize = 12; // Recommended GCM nonce size
    private const int TagSize = 16; // 128-bit authentication tag
    private const int Version = 3;

    private static readonly string Prefix = $"enc:v{Version}:";
    private static readonly string KeyInfo = $"VRCVideoCacher.ProtectedString.v{Version}";

    private static readonly Lazy<byte[]> EncryptionKey = new(GetDerivedKey);

    // Fallback key for backward compatibility with v1 static encryption
    private static readonly Lazy<byte[]> LegacyStaticKey =
        new(() => SHA256.HashData("VRCVideoCacher.SecretKey.ProtectedConfigValue.v1"u8.ToArray()));

    private static byte[] GetDerivedKey()
    {
        return Try.Run(() =>
        {
            var machineId = GetMachineId();
            var userName = Environment.UserName;

            // Secret generated once per local installation/user profile.
            var installationSecret = GetOrCreateInstallationSecret();

            // Public context that binds the key to this machine and user.
            var context = Encoding.UTF8.GetBytes($"VRCVideoCacher:{machineId}:{userName}:v{Version}");

            // HKDF-Extract
            var prk = HKDF.Extract(HashAlgorithmName.SHA256, installationSecret, context);

            // HKDF-Expand -> 32-byte AES-256 key
            return HKDF.Expand(HashAlgorithmName.SHA256, prk, KeySize, Encoding.UTF8.GetBytes(KeyInfo));
        }).GetOrElse(ex =>
        {
            Log.Warning(ex, "Failed to derive dynamic encryption key, falling back to static entropy");
            return LegacyStaticKey.Value;
        });
    }

    private static byte[] GetOrCreateInstallationSecret()
    {
        var path = Path.Join(Program.DataPath, "secret.key");

        if (File.Exists(path))
        {
            var existing = File.ReadAllBytes(path);
            return existing.Length != KeySize
                ? throw new CryptographicException("Invalid installation secret")
                : existing;
        }

        // Generate a cryptographically secure random 256-bit secret.
        var secret = RandomNumberGenerator.GetBytes(KeySize);

        File.WriteAllBytes(path, secret);

        // Restrict Unix permissions to owner read/write.
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);

        return secret;
    }

    [SuppressMessage("Interoperability", "CA1416")]
    private static string GetMachineId()
    {
        if (OperatingSystem.IsWindows())
        {
            var result = Try.Run(() =>
            {
                var guid =
                    Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Cryptography", "MachineGuid",
                        null) as string;
                return !string.IsNullOrWhiteSpace(guid) ? guid.Trim() : null;
            }).GetOrNull();
            if (result != null) return result;
        }
        else if (OperatingSystem.IsLinux())
        {
            var result = Try.Run(() =>
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

                return null;
            }).GetOrNull();
            if (result != null) return result;
        }

        return Environment.MachineName;
    }

    [PublicAPI]
    public static string Encrypt(string plainText)
    {
        if (string.IsNullOrEmpty(plainText))
            return string.Empty;

        return Try.Run(() =>
        {
            var plaintextBytes = Encoding.UTF8.GetBytes(plainText);
            var nonce = RandomNumberGenerator.GetBytes(NonceSize);
            var ciphertext = new byte[plaintextBytes.Length];
            var tag = new byte[TagSize];
            var associatedData = Encoding.UTF8.GetBytes(Prefix);

            using var aes = new AesGcm(EncryptionKey.Value, TagSize);
            aes.Encrypt(nonce, plaintextBytes, ciphertext, tag, associatedData);

            // nonce + ciphertext + tag
            var result = new byte[nonce.Length + ciphertext.Length + tag.Length];

            Buffer.BlockCopy(nonce, 0, result, 0, nonce.Length);
            Buffer.BlockCopy(ciphertext, 0, result, nonce.Length, ciphertext.Length);
            Buffer.BlockCopy(tag, 0, result, nonce.Length + ciphertext.Length, tag.Length);
            return Prefix + Convert.ToBase64String(result);
        }).GetOrElse(ex =>
        {
            Log.Warning(ex, "Failed to encrypt protected string");
            return plainText;
        });
    }

    [PublicAPI]
    public static string Decrypt(string cipherText)
    {
        if (string.IsNullOrEmpty(cipherText))
            return string.Empty;

        // Plain-text compatibility.
        if (!cipherText.StartsWith(Prefix, StringComparison.Ordinal))
            return cipherText;

        return Try.Run(() =>
        {
            var rawBase64 = cipherText[Prefix.Length..];

            var combinedBytes = Convert.FromBase64String(rawBase64);

            if (combinedBytes.Length < NonceSize + TagSize)
                throw new CryptographicException("Invalid encrypted payload");

            var nonce = combinedBytes.AsSpan(0, NonceSize);
            var ciphertextLength = combinedBytes.Length - NonceSize - TagSize;
            if (ciphertextLength < 0) throw new CryptographicException("Invalid encrypted payload");
            var ciphertext = combinedBytes.AsSpan(NonceSize, ciphertextLength);
            var tag = combinedBytes.AsSpan(NonceSize + ciphertextLength, TagSize);
            var plaintext = new byte[ciphertextLength];
            var associatedData = Encoding.UTF8.GetBytes(Prefix);

            using var aes = new AesGcm(EncryptionKey.Value, TagSize);
            aes.Decrypt(nonce, ciphertext, tag, plaintext, associatedData);
            return Encoding.UTF8.GetString(plaintext);
        }).GetOrElse(ex =>
        {
            Log.Warning(ex, "Failed to decrypt protected string");
            return string.Empty;
        });
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
        return string.IsNullOrEmpty(str) ? string.Empty : Decrypt(str);
    }
}