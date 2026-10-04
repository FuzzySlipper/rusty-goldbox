using RustyGoldbox.Core.Definitions;
using RustyGoldbox.Core.Modules;
using RustyGoldbox.Core.Rules;
using static RustyGoldbox.Tests.TempModules;

namespace RustyGoldbox.Tests;

public sealed class BehaviorDefinitionTests
{
    [Fact]
    public void LoadsReusableProfileWithDeterministicPolicyAndDiceActionParameter()
    {
        using TempModules modules = new();
        string tactics = modules.Module("tactics", "extension", requires: Require("classic", "*"));
        modules.Write("tactics/skirmisher.json", ProfileJson());

        ModuleSet set = ModuleLoader.Load(tactics, [modules.Root, Path.Combine(Rules.RepositoryRoot, "modules")]);

        Assert.Empty(set.Diagnostics);
        RuleSet rules = set.Rules!;
        Definition definition = Assert.Single(rules.OfType(DefinitionTypes.CombatBehavior));
        CombatBehaviorProfile profile = Assert.Single(rules.CombatBehaviors.Values);
        CombatBehaviorRule rule = Assert.Single(profile.Rules);
        CombatBehaviorStep step = Assert.Single(rule.Steps);

        Assert.Equal("tactics:skirmisher", definition.QualifiedId);
        Assert.Equal("classic:melee_attack", step.Action.QualifiedId);
        Assert.Equal(CombatBehaviorCommitment.Plan, rule.Commitment);
        Assert.Equal(CombatBehaviorTarget.Enemy, step.Target);
        Assert.Equal("outside", step.Destination!.Kind);
        Assert.Equal("behavior.safe_distance", step.Destination.Distance.Text);
        Assert.IsType<RustyGoldbox.Core.Expressions.DiceLiteral>(step.Parameters["damage"].Root);
    }

    [Fact]
    public void RejectsRandomPolicyExpressionsAndUnknownParametersWithSourcePaths()
    {
        using TempModules modules = new();
        string tactics = modules.Module("tactics", "extension", requires: Require("classic", "*"));
        modules.Write("tactics/random.json", ProfileJson("random_policy", "1d6 > 2"));
        modules.Write("tactics/dynamic.json", ProfileJson("dynamic_policy", "roll(1, 6) > 2"));
        modules.Write("tactics/unknown.json", ProfileJson("unknown_policy", "behavior.missing > 0"));

        ModuleSet set = ModuleLoader.Load(tactics, [modules.Root, Path.Combine(Rules.RepositoryRoot, "modules")]);

        Assert.Contains(set.Diagnostics, diagnostic =>
            diagnostic.Rule == "behavior.random"
            && diagnostic.File!.EndsWith("random.json", StringComparison.Ordinal)
            && diagnostic.JsonPath == "$.rules[0].when");
        Assert.Contains(set.Diagnostics, diagnostic =>
            diagnostic.Rule == "behavior.random"
            && diagnostic.File!.EndsWith("dynamic.json", StringComparison.Ordinal)
            && diagnostic.JsonPath == "$.rules[0].when");
        Assert.Contains(set.Diagnostics, diagnostic =>
            diagnostic.Rule == "expression.type"
            && diagnostic.File!.EndsWith("unknown.json", StringComparison.Ordinal)
            && diagnostic.JsonPath == "$.rules[0].when"
            && diagnostic.Message.Contains("not a parameter", StringComparison.Ordinal));
        Assert.NotEmpty(set.Diagnostics);
    }

    [Fact]
    public void ResolvesBehaviorParameterDependenciesAndRejectsCyclesAtLoad()
    {
        using TempModules modules = new();
        string tactics = modules.Module("tactics", "extension", requires: Require("classic", "*"));
        modules.Write("tactics/dependent.json", """
            {
              "type": "combat-behavior",
              "id": "dependent",
              "name": "Dependent",
              "parameters": { "near": 1, "far": "behavior.near + 2" },
              "rules": [
                { "steps": [ { "action": { "action": "classic:melee_attack", "damage": "1d6" }, "target": "enemy" } ] }
              ]
            }
            """);
        modules.Write("tactics/cyclic.json", """
            {
              "type": "combat-behavior",
              "id": "cyclic",
              "name": "Cyclic",
              "parameters": { "first": "behavior.second + 1", "second": "behavior.first + 1" },
              "rules": [
                { "steps": [ { "action": { "action": "classic:melee_attack", "damage": "1d6" }, "target": "enemy" } ] }
              ]
            }
            """);

        ModuleSet set = ModuleLoader.Load(tactics, [modules.Root, Path.Combine(Rules.RepositoryRoot, "modules")]);

        Assert.Contains(set.Diagnostics, diagnostic =>
            diagnostic.Rule == "behavior.parameter"
            && diagnostic.File!.EndsWith("cyclic.json", StringComparison.Ordinal)
            && diagnostic.JsonPath == "$.parameters.first"
            && diagnostic.Message.Contains("cycle", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(set.Diagnostics, diagnostic => diagnostic.File!.EndsWith("dependent.json", StringComparison.Ordinal));
        Assert.Contains(set.Rules!.CombatBehaviors.Values, profile => profile.Definition.Id == "dependent");
    }

    [Fact]
    public void RejectsStepsWhoseSpellHasNoEffectOrUsesAnotherAction()
    {
        using TempModules modules = new();
        string tactics = modules.Module("tactics", "extension", requires: Require("classic", "*"));
        modules.Write("tactics/empty_spell.json", """
            {
              "type": "spell",
              "id": "empty_spell",
              "name": "Empty spell",
              "lists": { "classic:magic_user": 1 },
              "range": "touch",
              "duration": "instant",
              "area": "one",
              "casting_time": "1 action",
              "description": "A spell without a combat effect."
            }
            """);
        modules.Write("tactics/no_effect.json", ProfileWithSpell("no_effect", "empty_spell"));
        modules.Write("tactics/mismatch.json", ProfileWithSpell("mismatch", "classic:cure_light_wounds"));

        ModuleSet set = ModuleLoader.Load(tactics, [modules.Root, Path.Combine(Rules.RepositoryRoot, "modules")]);

        Assert.Contains(set.Diagnostics, diagnostic =>
            diagnostic.Rule == "behavior.spell"
            && diagnostic.File!.EndsWith("no_effect.json", StringComparison.Ordinal)
            && diagnostic.JsonPath == "$.rules[0].steps[0].spell"
            && diagnostic.Message.Contains("has no combat effect", StringComparison.Ordinal));
        Assert.Contains(set.Diagnostics, diagnostic =>
            diagnostic.Rule == "behavior.spell"
            && diagnostic.File!.EndsWith("mismatch.json", StringComparison.Ordinal)
            && diagnostic.JsonPath == "$.rules[0].steps[0].spell"
            && diagnostic.Message.Contains("uses action", StringComparison.Ordinal));
    }

    private static string ProfileJson(string id = "skirmisher", string when = "self.hit_points > 0") => $$"""
        {
          "type": "combat-behavior",
          "id": "{{id}}",
          "name": "Skirmisher",
          "parameters": { "safe_distance": 3 },
          "fallback": "end-turn",
          "rules": [
            {
              "when": "{{when}}",
              "priority": 10,
              "commit": "plan",
              "steps": [
                {
                  "destination": { "kind": "outside", "distance": "behavior.safe_distance" },
                  "action": { "action": "classic:melee_attack", "damage": "1d6" },
                  "target": "enemy"
                }
              ],
              "fallback": "next"
            }
          ]
        }
        """;

    private static string ProfileWithSpell(string id, string spell) => $$"""
        {
          "type": "combat-behavior",
          "id": "{{id}}",
          "name": "Spell test",
          "rules": [
            {
              "steps": [
                {
                  "action": { "action": "classic:melee_attack", "damage": "1d6" },
                  "spell": "{{spell}}",
                  "target": "enemy"
                }
              ]
            }
          ]
        }
        """;
}
