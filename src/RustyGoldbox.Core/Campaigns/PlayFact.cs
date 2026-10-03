using System.Globalization;
using RustyGoldbox.Core.Combat;
using RustyGoldbox.Core.Definitions;
using RustyGoldbox.Core.Rules;

namespace RustyGoldbox.Core.Campaigns;

/// <summary>Something that happened in play, recorded after it took effect.</summary>
public abstract record PlayFact
{
    public IReadOnlyList<DiceRoll> Rolls { get; init; } = [];

    public abstract string Kind { get; }

    public abstract string Describe();

    protected static string N(decimal value) => value.ToString("0.############", CultureInfo.InvariantCulture);
}

public sealed record ArrivedFact(string Area, int X, int Y, Facing Facing) : PlayFact
{
    public override string Kind => "arrived";

    public override string Describe() => $"The party is in {Area} at [{X}, {Y}], facing {Facings.Name(Facing)}.";
}

public sealed record MovedFact(int X, int Y, Facing Facing) : PlayFact
{
    public override string Kind => "moved";

    public override string Describe() => $"The party moves to [{X}, {Y}], facing {Facings.Name(Facing)}.";
}

public sealed record TurnedFact(Facing Facing) : PlayFact
{
    public override string Kind => "turned";

    public override string Describe() => $"The party faces {Facings.Name(Facing)}.";
}

public sealed record RefusedFact(string Reason) : PlayFact
{
    public override string Kind => "refused";

    public override string Describe() => $"Can't: {Reason}";
}

public sealed record LookFact(string Area, int X, int Y, Facing Facing, IReadOnlyList<string> Sides, string? Zone) : PlayFact
{
    public override string Kind => "look";

    public override string Describe()
    {
        string zone = Zone is null ? "" : $" ({Zone})";
        return $"{Area} [{X}, {Y}]{zone}, facing {Facings.Name(Facing)}. {string.Join("; ", Sides)}.";
    }
}

public sealed record TextFact(string Text) : PlayFact
{
    public override string Kind => "text";

    public override string Describe() => Text;
}

public sealed record MenuFact(string Text, IReadOnlyList<(int Number, string Label)> Options) : PlayFact
{
    public override string Kind => "menu";

    public override string Describe() => $"{Text} {string.Join("  ", Options.Select(option => $"[{option.Number}] {option.Label}"))}";
}

public sealed record ChoseFact(int Number, string Label) : PlayFact
{
    public override string Kind => "chose";

    public override string Describe() => $"The party chooses {Number}: {Label}.";
}

public sealed record VariableFact(string Variable, Expressions.Value Value) : PlayFact
{
    public override string Kind => "variable";

    public override string Describe() => $"{Variable} is now {Value}.";
}

public sealed record TreasureFact(decimal Gold, IReadOnlyList<string> Items) : PlayFact
{
    public override string Kind => "treasure";

    public override string Describe()
    {
        List<string> parts = [];
        if (Gold != 0)
        {
            parts.Add($"{N(Gold)} gold");
        }

        parts.AddRange(Items);
        return parts.Count == 0 ? "The party finds nothing." : $"The party finds {string.Join(", ", parts)}.";
    }
}

public enum FightOutcome
{
    Won,
    Lost,
    Undecided,
}

/// <summary>
/// One combatant as the fight began: its side (0 is the party), the monster or
/// class it is, and its value on the combat's track (its maximum when the
/// ruleset gives one).
/// </summary>
public sealed record FightMember(string Name, int Side, Definition? Monster, Definition? Class, decimal Start, decimal? Max, Combat.Cell? Position = null);

/// <summary>A fight: who took part, what happened in order, and how it ended.</summary>
/// <param name="Track">The combat's track (usually hit points) that <see cref="FightMember"/> values are on.</param>
/// <param name="Field">The combat field the fight was on, or null for a fight without positions.</param>
public sealed record FightFact(string Encounter, Definition Track, IReadOnlyList<FightMember> Members, IReadOnlyList<CombatFact> Facts, FightOutcome Outcome, Combat.CombatField? Field = null) : PlayFact
{
    public override string Kind => "combat";

    public override string Describe() => Outcome switch
    {
        FightOutcome.Won => $"Combat with {Encounter}: the party wins.",
        FightOutcome.Lost => $"Combat with {Encounter}: the party loses.",
        _ => $"Combat with {Encounter}: neither side wins before the round limit.",
    };
}

/// <summary>Experience the party earned, and each character's share.</summary>
public sealed record ExperienceFact(IReadOnlyList<(string Who, decimal Amount)> Shares) : PlayFact
{
    public override string Kind => "experience";

    public override string Describe() => Shares.Count == 0
        ? "No one is standing to share the experience."
        : $"Experience: {string.Join(", ", Shares.Select(share => $"{share.Who} {N(share.Amount)}"))}.";
}

/// <summary>A level a character gained in play, or one it has the experience for but needs choices to take.</summary>
public sealed record LevelFact(string Who, int Member, int Level, string Class, decimal Gain, string Track, bool Waiting = false) : PlayFact
{
    public override string Kind => "level";

    public override string Describe() => Waiting
        ? $"{Who} has the experience for a new level, which needs choices: level {Member} [--class <id>] [--feature <id>,...] [--boosts <id>,...]."
        : $"{Who} reaches level {Level} ({Class}): +{N(Gain)} {Track}.";
}

public sealed record EndedFact(string Text) : PlayFact
{
    public override string Kind => "ended";

    public override string Describe() => $"The adventure ends. {Text}";
}

public sealed record StatusFact(IReadOnlyList<string> Lines) : PlayFact
{
    public override string Kind => "status";

    public override string Describe() => string.Join(" | ", Lines);
}
