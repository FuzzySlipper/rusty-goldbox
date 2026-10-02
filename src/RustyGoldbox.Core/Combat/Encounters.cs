using RustyGoldbox.Core.Definitions;
using RustyGoldbox.Core.Modules;
using RustyGoldbox.Core.Rules;

namespace RustyGoldbox.Core.Combat;

/// <summary>Turns encounters and parties into combat sides.</summary>
public static class Encounters
{
    /// <summary>Rolls an encounter's monster counts and hit points into combatants, numbering repeats.</summary>
    /// <exception cref="CombatFailure">A count or hit point expression failed or isn't a usable number.</exception>
    public static List<Combatant> Spawn(RuleSet rules, Definition encounter, DiceRoller dice)
    {
        Evaluator evaluator = new(rules, dice);
        List<Combatant> monsters = [];
        int index = 0;
        foreach (System.Text.Json.JsonElement _ in encounter.Json.GetProperty("monsters").EnumerateArray())
        {
            string path = $"$.monsters[{index}]";
            Definition monster = rules.Reference(encounter, $"{path}.monster");
            decimal count = Evaluate(rules, encounter, $"{path}.count", () => evaluator.Evaluate(rules.Expression(encounter, $"{path}.count"), null, null).Number);
            if (count < 0 || count != decimal.Truncate(count) || count > int.MaxValue)
            {
                throw new CombatFailure(new ModuleDiagnostic("combat.evaluate", $"The count came to {count}; it must be a whole number from 0 to {int.MaxValue}.", encounter.Module, encounter.File, $"{path}.count"));
            }

            for (int i = 1; i <= (int)count; i++)
            {
                string name = count == 1 ? monster.Name : $"{monster.Name} {i}";
                monsters.Add(Evaluate(rules, monster, "$.tracks", () => Combatant.FromMonster(rules, monster, name, evaluator)));
            }

            index++;
        }

        return monsters;
    }

    /// <summary>
    /// Makes side names and combatant names unique across a fight, adding a
    /// number to repeats, so transcripts and summaries can tell them apart.
    /// </summary>
    public static List<CombatSide> Distinct(IReadOnlyList<CombatSide> sides)
    {
        HashSet<string> sideNames = [];
        HashSet<string> names = [];
        List<CombatSide> result = [];
        foreach (CombatSide side in sides)
        {
            List<Combatant> members = side.Members.Select(member => member.Renamed(Unique(member.Name, names))).ToList();
            result.Add(new CombatSide(Unique(side.Name, sideNames), members));
        }

        return result;
    }

    private static string Unique(string name, HashSet<string> taken)
    {
        string candidate = name;
        for (int n = 2; !taken.Add(candidate); n++)
        {
            candidate = $"{name} ({n})";
        }

        return candidate;
    }

    private static T Evaluate<T>(RuleSet rules, Definition owner, string path, Func<T> work)
    {
        try
        {
            return work();
        }
        catch (Exception exception) when (exception is Expressions.ExpressionException or OverflowException)
        {
            string message = exception is OverflowException ? "A result is too large to be a number." : exception.Message;
            throw new CombatFailure(new ModuleDiagnostic("combat.evaluate", message, owner.Module, owner.File, path));
        }
    }
}
