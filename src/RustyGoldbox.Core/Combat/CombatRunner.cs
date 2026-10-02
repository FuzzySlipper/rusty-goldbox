using System.Text.Json;
using RustyGoldbox.Core.Definitions;
using RustyGoldbox.Core.Expressions;
using RustyGoldbox.Core.Modules;
using RustyGoldbox.Core.Rules;

namespace RustyGoldbox.Core.Combat;

/// <summary>A side in a fight.</summary>
public sealed record CombatSide(string Name, IReadOnlyList<Combatant> Members);

/// <summary>How a fight ended: the facts, the winning side (null if none) and the rounds fought.</summary>
/// <param name="Track">The combat's track, which summaries show.</param>
public sealed record CombatResult(IReadOnlyList<CombatFact> Facts, int? Winner, int Rounds, IReadOnlyList<CombatSide> Sides, Definition Track);

/// <summary>A rule expression that failed during a fight, located in its definition.</summary>
public sealed class CombatFailure(ModuleDiagnostic diagnostic) : Exception(diagnostic.Message)
{
    public ModuleDiagnostic Diagnostic { get; } = diagnostic;
}

/// <summary>
/// The fixed combat loop. Each round: initiative (every round or once, as the
/// combat definition says), then each creature's turn: start-of-turn condition
/// operations, then actions while its budget lasts, then its timed conditions
/// count down. The combat definition supplies the formulas and budget; actions
/// and operations supply what happens. Choices are a simple deterministic
/// policy: the first use in a creature's list it can afford and that has a
/// target.
/// </summary>
/// <exception cref="CombatFailure">A rule expression failed during the fight.</exception>
public sealed class CombatRunner
{
    private readonly RuleSet _rules;
    private readonly Definition _combat;
    private readonly Evaluator _evaluator;
    private readonly DiceRoller _dice;
    private readonly List<CombatSide> _sides;
    private readonly List<CombatFact> _facts = [];
    private readonly Definition _track;
    private Combatant? _turn;

    private CombatRunner(RuleSet rules, Definition combat, IReadOnlyList<CombatSide> sides, DiceRoller dice)
    {
        _rules = rules;
        _combat = combat;
        _dice = dice;
        _evaluator = new Evaluator(rules, dice);
        _track = rules.Reference(combat, "$.track");
        _sides = sides.ToList();
        for (int side = 0; side < _sides.Count; side++)
        {
            foreach (Combatant member in _sides[side].Members)
            {
                member.Side = side;
            }
        }
    }

    public static CombatResult Run(RuleSet rules, Definition combat, IReadOnlyList<CombatSide> sides, DiceRoller dice, int maxRounds)
    {
        return new CombatRunner(rules, combat, sides, dice).Fight(maxRounds);
    }

    private IEnumerable<Combatant> Everyone => _sides.SelectMany(side => side.Members);

    private CombatResult Fight(int maxRounds)
    {
        foreach (Combatant combatant in Everyone)
        {
            CheckDefeated(combatant);
        }

        RollSurprise();
        int round = 0;
        int? winner = Winner();
        bool rollEachRound = _combat.Json.GetProperty("initiative_each").GetString() == "round";
        List<Combatant>? order = null;
        while (winner is null && StandingSides() > 1 && round < maxRounds)
        {
            round++;
            Record(new RoundFact(round));
            if (order is null || rollEachRound)
            {
                order = TurnOrder();
            }

            foreach (Combatant combatant in order)
            {
                if (StandingSides() <= 1)
                {
                    break;
                }

                TakeTurn(combatant);
            }

            winner = Winner();
        }

        Record(new EndFact(winner is int side ? _sides[side].Name : null, round));
        return new CombatResult(_facts, winner, round, _sides, _track);
    }

    private void RollSurprise()
    {
        if (!_combat.Json.TryGetProperty("surprise", out _))
        {
            return;
        }

        foreach (CombatSide side in _sides)
        {
            int before = _dice.Rolls.Count;
            decimal rounds = Number(_combat, "$.surprise", new Scope(null, null));
            if (rounds > 0)
            {
                foreach (Combatant member in side.Members)
                {
                    member.SurprisedRounds = rounds;
                }

                Record(new SurprisedFact(side.Name, rounds), before);
            }
        }
    }

    /// <summary>
    /// Everyone still fighting, in initiative order. Creatures who fall later
    /// keep their place and are skipped; with initiative each round, the next
    /// roll drops them.
    /// </summary>
    private List<Combatant> TurnOrder()
    {
        bool highestFirst = _combat.Json.GetProperty("initiative_order").GetString() == "highest-first";
        List<(decimal Value, int Index, List<Combatant> Group)> groups = [];
        if (_combat.Json.GetProperty("initiative_by").GetString() == "side")
        {
            for (int side = 0; side < _sides.Count; side++)
            {
                List<Combatant> standing = _sides[side].Members.Where(member => !member.Defeated).ToList();
                if (standing.Count > 0)
                {
                    groups.Add((RollInitiative(standing[0], _sides[side].Name), side, standing));
                }
            }
        }
        else
        {
            int index = 0;
            foreach (Combatant combatant in Everyone.Where(member => !member.Defeated))
            {
                groups.Add((RollInitiative(combatant, combatant.Name), index, [combatant]));
                index++;
            }
        }

        IEnumerable<(decimal Value, int Index, List<Combatant> Group)> ordered = highestFirst
            ? groups.OrderByDescending(group => group.Value).ThenBy(group => group.Index)
            : groups.OrderBy(group => group.Value).ThenBy(group => group.Index);
        return ordered.SelectMany(group => group.Group).ToList();
    }

    private decimal RollInitiative(Combatant self, string who)
    {
        int before = _dice.Rolls.Count;
        decimal value = Number(_combat, "$.initiative", new Scope(self.Creature, null));
        Record(new InitiativeFact(who, value), before);
        return value;
    }

    private void TakeTurn(Combatant actor)
    {
        if (actor.Defeated)
        {
            return;
        }

        _turn = actor;
        ActOnTurn(actor);
        EndTurn(actor);
        _turn = null;
    }

    private void ActOnTurn(Combatant actor)
    {
        if (actor.SurprisedRounds > 0)
        {
            Record(new TurnSkippedFact(actor.Name, "surprised"));
            return;
        }

        foreach (Definition condition in actor.Creature.Conditions.ToList())
        {
            if (condition.Json.TryGetProperty("each_turn", out JsonElement operations))
            {
                RunOperations(condition, operations, "$.each_turn", new Scope(actor.Creature, null), actor, null);
                if (actor.Defeated)
                {
                    return;
                }
            }
        }

        Definition? preventing = actor.Creature.Conditions.FirstOrDefault(condition =>
            condition.Json.TryGetProperty("prevents_actions", out JsonElement prevents) && prevents.GetBoolean());
        if (preventing is not null)
        {
            Record(new TurnSkippedFact(actor.Name, preventing.Name.ToLowerInvariant()));
            return;
        }

        foreach (JsonElement entry in _combat.Json.GetProperty("budget").EnumerateArray())
        {
            actor.Budget[entry.GetProperty("id").GetString()!] = entry.GetProperty("per_turn").GetInt32();
        }

        bool acted = false;
        while (!actor.Defeated && StandingSides() > 1 && Choose(actor) is (UseOption use, List<Combatant> targets))
        {
            Spend(actor, use.Action);
            Act(actor, use, targets);
            acted = true;
        }

        if (!acted && !actor.Defeated)
        {
            Record(new TurnSkippedFact(actor.Name, "no action it can take"));
        }
    }

    /// <summary>
    /// Durations count down at the end of the holder's own turn, so a
    /// condition lasting 1 round always covers the holder's next turn,
    /// whatever the initiative order.
    /// </summary>
    private void EndTurn(Combatant combatant)
    {
        if (combatant.SurprisedRounds > 0)
        {
            combatant.SurprisedRounds--;
        }

        foreach (Definition condition in combatant.ConditionRounds.Keys.ToList())
        {
            if (combatant.AppliedThisTurn.Contains(condition))
            {
                continue;
            }

            decimal left = combatant.ConditionRounds[condition] - 1;
            if (left > 0)
            {
                combatant.ConditionRounds[condition] = left;
                continue;
            }

            combatant.ConditionRounds.Remove(condition);
            combatant.Creature.Conditions.Remove(condition);
            Record(new ConditionFact(combatant.Name, condition.Name, false, null));
        }

        combatant.AppliedThisTurn.Clear();
        CheckDefeated(combatant);
    }

    private (UseOption Use, List<Combatant> Targets)? Choose(Combatant actor)
    {
        foreach (UseOption use in actor.Uses)
        {
            if (!Affordable(actor, use.Action))
            {
                continue;
            }

            if (use.Action.Json.TryGetProperty("available", out _)
                && !Evaluate(use.Action, "$.available", new Scope(actor.Creature, null)).Boolean)
            {
                continue;
            }

            List<Combatant> targets = Targets(actor, use.Action.Json.GetProperty("target").GetString()!);
            if (targets.Count > 0)
            {
                return (use, targets);
            }
        }

        return null;
    }

    private List<Combatant> Targets(Combatant actor, string kind)
    {
        List<Combatant> enemies = Everyone.Where(member => !member.Defeated && member.Side != actor.Side).ToList();
        List<Combatant> allies = Everyone.Where(member => !member.Defeated && member.Side == actor.Side).ToList();
        return kind switch
        {
            "self" => [actor],
            "all_enemies" => enemies,
            "all_allies" => allies,
            "enemy" => enemies.OrderBy(Left).Take(1).ToList(),
            "ally" => allies.Take(1).ToList(),
            _ => allies
                .Where(member => Missing(member) > 0)
                .OrderByDescending(Missing)
                .Take(1)
                .ToList(),
        };
    }

    /// <summary>What a creature has left on the combat's track.</summary>
    private decimal Left(Combatant combatant) => combatant.Creature.Track(_track.Id).Current ?? 0;

    /// <summary>How far below its maximum a creature is on the combat's track.</summary>
    private decimal Missing(Combatant combatant)
    {
        return Located(_track, "$", () => _evaluator.TrackMax(combatant.Creature, _track)) - Left(combatant);
    }

    private void Act(Combatant actor, UseOption use, List<Combatant> targets)
    {
        Record(new ActionFact(actor.Name, use.Name, string.Join(", ", targets.Select(target => target.Name))));
        Definition action = use.Action;
        foreach (Combatant target in targets)
        {
            if (target.Defeated && target != actor)
            {
                continue;
            }

            Scope scope = new(actor.Creature, target.Creature, use.Parameters);
            if (action.Json.TryGetProperty("check", out _))
            {
                CheckResult result = MakeCheck(_rules.Reference(action, "$.check"), actor, target);
                if (action.Json.TryGetProperty("outcomes", out JsonElement outcomes)
                    && outcomes.TryGetProperty(result.Tier, out JsonElement operations))
                {
                    RunOperations(action, operations, $"$.outcomes.{result.Tier}", scope with { Check = result }, actor, target);
                }
            }

            if (action.Json.TryGetProperty("always", out JsonElement always))
            {
                RunOperations(action, always, "$.always", scope, actor, target);
            }
        }
    }

    private CheckResult MakeCheck(Definition check, Combatant by, Combatant against)
    {
        int before = _dice.Rolls.Count;
        CheckResult result = Located(check, "$", () => _evaluator.Check(check, by.Creature, against.Creature));
        Record(new CheckFact(by.Name, check.Name, result), before);
        return result;
    }

    private void RunOperations(Definition owner, JsonElement operations, string path, Scope scope, Combatant actor, Combatant? target)
    {
        int index = 0;
        foreach (JsonElement operation in operations.EnumerateArray())
        {
            Run(owner, operation, $"{path}[{index}]", scope, actor, target);
            index++;
        }
    }

    private void Run(Definition owner, JsonElement operation, string path, Scope scope, Combatant actor, Combatant? target)
    {
        string op = operation.GetProperty("op").GetString()!;
        Combatant who = operation.TryGetProperty("to", out JsonElement to) && to.GetString() == "self" ? actor : target ?? actor;
        if (op == "check")
        {
            RunCheck(owner, operation, path, scope, actor, target);
            return;
        }

        int before = _dice.Rolls.Count;
        Located(owner, path, () => Apply(owner, operation, path, scope, op, who, before));
        CheckDefeated(who);
    }

    private bool Apply(Definition owner, JsonElement operation, string path, Scope scope, string op, Combatant who, int before)
    {
        switch (op)
        {
            case "damage":
            {
                Definition track = operation.TryGetProperty("track", out _) ? _rules.Reference(owner, $"{path}.track") : _track;
                decimal amount = Math.Max(0, Number(owner, $"{path}.amount", scope));
                TrackValue value = who.Creature.Track(track.Id);
                decimal current = value.Current ?? 0;
                decimal lowered = current - amount;
                if (_evaluator.TrackMin(who.Creature, track) is decimal floor && lowered < floor)
                {
                    lowered = Math.Min(current, floor);
                }

                value.Current = lowered;
                Record(new DamageFact(who.Name, track.Name.ToLowerInvariant(), current - lowered, lowered), before);
                break;
            }

            case "heal":
            {
                Definition track = operation.TryGetProperty("track", out _) ? _rules.Reference(owner, $"{path}.track") : _track;
                decimal amount = Math.Max(0, Number(owner, $"{path}.amount", scope));
                TrackValue value = who.Creature.Track(track.Id);
                decimal current = value.Current ?? 0;
                decimal raised = Math.Max(current, Math.Min(_evaluator.TrackRestoreCap(who.Creature, track), current + amount));
                value.Current = raised;
                Record(new HealFact(who.Name, track.Name.ToLowerInvariant(), raised - current, raised), before);
                break;
            }

            case "apply_condition":
            {
                Definition condition = _rules.Reference(owner, $"{path}.condition");
                decimal? rounds = operation.TryGetProperty("rounds", out _) ? Number(owner, $"{path}.rounds", scope) : null;
                if (!who.Creature.Conditions.Contains(condition))
                {
                    who.Creature.Conditions.Add(condition);
                }

                if (rounds is decimal timed)
                {
                    who.ConditionRounds[condition] = timed;
                    if (who == _turn)
                    {
                        who.AppliedThisTurn.Add(condition);
                    }
                }
                else
                {
                    who.ConditionRounds.Remove(condition);
                }

                Record(new ConditionFact(who.Name, condition.Name, true, rounds), before);
                break;
            }

            default:
            {
                Definition condition = _rules.Reference(owner, $"{path}.condition");
                if (who.Creature.Conditions.Remove(condition))
                {
                    who.ConditionRounds.Remove(condition);
                    Record(new ConditionFact(who.Name, condition.Name, false, null));
                }

                break;
            }
        }

        return true;
    }

    private void RunCheck(Definition owner, JsonElement operation, string path, Scope scope, Combatant actor, Combatant? target)
    {
        Definition check = _rules.Reference(owner, $"{path}.check");
        bool bySelf = operation.TryGetProperty("by", out JsonElement by) && by.GetString() == "self";
        Combatant roller = bySelf ? actor : target ?? actor;
        Combatant other = bySelf ? target ?? actor : actor;
        CheckResult result = MakeCheck(check, roller, other);
        if (operation.GetProperty("outcomes").TryGetProperty(result.Tier, out JsonElement operations))
        {
            RunOperations(owner, operations, $"{path}.outcomes.{result.Tier}", scope with { Check = result }, actor, target);
        }
    }

    private decimal Number(Definition owner, string path, Scope scope) => Evaluate(owner, path, scope).Number;

    private Value Evaluate(Definition owner, string path, Scope scope)
    {
        return Located(owner, path, () => _evaluator.Evaluate(_rules.Expression(owner, path), scope));
    }

    /// <summary>Runs rule work, turning a failure into one that names the definition, file and path.</summary>
    private static T Located<T>(Definition owner, string path, Func<T> work)
    {
        try
        {
            return work();
        }
        catch (Exception exception) when (exception is ExpressionException or OverflowException)
        {
            string message = exception is OverflowException ? "A result is too large to be a number." : exception.Message;
            throw new CombatFailure(new ModuleDiagnostic("combat.evaluate", message, owner.Module, owner.File, path));
        }
    }

    /// <summary>Re-evaluates the combat's defeated rule; a creature can fall or get back up.</summary>
    private void CheckDefeated(Combatant combatant)
    {
        bool defeated = Evaluate(_combat, "$.defeated", new Scope(combatant.Creature, null)).Boolean;
        if (defeated != combatant.Defeated)
        {
            combatant.Defeated = defeated;
            Record(defeated ? new DefeatedFact(combatant.Name) : new ReturnedFact(combatant.Name));
        }
    }

    private bool Affordable(Combatant actor, Definition action)
    {
        return action.Json.GetProperty("cost").EnumerateObject()
            .All(cost => actor.Budget.TryGetValue(cost.Name, out int left) && left >= cost.Value.GetInt32());
    }

    private static void Spend(Combatant actor, Definition action)
    {
        foreach (JsonProperty cost in action.Json.GetProperty("cost").EnumerateObject())
        {
            actor.Budget[cost.Name] -= cost.Value.GetInt32();
        }
    }

    private int StandingSides() => _sides.Count(side => side.Members.Any(member => !member.Defeated));

    private int? Winner()
    {
        List<int> standing = Enumerable.Range(0, _sides.Count).Where(side => _sides[side].Members.Any(member => !member.Defeated)).ToList();
        return standing.Count == 1 ? standing[0] : null;
    }

    private void Record(CombatFact fact, int? rollsBefore = null)
    {
        if (rollsBefore is int before && _dice.Rolls.Count > before)
        {
            fact = fact with { Rolls = _dice.Rolls.Skip(before).ToList() };
        }

        _facts.Add(fact);
    }
}
