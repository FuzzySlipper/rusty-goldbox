using System.Text;

namespace RustyTales.Core.Rules;

/// <summary>A disposable scoped random stream returning uniform integers in <c>[0, exclusiveMaximum)</c>.</summary>
public interface ITalesRandomStream : IDisposable
{
    uint NextBounded(uint exclusiveMaximum);
}

/// <summary>
/// Deterministic randomness boundary for Core rules, character creation,
/// combat and campaign play. Hosts may supply an Engine-backed implementation
/// or use <see cref="DeterministicRandom.Shared"/> in pure-.NET tools and web clients.
/// </summary>
public interface ITalesRandom
{
    ITalesRandomStream CreateScoped(ulong seed, string scope);

    uint DrawKeyed(ulong seed, string scope, string key, uint exclusiveMaximum);
}

/// <summary>
/// Pure-.NET deterministic PRNG (SplitMix64 + FNV-1a domain separation + unbiased
/// Lemire range reduction). Identical across platforms, CLI tools and browser clients.
/// </summary>
public sealed class DeterministicRandom : ITalesRandom
{
    public static DeterministicRandom Shared { get; } = new();

    public ITalesRandomStream CreateScoped(ulong seed, string scope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        return new SplitMixStream(MixScope(seed, scope));
    }

    public uint DrawKeyed(ulong seed, string scope, string key, uint exclusiveMaximum)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (exclusiveMaximum == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(exclusiveMaximum), "Exclusive maximum must be positive.");
        }

        ulong state = MixScope(MixScope(seed, scope), key);
        return Bounded(ref state, exclusiveMaximum);
    }

    private static ulong MixScope(ulong seed, string text)
    {
        ulong hash = 14695981039346656037UL ^ seed;
        foreach (byte b in Encoding.UTF8.GetBytes(text))
        {
            hash ^= b;
            hash *= 1099511628211UL;
        }

        return NextU64(ref hash);
    }

    private static ulong NextU64(ref ulong state)
    {
        state += 0x9E3779B97F4A7C15UL;
        ulong z = state;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    private static uint Bounded(ref ulong state, uint exclusiveMaximum)
    {
        if (exclusiveMaximum <= 1)
        {
            return 0;
        }

        // Unbiased rejection sampling over 32-bit uniform outputs.
        uint threshold = (uint)(0x1_0000_0000UL % exclusiveMaximum);
        while (true)
        {
            uint draw = (uint)(NextU64(ref state) >> 32);
            if (draw >= threshold)
            {
                return draw % exclusiveMaximum;
            }
        }
    }

    private sealed class SplitMixStream(ulong state) : ITalesRandomStream
    {
        private ulong _state = state;

        public uint NextBounded(uint exclusiveMaximum)
        {
            if (exclusiveMaximum == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(exclusiveMaximum), "Exclusive maximum must be positive.");
            }

            return Bounded(ref _state, exclusiveMaximum);
        }

        public void Dispose()
        {
        }
    }
}
