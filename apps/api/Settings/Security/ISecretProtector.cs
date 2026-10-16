namespace Api.Settings.Security;

public interface ISecretProtector
{
    // The purpose binds a ciphertext to the setting it was created for.
    string Protect(string purpose, string plaintext);

    string Unprotect(string purpose, string protectedValue);
}

public sealed class SecretProtectionException(string message, Exception? innerException = null)
    : Exception(message, innerException);
