using RustyGoldbox.Core.Definitions;
using RustyGoldbox.Core.Expressions;
using RustyGoldbox.Core.Rules;

namespace RustyGoldbox.Core.Characters;

/// <summary>One stat on a sheet: its value, or why it couldn't be computed.</summary>
public sealed record SheetStat(string Id, string Name, string Kind, Value? Value, string? Problem);

/// <summary>A track on a sheet: current and maximum, or why the maximum couldn't be computed.</summary>
public sealed record SheetTrack(Definition Track, decimal? Current, decimal? Max, string? Problem);

/// <summary>A character's computed stats, in the rule set's definition order.</summary>
public static class CharacterSheet
{
    public static List<SheetTrack> Tracks(RuleSet rules, Character character)
    {
        Creature creature = character.ToCreature();
        Evaluator evaluator = new(rules, null);
        List<SheetTrack> tracks = [];
        foreach (Definition track in rules.Tracks.Values)
        {
            decimal? current = creature.Track(track.Id).Current;
            try
            {
                tracks.Add(new SheetTrack(track, current, evaluator.TrackMax(creature, track), null));
            }
            catch (ExpressionException exception)
            {
                tracks.Add(new SheetTrack(track, current, null, exception.Message));
            }
        }

        return tracks;
    }

    public static List<SheetStat> Stats(RuleSet rules, Character character)
    {
        Creature creature = character.ToCreature();
        Evaluator evaluator = new(rules, null);
        List<SheetStat> stats = [];
        // Attributes in the character's order (the creation order), then derived values in definition order.
        IEnumerable<Stat> ordered = character.Attributes.Keys.Select(id => rules.Stats[id])
            .Concat(rules.Stats.Values.Where(stat => !stat.IsAttribute).OrderBy(stat => rules.Definitions.IndexOf(stat.Definition)));
        foreach (Stat stat in ordered)
        {
            try
            {
                stats.Add(new SheetStat(stat.Id, stat.Definition.Name, stat.Definition.Type.Name, evaluator.Stat(creature, stat.Id), null));
            }
            catch (ExpressionException exception)
            {
                stats.Add(new SheetStat(stat.Id, stat.Definition.Name, stat.Definition.Type.Name, null, exception.Message));
            }
        }

        return stats;
    }
}
