using System.Text.Json;
using Rusty.Engine.Testing;
using RustyGoldbox.Core.Campaigns;
using RustyGoldbox.Core.Characters;
using RustyGoldbox.Core.Combat;
using RustyGoldbox.Core.Definitions;
using RustyGoldbox.Core.Modules;
using RustyGoldbox.Core.Rules;

namespace RustyGoldbox.Tests;

/// <summary>
/// Original campaign and field checks for the authored party-size boundary.
/// The fixture deliberately permits ten to twelve members; it does not add a
/// product-wide party cap.
/// </summary>
public sealed class LargePartyTests
{
    private static string Fixture => Path.Combine(Rules.RepositoryRoot, "tests", "RustyGoldbox.Tests", "Fixtures", "large-party");

    private static string ModuleLibrary => Path.Combine(Rules.RepositoryRoot, "modules");

    [Fact]
    public void AGridReservesDistinctCellsAcrossSidesAndReportsCapacity()
    {
        using TempModules modules = new();
        string root = Rules.WriteSmallRuleset(modules);
        modules.Write("rules/grid.json", """
            {
              "type": "combat",
              "id": "grid",
              "name": "Crowded grid",
              "initiative": "self.str",
              "initiative_by": "side",
              "initiative_order": "highest-first",
              "initiative_each": "combat",
              "round_seconds": 6,
              "field": { "width": 6, "height": 2 },
              "budget": [ { "id": "turn", "per_turn": 1 } ],
              "track": "hit_points",
              "defeated": "self.hit_points <= 0"
            }
            """);

        RuleSet rules = Rules.LoadValid(root);
        Definition combat = rules.Find(DefinitionTypes.Combat, "grid", out _)!;
        CombatField field = CombatField.Of(combat)!;
        HashSet<Cell> occupied = [];

        IReadOnlyList<Cell> party = field.Deploy(0, 10, occupied: occupied);
        IReadOnlyList<Cell> foes = field.Deploy(1, 2, occupied: occupied);

        Assert.Equal(10, party.Count);
        Assert.Equal(2, foes.Count);
        Assert.Equal(12, occupied.Count);
        Assert.Empty(party.Intersect(foes));
        Assert.All(occupied, cell => Assert.True(field.Passable(cell)));

        RuleFailure failure = Assert.Throws<RuleFailure>(() => field.Deploy(0, 1, occupied: occupied));
        Assert.Equal("combat.deployment", failure.Diagnostic.Rule);
        Assert.Equal(combat.Module, failure.Diagnostic.Module);
        Assert.Equal(combat.File, failure.Diagnostic.File);
        Assert.Equal("$.field", failure.Diagnostic.JsonPath);
        Assert.Contains("combat rules:grid", failure.Diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("1 distinct passable cells", failure.Diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("Grid fields do not stack", failure.Diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LaterSidesCanUseColumnsSkippedByTheirEdgeOffset()
    {
        using TempModules modules = new();
        string root = Rules.WriteSmallRuleset(modules);
        modules.Write("rules/grid.json", """
            {
              "type": "combat",
              "id": "grid",
              "name": "Three-sided grid",
              "initiative": "self.str",
              "initiative_by": "side",
              "initiative_order": "highest-first",
              "initiative_each": "combat",
              "round_seconds": 6,
              "field": { "width": 3, "height": 3 },
              "budget": [ { "id": "turn", "per_turn": 1 } ],
              "track": "hit_points",
              "defeated": "self.hit_points <= 0"
            }
            """);

        RuleSet rules = Rules.LoadValid(root);
        CombatField field = CombatField.Of(rules.Find(DefinitionTypes.Combat, "grid", out _)!)!;
        HashSet<Cell> occupied = [];
        IReadOnlyList<Cell> first = field.Deploy(0, 1, occupied: occupied);
        IReadOnlyList<Cell> second = field.Deploy(1, 1, occupied: occupied);
        IReadOnlyList<Cell> third = field.Deploy(2, 7, occupied: occupied);

        Assert.Equal(9, occupied.Count);
        Assert.Equal(9, first.Concat(second).Concat(third).Distinct().Count());
        Assert.All(occupied, cell => Assert.True(field.Passable(cell)));
    }

    [Fact]
    public void CrowdedEncounterFailureNamesTheEncounterSource()
    {
        using TempModules modules = new();
        string root = Rules.WriteSmallRuleset(modules);
        modules.Write("rules/grid.json", """
            {
              "type": "combat",
              "id": "grid",
              "name": "Encounter grid",
              "initiative": "self.str",
              "initiative_by": "side",
              "initiative_order": "highest-first",
              "initiative_each": "combat",
              "round_seconds": 6,
              "field": { "width": 2, "height": 1 },
              "budget": [ { "id": "turn", "per_turn": 1 } ],
              "track": "hit_points",
              "defeated": "self.hit_points <= 0"
            }
            """);
        modules.Write("rules/ambush.json", """
            {
              "type": "encounter",
              "id": "ambush",
              "name": "Ambush",
              "monsters": [],
              "terrain": [ ".." ]
            }
            """);

        RuleSet rules = Rules.LoadValid(root);
        Definition combat = rules.Find(DefinitionTypes.Combat, "grid", out _)!;
        Definition encounter = rules.Find(DefinitionTypes.Encounter, "ambush", out _)!;
        CombatField field = CombatField.Of(combat, encounter)!;
        HashSet<Cell> occupied = [];
        field.Deploy(0, 1, occupied: occupied);
        field.Deploy(1, 1, occupied: occupied);

        RuleFailure failure = Assert.Throws<RuleFailure>(() => field.Deploy(0, 1, occupied: occupied));
        Assert.Equal("combat.deployment", failure.Diagnostic.Rule);
        Assert.Equal(encounter.Module, failure.Diagnostic.Module);
        Assert.Equal(encounter.File, failure.Diagnostic.File);
        Assert.Equal("$.terrain", failure.Diagnostic.JsonPath);
        Assert.Contains("encounter rules:ambush", failure.Diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AZoneFieldStillAllowsSharedStartingCells()
    {
        using TempModules modules = new();
        string root = Rules.WriteSmallRuleset(modules);
        modules.Write("rules/zones.json", """
            {
              "type": "combat",
              "id": "zones",
              "name": "Shared zones",
              "initiative": "self.str",
              "initiative_by": "side",
              "initiative_order": "highest-first",
              "initiative_each": "combat",
              "round_seconds": 6,
              "field": { "width": 2, "height": 1, "mode": "zones" },
              "budget": [ { "id": "turn", "per_turn": 1 } ],
              "track": "hit_points",
              "defeated": "self.hit_points <= 0"
            }
            """);

        RuleSet rules = Rules.LoadValid(root);
        CombatField field = CombatField.Of(rules.Find(DefinitionTypes.Combat, "zones", out _)!)!;
        HashSet<Cell> occupied = [new Cell(0, 0), new Cell(1, 0)];

        Assert.Equal([new Cell(0, 0), new Cell(0, 0), new Cell(0, 0)], field.Deploy(0, 3, occupied: occupied));
        Assert.Equal([new Cell(0, 0)], field.Deploy(0, 1, anchor: new Cell(-1, 0), occupied: occupied));
        Assert.Equal(2, occupied.Count);
    }

    [Fact]
    public void AnEmptyZoneFieldReportsAnActionableZoneFailure()
    {
        using TempModules modules = new();
        string root = Rules.WriteSmallRuleset(modules);
        modules.Write("rules/zones.json", """
            {
              "type": "combat",
              "id": "zones",
              "name": "Blocked zones",
              "initiative": "self.str",
              "initiative_by": "side",
              "initiative_order": "highest-first",
              "initiative_each": "combat",
              "round_seconds": 6,
              "field": {
                "width": 2,
                "height": 1,
                "mode": "zones",
                "terrain": { "#": { "name": "Wall", "passable": false } }
              },
              "budget": [ { "id": "turn", "per_turn": 1 } ],
              "track": "hit_points",
              "defeated": "self.hit_points <= 0"
            }
            """);
        modules.Write("rules/blocked.json", """
            {
              "type": "encounter",
              "id": "blocked",
              "name": "Blocked encounter",
              "monsters": [],
              "terrain": [ "##" ]
            }
            """);

        RuleSet rules = Rules.LoadValid(root);
        Definition combat = rules.Find(DefinitionTypes.Combat, "zones", out _)!;
        Definition encounter = rules.Find(DefinitionTypes.Encounter, "blocked", out _)!;
        CombatField field = CombatField.Of(combat, encounter)!;

        RuleFailure failure = Assert.Throws<RuleFailure>(() => field.Deploy(0, 1));
        Assert.Equal("combat.deployment", failure.Diagnostic.Rule);
        Assert.Equal(encounter.Module, failure.Diagnostic.Module);
        Assert.Equal(encounter.File, failure.Diagnostic.File);
        Assert.Equal("$.terrain", failure.Diagnostic.JsonPath);
        Assert.Contains("no passable zone", failure.Diagnostic.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Grid fields do not stack", failure.Diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AuthoredPartyBoundsAcceptTenAndTwelveAndRefuseNineAndThirteen()
    {
        using TempModules scratch = new();
        WriteMembers(scratch, 13);
        File.WriteAllText(Path.Combine(scratch.Root, "status.script"), "status\n");

        foreach (int count in new[] { 10, 12 })
        {
            (int code, string output) = RunLargeParty(scratch, ["play", "--campaign", Fixture, "--modules", ModuleLibrary,
                "--party", PartyFiles(count), "--script", "status.script", "--json"]);
            Assert.True(code == 0, output);
            using JsonDocument document = JsonDocument.Parse(output);
            Assert.True(document.RootElement.GetProperty("ok").GetBoolean());
        }

        foreach (int count in new[] { 9, 13 })
        {
            (int code, string output) = RunLargeParty(scratch, ["play", "--campaign", Fixture, "--modules", ModuleLibrary,
                "--party", PartyFiles(count), "--script", "status.script", "--json"]);
            Assert.Equal(1, code);
            using JsonDocument document = JsonDocument.Parse(output);
            JsonElement diagnostic = document.RootElement.GetProperty("diagnostics")[0];
            Assert.Equal("play.party", diagnostic.GetProperty("rule").GetString());
            Assert.Equal("large-party", diagnostic.GetProperty("module").GetString());
            Assert.Equal("$.party", diagnostic.GetProperty("jsonPath").GetString());
            Assert.Contains($"has {count}", diagnostic.GetProperty("message").GetString(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TwelveMemberSaveRoundTripsEveryCharacter()
    {
        using TempModules scratch = new();
        WriteMembers(scratch, 12);
        File.WriteAllText(Path.Combine(scratch.Root, "status.script"), "status\n");

        (int code, string output) = RunLargeParty(scratch, ["play", "--campaign", Fixture, "--modules", ModuleLibrary,
            "--party", PartyFiles(12), "--script", "status.script", "--save", "party.json", "--json"]);
        Assert.True(code == 0, output);

        using (JsonDocument save = JsonDocument.Parse(File.ReadAllText(Path.Combine(scratch.Root, "party.json"))))
        {
            JsonElement party = save.RootElement.GetProperty("party");
            Assert.Equal(12, party.GetArrayLength());
            Assert.Equal("Member 01", party[0].GetProperty("name").GetString());
            Assert.Equal("Member 12", party[11].GetProperty("name").GetString());
        }

        (code, output) = RunLargeParty(scratch, ["play", "--campaign", Fixture, "--modules", ModuleLibrary,
            "--load", "party.json", "--script", "status.script", "--json"]);
        Assert.True(code == 0, output);
        using JsonDocument resumed = JsonDocument.Parse(output);
        Assert.True(resumed.RootElement.GetProperty("ok").GetBoolean());
    }

    [Fact]
    public void TwelveMemberPartyRunsThroughTheSeededCombatCli()
    {
        using TempModules scratch = new();
        WriteMembers(scratch, 12);

        (int code, string output) = CampaignTests.Run(scratch, ["sim", "combat", "--module", Rules.ClassicPath,
            "--party", PartyFiles(12), "--encounter", "crypt_guard", "--seed", "7", "--max-rounds", "1", "--json"]);
        Assert.True(code == 0, output);
        using JsonDocument transcript = JsonDocument.Parse(output);
        JsonElement combatants = transcript.RootElement.GetProperty("combatants");
        Assert.Equal(14, combatants.GetArrayLength());
        Assert.Equal(12, combatants.EnumerateArray().Count(member => member.GetProperty("side").GetString() == "Party"));
    }

    [Fact]
    public void TwelveMemberCampaignRunsTheExistingRewardsItemsAndServicesChain()
    {
        using TempModules scratch = new();
        string campaign = Path.Combine(scratch.Root, "sample-crypt");
        CopyDirectory(CampaignTests.SampleCrypt, campaign);
        string campaignFile = Path.Combine(campaign, "campaign.json");
        File.WriteAllText(campaignFile, File.ReadAllText(campaignFile)
            .Replace("\"max\": 4", "\"max\": 12", StringComparison.Ordinal)
            .Replace("\"intro\": \"intro\"", "\"intro\": \"award\"", StringComparison.Ordinal));
        File.WriteAllText(Path.Combine(campaign, "events", "award.json"), """
            {
              "type": "event",
              "id": "award",
              "kind": "experience",
              "amount": "1900",
              "each": true,
              "next": "intro"
            }
            """);
        WriteMembers(scratch, 12);
        string script = Path.Combine(scratch.Root, "large-crypt.script");
        string[] cryptLines = File.ReadAllLines(CampaignTests.Script("crypt.script"));
        File.WriteAllLines(script, ["status", .. cryptLines]);

        (int code, string output) = CampaignTests.Run(scratch, ["play", "--campaign", campaign, "--modules", ModuleLibrary,
            "--party", PartyFiles(12), "--seed", "1", "--script", script, "--json"]);
        Assert.True(code == 0, output);
        Assert.Contains("Member 12", output, StringComparison.Ordinal);
        Assert.Contains("\"kind\": \"experience\"", output, StringComparison.Ordinal);
        Assert.Contains("Member 12 1900", output, StringComparison.Ordinal);
        Assert.Contains("\"kind\": \"combat\"", output, StringComparison.Ordinal);
        Assert.Contains("\"kind\": \"treasure\"", output, StringComparison.Ordinal);
        Assert.Contains("\"kind\": \"give\"", output, StringComparison.Ordinal);
        Assert.Contains("\"kind\": \"bought\"", output, StringComparison.Ordinal);
        Assert.Contains("\"kind\": \"sold\"", output, StringComparison.Ordinal);
        Assert.Contains("\"kind\": \"temple\"", output, StringComparison.Ordinal);

        // The campaign projection builds its initial FightFact separately from
        // the resolver. It must consume the same reservation order, otherwise
        // the Game can show two actors on one grid cell while Core fights them
        // at different cells.
        ModuleSet set = ModuleLoader.Load(campaign, [ModuleLibrary]);
        Assert.Empty(set.Diagnostics);
        List<ModuleDiagnostic> problems = [];
        List<Character> party = PartyFiles(12).Split(',')
            .Select(file => CharacterFile.Read(Path.Combine(scratch.Root, file), set, problems)!)
            .ToList();
        Assert.Empty(problems);
        Definition campaignDefinition = set.Rules!.OfType(DefinitionTypes.Campaign).Single();
        List<PlayFact> facts;
        using (EngineTestHost host = EngineTestHost.Create())
        {
            facts = host.Call(engine =>
            {
                CampaignState state = CampaignRunner.NewState(set.Rules, campaignDefinition, party, 1);
                CampaignRunner runner = new(set.Rules, state);
                List<PlayFact> played = runner.Begin(engine.Random);
                foreach (string command in cryptLines
                    .Select(line => line.Contains('#', StringComparison.Ordinal) ? line[..line.IndexOf('#', StringComparison.Ordinal)] : line)
                    .Select(line => line.Trim())
                    .Where(line => line.Length > 0))
                {
                    played.AddRange(runner.Execute(command, engine.Random));
                }

                return played;
            });
        }

        FightFact fight = Assert.Single(facts.OfType<FightFact>());
        List<Cell> positions = fight.Members
            .Where(member => member.Position is Cell)
            .Select(member => member.Position!.Value)
            .ToList();
        Assert.Equal(positions.Count, positions.Distinct().Count());
        Definition standard = set.Rules.Find(DefinitionTypes.Combat, "standard", out _)!;
        Definition guard = set.Rules.Find(DefinitionTypes.Encounter, "crypt_guard", out _)!;
        CombatField expectedField = CombatField.Of(standard, guard)!;
        HashSet<Cell> reserved = [];
        IReadOnlyList<Cell> expectedParty = expectedField.Deploy(0, 12, occupied: reserved);
        IReadOnlyList<Cell> expectedFoes = expectedField.Deploy(1, 2, occupied: reserved);
        Assert.Equal(expectedParty.Concat(expectedFoes), positions);

        int gateLeave = Array.FindLastIndex(cryptLines, line => line.TrimStart().StartsWith("leave", StringComparison.Ordinal));
        Assert.True(gateLeave > 0);
        string restScript = Path.Combine(scratch.Root, "large-rest.script");
        File.WriteAllLines(restScript, [.. cryptLines[..(gateLeave + 1)], "choose 2"]);
        (code, output) = CampaignTests.Run(scratch, ["play", "--campaign", campaign, "--modules", ModuleLibrary,
            "--party", PartyFiles(12), "--seed", "2", "--script", restScript, "--json"]);
        Assert.True(code == 0, output);
        Assert.Contains("You rest beside the gate for a day.", output, StringComparison.Ordinal);

        string npcScript = Path.Combine(scratch.Root, "large-npc.script");
        File.WriteAllLines(npcScript, [.. cryptLines[..(gateLeave + 1)], "choose 3", "choose 3", "choose 1"]);
        (code, output) = CampaignTests.Run(scratch, ["play", "--campaign", campaign, "--modules", ModuleLibrary,
            "--party", PartyFiles(11), "--seed", "3", "--script", npcScript, "--json"]);
        Assert.True(code == 0, output);
        Assert.Contains("Gate guide joins the party (12 members).", output, StringComparison.Ordinal);
        Assert.Contains("Gate guide leaves the party (11 members).", output, StringComparison.Ordinal);
    }

    [Fact]
    public void PackedLargePartyCampaignAcceptsAllTwelveMembers()
    {
        using TempModules scratch = new();
        WriteMembers(scratch, 12);
        File.WriteAllText(Path.Combine(scratch.Root, "status.script"), "status\n");
        string library = Path.Combine(scratch.Root, "library");
        Directory.CreateDirectory(library);

        Pack(Path.Combine(Rules.RepositoryRoot, "modules", "classic"), scratch, library, includeModules: false);
        Pack(Path.Combine(Rules.RepositoryRoot, "modules", "placeholder-art"), scratch, library, includeModules: false);
        string packedCampaign = Pack(Fixture, scratch, library, includeModules: true);
        (int code, string output) = CampaignTests.Run(scratch, ["play", "--campaign", packedCampaign,
            "--party", PartyFiles(12), "--script", "status.script", "--json"]);

        Assert.True(code == 0, output);
        using JsonDocument document = JsonDocument.Parse(output);
        Assert.True(document.RootElement.GetProperty("ok").GetBoolean());
    }

    private static (int Code, string Output) RunLargeParty(TempModules scratch, string[] args) => CampaignTests.Run(scratch, args);

    private static string PartyFiles(int count) => string.Join(',', Enumerable.Range(1, count).Select(index => $"member{index:00}.json"));

    private static void WriteMembers(TempModules scratch, int count)
    {
        for (int index = 1; index <= count; index++)
        {
            (int code, string output) = CampaignTests.Run(scratch, ["character", "new", "--module", Rules.ClassicPath,
                "--class", "fighter", "--race", "human", "--name", $"Member {index:00}",
                "--attributes", "str=16,dex=12,con=14,int=10,wis=10,cha=10", "--seed", index.ToString(),
                "--out", $"member{index:00}.json"]);
            Assert.True(code == 0, output);
        }
    }

    private static void CopyDirectory(string source, string target)
    {
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            string destination = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination);
        }
    }

    private static string Pack(string module, TempModules scratch, string library, bool includeModules)
    {
        string target = Path.Combine(library, $"{Path.GetFileName(module)}-0.1.0.rpak");
        List<string> args = ["module", "pack", module, "--output", target];
        if (includeModules)
        {
            args.Add("--modules");
            args.Add(ModuleLibrary);
        }

        (int code, string output) = CampaignTests.Run(scratch, args.ToArray());
        Assert.True(code == 0, output);
        return target;
    }
}
