using Rusty.Engine;

namespace RustyGoldbox.Core.Rules;

/// <summary>One roll of a set of dice.</summary>
public sealed record DiceRoll(int Count, int Sides, IReadOnlyList<int> Faces)
{
    public long Total => Faces.Sum(face => (long)face);

    public override string ToString() => $"{Count}d{Sides}: {string.Join("+", Faces)} = {Total}";
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

    public long Roll(int count, int sides)
    {
        int[] faces = new int[count];
        for (int i = 0; i < count; i++)
        {
            faces[i] = (int)random.NextBoundedU32(new ScopedRngBoundedRequest(stream, (uint)sides)).Value + 1;
        }

        DiceRoll roll = new(count, sides, faces);
        _rolls.Add(roll);
        return roll.Total;
    }
}
