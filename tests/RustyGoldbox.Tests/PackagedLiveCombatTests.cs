using Rusty.Engine;
using Rusty.Engine.Testing;
using RustyGoldbox.Core.Campaigns;
using RustyGoldbox.Core.Characters;
using RustyGoldbox.Core.Combat;
using RustyGoldbox.Core.Definitions;
using RustyGoldbox.Core.Modules;

namespace RustyGoldbox.Tests;

/// <summary>Live decisions and Engine save slots with independently packed original modules.</summary>
public sealed class PackagedLiveCombatTests
{
    [Fact]
    public void SharedZoneCampaignResumesAndFinishesIdenticallyFromContainers()
    {
        using TempModules scratch = new();
        string fixtures = Path.Combine(Rules.RepositoryRoot, "tests", "RustyGoldbox.Tests", "Fixtures");
        string modules = Path.Combine(Rules.RepositoryRoot, "modules");
        string campaign = Path.Combine(fixtures, "tactical-zones-trial");
        string library = Path.Combine(scratch.Root, "library");
        List<string> containers = [];
        foreach (string directory in new[]
        {
            Path.Combine(fixtures, "ascend"),
            Path.Combine(fixtures, "tactical-zones"),
            Path.Combine(modules, "placeholder-art"),
            campaign,
        })
        {
            string output = Path.Combine(library, $"{Path.GetFileName(directory)}-0.1.0.rpak");
            (int code, string printed) = CampaignTests.Run(scratch, "module", "pack", directory,
                "--modules", fixtures, "--modules", modules, "--output", output);
            Assert.True(code == 0, printed);
            containers.Add(output);
        }

        (int creationCode, string creationOutput) = CampaignTests.Run(scratch, "character", "new",
            "--module", Path.Combine(fixtures, "ascend"), "--creation", "array",
            "--race", "folk", "--class", "warrior", "--name", "Quill",
            "--priority", "might,grit,grace,wit", "--feature", "iron_will,improved_initiative",
            "--seed", "9346", "--out", "quill.json");
        Assert.True(creationCode == 0, creationOutput);

        ModuleSet source = ModuleLoader.Load(campaign, [fixtures, modules]);
        Assert.Empty(source.Diagnostics);
        using EngineTestHost host = EngineTestHost.Create(new EngineTestHostOptions
        {
            PersistenceRoot = Path.Combine(scratch.Root, "persistence"),
        });
        string sourceResult = host.Call(engine => PlaySavedBattle(source, scratch, engine));
        string packedResult = host.Call(engine =>
        {
            List<ProductContentBundle> bundles = containers.Select(path => ProductContentBundle.OpenContainer(engine.Content, path)).ToList();
            try
            {
                List<ModuleSource> sources = bundles.Select(bundle => (ModuleSource)new BundleModuleSource(bundle)).ToList();
                ModuleSet packed = ModuleLoader.Load(sources[^1], sources, containers, "Install these independent module containers.");
                Assert.Empty(packed.Diagnostics);
                return PlaySavedBattle(packed, scratch, engine);
            }
            finally
            {
                bundles.ForEach(bundle => bundle.Dispose());
            }
        });

        Assert.Equal(sourceResult, packedResult);
    }

    private static string PlaySavedBattle(ModuleSet set, TempModules scratch, IEngineContext engine)
    {
        List<Character> party = [];
        for (int index = 0; index < 2; index++)
        {
            List<ModuleDiagnostic> problems = [];
            Character member = CharacterFile.Read(Path.Combine(scratch.Root, "quill.json"), set, problems)!;
            Assert.Empty(problems);
            member.Name = $"Trainee {index + 1}";
            party.Add(member);
        }

        Definition campaign = set.Rules!.Find(DefinitionTypes.Campaign, "tactical-zones-trial:trial", out _)!;
        CampaignState state = CampaignRunner.NewState(set.Rules, campaign, party, 9346);
        CampaignRunner runner = new(set.Rules, state) { DefaultCombatControl = CombatControlMode.Manual };
        runner.Begin(engine.Random);
        runner.Execute("choose 1", engine.Random);
        CombatObservation waiting = Assert.IsType<CombatObservation>(runner.ObserveCombat(engine.Random));
        Assert.Equal(CombatDecisionKind.Action, waiting.PendingDecision!.Kind);
        CombatantObservation actor = Assert.Single(waiting.Combatants, member => member.Id == waiting.ActiveActorId);
        Assert.Equal(3, actor.Budget["standard"]);

        CombatActionChoice attack = waiting.PendingDecision.Actions.First(choice => choice.ActionId == "ascend:melee_attack");
        CampaignCombatCommandResult first = runner.SubmitCombat(
            new CombatCommand.UseAction(actor.Id, attack.Id, [attack.Targets[0].Id]), engine.Random);
        Assert.True(first.Accepted, first.Reason);
        Assert.Equal(2, first.Observation.Combatants.Single(member => member.Id == actor.Id).Budget["standard"]);
        Assert.NotNull(state.PendingCombat);

        using SaveSlots slots = new(engine);
        slots.Write("live-zone", SaveFile.ToJson(state, set));
        List<ModuleDiagnostic> diagnostics = [];
        CampaignState loaded = SaveFile.Read(slots.Read("live-zone")!, SaveSlots.Location("live-zone"), set, diagnostics)!;
        Assert.Empty(diagnostics);
        CampaignRunner resumed = new(set.Rules, loaded) { DefaultCombatControl = CombatControlMode.Manual };
        Assert.Equal(CombatContinuationState.ToJson(state.PendingCombat!.Continuation),
            CombatContinuationState.ToJson(loaded.PendingCombat!.Continuation));

        while (state.PendingCombat is not null)
        {
            CombatObservation before = Assert.IsType<CombatObservation>(runner.ObserveCombat(engine.Random));
            CombatObservation after = Assert.IsType<CombatObservation>(resumed.ObserveCombat(engine.Random));
            Assert.Equal(before.Phase, after.Phase);
            Assert.Equal(before.ActiveActorId, after.ActiveActorId);
            Assert.Equal(before.PendingDecision?.Id, after.PendingDecision?.Id);
            CombatCommand command = ChooseCommand(before);
            CampaignCombatCommandResult uninterrupted = runner.SubmitCombat(command, engine.Random);
            CampaignCombatCommandResult restored = resumed.SubmitCombat(command, engine.Random);
            Assert.True(uninterrupted.Accepted, uninterrupted.Reason);
            Assert.True(restored.Accepted, restored.Reason);
            Assert.Equal(uninterrupted.Observation.Facts.Select(fact => fact.Describe()),
                restored.Observation.Facts.Select(fact => fact.Describe()));
            Assert.Equal(uninterrupted.Facts.Select(fact => fact.Describe()), restored.Facts.Select(fact => fact.Describe()));
        }

        Assert.Null(loaded.PendingCombat);
        string result = SaveFile.ToJson(state, set);
        Assert.Equal(result, SaveFile.ToJson(loaded, set));
        return result;
    }

    private static CombatCommand ChooseCommand(CombatObservation observation)
    {
        CombatDecision decision = Assert.IsType<CombatDecision>(observation.PendingDecision);
        if (decision.Kind is CombatDecisionKind.Interrupt or CombatDecisionKind.PostRoll)
        {
            return new CombatCommand.Decide(decision.Id);
        }

        if (decision.Kind == CombatDecisionKind.Initiative)
        {
            return new CombatCommand.Decide(decision.Id, decision.Options![0].Id);
        }

        if (decision.Actions.Count == 0)
        {
            return new CombatCommand.EndTurn(decision.ActorId);
        }

        CombatActionChoice choice = decision.Actions[0];
        return new CombatCommand.UseAction(decision.ActorId, choice.Id, [choice.Targets[0].Id],
            choice.Moves.Count > 0 ? choice.Moves[0].Path : null);
    }
}
