using RustyGoldbox.Core.Definitions;

namespace RustyGoldbox.Core.Rules;

internal static class TrackOperations
{
    public static decimal Damage(Evaluator evaluator, Creature creature, Definition track, decimal amount)
    {
        TrackValue value = creature.Track(track.Id);
        decimal current = value.Current ?? 0;
        decimal lowered = current - Math.Max(0, amount);
        if (evaluator.TrackMin(creature, track) is decimal floor && lowered < floor)
        {
            lowered = Math.Min(current, floor);
        }

        value.Current = lowered;
        return current - lowered;
    }

    public static decimal Heal(Evaluator evaluator, Creature creature, Definition track, decimal amount)
    {
        TrackValue value = creature.Track(track.Id);
        decimal current = value.Current ?? 0;
        decimal raised = Math.Max(current, Math.Min(evaluator.TrackRestoreCap(creature, track), checked(current + Math.Max(0, amount))));
        value.Current = raised;
        return raised - current;
    }
}
