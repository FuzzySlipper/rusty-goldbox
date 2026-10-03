using System.Text.Json;
using RustyGoldbox.Core.Characters;
using RustyGoldbox.Core.Definitions;
using RustyGoldbox.Core.Rules;

namespace RustyGoldbox.Core.Campaigns;

public sealed partial class CampaignRunner
{
    private Definition? Rest(Definition evt, DiceRoller dice, List<PlayFact> facts)
    {
        JsonElement json = evt.Json;
        facts.Add(new TextFact(json.GetProperty("text").GetString()!));
        if (json.TryGetProperty("resting", out _))
        {
            Definition policy = _rules.Reference(evt, "$.resting");
            decimal days = policy.Json.GetProperty("unit").GetString() switch
            {
                "hours" => 1m / 24,
                "rounds" => _rules.Reference(policy, "$.combat").Json.GetProperty("round_seconds").GetDecimal() / 86400,
                _ => 1,
            };
            int periods = json.GetProperty("periods").GetInt32();
            for (int period = 0; period < periods; period++)
            {
                _state.ElapsedDays = Located(evt, "$.periods", () => checked(_state.ElapsedDays + days));
                int before = dice.Rolls.Count;
                bool interrupted = json.TryGetProperty("wandering", out _) && Located(evt, "$.wandering.when", () => Evaluate(evt, "$.wandering.when", dice).Boolean);
                facts.Add(new TextFact($"Rest period {period + 1}/{periods} ({policy.Json.GetProperty("unit").GetString()}); campaign time: {Fact(_state.ElapsedDays)} days{(interrupted ? "; interrupted by a wandering encounter" : "")}.") { Rolls = dice.Rolls.Skip(before).ToList() });
                if (interrupted)
                {
                    // Use the same campaign combat event and outcome chains; the rest's next is not taken.
                    return _rules.Reference(evt, "$.wandering.event");
                }

                Recover(policy, dice, facts);
            }
        }

        Evaluator evaluator = new(_rules, null);
        for (int index = 0; index < json.GetProperty("tracks").GetArrayLength(); index++)
        {
            Definition track = _rules.Reference(evt, $"$.tracks[{index}]");
            foreach (Character character in _state.Party)
            {
                if (Located(track, "$", () => evaluator.KnownTrackMax(character.ToCreature(), track)) is decimal max)
                {
                    character.Tracks[track.Id].Current = max;
                }
            }
        }

        if (json.TryGetProperty("prepare", out JsonElement prepare) && prepare.GetBoolean())
        {
            foreach (Character character in _state.Party)
            {
                character.Prepared = null;
            }
        }

        return Next(evt, "$.next");
    }

    private void Recover(Definition policy, DiceRoller dice, List<PlayFact> facts)
    {
        Evaluator evaluator = new(_rules, dice);
        JsonElement restore = policy.Json.GetProperty("restore");
        for (int index = 0; index < restore.GetArrayLength(); index++)
        {
            string path = $"$.restore[{index}]";
            Definition track = _rules.Reference(policy, $"{path}.track");
            foreach (Character character in _state.Party)
            {
                int before = dice.Rolls.Count;
                Creature creature = character.ToCreature();
                decimal healed = Located(policy, $"{path}.amount", () =>
                {
                    decimal amount = evaluator.Evaluate(_rules.Expression(policy, $"{path}.amount"), creature, null).Number;
                    return TrackOperations.Heal(evaluator, creature, track, amount);
                });
                character.Tracks[track.Id] = creature.Track(track.Id);
                facts.Add(new TextFact($"{character.Name} recovers {Fact(healed)} {track.Name.ToLowerInvariant()}.") { Rolls = dice.Rolls.Skip(before).ToList() });
            }
        }
    }
}
