using System.Text.Json;
using RustyTales.Core.Campaigns;
using RustyTales.Core.Characters;
using RustyTales.Core.Definitions;
using RustyTales.Core.Modules;
using RustyTales.Core.Persistence;
using RustyTales.Core.Rules;

namespace RustyTales.Tests;

/// <summary>
/// Proves that <c>RustyTales.Core</c> loads modules, rolls dice, creates
/// characters, runs campaigns, and persists saves and the character stable
/// purely in .NET with zero <c>Rusty.Engine</c> host dependency.
/// </summary>
public sealed class PureCoreTests
{
    [Fact]
    public void DeterministicRandomRepeatsIdenticalScopedAndKeyedDraws()
    {
        ITalesRandom random = DeterministicRandom.Shared;

        List<uint> run1 = [];
        using (ITalesRandomStream stream = random.CreateScoped(42, "pure.scope"))
        {
            for (int i = 0; i < 16; i++)
            {
                run1.Add(stream.NextBounded(20));
            }
        }

        List<uint> run2 = [];
        using (ITalesRandomStream stream = random.CreateScoped(42, "pure.scope"))
        {
            for (int i = 0; i < 16; i++)
            {
                run2.Add(stream.NextBounded(20));
            }
        }

        List<uint> otherScope = [];
        using (ITalesRandomStream stream = random.CreateScoped(42, "pure.other"))
        {
            for (int i = 0; i < 16; i++)
            {
                otherScope.Add(stream.NextBounded(20));
            }
        }

        Assert.Equal(run1, run2);
        Assert.NotEqual(run1, otherScope);
        Assert.All(run1, value => Assert.InRange(value, 0u, 19u));

        uint keyedA = random.DrawKeyed(99, "combat.init", "actor-1", 20);
        uint keyedB = random.DrawKeyed(99, "combat.init", "actor-1", 20);
        Assert.Equal(keyedA, keyedB);
        Assert.InRange(keyedA, 0u, 19u);
    }

    [Fact]
    public void CampaignRunsAndPersistsSavesAndStableWithFileStateStore()
    {
        using TempModules scratch = new();
        ModuleSet set = ModuleLoader.Load(CampaignTests.SampleCrypt, []);
        Assert.Empty(set.Diagnostics);
        RuleSet rules = set.Rules!;

        List<ModuleDiagnostic> problems = [];
        Character? ada = null;
        for (int attempt = 1; ada is null && attempt <= 20; attempt++)
        {
            problems.Clear();
            using ITalesRandomStream stream = DeterministicRandom.Shared.CreateScoped(12, $"pure.roll.{attempt}");
            DiceRoller dice = new(DeterministicRandom.Shared, stream);
            ada = CharacterRules.Create(rules, Character.StampsOf(set), new CreationRequest("Ada", "classic:fighter", "classic:human"), dice, problems);
        }

        Assert.NotNull(ada);

        // Persist and reload the character in the portable stable over FileStateStore.
        string storeRoot = Path.Combine(scratch.Root, "state");
        using (FileStateStore stableStore = new(storeRoot, CharacterStableStore.Scope))
        {
            StableEntry entry = CharacterRoster.CreateEntry(set, ada!, null, "Sample Crypt", []);
            CharacterStableStore.Save(stableStore, [entry]);

            List<string> warnings = [];
            List<StableEntry> reloaded = CharacterStableStore.Load(stableStore, warnings);
            Assert.Empty(warnings);
            StableEntry restored = Assert.Single(reloaded);
            Assert.Equal("Ada", restored.Name);
            Assert.Equal("classic", restored.RulesetId);
        }

        // Run a campaign turn purely with DeterministicRandom.Shared and round-trip via SaveSlots + FileStateStore.
        Definition campaign = rules.OfType(DefinitionTypes.Campaign).Single(definition => definition.Module == set.Root!.Id);
        CampaignState state = CampaignRunner.NewState(rules, campaign, [ada!], 7);
        CampaignRunner runner = new(rules, state);
        List<PlayFact> intro = runner.Begin(DeterministicRandom.Shared);
        Assert.NotEmpty(intro);

        using (SaveSlots slots = new(new FileStateStore(storeRoot, SaveSlots.Scope)))
        {
            slots.Write("pure-slot", SaveFile.ToJson(state, set));
            byte[]? bytes = slots.Read("pure-slot");
            Assert.NotNull(bytes);

            List<ModuleDiagnostic> loadProblems = [];
            CampaignState? loaded = SaveFile.Read(bytes!, SaveSlots.Location("pure-slot"), set, loadProblems);
            Assert.Empty(loadProblems);
            Assert.NotNull(loaded);
            Assert.Equal(state.Seed, loaded!.Seed);
            Assert.Equal(state.Party[0].Name, loaded.Party[0].Name);
        }
    }
}
