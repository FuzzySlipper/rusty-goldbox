using System.Runtime.Versioning;
using RustyGoldbox.Core.Modules;
using static RustyGoldbox.Tests.TempModules;

namespace RustyGoldbox.Tests;

public sealed class ModuleLoaderTests
{
    [Fact]
    public void CampaignLoadsAfterEverythingItRequires()
    {
        using TempModules modules = new();
        modules.Module("classic", "ruleset");
        modules.Module("crypt-art", "assets");
        modules.Module("house", "extension", requires: Require("classic", "^0.1.0"));
        string campaign = modules.Module("sample", "campaign",
            requires: $"{Require("house", "*")}, {Require("classic", "^0.1.0")}, {Require("crypt-art", "*")}");
        modules.Write("sample/area.json", """{ "type": "area", "id": "start", "name": "Start", "map": ["+--+", "|  |", "+--+"], "entries": { "in": { "at": [0, 0], "facing": "north" } } }""");
        modules.Write("sample/campaign.json", """{ "type": "campaign", "id": "c", "name": "C", "start": { "area": "start", "entry": "in" }, "party": { "min": 1, "max": 6 } }""");

        ModuleSet set = ModuleLoader.Load(campaign, []);

        Assert.Empty(set.Diagnostics);
        Assert.Equal(["classic", "house", "crypt-art", "sample"], set.LoadOrder.Select(loaded => loaded.Manifest.Id));
    }

    [Fact]
    public void AddedExtensionsLoadAfterTheModuleWithWhatTheyRequire()
    {
        using TempModules modules = new();
        string classic = modules.Module("classic", "ruleset");
        modules.Module("book-art", "assets");
        modules.Module("blades", "extension", "0.1.0", requires: Require("classic", "^0.1.0"), directory: "blades-0.1");
        modules.Module("blades", "extension", "0.2.0", requires: $"{Require("classic", "^0.1.0")}, {Require("book-art", "*")}", directory: "blades-0.2");
        modules.Module("beasts", "extension", requires: Require("classic", "^0.1.0"));

        ModuleSet set = ModuleLoader.Load(classic, [], extensions: ["blades", "beasts", "blades"]);

        // The highest version, after the ruleset and its own requirement; each added once.
        Assert.Empty(set.Diagnostics);
        Assert.Equal(["classic", "book-art", "blades", "beasts"], set.LoadOrder.Select(loaded => loaded.Manifest.Id));
        Assert.Equal(new ModuleVersion(0, 2, 0), set.LoadOrder[2].Manifest.Version);
        Assert.Equal(["blades", "beasts"], set.Extensions);
    }

    [Fact]
    public void AddedExtensionsMustExistBeExtensionsAndShareTheRuleset()
    {
        using TempModules modules = new();
        string classic = modules.Module("classic", "ruleset");
        modules.Module("other", "ruleset");
        modules.Module("art", "assets");
        modules.Module("elsewhere", "extension", requires: Require("other", "*"));

        ModuleDiagnostic missing = Assert.Single(ModuleLoader.Load(classic, [], extensions: ["nope"]).Diagnostics);
        ModuleDiagnostic kind = Assert.Single(ModuleLoader.Load(classic, [], extensions: ["art"]).Diagnostics);
        ModuleDiagnostic ruleset = Assert.Single(ModuleLoader.Load(classic, [], extensions: ["elsewhere"]).Diagnostics);

        Assert.Equal("extension.not-found", missing.Rule);
        Assert.Contains(modules.Root, missing.Message, StringComparison.Ordinal);
        Assert.Equal("extension.kind", kind.Rule);
        Assert.Contains("'art' is a module of kind assets", kind.Message, StringComparison.Ordinal);
        Assert.Equal("resolve.rulesets", ruleset.Rule);
    }

    [Fact]
    public void PicksTheHighestMatchingVersion()
    {
        using TempModules modules = new();
        modules.Module("classic", "ruleset", "0.1.0", directory: "classic-0.1");
        modules.Module("classic", "ruleset", "0.1.4", directory: "classic-0.1.4");
        modules.Module("classic", "ruleset", "0.2.0", directory: "classic-0.2");
        string extension = modules.Module("house", "extension", requires: Require("classic", "^0.1.0"));

        ModuleSet set = ModuleLoader.Load(extension, []);

        Assert.Empty(set.Diagnostics);
        Assert.Equal(new ModuleVersion(0, 1, 4), set.LoadOrder[0].Manifest.Version);
    }

    [Fact]
    public void MissingRequirementNamesTheSearchDirectories()
    {
        using TempModules modules = new();
        string extension = modules.Module("house", "extension", requires: Require("classic", "^0.1.0"));

        ModuleSet set = ModuleLoader.Load(extension, []);

        ModuleDiagnostic diagnostic = Assert.Single(set.Diagnostics);
        Assert.Equal("resolve.not-found", diagnostic.Rule);
        Assert.Equal("house", diagnostic.Module);
        Assert.Equal("$.requires[0].id", diagnostic.JsonPath);
        Assert.Contains(modules.Root, diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ConflictingRangesAreAnError()
    {
        using TempModules modules = new();
        modules.Module("classic", "ruleset", "0.1.0", directory: "classic-0.1");
        modules.Module("classic", "ruleset", "0.2.0", directory: "classic-0.2");
        modules.Module("crypt-art", "assets");
        modules.Module("house", "extension", requires: Require("classic", "~0.2.0"));
        string campaign = modules.Module("clash", "campaign",
            requires: $"{Require("classic", "^0.1.0")}, {Require("house", "*")}, {Require("crypt-art", "*")}");

        ModuleSet set = ModuleLoader.Load(campaign, []);

        ModuleDiagnostic diagnostic = Assert.Single(set.Diagnostics);
        Assert.Equal("resolve.conflict", diagnostic.Rule);
        Assert.Equal("house", diagnostic.Module);
        Assert.Equal("$.requires[0].version", diagnostic.JsonPath);
        Assert.Contains("'clash' requires ^0.1.0", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RequirementCyclesAreAnError()
    {
        using TempModules modules = new();
        modules.Module("walls", "assets", requires: Require("doors", "*"));
        string doors = modules.Module("doors", "assets", requires: Require("walls", "*"));

        ModuleSet set = ModuleLoader.Load(doors, []);

        ModuleDiagnostic diagnostic = Assert.Single(set.Diagnostics);
        Assert.Equal("resolve.cycle", diagnostic.Rule);
        Assert.Contains("doors -> walls -> doors", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void KindRulesApplyToRequirements()
    {
        using TempModules modules = new();
        modules.Module("classic", "ruleset");
        modules.Module("house", "extension", requires: Require("classic", "*"));
        string ruleset = modules.Module("bad-rules", "ruleset", requires: Require("house", "*"));

        ModuleSet set = ModuleLoader.Load(ruleset, []);

        Assert.Contains(set.Diagnostics, diagnostic => diagnostic.Rule == "requires.kind" && diagnostic.JsonPath == "$.requires[0].id");
    }

    [Fact]
    public void CampaignNeedsOneRulesetAndAssets()
    {
        using TempModules modules = new();
        string campaign = modules.Module("bare", "campaign");

        ModuleSet set = ModuleLoader.Load(campaign, []);

        Assert.Equal(2, set.Diagnostics.Count(diagnostic => diagnostic.Rule == "requires.kind"));
    }

    [Fact]
    public void ModuleSetHasOneRuleset()
    {
        using TempModules modules = new();
        modules.Module("classic", "ruleset");
        modules.Module("srd35", "ruleset");
        modules.Module("art", "assets");
        modules.Module("house", "extension", requires: Require("srd35", "*"));
        string campaign = modules.Module("mixed", "campaign",
            requires: $"{Require("classic", "*")}, {Require("house", "*")}, {Require("art", "*")}");

        ModuleSet set = ModuleLoader.Load(campaign, []);

        ModuleDiagnostic diagnostic = Assert.Single(set.Diagnostics);
        Assert.Equal("resolve.rulesets", diagnostic.Rule);
    }

    [Fact]
    public void SameVersionWithDifferentContentIsAmbiguous()
    {
        using TempModules modules = new();
        modules.Module("classic", "ruleset", directory: "classic-a");
        modules.Module("classic", "ruleset", directory: "classic-b");
        modules.Write("classic-b/notes.txt", "A different copy.");
        string extension = modules.Module("house", "extension", requires: Require("classic", "*"));

        ModuleSet set = ModuleLoader.Load(extension, []);

        Assert.Equal("resolve.ambiguous", Assert.Single(set.Diagnostics).Rule);
    }

    [Fact]
    public void WorkspaceFileListsSearchDirectories()
    {
        using TempModules modules = new();
        modules.Write("goldbox.json", """{ "modules": ["rules", "campaigns"] }""");
        modules.Module("classic", "ruleset", directory: "rules/classic");
        string extension = modules.Module("house", "extension", requires: Require("classic", "*"), directory: "campaigns/house");

        ModuleSet set = ModuleLoader.Load(extension, []);

        Assert.Empty(set.Diagnostics);
        Assert.Equal(2, set.LoadOrder.Count);
    }

    [Fact]
    public void TrailingSeparatorStillSearchesSiblings()
    {
        using TempModules modules = new();
        modules.Module("classic", "ruleset");
        string extension = modules.Module("house", "extension", requires: Require("classic", "*"));

        ModuleSet set = ModuleLoader.Load(extension + Path.DirectorySeparatorChar, []);

        Assert.Empty(set.Diagnostics);
        Assert.Equal(["classic", "house"], set.LoadOrder.Select(loaded => loaded.Manifest.Id));
    }

    [Fact]
    public void ExplicitSearchDirectoriesReplaceSiblings()
    {
        using TempModules modules = new();
        modules.Module("classic", "ruleset", "0.1.0", directory: "shared/classic");
        modules.Module("classic", "ruleset", "0.2.0", directory: "campaigns/classic");
        string extension = modules.Module("house", "extension", requires: Require("classic", "*"), directory: "campaigns/house");

        ModuleSet set = ModuleLoader.Load(extension, [Path.Combine(modules.Root, "shared")]);

        Assert.Empty(set.Diagnostics);
        Assert.Equal(new ModuleVersion(0, 1, 0), set.LoadOrder[0].Manifest.Version);
    }

    [Fact]
    public void MissingSearchDirectoryIsAnError()
    {
        using TempModules modules = new();
        string ruleset = modules.Module("classic", "ruleset");

        ModuleSet set = ModuleLoader.Load(ruleset, [Path.Combine(modules.Root, "nowhere")]);

        Assert.Equal("search.not-found", Assert.Single(set.Diagnostics).Rule);
    }

    [Fact]
    [UnsupportedOSPlatform("windows")]
    public void UnreadableFilesAndDirectoriesAreDiagnostics()
    {
        using TempModules modules = new();
        string ruleset = modules.Module("classic", "ruleset");
        modules.Write("classic/locked.json", """{ "type": "attribute" }""");
        modules.Write("classic/sealed/x.json", """{ "type": "attribute" }""");
        File.SetUnixFileMode(Path.Combine(ruleset, "locked.json"), UnixFileMode.None);
        File.SetUnixFileMode(Path.Combine(ruleset, "sealed"), UnixFileMode.None);
        try
        {
            ModuleSet set = ModuleLoader.Load(ruleset, []);

            Assert.Equal(["directory.read", "file.read"], set.Diagnostics.Select(diagnostic => diagnostic.Rule));
        }
        finally
        {
            File.SetUnixFileMode(Path.Combine(ruleset, "sealed"), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    [Fact]
    public void DuplicateJsonKeysAreSyntaxErrors()
    {
        using TempModules modules = new();
        string directory = modules.Manifest("dup", """{ "format": 1, "format": 1 }""");

        ModuleSet set = ModuleLoader.Load(directory, []);

        Assert.Equal("json.syntax", Assert.Single(set.Diagnostics).Rule);
    }

    [Fact]
    public void DefinitionFilesMustNameAKnownType()
    {
        using TempModules modules = new();
        string ruleset = modules.Module("classic", "ruleset");
        modules.Write("classic/classes/fighter.json", """{ "type": "spellbook", "id": "fighter" }""");
        modules.Write("classic/notes.json", """[1, 2]""");

        ModuleSet set = ModuleLoader.Load(ruleset, []);

        Assert.Equal(
            [("definition.type-unknown", "fighter.json"), ("definition.type-missing", "notes.json")],
            set.Diagnostics.Select(diagnostic => (diagnostic.Rule, Path.GetFileName(diagnostic.File!))));
        Assert.Contains("currency", set.Diagnostics[0].Message, StringComparison.Ordinal);
    }
}
