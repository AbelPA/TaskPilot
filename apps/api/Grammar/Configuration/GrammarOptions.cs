namespace Api.Grammar.Configuration;

public sealed class GrammarOptions
{
    public const string SectionName = "Grammar";

    public bool ApplyMigrationsOnStartup { get; set; } = true;
}
