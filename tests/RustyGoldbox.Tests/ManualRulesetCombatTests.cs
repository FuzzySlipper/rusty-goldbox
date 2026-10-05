using System.Text.Json.Nodes;
using Rusty.Engine.Testing;
using RustyGoldbox.Core.Characters;
using RustyGoldbox.Core.Combat;
using RustyGoldbox.Core.Definitions;
using RustyGoldbox.Core.Modules;
using RustyGoldbox.Core.Rules;

namespace RustyGoldbox.Tests;

/// <summary>A manual fighter plays a whole fight in rulesets of different shapes, through the same commands the Game and CLI send.</summary>
public sealed class ManualRulesetCombatTests
{
    [Fact]
    public void AThreeActionFighterSpendsItsThreeActionsAcrossDecisions()
    {
        List<CombatObservation> decisions = PlayManualFight(
            "three-action",
            ["--class", "fighter", "--race", "human", "--feature", "skilled_human,warrior,sudden_charge", "--boosts", "str,con,str,dex,str,str,con,dex,wis"],
            ["longsword", "chain_mail", "steel_shield"],
            "wolf_pack");
        List<CombatObservation> turn = decisions.Where(observation => observation.PendingDecision!.Round == decisions[0].PendingDecision!.Round).ToList();

        // Each action spends part of the turn's three, and the same fighter decides again.
        Assert.True(turn.Count >= 2, $"The fighter decided {turn.Count} time(s) in its first turn.");
        int[] actions = turn.Select(observation => Budget(observation, "action")).ToArray();
        Assert.Equal(3, actions[0]);
        Assert.Equal(actions.Length, actions.Distinct().Count());
        Assert.True(actions.Zip(actions.Skip(1)).All(pair => pair.Second < pair.First));
    }

    [Fact]
    public void AFifthEditionFighterKeepsItsBonusActionAfterItsAction()
    {
        List<CombatObservation> decisions = PlayManualFight(
            "fifth-srd",
            ["--class", "fighter", "--race", "human", "--priority", "str,con,dex,wis,cha,int", "--feature", "soldier,savage_attacker,defense"],
            ["longsword", "chain_mail", "shield"],
            "goblin_ambush");

        // One action a turn: once it is spent, the fighter still decides with
        // its bonus action left, and nothing it is offered costs another action.
        Assert.All(decisions, observation => Assert.True(Budget(observation, "action") <= 1));
        List<CombatObservation> spent = decisions.Where(observation => Budget(observation, "action") == 0).ToList();
        Assert.NotEmpty(spent);
        Assert.All(spent, observation =>
        {
            Assert.Equal(1, Budget(observation, "bonus"));
            Assert.DoesNotContain(observation.PendingDecision!.Actions, action => action.Cost.GetValueOrDefault("action") > 0);
        });
    }

    /// <summary>
    /// Makes one manual fighter, fights the encounter to its end choosing the
    /// first legal attack on a foe each time, and returns what the fighter saw
    /// at each of its action decisions.
    /// </summary>
    private static List<CombatObservation> PlayManualFight(string module, string[] creation, string[] equipment, string encounterId)
    {
        using TempModules scratch = new();
        string path = Path.Combine(Rules.RepositoryRoot, "modules", module);
        string transcript = CliTranscript.Run(scratch.Root,
            ["character", "new", "--module", path, "--name", "Bram", .. creation, "--out", "bram.json"]);
        Assert.Contains("[exit 0]", transcript, StringComparison.Ordinal);
        string file = Path.Combine(scratch.Root, "bram.json");
        JsonNode node = JsonNode.Parse(File.ReadAllText(file))!;
        node["equipment"] = new JsonArray(equipment.Select(item => (JsonNode)$"{module}:{item}"!).ToArray());
        File.WriteAllText(file, node.ToJsonString());

        ModuleSet set = ModuleLoader.Load(path, [Path.Combine(Rules.RepositoryRoot, "modules")]);
        Assert.Empty(set.Diagnostics);
        RuleSet rules = set.Rules!;
        List<ModuleDiagnostic> problems = [];
        Character character = CharacterFile.Read(file, set, problems)!;
        Assert.Empty(problems);
        Definition encounter = rules.Find(DefinitionTypes.Encounter, encounterId, out _)!;
        Definition combat = rules.OfType(DefinitionTypes.Combat).Single();

        List<CombatObservation> decisions = [];
        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            DiceRoller dice = new(engine.Random, 3, $"manual-{module}");
            Combatant hero = Combatant.FromCharacter(rules, character);
            CombatRunner runner = CombatRunner.Create(rules, combat,
            [
                new CombatSide("Party", [hero]),
                new CombatSide(encounter.Name, Encounters.Spawn(rules, encounter, dice)),
            ], dice, encounter);
            Assert.True(runner.SetController(hero.Id, CombatControlMode.Manual));

            CombatObservation observation = runner.Start();
            for (int step = 0; observation.Phase != CombatPhase.Ended; step++)
            {
                Assert.True(step < 500, "The fight didn't end.");
                CombatDecision decision = Assert.IsType<CombatDecision>(observation.PendingDecision);
                Assert.Equal(hero.Id, decision.ActorId);
                if (decision.Kind == CombatDecisionKind.Action)
                {
                    decisions.Add(observation);
                }

                CombatCommandResult result = runner.Submit(Choose(decision, hero.Id));
                Assert.True(result.Accepted, result.Reason);
                observation = result.Observation;
            }
        });

        Assert.NotEmpty(decisions);
        return decisions;
    }

    /// <summary>The first legal attack on a foe, else End turn; optional choices are declined and initiative takes the first option.</summary>
    private static CombatCommand Choose(CombatDecision decision, string heroId)
    {
        if (decision.Kind != CombatDecisionKind.Action)
        {
            return decision.Kind == CombatDecisionKind.Initiative
                ? new CombatCommand.Decide(decision.Id, decision.Options![0].Id)
                : new CombatCommand.Decide(decision.Id);
        }

        foreach (CombatActionChoice action in decision.Actions.Where(action => action.TargetMode is "one" or "portions"))
        {
            foreach (CombatTargetChoice foe in action.Targets.Where(target => target.Side != 0 && !target.Defeated && !target.Escaped))
            {
                // An action that moves first needs a path ending by this foe.
                CombatMoveChoice? move = action.Moves.FirstOrDefault(candidate => candidate.TargetIds?.Contains(foe.Id) == true);
                if (action.Moves.Count == 0 || move is not null)
                {
                    // Every portion of a multi-portion attack goes to the same foe.
                    string[] targets = Enumerable.Repeat(foe.Id, Math.Max(1, action.PortionCount ?? 1)).ToArray();
                    return new CombatCommand.UseAction(heroId, action.Id, targets, move?.Path);
                }
            }
        }

        return new CombatCommand.EndTurn(heroId);
    }

    private static int Budget(CombatObservation observation, string budget)
    {
        CombatantObservation actor = observation.Combatants.Single(member => member.Id == observation.PendingDecision!.ActorId);
        return actor.Budget[budget];
    }
}
