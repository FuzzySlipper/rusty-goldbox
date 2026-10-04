using Rusty.Engine;

namespace RustyGoldbox.Core.Rules;

/// <summary>
/// One roll of a set of dice. <see cref="Kept"/> is how many of the highest
/// faces count; with <see cref="AtLeast"/>, the total is how many faces reach
/// it, less how many are <see cref="Cancel"/> or lower. With
/// <see cref="Again"/>, every face of that or more rolled another die, so
/// <see cref="Faces"/> holds more than <see cref="Count"/>.
/// </summary>
/// <param name="Fudge">Fudge dice: each face is -1, 0 or +1.</param>
public sealed record DiceRoll(int Count, int Sides, IReadOnlyList<int> Faces, int Kept, int? AtLeast = null, int? Again = null, int? Cancel = null, bool Fudge = false)
{
    public long Total => AtLeast is int threshold
        ? Faces.Count(face => face >= threshold) - (Cancel is int cancel ? Faces.Count(face => face <= cancel) : 0)
        : Again is null ? Faces.OrderDescending().Take(Kept).Sum(face => (long)face) : Faces.Sum(face => (long)face);

    public override string ToString()
    {
        if (Fudge)
        {
            return $"{Count}dF: {string.Join(" ", Faces.Select(face => face > 0 ? "+" : face < 0 ? "-" : "0"))} = {Total}";
        }

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
public sealed class DiceRoller
{
    private readonly IRandomService _random;
    private readonly Rng? _stream;
    private readonly ulong? _seed;
    private readonly string? _scope;
    private long _nextKey;
    private readonly List<DiceRoll> _rolls = [];

    public DiceRoller(IRandomService random, Rng stream)
    {
        _random = random;
        _stream = stream;
    }

    /// <summary>
    /// Creates a callback-confined roller whose individual draws are keyed by
    /// their persisted ordinal. Unlike an Engine stream, this roller has no
    /// disposable cursor to retain between callbacks.
    /// </summary>
    public DiceRoller(IRandomService random, ulong seed, string scope, long nextKey = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        if (nextKey < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(nextKey), "A keyed dice cursor cannot be negative.");
        }

        _random = random;
        _seed = seed;
        _scope = scope;
        _nextKey = nextKey;
    }

    public IReadOnlyList<DiceRoll> Rolls => _rolls;

    /// <summary>The deterministic keyed scope, or null for a legacy stream roller.</summary>
    public string? RandomScope => _scope;

    /// <summary>The next keyed draw ordinal, or zero for a legacy stream roller.</summary>
    public long NextRandomKey => _nextKey;

    /// <summary>Whether this roller can be reconstructed without retaining an Engine stream.</summary>
    public bool IsKeyed => _scope is not null;

    /// <summary>Creates another keyed roller over the same Engine random service.</summary>
    public DiceRoller Keyed(ulong seed, string scope, long nextKey = 0) => new(_random, seed, scope, nextKey);

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

    /// <summary>Rolls <paramref name="count"/> Fudge dice (faces -1, 0, +1) and adds them.</summary>
    public long Fudge(int count)
    {
        List<int> faces = [];
        for (int i = 0; i < count; i++)
        {
            faces.Add((int)Next(3) - 1);
        }

        DiceRoll roll = new(count, 3, faces, count, Fudge: true);
        _rolls.Add(roll);
        return roll.Total;
    }

    private long Roll(int count, int sides, int keep, int? atLeast, int? again = null, int? cancel = null)
    {
        List<int> faces = [];
        int left = count;
        while (left > 0)
        {
            int face = (int)Next(sides) + 1;
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

    private uint Next(int exclusiveMaximum)
    {
        if (exclusiveMaximum < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(exclusiveMaximum), "Dice need at least one side.");
        }

        if (_scope is string scope && _seed is ulong seed)
        {
            long key = _nextKey++;
            return checked((uint)_random.DrawKeyed(new KeyedRngRequest(seed, scope, $"draw:{key}", 0, exclusiveMaximum - 1)).Value);
        }

        return checked((uint)_random.NextBoundedU32(new ScopedRngBoundedRequest(_stream!, (uint)exclusiveMaximum)).Value);
    }
}
