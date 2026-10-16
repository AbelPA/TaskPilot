namespace Api.Settings.Persistence;

public sealed class AppSettingEntity
{
    public required string Key { get; set; }
    public required string ProtectedValue { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
