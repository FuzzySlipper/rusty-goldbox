using System.Text.Json;
using RustyGoldbox.Core.Characters;
using RustyGoldbox.Core.Definitions;
using RustyGoldbox.Core.Rules;

namespace RustyGoldbox.Core.Campaigns;

public sealed partial class CampaignRunner
{
    private Definition? SpellReward(Definition evt, DiceRoller dice, List<PlayFact> facts)
    {
        if (evt.Json.TryGetProperty("text", out JsonElement text))
        {
            facts.Add(new TextFact(text.GetString()!));
        }

        if (!Members(evt, out List<int> members, facts))
        {
            return null;
        }

        foreach (int member in members)
        {
            Character character = _state.Party[member];
            Dictionary<Definition, int> classes = character.ToCreature().ClassLevels;
            List<(Definition Spell, int Level)> eligible = CharacterRules.CastableSpells(_rules, character)
                .Select(spell => (Spell: spell, Level: SpellLevel(spell, classes)))
                .Where(candidate => candidate.Level is not null)
                .Select(candidate => (candidate.Spell, candidate.Level!.Value))
                .ToList();
            int? highest = eligible.Count == 0 ? null : eligible.Max(candidate => candidate.Level);
            // Filter known spells within the highest castable level: a completed
            // list does not turn this reward into a lower-level spell.
            List<(Definition Spell, int Level)> candidates = eligible
                .Where(candidate => candidate.Level == highest && !character.Spells.Contains(candidate.Spell))
                .ToList();

            if (candidates.Count == 0)
            {
                facts.Add(new SpellRewardFact(
                    member + 1,
                    character.Name,
                    null,
                    null,
                    false,
                    "no unknown castable spell remains at this member's highest spell level."));
                continue;
            }

            int before = dice.Rolls.Count;
            int selected = checked((int)dice.Roll(1, candidates.Count)) - 1;
            (Definition spell, int level) = candidates[selected];
            character.Spells.Add(spell);
            facts.Add(new SpellRewardFact(member + 1, character.Name, spell, level, true, null)
            {
                Rolls = dice.Rolls.Skip(before).ToList(),
            });
        }

        return Next(evt, "$.next");
    }

    private int? SpellLevel(Definition spell, IReadOnlyDictionary<Definition, int> classes)
    {
        if (!spell.Json.TryGetProperty("lists", out JsonElement lists))
        {
            return null;
        }

        int? highest = null;
        foreach (JsonProperty entry in lists.EnumerateObject())
        {
            Definition listing = _rules.Reference(spell, $"$.lists.{entry.Name}");
            if (classes.ContainsKey(listing))
            {
                int level = entry.Value.GetInt32();
                highest = highest is int current ? Math.Max(current, level) : level;
            }
        }

        return highest;
    }
}
