namespace Api.Settings.Services;

public static class SecretMasking
{
    public const string MaskedPassword = "*********************";

    // Keeps a short prefix and suffix; the mask length is fixed so it does not reveal the real length.
    public static string MaskApiKey(string value)
    {
        var edge = Math.Min(5, value.Length / 4);
        var suffix = Math.Min(3, value.Length / 4);
        return value[..edge] + "*********" + value[^suffix..];
    }

    // Masks the middle of the local part and keeps the domain: "tp***pa@gmail.com".
    public static string MaskUsername(string value)
    {
        var at = value.LastIndexOf('@');
        var local = at > 0 ? value[..at] : value;
        var domain = at > 0 ? value[at..] : string.Empty;
        var masked = local.Length <= 4
            ? local[..Math.Min(1, local.Length)] + "***"
            : local[..2] + "***" + local[^2..];
        return masked + domain;
    }
}
