using System.Security.Cryptography;
using System.Text;
using Api.Settings.Configuration;
using Microsoft.Extensions.Options;

namespace Api.Settings.Security;

public sealed class AesGcmSecretProtector(IOptions<SettingsOptions> options) : ISecretProtector
{
    private const byte FormatVersion = 1;
    private const int KeySize = 32;
    private const int NonceSize = 12;
    private const int TagSize = 16;

    public string Protect(string purpose, string plaintext)
    {
        var key = ReadKey();
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var payload = new byte[1 + NonceSize + TagSize + plaintextBytes.Length];
        var nonce = payload.AsSpan(1, NonceSize);
        var tag = payload.AsSpan(1 + NonceSize, TagSize);
        var ciphertext = payload.AsSpan(1 + NonceSize + TagSize);

        payload[0] = FormatVersion;
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plaintextBytes, ciphertext, tag, Encoding.UTF8.GetBytes(purpose));
        CryptographicOperations.ZeroMemory(plaintextBytes);

        return Convert.ToBase64String(payload);
    }

    public string Unprotect(string purpose, string protectedValue)
    {
        var key = ReadKey();
        byte[] payload;
        try
        {
            payload = Convert.FromBase64String(protectedValue);
        }
        catch (FormatException exception)
        {
            throw new SecretProtectionException("The stored secret is not valid.", exception);
        }

        if (payload.Length < 1 + NonceSize + TagSize || payload[0] != FormatVersion)
        {
            throw new SecretProtectionException("The stored secret is not valid.");
        }

        var nonce = payload.AsSpan(1, NonceSize);
        var tag = payload.AsSpan(1 + NonceSize, TagSize);
        var ciphertext = payload.AsSpan(1 + NonceSize + TagSize);
        var plaintext = new byte[ciphertext.Length];
        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(nonce, ciphertext, tag, plaintext, Encoding.UTF8.GetBytes(purpose));
            return Encoding.UTF8.GetString(plaintext);
        }
        catch (CryptographicException exception)
        {
            throw new SecretProtectionException(
                "The stored secret could not be decrypted. Check Settings:EncryptionKey.",
                exception);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private byte[] ReadKey()
    {
        var configured = options.Value.EncryptionKey;
        if (string.IsNullOrWhiteSpace(configured))
        {
            throw new SecretProtectionException("Settings:EncryptionKey is not configured.");
        }

        try
        {
            var key = Convert.FromBase64String(configured.Trim());
            if (key.Length == KeySize)
            {
                return key;
            }
        }
        catch (FormatException)
        {
        }

        throw new SecretProtectionException("Settings:EncryptionKey must be a base64-encoded 256-bit key.");
    }
}
