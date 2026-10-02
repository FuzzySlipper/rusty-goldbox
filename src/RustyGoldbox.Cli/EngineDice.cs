using Rusty.Engine;
using Rusty.Engine.Testing;
using RustyGoldbox.Core.Rules;

namespace RustyGoldbox.Cli;

/// <summary>Runs Core work that rolls dice inside the in-process Engine host.</summary>
internal static class EngineDice
{
    /// <summary>
    /// Runs <paramref name="work"/> inside one Engine host callback with a dice
    /// roller on a stream seeded from <paramref name="seed"/> in <paramref name="scope"/>.
    /// </summary>
    public static (T Result, IReadOnlyList<DiceRoll> Rolls) Run<T>(ulong seed, string scope, Func<DiceRoller, T> work)
    {
        using EngineTestHost host = EngineTestHost.Create();
        return host.Call(engine =>
        {
            using Rng stream = engine.Random.CreateScoped(new ScopedRngCreateRequest(seed, scope));
            DiceRoller dice = new(engine.Random, stream);
            return (work(dice), dice.Rolls);
        });
    }
}
