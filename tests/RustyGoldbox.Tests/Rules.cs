using RustyGoldbox.Core.Modules;
using RustyGoldbox.Core.Rules;

namespace RustyGoldbox.Tests;

/// <summary>Loads rule sets for tests: the first-party classic ruleset, or a small one written for a test.</summary>
internal static class Rules
{
    public static string RepositoryRoot { get; } = FindRoot();

    public static string ClassicPath => Path.Combine(RepositoryRoot, "modules", "classic");

    public static RuleSet Classic()
    {
        return LoadValid(ClassicPath);
    }

    /// <summary>A ruleset module "rules" with str (default 10), a strength table and a few derived values.</summary>
    public static string WriteSmallRuleset(TempModules modules)
    {
        string root = modules.Module("rules", "ruleset");
        modules.Write("rules/str.json", """{ "type": "attribute", "id": "str", "name": "Strength", "min": 3, "max": 18, "default": 10 }""");
        modules.Write("rules/bonus.json", """
            { "type": "table", "id": "bonus", "keys": [ { "name": "score", "type": "number" } ], "value": "number",
              "rows": [ [3, -3], ["4-8", -1], ["9-12", 0], ["13+", 2] ] }
            """);
        modules.Write("rules/class_bonus.json", """
            { "type": "table", "id": "class_bonus", "keys": [ { "name": "class", "type": "text" }, { "name": "level", "type": "number" } ], "value": "number",
              "rows": [ ["warrior", "1-3", 1], ["warrior", "4+", 2] ] }
            """);
        modules.Write("rules/hit.json", """{ "type": "derived", "id": "hit", "name": "Hit bonus", "value": "table(bonus, self.str)" }""");
        modules.Write("rules/strong.json", """{ "type": "derived", "id": "strong", "name": "Strong", "value": "self.str >= 13" }""");
        modules.Write("rules/warrior.json", """{ "type": "class", "id": "warrior", "name": "Warrior", "levels": [ { "xp": 0, "hp": "1d10" } ] }""");
        modules.Write("rules/ready.json", """{ "type": "condition", "id": "ready", "name": "Ready", "modifiers": [ { "stat": "hit", "value": "1" } ] }""");
        return root;
    }

    public static RuleSet LoadValid(string path)
    {
        ModuleSet set = ModuleLoader.Load(path, []);
        Assert.Empty(set.Diagnostics);
        return set.Rules!;
    }

    private static string FindRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "RustyGoldbox.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Can't find the repository root from the test output directory.");
    }
}
