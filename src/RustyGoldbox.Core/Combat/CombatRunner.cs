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

/// <summary>
/// The fixed combat loop. Each round: initiative (every round or once, as the
/// combat definition says), then each creature's turn: start-of-turn condition
/// operations, then actions while its budget lasts, then its timed conditions
/// count down. The combat definition supplies the formulas and budget; actions
/// and operations supply what happens. Choices are a simple deterministic
/// policy: the first use in a creature's list it can afford and that has a
/// target, or the highest-scoring one where its uses are scored.
/// </summary>
/// <exception cref="RuleFailure">A rule expression failed during the fight.</exception>
public sealed class CombatRunner
{
    private readonly RuleSet _rules;
    private readonly Definition _combat;
    private readonly Evaluator _evaluator;
    private readonly DiceRoller _dice;
    private readonly List<CombatSide> _sides;
    private readonly List<CombatFact> _facts = [];
    private readonly Definition _track;
    private readonly CombatField? _field;
    private Combatant? _turn;
    /// <summary>How many reactions are resolving: 0 on a turn, 1 in a reaction, 2 in a counter-reaction.</summary>
    private int _reactions;

    private CombatRunner(RuleSet rules, Definition combat, IReadOnlyList<CombatSide> sides, DiceRoller dice, Definition? encounter)
    {
        _rules = rules;
        _combat = combat;
        _dice = dice;
        _evaluator = new Evaluator(rules, dice);
        _track = rules.Reference(combat, "$.track");
        _sides = sides.ToList();
        _field = CombatField.Of(combat, encounter);
        for (int side = 0; side < _sides.Count; side++)
        {
            IReadOnlyList<Cell>? cells = _field?.Deploy(side, _sides[side].Members.Count);
            for (int index = 0; index < _sides[side].Members.Count; index++)
            {
                Combatant member = _sides[side].Members[index];
                member.Side = side;
                member.Creature.Position = cells?[index];
            }
        }
    }

    /// <summary>Cells apart on the field; without a field, everyone is 1 apart (within reach).</summary>
    private decimal Distance(Creature from, Creature to)
    {
        return _field is not null && from.Position is Cell a && to.Position is Cell b ? _field.Distance(a, b) : 1;
    }

    /// <summary>Whether nothing on the field blocks the line of sight between two creatures; without a field, always.</summary>
    private bool CanSee(Creature from, Creature to)
    {
        return _field is null || from.Position is not Cell a || to.Position is not Cell b || _field.CanSee(a, b);
    }

    /// <summary>How far a creature is from its nearest enemy still fighting (0 with none).</summary>
    private decimal Nearest(Creature creature)
    {
        Combatant? self = Everyone.FirstOrDefault(member => member.Creature == creature);
        return Everyone.Where(member => !member.Defeated && self is not null && member.Side != self.Side)
            .Select(member => Distance(creature, member.Creature))
            .DefaultIfEmpty(0)
            .Min();
    }

    /// <summary>Rounds a fight runs when its combat definition sets no round_limit.</summary>
    public const int DefaultRoundLimit = 100;

    /// <summary>The combat definition's round_limit, or the default.</summary>
    public static int RoundLimit(Definition combat)
    {
        return combat.Json.TryGetProperty("round_limit", out JsonElement limit) ? limit.GetInt32() : DefaultRoundLimit;
    }

    /// <summary>Fights the sides under the combat definition, on the encounter's terrain when it has some.</summary>
    public static CombatResult Run(RuleSet rules, Definition combat, IReadOnlyList<CombatSide> sides, DiceRoller dice, int maxRounds, Definition? encounter = null)
    {
        return new CombatRunner(rules, combat, sides, dice, encounter).Fight(maxRounds);
    }

    private IEnumerable<Combatant> Everyone => _sides.SelectMany(side => side.Members);

    private CombatResult Fight(int maxRounds)
    {
        foreach (Combatant combatant in Everyone)
        {
            CheckDefeated(combatant);
        }

        RollSurprise();

        // Budgets start full, so reactions can be taken before a creature's first turn.
        _evaluator.Combat = new CombatMoment(0, false, Distance, Nearest, CanSee);
        foreach (Combatant member in Everyone)
        {
            Refill(member);
        }

        int round = 0;
        int? winner = Winner();
        bool rollEachRound = _combat.Json.GetProperty("initiative_each").GetString() == "round";
        List<Combatant>? order = null;
        while (winner is null && StandingSides() > 1 && round < maxRounds)
        {
            round++;
            _evaluator.Combat = new CombatMoment(round, Everyone.Any(member => member.SurprisedRounds > 0), Distance, Nearest, CanSee);
            Record(new RoundFact(round));
            if (order is null || rollEachRound)
            {
                order = TurnOrder();
            }

            HashSet<Combatant> tookTurns = [];
            foreach (Combatant combatant in order)
            {
                if (StandingSides() <= 1)
                {
                    break;
                }

                if (TakeTurn(combatant))
                {
                    tookTurns.Add(combatant);
                }
                else if (DownedConditions && !tookTurns.Contains(combatant))
                {
                    // A creature that fell keeps its place, where its conditions run.
                    DownedTurn(combatant);
                    tookTurns.Add(combatant);
                }
            }

            // Those not in the order (down from the start, or dropped by a new roll) run theirs last.
            if (DownedConditions && StandingSides() > 1)
            {
                foreach (Combatant downed in Everyone.Where(member => member.Defeated && !tookTurns.Contains(member)).ToList())
                {
                    DownedTurn(downed);
                }
            }

            winner = Winner();
        }

        Record(new EndFact(winner is int side ? _sides[side].Name : null, round));
        return new CombatResult(_facts, winner, round, _sides, _track);
    }

    /// <summary>
    /// Rolls surprise at the start. By side, once per side with self as the
    /// side's lead (the member surprise_lead ranks highest, else the first)
    /// and target as the next side's lead. By creature, each creature against
    /// each enemy, losing the most rounds any enemy gives it, so only some may
    /// be surprised.
    /// </summary>
    private void RollSurprise()
    {
        if (!_combat.Json.TryGetProperty("surprise", out _))
        {
            return;
        }

        bool byCreature = _combat.Json.TryGetProperty("surprise_by", out JsonElement by) && by.GetString() == "creature";
        if (byCreature)
        {
            foreach (Combatant member in Everyone)
            {
                int before = _dice.Rolls.Count;
                decimal rounds = Everyone.Where(enemy => enemy.Side != member.Side)
                    .Select(enemy => Number(_combat, "$.surprise", new Scope(member.Creature, enemy.Creature)))
                    .DefaultIfEmpty(0)
                    .Max();
                if (rounds > 0)
                {
                    member.SurprisedRounds = rounds;
                    Record(new SurprisedFact(member.Name, rounds), before);
                }
            }

            return;
        }

        for (int index = 0; index < _sides.Count; index++)
        {
            CombatSide side = _sides[index];
            int before = _dice.Rolls.Count;
            Combatant? lead = Lead(side);
            Combatant? other = _sides.Count > 1 ? Lead(_sides[(index + 1) % _sides.Count]) : null;
            decimal rounds = Number(_combat, "$.surprise", new Scope(lead?.Creature, other?.Creature));
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

    /// <summary>The member of a side that surprise_lead ranks highest (the first on a tie), or its first member.</summary>
    private Combatant? Lead(CombatSide side)
    {
        if (side.Members.Count == 0 || !_combat.Json.TryGetProperty("surprise_lead", out _))
        {
            return side.Members.FirstOrDefault();
        }

        return side.Members.OrderByDescending(member => Number(_combat, "$.surprise_lead", new Scope(member.Creature, null))).First();
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

    /// <summary>Fills a creature's budgets to their per_turn amounts.</summary>
    private void Refill(Combatant actor)
    {
        int index = 0;
        foreach (JsonElement entry in _combat.Json.GetProperty("budget").EnumerateArray())
        {
            decimal perTurn = Number(_combat, $"$.budget[{index}].per_turn", new Scope(actor.Creature, null));
            actor.Budget[entry.GetProperty("id").GetString()!] = (int)Math.Clamp(decimal.Floor(perTurn), 0, int.MaxValue);
            index++;
        }
    }

    /// <summary>
    /// Lets <paramref name="reactor"/> react to <paramref name="source"/>: the
    /// first of its reactions to the trigger that it can afford, that
    /// <paramref name="fits"/> and whose "when" holds. A creature out of the
    /// fight, or one a condition stops acting, doesn't react. During a
    /// reaction only counter-reactions can be taken, and nothing reacts to a
    /// counter-reaction.
    /// </summary>
    private void React(string trigger, Combatant reactor, Combatant source, Func<Definition, bool>? fits = null)
    {
        if (_reactions > 1 || reactor.Defeated || reactor == source
            || reactor.Creature.Conditions.Any(condition => condition.Json.TryGetProperty("prevents_actions", out JsonElement prevents) && prevents.GetBoolean()))
        {
            return;
        }

        foreach ((Definition reaction, UseOption use) in reactor.Reactions)
        {
            bool counter = reaction.Json.TryGetProperty("counter", out JsonElement counters) && counters.GetBoolean();
            if (reaction.Json.GetProperty("trigger").GetString() != trigger || (_reactions == 1 && !counter) || !Affordable(reactor, reaction) || (fits is not null && !fits(reaction))
                || (reaction.Json.TryGetProperty("when", out _) && !Evaluate(reaction, "$.when", new Scope(reactor.Creature, source.Creature)).Boolean))
            {
                continue;
            }

            Spend(reactor, reaction);
            Record(new ReactionFact(reactor.Name, reaction.Name, source.Name));
            _reactions++;
            try
            {
                Act(reactor, use, [use.Action.Json.GetProperty("target").GetString() == "self" ? reactor : source]);
            }
            finally
            {
                _reactions--;
            }

            return;
        }
    }

    /// <summary>Takes a creature's turn unless it is out of the fight; returns whether it had one.</summary>
    private bool TakeTurn(Combatant actor)
    {
        if (actor.Defeated)
        {
            return false;
        }

        _turn = actor;
        actor.Creature.Rolled.Clear();
        ActOnTurn(actor);
        EndTurn(actor);
        _turn = null;
        return true;
    }

    /// <summary>Whether defeated creatures still run their conditions and count them down (the combat's downed_conditions).</summary>
    private bool DownedConditions => _combat.Json.TryGetProperty("downed_conditions", out JsonElement downed) && downed.GetBoolean();

    /// <summary>
    /// A defeated creature's turn when downed_conditions is on, at its place
    /// in the order (or at the round's end if it has none): its
    /// conditions' start-of-turn operations, then their end-of-turn ones and
    /// durations, but no actions (bleeding out, a save to stabilise).
    /// </summary>
    private void DownedTurn(Combatant downed)
    {
        _turn = downed;
        foreach (Definition condition in downed.Creature.Conditions.ToList())
        {
            if (condition.Json.TryGetProperty("each_turn", out JsonElement operations))
            {
                RunOperations(condition, operations, "$.each_turn", ConditionScope(downed, condition), downed, null);
            }
        }

        EndTurn(downed, downed: true);
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
                RunOperations(condition, operations, "$.each_turn", ConditionScope(actor, condition), actor, null);
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

        Refill(actor);

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
    private void EndTurn(Combatant combatant, bool downed = false)
    {
        if (combatant.SurprisedRounds > 0)
        {
            combatant.SurprisedRounds--;
        }

        foreach (Definition condition in combatant.Creature.Conditions.ToList())
        {
            if ((downed || !combatant.Defeated) && condition.Json.TryGetProperty("end_of_turn", out JsonElement operations))
            {
                RunOperations(condition, operations, "$.end_of_turn", ConditionScope(combatant, condition), combatant, null);
            }
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
            combatant.Creature.ConditionValues.Remove(condition);
            Record(new ConditionFact(combatant.Name, condition.Name, false, null));
        }

        combatant.AppliedThisTurn.Clear();
        CheckDefeated(combatant);
    }

    /// <summary>
    /// The use a creature takes next: the first in its list it can afford,
    /// that is available and has a target. When any of its uses has a score,
    /// every such option is scored against its target (a use without one
    /// scores 0) and the highest is taken, the first on a tie.
    /// </summary>
    private (UseOption Use, List<Combatant> Targets)? Choose(Combatant actor)
    {
        bool scored = actor.Uses.Any(use => use.Action.Json.TryGetProperty("score", out _));
        (UseOption Use, List<Combatant> Targets)? best = null;
        decimal bestScore = 0;
        foreach ((UseOption use, List<Combatant> targets) in Options(actor))
        {
            if (!scored)
            {
                return (use, targets);
            }

            decimal score = use.Action.Json.TryGetProperty("score", out _)
                ? Number(use.Action, "$.score", new Scope(actor.Creature, targets[0].Creature, use.Parameters))
                : 0;
            if (best is null || score > bestScore)
            {
                best = (use, targets);
                bestScore = score;
            }
        }

        return best;
    }

    /// <summary>The uses a creature could take now, in its list's order, each with its targets.</summary>
    private IEnumerable<(UseOption Use, List<Combatant> Targets)> Options(Combatant actor)
    {
        foreach (UseOption use in actor.Uses)
        {
            if (!Affordable(actor, use.Action) || (use.Spell is Definition spell && (!actor.CanCast(spell) || (!actor.CastsLeft.ContainsKey(spell) && !SpellAffordable(actor, spell)))))
            {
                continue;
            }

            if (use.Action.Json.TryGetProperty("available", out _)
                && !Evaluate(use.Action, "$.available", new Scope(actor.Creature, null)).Boolean)
            {
                continue;
            }

            List<Combatant> targets = Targets(actor, use);
            if (targets.Count > 0)
            {
                yield return (use, targets);
            }
        }
    }

    /// <summary>
    /// Who a use would target now: the candidates of the action's target kind
    /// that its valid_target accepts, then for a single target the one its
    /// prefer ranks highest (the first on a tie), or without prefer the enemy
    /// with the least left, the first ally, the ally missing the most, or the
    /// first fallen ally.
    /// </summary>
    private List<Combatant> Targets(Combatant actor, UseOption use)
    {
        Definition action = use.Action;
        string kind = action.Json.GetProperty("target").GetString()!;
        List<Combatant> enemies = Everyone.Where(member => !member.Defeated && member.Side != actor.Side).ToList();
        List<Combatant> allies = Everyone.Where(member => !member.Defeated && member.Side == actor.Side).ToList();
        List<Combatant> candidates = kind switch
        {
            "self" => [actor],
            "enemy" or "all_enemies" => enemies,
            "ally" or "all_allies" => allies,
            "fallen_ally" => Everyone.Where(member => member.Defeated && member.Side == actor.Side && member != actor).ToList(),
            _ => allies.Where(member => Missing(member) > 0).ToList(),
        };
        // A range is how far it reaches and needs line of sight; without one, it reaches anyone (moving toward an enemy out of sight).
        if (_field is not null && kind != "self" && action.Json.TryGetProperty("range", out _))
        {
            decimal range = Number(action, "$.range", new Scope(actor.Creature, null, use.Parameters));
            candidates = candidates.Where(candidate => Distance(actor.Creature, candidate.Creature) <= range && CanSee(actor.Creature, candidate.Creature)).ToList();
        }

        if (action.Json.TryGetProperty("valid_target", out _))
        {
            candidates = candidates.Where(candidate => Evaluate(action, "$.valid_target", new Scope(actor.Creature, candidate.Creature, use.Parameters)).Boolean).ToList();
        }

        if (kind is "self" or "all_enemies" or "all_allies" || candidates.Count == 0)
        {
            return candidates;
        }

        if (action.Json.TryGetProperty("prefer", out _))
        {
            // Highest first; OrderByDescending is stable, so ties keep listing order.
            return [candidates.OrderByDescending(candidate => Number(action, "$.prefer", new Scope(actor.Creature, candidate.Creature, use.Parameters))).First()];
        }

        return kind switch
        {
            "enemy" => [candidates.OrderBy(Left).First()],
            "hurt_ally" => [candidates.OrderByDescending(Missing).First()],
            _ => [candidates[0]],
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
        Definition action = use.Action;
        if (action.Json.TryGetProperty("max_targets", out _) && targets.Count > 1)
        {
            int before = _dice.Rolls.Count;
            decimal most = Number(action, "$.max_targets", new Scope(actor.Creature, null, use.Parameters));
            IEnumerable<Combatant> ranked = action.Json.TryGetProperty("prefer", out _)
                ? targets.OrderByDescending(target => Number(action, "$.prefer", new Scope(actor.Creature, target.Creature, use.Parameters)))
                : targets;
            targets = ranked.Take((int)Math.Clamp(decimal.Floor(most), 0, targets.Count)).ToList();
            Record(new ActionFact(actor.Name, use.Name, string.Join(", ", targets.Select(target => target.Name))), before);
        }
        else
        {
            Record(new ActionFact(actor.Name, use.Name, string.Join(", ", targets.Select(target => target.Name))));
        }

        if (use.Spell is Definition cast)
        {
            // A spell cast a number of times a day costs nothing else.
            if (!actor.CastsLeft.ContainsKey(cast))
            {
                PaySpell(actor, cast);
            }

            actor.Cast(cast);
        }

        foreach (Combatant target in targets)
        {
            if (target.Defeated && target != actor && action.Json.GetProperty("target").GetString() != "fallen_ally")
            {
                continue;
            }

            // An enemy it targets may interrupt first; it may not survive to act.
            if (target != actor && target.Side != actor.Side)
            {
                React("targeted", target, actor);
                if (actor.Defeated)
                {
                    return;
                }
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
        by.Creature.Rolled[check.Id] = by.Creature.Rolled.GetValueOrDefault(check.Id) + 1;
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

        if (op == "move")
        {
            Move(owner, operation, path, scope, actor, target);
            return;
        }

        if (op == "if")
        {
            string branch = Evaluate(owner, $"{path}.when", scope).Boolean ? "then" : "else";
            if (operation.TryGetProperty(branch, out JsonElement operations))
            {
                RunOperations(owner, operations, $"{path}.{branch}", scope, actor, target);
            }

            return;
        }

        int before = _dice.Rolls.Count;
        int factsBefore = _facts.Count;
        Located(owner, path, () => Apply(owner, operation, path, scope, op, who, before));
        bool fell = CheckDefeated(who);
        if (op == "damage" && who != actor && who.Side != actor.Side
            && _facts.Skip(factsBefore).OfType<DamageFact>().Any(damage => damage.Who == who.Name && damage.Amount > 0))
        {
            React("damaged", who, actor);
        }

        // An enemy felled it: its allies still fighting may react against that enemy.
        if (fell && who.Side != actor.Side)
        {
            foreach (Combatant ally in Everyone.Where(member => member.Side == who.Side && member != who).ToList())
            {
                React("ally_defeated", ally, actor);
            }
        }
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
                Record(new DamageFact(who.Name, track, current - lowered, lowered), before);
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
                Record(new HealFact(who.Name, track, raised - current, raised), before);
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

                // Values are worked out now, by whoever applies the condition; the rest keep their defaults.
                who.Creature.ConditionValues.Remove(condition);
                if (operation.TryGetProperty("values", out JsonElement values))
                {
                    who.Creature.ConditionValues[condition] = values.EnumerateObject()
                        .ToDictionary(value => value.Name, value => Number(owner, $"{path}.values.{value.Name}", scope));
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
                    who.Creature.ConditionValues.Remove(condition);
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
            RunOperations(owner, operations, $"{path}.outcomes.{result.Tier}", scope with { Check = result, Outer = scope.Check }, actor, target);
        }
    }

    /// <summary>
    /// The move operation: the actor spends up to the distance in movement
    /// (1 a cell, or the terrain's cost) stepping toward its target along the
    /// cheapest way round obstacles, stopping once within its "within" (1 by
    /// default) and in sight, or away from it, each step to the open cell
    /// that most increases the distance, stopping once at least "beyond".
    /// Creatures still fighting and impassable terrain block cells. Without a
    /// field it does nothing.
    /// </summary>
    private void Move(Definition owner, JsonElement operation, string path, Scope scope, Combatant actor, Combatant? target)
    {
        if (_field is null || actor.Creature.Position is not Cell start || target?.Creature.Position is not Cell goal || target == actor)
        {
            return;
        }

        bool away = operation.TryGetProperty("toward", out JsonElement toward) && toward.GetString() == "away";
        decimal allowed = Number(owner, $"{path}.distance", scope);
        decimal within = operation.TryGetProperty("within", out _) ? Number(owner, $"{path}.within", scope) : 1;
        decimal? beyond = operation.TryGetProperty("beyond", out _) ? Number(owner, $"{path}.beyond", scope) : null;
        HashSet<Cell> blocked = Everyone.Where(member => member != actor && !member.Defeated && member.Creature.Position is not null)
            .Select(member => member.Creature.Position!.Value)
            .ToHashSet();
        Dictionary<Cell, int>? toGoal = away ? null : CostsToReach(goal, within, blocked);
        Cell here = start;
        decimal spent = 0;
        int steps = 0;
        while (away ? beyond is not decimal far || _field.Distance(here, goal) < far : !(_field.Distance(here, goal) <= within && _field.CanSee(here, goal)))
        {
            Cell? next = away ? StepAway(here, goal, blocked) : StepToward(here, toGoal!);
            if (next is not Cell step || spent + _field.Cost(step) > allowed)
            {
                break;
            }

            // Enemies whose reach this step leaves may strike first.
            foreach (Combatant enemy in Everyone.Where(member => member.Side != actor.Side && !member.Defeated && member.Creature.Position is not null).ToList())
            {
                Cell watcher = enemy.Creature.Position!.Value;
                Cell from = here;
                React("leaves_reach", enemy, actor, reaction =>
                {
                    decimal reach = reaction.Json.TryGetProperty("reach", out _) ? Number(reaction, "$.reach", new Scope(enemy.Creature, null)) : 1;
                    return _field.Distance(watcher, from) <= reach && _field.Distance(watcher, step) > reach;
                });
            }

            if (actor.Defeated)
            {
                break;
            }

            here = step;
            spent += _field.Cost(step);
            steps++;
        }

        if (steps > 0)
        {
            actor.Creature.Position = here;
            Record(new MoveFact(actor.Name, start, here, steps));
        }
    }

    /// <summary>
    /// The least movement from each open cell to a cell within
    /// <paramref name="within"/> of the goal and in sight of it (0 there),
    /// going round impassable terrain and <paramref name="blocked"/> cells.
    /// </summary>
    private Dictionary<Cell, int> CostsToReach(Cell goal, decimal within, HashSet<Cell> blocked)
    {
        Dictionary<Cell, int> costs = [];
        PriorityQueue<Cell, int> frontier = new();
        for (int x = 0; x < _field!.Width; x++)
        {
            for (int y = 0; y < _field.Height; y++)
            {
                Cell cell = new(x, y);
                if (_field.Distance(cell, goal) <= within && cell != goal && _field.Passable(cell) && !blocked.Contains(cell) && _field.CanSee(cell, goal))
                {
                    costs[cell] = 0;
                    frontier.Enqueue(cell, 0);
                }
            }
        }

        while (frontier.TryDequeue(out Cell cell, out int cost))
        {
            if (cost > costs[cell])
            {
                continue;
            }

            // Stepping from a neighbour into this cell costs this cell's terrain.
            foreach (Cell neighbour in _field.Neighbours(cell).Where(neighbour => _field.Passable(neighbour) && !blocked.Contains(neighbour)))
            {
                int through = cost + _field.Cost(cell);
                if (!costs.TryGetValue(neighbour, out int known) || through < known)
                {
                    costs[neighbour] = through;
                    frontier.Enqueue(neighbour, through);
                }
            }
        }

        return costs;
    }

    /// <summary>The neighbouring cell on the cheapest way to the goal, the first in neighbour order on a tie, or null if none gets closer.</summary>
    private Cell? StepToward(Cell here, Dictionary<Cell, int> toGoal)
    {
        int now = toGoal.TryGetValue(here, out int cost) ? cost : int.MaxValue;
        return _field!.Neighbours(here)
            .Where(cell => toGoal.ContainsKey(cell))
            .Select(cell => (Cell: cell, Total: _field.Cost(cell) + toGoal[cell]))
            .Where(entry => entry.Total <= now && toGoal[entry.Cell] < now)
            .OrderBy(entry => entry.Total)
            .Select(entry => (Cell?)entry.Cell)
            .FirstOrDefault();
    }

    /// <summary>The open neighbouring cell that most increases the distance from the goal, or null if none does.</summary>
    private Cell? StepAway(Cell here, Cell goal, HashSet<Cell> blocked)
    {
        int now = _field!.Distance(here, goal);
        return _field.Neighbours(here)
            .Where(cell => !blocked.Contains(cell) && _field.Passable(cell))
            .Select(cell => (Cell: cell, Distance: _field.Distance(cell, goal)))
            .Where(entry => entry.Distance > now)
            .OrderByDescending(entry => entry.Distance)
            .Select(entry => (Cell?)entry.Cell)
            .FirstOrDefault();
    }

    /// <summary>What a condition's own operations read: its holder as self, and the values it was applied with.</summary>
    private static Scope ConditionScope(Combatant holder, Definition condition)
    {
        return new Scope(holder.Creature, null, ConditionValues: Evaluator.ConditionValuesOf(holder.Creature, condition));
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
            throw new RuleFailure(new ModuleDiagnostic("combat.evaluate", message, owner.Module, owner.File, path));
        }
    }

    /// <summary>Re-evaluates the combat's defeated rule; a creature can fall or get back up. Returns whether it just fell.</summary>
    private bool CheckDefeated(Combatant combatant)
    {
        bool defeated = Evaluate(_combat, "$.defeated", new Scope(combatant.Creature, null)).Boolean;
        if (defeated == combatant.Defeated)
        {
            return false;
        }

        combatant.Defeated = defeated;
        Record(defeated ? new DefeatedFact(combatant.Name) : new ReturnedFact(combatant.Name));
        return defeated;
    }

    /// <summary>Whether the caster's tracks can pay every cost of the spell.</summary>
    private bool SpellAffordable(Combatant caster, Definition spell)
    {
        if (!spell.Json.TryGetProperty("cost", out JsonElement cost))
        {
            return true;
        }

        return cost.EnumerateObject().All(entry =>
        {
            Definition track = _rules.Reference(spell, $"$.cost.{entry.Name}");
            return _evaluator.TrackCurrent(caster.Creature, track) >= Number(spell, $"$.cost.{entry.Name}", new Scope(caster.Creature, null));
        });
    }

    /// <summary>Spends the spell's cost from the caster's tracks.</summary>
    private void PaySpell(Combatant caster, Definition spell)
    {
        if (!spell.Json.TryGetProperty("cost", out JsonElement cost))
        {
            return;
        }

        foreach (JsonProperty entry in cost.EnumerateObject())
        {
            Definition track = _rules.Reference(spell, $"$.cost.{entry.Name}");
            decimal amount = Number(spell, $"$.cost.{entry.Name}", new Scope(caster.Creature, null));
            TrackValue value = caster.Creature.Track(track.Id);
            value.Current = _evaluator.TrackCurrent(caster.Creature, track) - amount;
            Record(new SpentFact(caster.Name, track, amount, value.Current.Value));
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
