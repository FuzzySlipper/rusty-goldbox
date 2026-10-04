using System.Text.Json;
using RustyGoldbox.Cli;
using RustyGoldbox.Core.Combat;
using RustyGoldbox.Core.Definitions;
using RustyGoldbox.Core.Expressions;
using RustyGoldbox.Core.Modules;
using RustyGoldbox.Core.Rules;
using static RustyGoldbox.Tests.TempModules;

namespace RustyGoldbox.Tests;

/// <summary>
/// Acceptance checks for the author-facing combat trace. Each case is a small
/// authored situation whose trace explains an observable decision and points
/// back to the data an agent would edit.
/// </summary>
public sealed class DiagnosticCombatTraceAcceptanceTests
{
    private static string FixtureRoot => Path.Combine(Rules.RepositoryRoot, "tests", "RustyGoldbox.Tests", "Fixtures", "tactical-diagnostics");

    private static string ModuleLibrary => Path.Combine(Rules.RepositoryRoot, "modules");

    [Fact]
    public void CliListsNamedDiagnosticFixturesWithRepairableSources()
    {
        using StringWriter output = new();
        int code = GoldboxCli.Run(
            ["module", "inspect", FixtureRoot, "combat-behavior", "--trace", "--json", "--modules", ModuleLibrary],
            output,
            Rules.RepositoryRoot);

        Assert.Equal(GoldboxCli.Ok, code);
        using JsonDocument json = JsonDocument.Parse(output.ToString());
        JsonElement definitions = json.RootElement.GetProperty("definitions");
        Assert.Equal(
            [
                "tactical-diagnostics:blocked_ranged_plan",
                "tactical-diagnostics:fallback",
                "tactical-diagnostics:faulty_profile",
                "tactical-diagnostics:support_threshold_resource",
                "tactical-diagnostics:target_loss",
            ],
            definitions.EnumerateArray().Select(definition => definition.GetProperty("id").GetString()).Order());

        foreach (JsonElement definition in definitions.EnumerateArray())
        {
            Assert.Equal("tactical-diagnostics", definition.GetProperty("source").GetProperty("module").GetString());
            Assert.EndsWith(
                $"tests/RustyGoldbox.Tests/Fixtures/tactical-diagnostics/behaviors/{definition.GetProperty("id").GetString()!.Split(':')[1]}.json",
                definition.GetProperty("source").GetProperty("file").GetString(),
                StringComparison.Ordinal);
            Assert.Equal("$", definition.GetProperty("source").GetProperty("jsonPath").GetString());
        }
    }

    [Fact]
    public void BlockedRangedPlanTraceNamesTheBlockedDestinationAndChosenFallback()
    {
        (RuleSet rules, CombatBehaviorProfile profile, Definition combat) = LoadProfile("blocked_ranged_plan");
        Combatant actor = Actor("Actor", "actor", 10, new Cell(0, 0), Uses(profile));
        Combatant enemy = Actor("Enemy", "enemy", 10, new Cell(8, 0), []);
        CombatBehaviorController controller = Controller(rules, combat, profile, actor, enemy);
        CombatBehaviorProposal proposal = Required(controller.Propose(
            actor,
            Observation(actor, [
                Choice(actor, profile.Rules[0].Steps[0], enemy),
                Choice(actor, profile.Rules[1].Steps[0], enemy),
            ])));

        CombatBehaviorTrace trace = Required(proposal.Trace);
        Assert.Equal("tactical-diagnostics:blocked_ranged_plan", trace.BehaviorId);
        Assert.EndsWith("behaviors/blocked_ranged_plan.json", trace.BehaviorFile, StringComparison.Ordinal);
        Assert.Equal(1, trace.RuleIndex);
        Assert.EndsWith("classic:melee_attack", trace.ActionId, StringComparison.Ordinal);
        Assert.Equal("selected", trace.Alternatives[1].Status);
        CombatBehaviorAlternativeTrace blocked = trace.Alternatives[0];
        Assert.Equal("blocked", blocked.Status);
        Assert.Equal("$.rules[0].steps[0]", blocked.StepPath);
        Assert.Contains("destination", blocked.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SupportTraceExplainsThresholdAndMissingHealingResource()
    {
        (RuleSet rules, CombatBehaviorProfile profile, Definition combat) = LoadProfile("support_threshold_resource");

        Combatant woundedActor = Actor("Warder", "warder", 10, new Cell(0, 0), Uses(profile));
        Combatant woundedAlly = Actor("Wounded ally", "ally", 3, new Cell(1, 0), []);
        Combatant enemy = Actor("Enemy", "enemy", 10, new Cell(2, 0), []);
        CombatBehaviorController controller = Controller(rules, combat, profile, woundedActor, woundedAlly, enemy);
        CombatBehaviorProposal support = Required(controller.Propose(
            woundedActor,
            Observation(woundedActor, [
                Choice(woundedActor, profile.Rules[0].Steps[0], woundedAlly),
                Choice(woundedActor, profile.Rules[1].Steps[0], enemy),
            ])));

        Assert.EndsWith("classic:spell_heal", support.Trace!.ActionId, StringComparison.Ordinal);
        Assert.Equal(woundedAlly.Id, support.Trace.TargetId);
        Assert.Equal("target.hit_points < target.max_hit_points / 2", support.Trace.Alternatives[0].Guard);
        Assert.True(support.Trace.Alternatives[0].GuardResult == true);
        Assert.Equal(30m, support.Trace.Alternatives[0].PriorityValue);

        // The actor still owns the healing use, but the current legal choice
        // list omits it: this is the observable exhausted-resource case.
        Combatant resourceActor = Actor("Warder", "warder-no-heal", 10, new Cell(0, 0), Uses(profile));
        CombatBehaviorController noResourceController = Controller(rules, combat, profile, resourceActor, woundedAlly, enemy);
        CombatBehaviorProposal noResource = Required(noResourceController.Propose(
            resourceActor,
            Observation(resourceActor, [Choice(resourceActor, profile.Rules[1].Steps[0], enemy)])));

        Assert.EndsWith("classic:melee_attack", noResource.Trace!.ActionId, StringComparison.Ordinal);
        CombatBehaviorAlternativeTrace unavailable = noResource.Trace.Alternatives[0];
        Assert.Equal("unavailable", unavailable.Status);
        Assert.Contains("current legal choice", unavailable.Reason, StringComparison.Ordinal);

        Combatant healthyAlly = Actor("Healthy ally", "healthy", 9, new Cell(1, 0), []);
        Combatant thresholdActor = Actor("Warder", "warder-healthy", 10, new Cell(0, 0), Uses(profile));
        CombatBehaviorController thresholdController = Controller(rules, combat, profile, thresholdActor, healthyAlly, enemy);
        CombatBehaviorProposal threshold = Required(thresholdController.Propose(
            thresholdActor,
            Observation(thresholdActor, [
                Choice(thresholdActor, profile.Rules[0].Steps[0], healthyAlly),
                Choice(thresholdActor, profile.Rules[1].Steps[0], enemy),
            ])));

        Assert.False(threshold.Trace!.Alternatives[0].GuardResult == true);
        Assert.Equal("guarded", threshold.Trace.Alternatives[0].Status);
        Assert.EndsWith("classic:melee_attack", threshold.Trace.ActionId, StringComparison.Ordinal);
    }

    [Fact]
    public void TargetLossTraceIdentifiesTheInterruptedStepAndPlanAbandonment()
    {
        (RuleSet rules, CombatBehaviorProfile profile, Definition combat) = LoadProfile("target_loss");
        Combatant actor = Actor("Actor", "actor", 10, new Cell(0, 0), Uses(profile));
        Combatant enemy = Actor("Enemy", "enemy", 10, new Cell(1, 0), []);
        CombatBehaviorController controller = Controller(rules, combat, profile, actor, enemy);
        CombatBehaviorProposal opening = Required(controller.Propose(
            actor,
            Observation(actor, [Choice(actor, profile.Rules[0].Steps[0], enemy)])));
        Assert.NotNull(opening.Command);
        controller.Commit(opening);

        enemy.Defeated = true;
        CombatBehaviorProposal abandoned = Required(controller.Propose(
            actor,
            Observation(actor, [Choice(actor, profile.Rules[0].Steps[1], enemy, includeTarget: false)])));

        Assert.Null(abandoned.Command);
        Assert.Equal(CombatBehaviorFallback.Next, abandoned.Fallback);
        CombatBehaviorTrace trace = Required(abandoned.Trace);
        Assert.True(trace.Abandoned);
        Assert.Equal(0, trace.RuleIndex);
        Assert.Equal(1, trace.StepIndex);
        Assert.Equal("$.rules[0].steps[1]", trace.StepPath);
        CombatBehaviorAlternativeTrace lostTarget = Assert.Single(trace.Alternatives);
        Assert.Contains("No legal target", lostTarget.Reason, StringComparison.Ordinal);
        Assert.Equal("unavailable", lostTarget.Status);
    }

    [Fact]
    public void FallbackTraceExplainsWhyTheAuthoredUseWasUnavailable()
    {
        (RuleSet rules, CombatBehaviorProfile profile, Definition combat) = LoadProfile("fallback");
        Definition melee = rules.Find(DefinitionTypes.Action, "classic:melee_attack", out _)!;
        Combatant actor = Actor("Actor", "actor", 10, new Cell(0, 0), [new UseOption(melee, "Fallback strike", new Dictionary<string, CompiledExpression>())]);
        Combatant enemy = Actor("Enemy", "enemy", 10, new Cell(1, 0), []);
        CombatBehaviorController controller = Controller(rules, combat, profile, actor, enemy);
        CombatBehaviorProposal proposal = Required(controller.Propose(actor, Observation(actor, [])));

        Assert.Null(proposal.Command);
        Assert.Equal(CombatBehaviorFallback.Flee, proposal.Fallback);
        Assert.Equal("flee", proposal.Trace!.Fallback?.ToString().ToLowerInvariant());
        Assert.Contains("unavailable", proposal.Trace.Alternatives.Single().Reason, StringComparison.OrdinalIgnoreCase);
        Assert.False(proposal.Trace.Abandoned);
    }

    [Fact]
    public void CliTraceDiagnosesAndVerifiesADataOnlyFaultyProfileRepair()
    {
        using TempModules scratch = new();
        CampaignTests.WriteParty(scratch, Rules.ClassicPath);
        string copiedFixture = Path.Combine(scratch.Root, "tactical-diagnostics");
        CopyDirectory(FixtureRoot, copiedFixture);

        JsonElement Before() => RunDiagnosticSimulation(scratch, copiedFixture);

        JsonElement before = Before();
        JsonElement beforeTrace = FindFaultyTrace(before);
        Assert.Equal("tactical-diagnostics", beforeTrace.GetProperty("source").GetProperty("module").GetString());
        Assert.EndsWith("behaviors/faulty_profile.json", beforeTrace.GetProperty("source").GetProperty("file").GetString(), StringComparison.Ordinal);
        Assert.Equal("$.rules[1].steps[0]", beforeTrace.GetProperty("selected").GetProperty("stepPath").GetString());
        JsonElement beforeAlternative = beforeTrace.GetProperty("alternatives").EnumerateArray().First(alternative => alternative.GetProperty("ruleIndex").GetInt32() == 0);
        Assert.Equal("unavailable", beforeAlternative.GetProperty("status").GetString());
        Assert.Contains("unavailable", beforeAlternative.GetProperty("reason").GetString(), StringComparison.OrdinalIgnoreCase);

        string behaviorPath = Path.Combine(copiedFixture, "behaviors", "faulty_profile.json");
        File.WriteAllText(behaviorPath, File.ReadAllText(behaviorPath).Replace("Wrong volley", "Needle volley", StringComparison.Ordinal));

        JsonElement after = Before();
        JsonElement afterTrace = FindFaultyTrace(after);
        Assert.Equal("$.rules[0].steps[0]", afterTrace.GetProperty("source").GetProperty("jsonPath").GetString());
        Assert.Equal(0, afterTrace.GetProperty("selected").GetProperty("ruleIndex").GetInt32());
        Assert.EndsWith("classic:missile_attack", afterTrace.GetProperty("selected").GetProperty("actionId").GetString(), StringComparison.Ordinal);
        Assert.True(afterTrace.GetProperty("sideEffectFree").GetBoolean());
    }

    private static JsonElement RunDiagnosticSimulation(TempModules scratch, string fixture)
    {
        (int code, string output) = CampaignTests.Run(
            scratch,
            "sim", "combat",
            "--module", fixture,
            "--modules", ModuleLibrary,
            "--party", "ada.json",
            "--encounter", "tactical-diagnostics:diagnostic_pair",
            "--combat", "tactical-diagnostics:diagnostic",
            "--seed", "1",
            "--max-rounds", "1",
            "--trace",
            "--json");
        Assert.Equal(GoldboxCli.Ok, code);
        return JsonDocument.Parse(output).RootElement.Clone();
    }

    private static JsonElement FindFaultyTrace(JsonElement simulation)
    {
        return simulation.GetProperty("trace").EnumerateArray()
            .Single(trace => trace.GetProperty("behaviorId").GetString() == "tactical-diagnostics:faulty_profile");
    }

    private static (RuleSet Rules, CombatBehaviorProfile Profile, Definition Combat) LoadProfile(string id)
    {
        ModuleSet set = ModuleLoader.Load(FixtureRoot, [ModuleLibrary]);
        Assert.Empty(set.Diagnostics);
        RuleSet rules = set.Rules!;
        Definition definition = rules.Find(DefinitionTypes.CombatBehavior, $"tactical-diagnostics:{id}", out _)!;
        Definition combat = rules.Find(DefinitionTypes.Combat, "classic:standard", out _)!;
        return (rules, rules.CombatBehaviorOf(definition)!, combat);
    }

    private static T Required<T>(T? value) where T : class
    {
        Assert.NotNull(value);
        return value!;
    }

    private static CombatBehaviorController Controller(
        RuleSet rules,
        Definition combat,
        CombatBehaviorProfile profile,
        params Combatant[] actors)
    {
        CombatBehaviorController controller = new(rules, combat, CombatField.Of(combat));
        Combatant actor = actors[0];
        controller.Assign(actor.Id, profile);
        controller.Register(actors);
        return controller;
    }

    private static Combatant Actor(string name, string id, decimal hitPoints, Cell position, IReadOnlyList<UseOption> uses)
    {
        Creature creature = new(name);
        creature.Track("hit_points").Current = hitPoints;
        creature.Track("hit_points").Max = 10;
        creature.Position = position;
        return new Combatant(name, creature, uses, id)
        {
            Side = id is "actor" or "warder" or "warder-no-heal" or "warder-healthy" ? 0 : id is "ally" or "healthy" ? 0 : 1,
            Controller = CombatControlMode.Automatic,
        };
    }

    private static IReadOnlyList<UseOption> Uses(CombatBehaviorProfile profile)
    {
        return profile.Rules
            .SelectMany(rule => rule.Steps)
            .Select(step => new UseOption(step.Action, step.Name ?? step.Action.Name, step.Parameters, step.Spell, step.FromItem))
            .DistinctBy(use => (use.Action, use.Name, use.Spell))
            .ToList();
    }

    private static CombatActionChoice Choice(Combatant actor, CombatBehaviorStep step, Combatant target, bool includeTarget = true)
    {
        return new CombatActionChoice(
            $"{actor.Id}/choice/{step.Action.QualifiedId}",
            step.Action.QualifiedId,
            step.Name ?? step.Action.Name,
            step.Spell?.QualifiedId,
            new Dictionary<string, int> { ["action"] = 1 },
            includeTarget ? [Target(target)] : [],
            []);
    }

    private static CombatTargetChoice Target(Combatant target)
    {
        TrackValue hitPoints = target.Creature.Track("hit_points");
        return new CombatTargetChoice(
            target.Id,
            target.Name,
            target.Side,
            target.Defeated,
            target.Escaped,
            target.Creature.Position,
            hitPoints.Current,
            hitPoints.Max);
    }

    private static CombatObservation Observation(Combatant actor, IReadOnlyList<CombatActionChoice> actions)
    {
        CombatDecision decision = new(
            $"decision:{actor.Id}",
            CombatDecisionKind.Action,
            actor.Id,
            1,
            actions,
            [],
            true);
        return new CombatObservation(CombatPhase.AwaitingAction, 1, actor.Id, decision, null, null, [], []);
    }

    private static void CopyDirectory(string source, string destination)
    {
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }
}
