namespace RustyGoldbox.Core.Modules;

public enum ModuleKind
{
    Ruleset,
    Extension,
    Assets,
    Campaign,
}

public static class ModuleKinds
{
    public static IReadOnlyList<string> Names { get; } = ["ruleset", "extension", "assets", "campaign"];

    public static bool TryParse(string text, out ModuleKind kind)
    {
        switch (text)
        {
            case "ruleset":
                kind = ModuleKind.Ruleset;
                return true;
            case "extension":
                kind = ModuleKind.Extension;
                return true;
            case "assets":
                kind = ModuleKind.Assets;
                return true;
            case "campaign":
                kind = ModuleKind.Campaign;
                return true;
            default:
                kind = default;
                return false;
        }
    }

    public static string Name(ModuleKind kind)
    {
        return kind switch
        {
            ModuleKind.Ruleset => "ruleset",
            ModuleKind.Extension => "extension",
            ModuleKind.Assets => "assets",
            ModuleKind.Campaign => "campaign",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
    }
}
