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
        modules.Module("osric", "ruleset");
        modules.Module("crypt-art", "assets");
        modules.Module("house", "extension", requires: Require("osric", "^0.1.0"));
        string campaign = modules.Module("sample", "campaign",
            requires: $"{Require("house", "*")}, {Require("osric", "^0.1.0")}, {Require("crypt-art", "*")}");

        ModuleSet set = ModuleLoader.Load(campaign, []);

        Assert.Empty(set.Diagnostics);
        Assert.Equal(["osric", "house", "crypt-art", "sample"], set.LoadOrder.Select(loaded => loaded.Manifest.Id));
    }

    [Fact]
    public void PicksTheHighestMatchingVersion()
    {
        using TempModules modules = new();
        modules.Module("osric", "ruleset", "0.1.0", directory: "osric-0.1");
        modules.Module("osric", "ruleset", "0.1.4", directory: "osric-0.1.4");
        modules.Module("osric", "ruleset", "0.2.0", directory: "osric-0.2");
        string extension = modules.Module("house", "extension", requires: Require("osric", "^0.1.0"));

        ModuleSet set = ModuleLoader.Load(extension, []);

        Assert.Empty(set.Diagnostics);
        Assert.Equal(new ModuleVersion(0, 1, 4), set.LoadOrder[0].Manifest.Version);
    }

    [Fact]
    public void MissingRequirementNamesTheSearchDirectories()
    {
        using TempModules modules = new();
        string extension = modules.Module("house", "extension", requires: Require("osric", "^0.1.0"));

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
        modules.Module("osric", "ruleset", "0.1.0", directory: "osric-0.1");
        modules.Module("osric", "ruleset", "0.2.0", directory: "osric-0.2");
        modules.Module("crypt-art", "assets");
        modules.Module("house", "extension", requires: Require("osric", "~0.2.0"));
        string campaign = modules.Module("clash", "campaign",
            requires: $"{Require("osric", "^0.1.0")}, {Require("house", "*")}, {Require("crypt-art", "*")}");

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
        modules.Module("osric", "ruleset");
        modules.Module("house", "extension", requires: Require("osric", "*"));
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
        modules.Module("osric", "ruleset");
        modules.Module("srd35", "ruleset");
        modules.Module("art", "assets");
        modules.Module("house", "extension", requires: Require("srd35", "*"));
        string campaign = modules.Module("mixed", "campaign",
            requires: $"{Require("osric", "*")}, {Require("house", "*")}, {Require("art", "*")}");

        ModuleSet set = ModuleLoader.Load(campaign, []);

        ModuleDiagnostic diagnostic = Assert.Single(set.Diagnostics);
        Assert.Equal("resolve.rulesets", diagnostic.Rule);
    }

    [Fact]
    public void SameVersionInTwoDirectoriesIsAmbiguous()
    {
        using TempModules modules = new();
        modules.Module("osric", "ruleset", directory: "osric-a");
        modules.Module("osric", "ruleset", directory: "osric-b");
        string extension = modules.Module("house", "extension", requires: Require("osric", "*"));

        ModuleSet set = ModuleLoader.Load(extension, []);

        Assert.Equal("resolve.ambiguous", Assert.Single(set.Diagnostics).Rule);
    }

    [Fact]
    public void WorkspaceFileListsSearchDirectories()
    {
        using TempModules modules = new();
        modules.Write("goldbox.json", """{ "modules": ["rules", "campaigns"] }""");
        modules.Module("osric", "ruleset", directory: "rules/osric");
        string extension = modules.Module("house", "extension", requires: Require("osric", "*"), directory: "campaigns/house");

        ModuleSet set = ModuleLoader.Load(extension, []);

        Assert.Empty(set.Diagnostics);
        Assert.Equal(2, set.LoadOrder.Count);
    }

    [Fact]
    public void TrailingSeparatorStillSearchesSiblings()
    {
        using TempModules modules = new();
        modules.Module("osric", "ruleset");
        string extension = modules.Module("house", "extension", requires: Require("osric", "*"));

        ModuleSet set = ModuleLoader.Load(extension + Path.DirectorySeparatorChar, []);

        Assert.Empty(set.Diagnostics);
        Assert.Equal(["osric", "house"], set.LoadOrder.Select(loaded => loaded.Manifest.Id));
    }

    [Fact]
    public void ExplicitSearchDirectoriesReplaceSiblings()
    {
        using TempModules modules = new();
        modules.Module("osric", "ruleset", "0.1.0", directory: "shared/osric");
        modules.Module("osric", "ruleset", "0.2.0", directory: "campaigns/osric");
        string extension = modules.Module("house", "extension", requires: Require("osric", "*"), directory: "campaigns/house");

        ModuleSet set = ModuleLoader.Load(extension, [Path.Combine(modules.Root, "shared")]);

        Assert.Empty(set.Diagnostics);
        Assert.Equal(new ModuleVersion(0, 1, 0), set.LoadOrder[0].Manifest.Version);
    }

    [Fact]
    public void MissingSearchDirectoryIsAnError()
    {
        using TempModules modules = new();
        string ruleset = modules.Module("osric", "ruleset");

        ModuleSet set = ModuleLoader.Load(ruleset, [Path.Combine(modules.Root, "nowhere")]);

        Assert.Equal("search.not-found", Assert.Single(set.Diagnostics).Rule);
    }

    [Fact]
    [UnsupportedOSPlatform("windows")]
    public void UnreadableFilesAndDirectoriesAreDiagnostics()
    {
        using TempModules modules = new();
        string ruleset = modules.Module("osric", "ruleset");
        modules.Write("osric/locked.json", """{ "type": "class" }""");
        modules.Write("osric/sealed/x.json", """{ "type": "class" }""");
        File.SetUnixFileMode(Path.Combine(ruleset, "locked.json"), UnixFileMode.None);
        File.SetUnixFileMode(Path.Combine(ruleset, "sealed"), UnixFileMode.None);
        try
        {
            ModuleSet set = ModuleLoader.Load(ruleset, []);

            Assert.Equal(["file.read", "directory.read"], set.Diagnostics.Select(diagnostic => diagnostic.Rule));
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
        string ruleset = modules.Module("osric", "ruleset");
        modules.Write("osric/classes/fighter.json", """{ "type": "class" }""");
        modules.Write("osric/notes.json", """[1, 2]""");

        ModuleSet set = ModuleLoader.Load(ruleset, []);

        Assert.Equal(
            [("definition.type-missing", "notes.json"), ("definition.type-unknown", "fighter.json")],
            set.Diagnostics.Select(diagnostic => (diagnostic.Rule, Path.GetFileName(diagnostic.File!))));
    }
}
