using System.Globalization;
using RustyGoldbox.Core.Rules;

namespace RustyGoldbox.Core.Combat;

/// <summary>
/// Something that happened in a combat, recorded after it took effect. The
/// facts in order are the combat's transcript.
/// </summary>
public abstract record CombatFact
{
    /// <summary>Dice rolled to produce this fact.</summary>
    public IReadOnlyList<DiceRoll> Rolls { get; init; } = [];

    public abstract string Kind { get; }

    public abstract string Describe();

    protected static string N(decimal value) => value.ToString("0.############", CultureInfo.InvariantCulture);
}

public sealed record SurprisedFact(string Side, decimal Rounds) : CombatFact
{
    public override string Kind => "surprised";

    public override string Describe() => $"{Side} is surprised for {N(Rounds)} round{(Rounds == 1 ? "" : "s")}.";
}

public sealed record RoundFact(int Round) : CombatFact
{
    public override string Kind => "round";

    public override string Describe() => $"Round {Round}.";
}

public sealed record InitiativeFact(string Who, decimal Value) : CombatFact
{
    public override string Kind => "initiative";

    public override string Describe() => $"{Who} rolls {N(Value)} for initiative.";
}

public sealed record TurnSkippedFact(string Who, string Reason) : CombatFact
{
    public override string Kind => "turn_skipped";

    public override string Describe() => $"{Who} doesn't act ({Reason}).";
}

public sealed record ActionFact(string Who, string Action, string Target) : CombatFact
{
    public override string Kind => "action";

    public override string Describe() => $"{Who} uses {Action} on {Target}.";
}

public sealed record CheckFact(string Who, string Check, CheckResult Result) : CombatFact
{
    public override string Kind => "check";

    public override string Describe()
    {
        string bonus = Result.Bonus == 0 ? "" : $" {Sign(Result.Bonus)}";
        string modifier = Result.Modifier == 0 ? "" : $" {Sign(Result.Modifier)} modifiers";
        string total = bonus.Length == 0 && modifier.Length == 0 ? "" : $" = {N(Result.Total)}";
        return $"{Who} rolls {Check}: {N(Result.Roll)}{bonus}{modifier}{total} against {N(Result.Target)}: {Result.Tier}.";
    }

    private static string Sign(decimal value) => value < 0 ? $"- {N(-value)}" : $"+ {N(value)}";
}

public sealed record DamageFact(string Who, string Track, decimal Amount, decimal Left) : CombatFact
{
    public override string Kind => "damage";

    public override string Describe() => $"{Who} loses {N(Amount)} {Track} ({N(Left)} left).";
}

public sealed record HealFact(string Who, string Track, decimal Amount, decimal Now) : CombatFact
{
    public override string Kind => "heal";

    public override string Describe() => $"{Who} regains {N(Amount)} {Track} ({N(Now)}).";
}

public sealed record ConditionFact(string Who, string Condition, bool Applied, decimal? Rounds) : CombatFact
{
    public override string Kind => Applied ? "condition_applied" : "condition_ended";

    public override string Describe()
    {
        if (!Applied)
        {
            return $"{Who} is no longer {Condition}.";
        }

        return Rounds is decimal rounds ? $"{Who} is {Condition} for {N(rounds)} round{(rounds == 1 ? "" : "s")}." : $"{Who} is {Condition}.";
    }
}

public sealed record DefeatedFact(string Who) : CombatFact
{
    public override string Kind => "defeated";

    public override string Describe() => $"{Who} is out of the fight.";
}

public sealed record ReturnedFact(string Who) : CombatFact
{
    public override string Kind => "returned";

    public override string Describe() => $"{Who} is back in the fight.";
}

public sealed record EndFact(string? Winner, int Rounds) : CombatFact
{
    public override string Kind => "end";

    public override string Describe() => Winner is null
        ? $"No side won after {Rounds} round{(Rounds == 1 ? "" : "s")}."
        : $"{Winner} wins after {Rounds} round{(Rounds == 1 ? "" : "s")}.";
}
