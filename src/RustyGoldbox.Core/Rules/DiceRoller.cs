using Rusty.Engine;

namespace RustyGoldbox.Core.Rules;

/// <summary>
/// One roll of a set of dice. <see cref="Kept"/> is how many of the highest
/// faces count; with <see cref="AtLeast"/>, the total is how many faces reach
/// it, less how many are <see cref="Cancel"/> or lower. With
/// <see cref="Again"/>, every face of that or more rolled another die, so
/// <see cref="Faces"/> holds more than <see cref="Count"/>.
/// </summary>
public sealed record DiceRoll(int Count, int Sides, IReadOnlyList<int> Faces, int Kept, int? AtLeast = null, int? Again = null, int? Cancel = null)
{
    public long Total => AtLeast is int threshold
        ? Faces.Count(face => face >= threshold) - (Cancel is int cancel ? Faces.Count(face => face <= cancel) : 0)
        : Again is null ? Faces.OrderDescending().Take(Kept).Sum(face => (long)face) : Faces.Sum(face => (long)face);

    public override string ToString()
    {
        string mode = AtLeast is int threshold ? $" count {threshold}+" : Kept == Count || Again is not null ? "" : $" keep {Kept}";
        mode += (Again is int again ? $" again {again}+" : "") + (Cancel is int cancel ? $" cancel {cancel}-" : "");
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

    /// <summary>
    /// Rolls <paramref name="count"/> dice where each face of <paramref name="again"/>
    /// or more rolls another (needs 2 or more), and adds every face.
    /// </summary>
    public long Explode(int count, int sides, int again) => Roll(count, sides, count, null, again, null);

    /// <summary>
    /// A dice pool: counts the faces of <paramref name="atLeast"/> or more, less those of
    /// <paramref name="cancel"/> or lower; faces of <paramref name="again"/> or more roll another die.
    /// </summary>
    public long Pool(int count, int sides, int atLeast, int? again, int? cancel) => Roll(count, sides, count, atLeast, again, cancel);

    private long Roll(int count, int sides, int keep, int? atLeast, int? again = null, int? cancel = null)
    {
        List<int> faces = [];
        int left = count;
        while (left > 0)
        {
            int face = (int)random.NextBoundedU32(new ScopedRngBoundedRequest(stream, (uint)sides)).Value + 1;
            faces.Add(face);
            left--;
            if (again is int threshold && face >= threshold)
            {
                left++;
            }
        }

        DiceRoll roll = new(count, sides, faces, keep, atLeast, again, cancel);
        _rolls.Add(roll);
        return roll.Total;
    }
}
