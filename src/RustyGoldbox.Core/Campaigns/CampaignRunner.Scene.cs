using System.Text.Json;
using RustyGoldbox.Core.Characters;
using RustyGoldbox.Core.Definitions;
using RustyGoldbox.Core.Expressions;
using RustyGoldbox.Core.Rules;

namespace RustyGoldbox.Core.Campaigns;

public sealed partial class CampaignRunner
{
    private Definition? RunSceneCheck(Definition evt, DiceRoller dice, List<PlayFact> facts)
    {
        int member = evt.Json.GetProperty("member").GetInt32();
        if (member < 1 || member > _state.Party.Count)
        {
            facts.Add(new RefusedFact($"{member} is not an active party member; members are 1 to {_state.Party.Count}."));
            return null;
        }

        Character character = _state.Party[member - 1];
        Creature creature = character.ToCreature();
        Definition check = _rules.Reference(evt, "$.check");
        Evaluator evaluator = new(_rules, dice);
        int before = dice.Rolls.Count;
        decimal modifier = evt.Json.TryGetProperty("modifier", out _)
            ? Located(evt, "$.modifier", () => evaluator.Evaluate(
                _rules.Expression(evt, "$.modifier"),
                SceneScope(creature))).Number
            : 0;
        CheckResult result = Located(evt, "$.check", () => evaluator.Check(check, creature, null, modifier));
        if (result.Success && check.Json.TryGetProperty("skill", out JsonElement skill))
        {
            CharacterRules.MarkSkillUse(_rules, character, skill.GetString()!, []);
        }

        facts.Add(new SceneCheckFact(member, character.Name, check.Name, result)
        {
            Rolls = dice.Rolls.Skip(before).ToList(),
        });
        return Next(evt, result.Success ? "$.on_success" : "$.on_failure");
    }

    private Definition? RunSceneEffect(Definition evt, DiceRoller dice, List<PlayFact> facts)
    {
        IEnumerable<(Character Character, int Member)> targets;
        if (evt.Json.TryGetProperty("member", out JsonElement memberValue))
        {
            int member = memberValue.GetInt32();
            if (member < 1 || member > _state.Party.Count)
            {
                facts.Add(new RefusedFact($"{member} is not an active party member; members are 1 to {_state.Party.Count}."));
                return null;
            }

            targets = [( _state.Party[member - 1], member )];
        }
        else
        {
            targets = _state.Party.Select((character, index) => (character, index + 1)).ToList();
        }

        JsonElement operations = evt.Json.GetProperty("operations");
        foreach ((Character character, int member) in targets)
        {
            Creature creature = character.ToCreature();
            Evaluator evaluator = new(_rules, dice);
            for (int index = 0; index < operations.GetArrayLength(); index++)
            {
                JsonElement operation = operations[index];
                string path = $"$.operations[{index}]";
                string op = operation.GetProperty("op").GetString()!;
                int before = dice.Rolls.Count;
                switch (op)
                {
                    case "damage":
                    {
                        Definition track = _rules.Reference(evt, $"{path}.track");
                        decimal amount = Located(evt, $"{path}.amount", () => Math.Max(0, evaluator.Evaluate(
                            _rules.Expression(evt, $"{path}.amount"), SceneScope(creature)).Number));
                        decimal applied = Located(evt, path, () => TrackOperations.Damage(evaluator, creature, track, amount));
                        facts.Add(new SceneDamageFact(member, character.Name, track, applied, creature.Track(track.Id).Current ?? 0)
                        {
                            Rolls = dice.Rolls.Skip(before).ToList(),
                        });
                        break;
                    }
                    case "heal":
                    {
                        Definition track = _rules.Reference(evt, $"{path}.track");
                        decimal amount = Located(evt, $"{path}.amount", () => Math.Max(0, evaluator.Evaluate(
                            _rules.Expression(evt, $"{path}.amount"), SceneScope(creature)).Number));
                        decimal applied = Located(evt, path, () => TrackOperations.Heal(evaluator, creature, track, amount));
                        facts.Add(new SceneHealFact(member, character.Name, track, applied, creature.Track(track.Id).Current ?? 0)
                        {
                            Rolls = dice.Rolls.Skip(before).ToList(),
                        });
                        break;
                    }
                    case "apply_condition":
                    {
                        Definition condition = _rules.Reference(evt, $"{path}.condition");
                        if (!creature.Conditions.Contains(condition))
                        {
                            creature.Conditions.Add(condition);
                        }

                        facts.Add(new SceneConditionFact(member, character.Name, condition.Name, true)
                        {
                            Rolls = dice.Rolls.Skip(before).ToList(),
                        });
                        break;
                    }
                    case "remove_condition":
                    {
                        Definition condition = _rules.Reference(evt, $"{path}.condition");
                        if (creature.Conditions.Remove(condition))
                        {
                            facts.Add(new SceneConditionFact(member, character.Name, condition.Name, false)
                            {
                                Rolls = dice.Rolls.Skip(before).ToList(),
                            });
                        }

                        break;
                    }
                }
            }

            SyncCharacter(character, creature);
        }

        return Next(evt, "$.next");
    }

    private Scope SceneScope(Creature creature)
    {
        return new Scope(
            creature,
            null,
            Variables: _state.Variables,
            PartyItems: _state.CarriedItems,
            AreaVariables: _state.ValuesFor(_state.Area),
            PartySize: _state.Party.Count);
    }

    private static void SyncCharacter(Character character, Creature creature)
    {
        character.Tracks.Clear();
        foreach ((string id, TrackValue value) in creature.Tracks)
        {
            character.Tracks[id] = new TrackValue { Current = value.Current, Max = value.Max };
        }

        character.Conditions.Clear();
        character.Conditions.AddRange(creature.Conditions);
    }
}
