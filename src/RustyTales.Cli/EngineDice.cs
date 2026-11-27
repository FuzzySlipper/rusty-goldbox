using RustyTales.Core.Rules;

namespace RustyTales.Cli;

/// <summary>Runs Core work that rolls dice with Core's pure deterministic RNG.</summary>
internal static class EngineDice
{
    /// <summary>
    /// Runs <paramref name="work"/> with a dice roller on a deterministic
    /// stream seeded from <paramref name="seed"/> in <paramref name="scope"/>.
    /// </summary>
    public static (T Result, IReadOnlyList<DiceRoll> Rolls) Run<T>(ulong seed, string scope, Func<DiceRoller, T> work)
    {
        using ITalesRandomStream stream = DeterministicRandom.Shared.CreateScoped(seed, scope);
        DiceRoller dice = new(DeterministicRandom.Shared, stream);
        return (work(dice), dice.Rolls);
    }
}
