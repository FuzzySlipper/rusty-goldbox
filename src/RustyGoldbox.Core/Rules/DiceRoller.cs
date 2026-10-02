using Rusty.Engine;

namespace RustyGoldbox.Core.Rules;

/// <summary>
/// One roll of a set of dice. <see cref="Kept"/> is how many of the highest
/// faces count; with <see cref="AtLeast"/>, the total is how many faces reach it.
/// </summary>
public sealed record DiceRoll(int Count, int Sides, IReadOnlyList<int> Faces, int Kept, int? AtLeast = null)
{
    public long Total => AtLeast is int threshold
        ? Faces.Count(face => face >= threshold)
        : Faces.OrderDescending().Take(Kept).Sum(face => (long)face);

    public override string ToString()
    {
        string mode = AtLeast is int threshold ? $" count {threshold}+" : Kept == Count ? "" : $" keep {Kept}";
        string faces = AtLeast is null ? string.Join("+", Faces) : string.Join(",", Faces);
        return $"{Count}d{Sides}{mode}: {faces} = {Total}";
    }
}

/// <summary>
/// Rolls dice from an Engine random stream and records every roll. Engine
/// calls are callback-confined, so use it only inside the host callback that
/// supplied <paramref name="random"/>.
/// </summary>
public sealed class DiceRoller(IRandomService random, Rng stream)
{
    private readonly List<DiceRoll> _rolls = [];

    public IReadOnlyList<DiceRoll> Rolls => _rolls;

    public long Roll(int count, int sides) => Roll(count, sides, count);

    /// <summary>Rolls <paramref name="count"/> dice and adds the highest <paramref name="keep"/>.</summary>
    public long Roll(int count, int sides, int keep) => Roll(count, sides, keep, null);

    /// <summary>Rolls <paramref name="count"/> dice and counts the faces of <paramref name="atLeast"/> or more.</summary>
    public long Count(int count, int sides, int atLeast) => Roll(count, sides, count, atLeast);

    private long Roll(int count, int sides, int keep, int? atLeast)
    {
        int[] faces = new int[count];
        for (int i = 0; i < count; i++)
        {
            faces[i] = (int)random.NextBoundedU32(new ScopedRngBoundedRequest(stream, (uint)sides)).Value + 1;
        }

        DiceRoll roll = new(count, sides, faces, keep, atLeast);
        _rolls.Add(roll);
        return roll.Total;
    }
}
