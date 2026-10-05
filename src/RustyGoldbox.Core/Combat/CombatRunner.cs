using System.Text.Json;
using System.Globalization;
using RustyGoldbox.Core.Characters;
using RustyGoldbox.Core.Definitions;
using RustyGoldbox.Core.Expressions;
using RustyGoldbox.Core.Modules;
using RustyGoldbox.Core.Rules;

namespace RustyGoldbox.Core.Combat;

/// <summary>A side in a fight.</summary>
public sealed record CombatSide(string Name, IReadOnlyList<Combatant> Members);

/// <summary>How a fight ended: the facts, the winning side (null if none), the rounds fought and a side that fled (if any).</summary>
/// <param name="Track">The combat's track, which summaries show.</param>
public sealed record CombatResult(
    IReadOnlyList<CombatFact> Facts,
    int? Winner,
    int Rounds,
    IReadOnlyList<CombatSide> Sides,
    Definition Track,
    int? FledSide = null,
    IReadOnlyList<CombatBehaviorTrace>? BehaviorTraces = null);

/// <summary>Optional encounter-specific starting positions and surprise override.</summary>
public sealed record CombatSetup(IReadOnlyList<Cell?> Starts, int? SurprisedSide = null, decimal SurpriseRounds = 1);

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
public sealed partial class CombatRunner
{
    private readonly RuleSet _rules;
    private readonly Definition _combat;
    private readonly Evaluator _evaluator;
    private readonly Evaluator _previewEvaluator;
    private readonly DiceRoller _dice;
    private readonly List<CombatSide> _sides;
    private readonly CombatSetup? _setup;
    private int? _fledSide;
    private Combatant? _lastActor;
    private readonly List<CombatFact> _facts = [];
    private readonly Definition _track;
    private readonly CombatField? _field;
    private Combatant? _turn;
    private PendingDamage? _pendingDamage;
    private bool _combatInitialized;
    /// <summary>How many reactions are resolving: 0 on a turn, 1 in a reaction, 2 in a counter-reaction.</summary>
    private int _reactions;

    private sealed class PendingDamage(Combatant target, Combatant source, decimal amount)
    {
        public Combatant Target { get; } = target;

        public Combatant Source { get; } = source;

        public decimal Amount { get; set; } = amount;
    }

    private CombatRunner(RuleSet rules, Definition combat, IReadOnlyList<CombatSide> sides, DiceRoller dice, Definition? encounter, CombatSetup? setup, bool initialize)
    {
        _rules = rules;
        _combat = combat;
        _dice = dice;
        _setup = setup;
        _evaluator = new Evaluator(rules, dice);
        _previewEvaluator = new Evaluator(rules, null);
        _track = rules.Reference(combat, "$.track");
        _sides = sides.ToList();
        _field = CombatField.Of(combat, encounter);
        if (initialize)
        {
            InitializeMembers();
        }
        else
        {
            EnsureCommonActions();
        }

        AssignStableIds();
        InitializeBehaviorController();
    }

    private void InitializeMembers()
    {
        HashSet<Cell> occupied = [];
        for (int side = 0; side < _sides.Count; side++)
        {
            Cell? anchor = _setup is not null && _setup.Starts.Count > side ? _setup.Starts[side] : null;
            IReadOnlyList<Cell>? cells = _field?.Deploy(side, _sides[side].Members.Count, anchor, occupied);
            for (int index = 0; index < _sides[side].Members.Count; index++)
            {
                Combatant member = _sides[side].Members[index];
                StartCombatTracks(member);
                // Actions every creature in these fights has come after its own.
                member.Uses.AddRange(Combatant.ReadUses(_rules, _combat, "$.actions", member.Creature.Equipment)
                    .Where(common => !member.Uses.Any(own => own.Action == common.Action && own.Name == common.Name)));
                member.Side = side;
                member.Creature.Position = cells?[index];
            }
        }
    }

    private void EnsureCommonActions()
    {
        foreach (CombatSide side in _sides)
        {
            foreach (Combatant member in side.Members)
            {
                member.Uses.AddRange(Combatant.ReadUses(_rules, _combat, "$.actions", member.Creature.Equipment)
                    .Where(common => !member.Uses.Any(own => own.Action == common.Action && own.Name == common.Name)));
            }
        }
    }

    /// <summary>Starts data-declared pools whose maximum depends on equipment that was added after character creation.</summary>
    private void StartCombatTracks(Combatant member)
    {
        foreach (Definition track in _rules.Tracks.Values)
        {
            if (!track.Json.TryGetProperty("start_on_combat", out _)
                || !_evaluator.Evaluate(_rules.Expression(track, "$.start_on_combat"), member.Creature, null).Boolean)
            {
                continue;
            }

            TrackValue value = member.Creature.Track(track.Id);
            if (value.Current == 0)
            {
                value.Current = _evaluator.TrackMax(member.Creature, track);
            }
        }
    }

    private decimal Distance(Creature from, Creature to) => CombatGeometry.Distance(_field, from, to);

    private bool CanSee(Creature from, Creature to) => CombatGeometry.CanSee(_field, from, to);

    private decimal AlliesNear(Creature creature, Creature target) => CombatGeometry.AlliesNear(_field, OwnerOf(creature), target, Everyone);

    private decimal Nearest(Creature creature) => CombatGeometry.Nearest(_field, OwnerOf(creature), creature, Everyone);

    /// <summary>The combatant a creature (or its preview copy) belongs to.</summary>
    private Combatant? OwnerOf(Creature creature)
    {
        return Everyone.FirstOrDefault(member => member.Creature == creature) ?? _previewOwners.GetValueOrDefault(creature);
    }

    /// <summary>
    /// Whether <paramref name="use"/> could target <paramref name="targetId"/>
    /// if <paramref name="actor"/> stood on <paramref name="endpoint"/>: the
    /// same legality the actor's next decision would offer from there.
    /// </summary>
    internal bool LegalFrom(Combatant actor, UseOption use, string targetId, Cell endpoint)
    {
        Cell? position = actor.Creature.Position;
        actor.Creature.Position = endpoint;
        try
        {
            return Options(actor, preview: true, allCandidates: true)
                .Any(option => ReferenceEquals(option.Use, use) && option.Targets.Any(target => target.Id == targetId));
        }
        finally
        {
            actor.Creature.Position = position;
        }
    }

    /// <summary>Rounds a fight runs when its combat definition sets no round_limit.</summary>
    public const int DefaultRoundLimit = 100;

    /// <summary>The combat definition's round_limit, or the default.</summary>
    public static int RoundLimit(Definition combat)
    {
        return combat.Json.TryGetProperty("round_limit", out JsonElement limit) ? limit.GetInt32() : DefaultRoundLimit;
    }

    /// <summary>Fights the sides under the combat definition, on the encounter's terrain when it has some.</summary>
    public static CombatResult Run(
        RuleSet rules,
        Definition combat,
        IReadOnlyList<CombatSide> sides,
        DiceRoller dice,
        int maxRounds,
        Definition? encounter = null,
        CombatSetup? setup = null,
        bool collectBehaviorTraces = false)
    {
        CombatRunner runner = new(rules, combat, sides, dice, encounter, setup, initialize: true)
        {
            CollectBehaviorTraces = collectBehaviorTraces,
        };
        return runner.Fight(maxRounds);
    }

    /// <summary>Creates a live owner. Call <see cref="Start(int)"/> to begin rolling and advancing it.</summary>
    public static CombatRunner Create(RuleSet rules, Definition combat, IReadOnlyList<CombatSide> sides, DiceRoller dice, Definition? encounter = null, CombatSetup? setup = null)
    {
        return new CombatRunner(rules, combat, sides, dice, encounter, setup, initialize: true);
    }

    private IEnumerable<Combatant> Everyone => _sides.SelectMany(side => side.Members);

    private CombatResult Fight(int maxRounds)
    {
        return RunAllAutomatic(maxRounds);

    }

    private bool ElectiveInitiative => _combat.Json.TryGetProperty("initiative_mode", out JsonElement mode) && mode.GetString() == "elective";

    private Combatant? NextElective(Combatant? last, HashSet<Combatant> tookTurns)
    {
        List<Combatant> candidates = Everyone.Where(member => !member.Defeated && !tookTurns.Contains(member)).ToList();
        if (candidates.Count == 0)
        {
            return null;
        }

        if (ShouldSuspendInitiative(last, candidates))
        {
            return null;
        }

        if (last is null || last.Defeated || !_combat.Json.TryGetProperty("initiative_score", out _))
        {
            return candidates[0];
        }

        int before = _dice.Rolls.Count;
        (Combatant Member, decimal Score, int Index) best = candidates
            .Select((member, index) => (Member: member, Score: Number(_combat, "$.initiative_score", new Scope(last.Creature, member.Creature)), Index: index))
            .OrderByDescending(entry => entry.Score)
            .ThenBy(entry => entry.Index)
            .First();
        Record(new InitiativeChoiceFact(last.Name, best.Member.Name, best.Score), before, last, [best.Member]);
        return best.Member;
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
        if (_setup?.SurprisedSide is int forced)
        {
            if (forced < 0 || forced >= _sides.Count || _setup.SurpriseRounds <= 0)
            {
                return;
            }

            CombatSide side = _sides[forced];
            foreach (Combatant member in side.Members)
            {
                member.SurprisedRounds = _setup.SurpriseRounds;
            }

            Record(new SurprisedFact(side.Name, _setup.SurpriseRounds));
            return;
        }

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
        Combatant? subject = Everyone.FirstOrDefault(member => member.Name == who);
        Record(new InitiativeFact(who, value), before, subject);
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
    private void React(string trigger, Combatant reactor, Combatant source, Func<Definition, bool>? fits = null, bool physical = false, bool attack = false)
    {
        if (_reactions > 1 || reactor.Defeated || reactor == source
            || reactor.Creature.Conditions.Any(condition => condition.Json.TryGetProperty("prevents_actions", out JsonElement prevents) && prevents.GetBoolean()))
        {
            return;
        }

        List<(Definition Reaction, UseOption Use)> legal = reactor.Reactions
            .Where(entry => ReactionFits(trigger, reactor, source, entry.Reaction, fits, physical, attack))
            .ToList();
        if (legal.Count > 0 && reactor.Controller == CombatControlMode.Manual)
        {
            SuspendReaction(trigger, reactor, source, legal, physical, attack);
            return;
        }

        foreach ((Definition reaction, UseOption use) in legal)
        {
            Spend(reactor, reaction);
            Record(new ReactionFact(reactor.Name, reaction.Name, source.Name), subject: reactor, targets: [source]);
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

    /// <summary>Whether self.rolled counts checks over the whole round (the combat's rolled: "round") rather than since the creature's own turn began.</summary>
    private bool RolledByRound => _combat.Json.TryGetProperty("rolled", out JsonElement rolled) && rolled.GetString() == "round";

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
        CountDown(downed, atStart: true);
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

    /// <summary>
    /// Durations count down at the end of the holder's own turn, so a
    /// condition lasting 1 round always covers the holder's next turn,
    /// whatever the initiative order (unless its rounds_end is turn_start:
    /// those count down as the holder's turn begins).
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

        CountDown(combatant, atStart: false);
        combatant.AppliedThisTurn.Clear();
        CheckDefeated(combatant);
    }

    /// <summary>
    /// Counts down the creature's timed conditions that end at this point of
    /// its turn (a condition's rounds_end), removing those that run out. At
    /// the end of a turn, one applied during it isn't counted yet.
    /// </summary>
    private void CountDown(Combatant combatant, bool atStart)
    {
        foreach (Definition condition in combatant.ConditionRounds.Keys.ToList())
        {
            bool endsAtStart = condition.Json.TryGetProperty("rounds_end", out JsonElement end) && end.GetString() == "turn_start";
            if (endsAtStart != atStart || (!atStart && combatant.AppliedThisTurn.Contains(condition)))
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
            Record(new ConditionFact(combatant.Name, condition.Name, false, null), subject: combatant);
        }
    }

    /// <summary>
    /// The use a creature takes next: the first in its list it can afford,
    /// that is available and has a target. When any of its uses has a score,
    /// every such option is scored against its target (a use without one
    /// scores 0) and the highest is taken, the first on a tie.
    /// </summary>
    private (UseOption Use, List<Combatant> Targets, IReadOnlyDictionary<string, decimal>? SpellCosts, int? PortionCount)? Choose(
        Combatant actor,
        IReadOnlyList<UseOption>? excluded = null)
    {
        bool scored = actor.Uses.Any(use => use.Action.Json.TryGetProperty("score", out _));
        (UseOption Use, List<Combatant> Targets, IReadOnlyDictionary<string, decimal>? SpellCosts, int? PortionCount)? best = null;
        decimal bestScore = 0;
        foreach ((UseOption use, List<Combatant> targets, IReadOnlyDictionary<string, decimal>? spellCosts, int? portionCount) in Options(actor, excluded: excluded))
        {
            if (!scored)
            {
                return (use, targets, spellCosts, portionCount);
            }

            decimal score = use.Action.Json.TryGetProperty("score", out _)
                ? Number(use.Action, "$.score", new Scope(actor.Creature, targets[0].Creature, use.Parameters))
                : 0;
            if (best is null || score > bestScore)
            {
                best = (use, targets, spellCosts, portionCount);
                bestScore = score;
            }
        }

        return best;
    }

    /// <summary>The uses a creature could take now, in its list's order, each with its targets.</summary>
    private IEnumerable<(UseOption Use, List<Combatant> Targets, IReadOnlyDictionary<string, decimal>? SpellCosts, int? PortionCount)> Options(
        Combatant actor,
        bool preview = false,
        bool allCandidates = false,
        IReadOnlyList<UseOption>? excluded = null)
    {
        foreach (UseOption use in actor.Uses)
        {
            if (excluded is not null && excluded.Any(candidate => ReferenceEquals(candidate, use)))
            {
                continue;
            }

            if (!Affordable(actor, use.Action))
            {
                continue;
            }

            bool available = true;
            if (use.Action.Json.TryGetProperty("available", out _))
            {
                try
                {
                    available = PreviewBoolean(use.Action, "$.available", new Scope(actor.Creature, null, use.Parameters)) is true;
                }
                catch (RuleFailure)
                {
                    continue;
                }
            }

            if (!available)
            {
                continue;
            }

            List<Combatant> targets;
            try
            {
                targets = Targets(actor, use, preview, allCandidates);
            }
            catch (RuleFailure)
            {
                continue;
            }

            if (targets.Count == 0)
            {
                continue;
            }

            int? portionCount = null;
            if (use.Action.Json.TryGetProperty("portions", out _))
            {
                portionCount = PreviewPortionCount(actor, use);
                if (portionCount is null)
                {
                    continue;
                }
            }

            IReadOnlyDictionary<string, decimal>? spellCosts = null;
            bool spellUnavailable = false;
            if (use.Spell is Definition spell)
            {
                spellUnavailable = !actor.CanCast(spell);
                if (!spellUnavailable && !actor.CastsLeft.ContainsKey(spell))
                {
                    try
                    {
                        // Candidate inspection must not consume the shared
                        // random stream. Deterministic prices remain visible
                        // and can be filtered for affordability; a random
                        // price is committed only after this use is selected.
                        spellCosts = PreviewSpellCostValues(actor, spell);
                        spellUnavailable = spellCosts is not null && !SpellAffordable(actor, spell, spellCosts);
                    }
                    catch (RuleFailure)
                    {
                        continue;
                    }
                }
            }

            if (!spellUnavailable)
            {
                yield return (use, targets, spellCosts, portionCount);
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
    private List<Combatant> Targets(Combatant actor, UseOption use, bool preview = false, bool allCandidates = false)
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
            "fallen_ally" => Everyone.Where(member => member.Defeated && !member.Escaped && member.Side == actor.Side && member != actor).ToList(),
            _ => allies.Where(member => preview ? PreviewMissing(member) > 0 : Missing(member) > 0).ToList(),
        };
        // A range is how far it reaches and needs line of sight; without one, it reaches anyone (moving toward an enemy out of sight).
        if (_field is not null && kind != "self" && action.Json.TryGetProperty("range", out _))
        {
            decimal? range = PreviewNumber(action, "$.range", new Scope(actor.Creature, null, use.Parameters));
            if (range is not decimal knownRange)
            {
                return [];
            }

            candidates = candidates.Where(candidate => Distance(actor.Creature, candidate.Creature) <= knownRange && CanSee(actor.Creature, candidate.Creature)).ToList();
        }

        if (action.Json.TryGetProperty("valid_target", out _))
        {
            candidates = candidates.Where(candidate =>
                PreviewBoolean(action, "$.valid_target", new Scope(actor.Creature, candidate.Creature, use.Parameters)) is true).ToList();
        }

        if (preview || allCandidates)
        {
            return candidates;
        }

        // A capped action still needs the complete legal candidate set before
        // Act evaluates its committed cap. Reducing an ordinary enemy/ally
        // action to one here would make automatic max_targets actions silently
        // affect one target even when the authored cap is larger.
        if (kind is "self" or "all_enemies" or "all_allies" || HasMaximumTargets(action) || candidates.Count == 0)
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

    private void Act(
        Combatant actor,
        UseOption use,
        List<Combatant> targets,
        IReadOnlyList<Combatant>? selectedTargets = null,
        int? committedMaxTargets = null,
        int? rollsBefore = null,
        bool alreadyPaid = false,
        IReadOnlyList<DiceRoll>? committedRolls = null,
        int? committedPortions = null,
        IReadOnlyDictionary<string, decimal>? committedSpellCosts = null)
    {
        Definition action = use.Action;
        if (selectedTargets is not null)
        {
            targets = selectedTargets.ToList();
        }

        if (action.Json.TryGetProperty("max_targets", out _))
        {
            int before = rollsBefore ?? _dice.Rolls.Count;
            decimal most = committedMaxTargets is int committed
                ? committed
                : Number(action, "$.max_targets", new Scope(actor.Creature, null, use.Parameters));
            IEnumerable<Combatant> ranked = selectedTargets is null && action.Json.TryGetProperty("prefer", out _)
                ? targets.OrderByDescending(target => Number(action, "$.prefer", new Scope(actor.Creature, target.Creature, use.Parameters)))
                : targets;
            targets = ranked.Take((int)Math.Clamp(decimal.Floor(most), 0, targets.Count)).ToList();
            Record(new ActionFact(actor.Name, use.Name, string.Join(", ", targets.Select(target => target.Name))), before, actor, targets, committedRolls);
        }
        else
        {
            Record(new ActionFact(actor.Name, use.Name, string.Join(", ", targets.Select(target => target.Name))), subject: actor, targets: targets);
        }

        if (!alreadyPaid)
        {
            PayUse(actor, use, committedSpellCosts);
        }

        if (action.Json.TryGetProperty("portions", out _))
        {
            Divide(actor, use, targets.FirstOrDefault(), selectedTargets, committedPortions);
            return;
        }

        for (int targetIndex = 0; targetIndex < targets.Count; targetIndex++)
        {
            Combatant target = targets[targetIndex];
            if (target.Defeated && target != actor && action.Json.GetProperty("target").GetString() != "fallen_ally")
            {
                continue;
            }

            SetActionTargetContinuation(actor, use, targets, targetIndex, alreadyPaid);
            if (!Resolve(actor, use, target))
            {
                return;
            }
        }
    }

    private void PayUse(
        Combatant actor,
        UseOption use,
        IReadOnlyDictionary<string, decimal>? committedSpellCosts = null)
    {
        if (use.Spell is not Definition cast)
        {
            return;
        }

        // A spell cast a number of times a day costs nothing else.
        if (!actor.CastsLeft.ContainsKey(cast))
        {
            PaySpell(actor, cast, committedSpellCosts);
        }

        actor.Cast(cast);
    }

    /// <summary>
    /// An action with portions (missiles, shared damage): its effect resolves
    /// once per portion, the first on the target it was taken against (a
    /// reaction's provoker, or the one chosen), each after that on the target
    /// it would choose now, so a target a portion felled passes its share to
    /// the next. It stops when no target is left or the actor falls.
    /// </summary>
    private void Divide(
        Combatant actor,
        UseOption use,
        Combatant? first,
        IReadOnlyList<Combatant>? selectedTargets = null,
        int? committedPortions = null)
    {
        int before = _dice.Rolls.Count;
        int count = committedPortions
            ?? PreviewPortionCount(actor, use)
            ?? 0;
        for (int portion = 1; portion <= count; portion++)
        {
            Combatant? chosen = selectedTargets is not null && selectedTargets.Count >= portion
                ? selectedTargets[portion - 1]
                : portion == 1 && first is not null && !first.Defeated ? first : Targets(actor, use).FirstOrDefault();
            if (chosen is not Combatant target)
            {
                return;
            }

            // A single portion is just the action; only several say where each goes.
            if (count > 1)
            {
                Record(new PortionFact(use.Name, portion, count, target.Name), portion == 1 ? before : null, actor, [target]);
            }
            if (!Resolve(actor, use, target))
            {
                return;
            }
        }
    }

    /// <summary>Resolves the action against one target: its reaction first, then the check and operations. Returns false if the actor fell to a reaction.</summary>
    private bool Resolve(Combatant actor, UseOption use, Combatant target)
    {
        Definition action = use.Action;
        BeginActionContinuation(actor, use, target);
        try
        {
            // An enemy it targets may interrupt first; it may not survive to act.
            if (target != actor && target.Side != actor.Side)
            {
                React("targeted", target, actor);
                if (actor.Defeated)
                {
                    return false;
                }
            }

            Scope scope = new(actor.Creature, target.Creature, use.Parameters);
            if (action.Json.TryGetProperty("check", out _))
            {
                decimal extra = action.Json.TryGetProperty("check_bonus", out _) ? Number(action, "$.check_bonus", scope) : 0;
                CheckResult result = MakeCheck(
                    _rules.Reference(action, "$.check"),
                    actor,
                    target,
                    extra,
                    new CheckOperationContext(action, "$.check", scope, actor, target, null, IsOperation: false));
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

            return true;
        }
        finally
        {
            EndActionContinuation();
        }
    }

    private CheckResult MakeCheck(
        Definition check,
        Combatant by,
        Combatant against,
        decimal extra = 0,
        CheckOperationContext? context = null)
    {
        int before = _dice.Rolls.Count;
        CheckResult result = Located(check, "$", () => _evaluator.Check(check, by.Creature, against.Creature, extra));
        by.Creature.Rolled[check.Id] = by.Creature.Rolled.GetValueOrDefault(check.Id) + 1;
        Record(new CheckFact(by.Name, check.Name, result), before, by, [against]);
        result = PostRoll(check, by, against, result, context);
        if (result.Success && by.Character is Character character && check.Json.TryGetProperty("skill", out _))
        {
            CharacterRules.MarkSkillUse(_rules, character, check.Json.GetProperty("skill").GetString()!, []);
        }

        return result;
    }

    /// <summary>
    /// Chooses at most one affordable post-roll option by its ruleset score.
    /// The check fact keeps the final result while a separate fact records the
    /// resource and effect, so a transcript can explain why the result changed.
    /// </summary>
    private CheckResult PostRoll(
        Definition check,
        Combatant by,
        Combatant against,
        CheckResult result,
        CheckOperationContext? context = null)
    {
        if (!check.Json.TryGetProperty("post_roll", out JsonElement options))
        {
            return result;
        }

        List<PostRollCandidate> legal = BuildPostRollCandidates(check, by, against, result);
        if (by.Controller == CombatControlMode.Manual && legal.Count > 0)
        {
            SuspendPostRoll(check, by, against, result, legal, context);
        }

        PostRollCandidate? best = legal
            .Where(candidate => candidate.Score > 0)
            .OrderByDescending(candidate => candidate.Score)
            .FirstOrDefault();
        if (best is null)
        {
            return result;
        }
        return ApplyPostRollOption(check, by, against, result, best);
    }

    private void RunOperations(Definition owner, JsonElement operations, string path, Scope scope, Combatant actor, Combatant? target, Combatant? source = null)
    {
        OperationFrame? parent = _operationFrames.LastOrDefault();
        OperationFrame frame = new()
        {
            Owner = owner,
            Operations = operations,
            Path = path,
            Scope = scope,
            Actor = actor,
            Target = target,
            Source = source,
            UseId = _actionContinuation is null ? parent?.UseId : UseId(_actionContinuation.Actor, _actionContinuation.Use),
            ActionId = _actionContinuation?.Use.Action.QualifiedId ?? parent?.ActionId,
        };
        _operationFrames.Add(frame);
        try
        {
            RunOperationFrame(frame, source);
        }
        finally
        {
            if (!_suspending && _operationFrames.Count > 0 && ReferenceEquals(_operationFrames[^1], frame))
            {
                RemoveOperationFrame(frame);
            }
        }
    }

    private void RunOperationFrame(OperationFrame frame, Combatant? source = null)
    {
        while (frame.Index < frame.Operations.GetArrayLength())
        {
            int index = frame.Index++;
            SyncOperationFrames();
            Run(
                frame.Owner,
                frame.Operations[index],
                $"{frame.Path}[{index}]",
                frame.Scope,
                frame.Actor,
                frame.Target,
                source ?? frame.Source);
        }

        SyncOperationFrames();
    }

    private void ResumeOperationFramesForAction(ActionContinuation action)
    {
        ResumeOperationFrames(action.OperationFrameStart);
    }

    private void ResumeOperationFrames(int start)
    {
        if (start > _operationFrames.Count)
        {
            return;
        }

        while (_operationFrames.Count > start)
        {
            OperationFrame frame = _operationFrames[^1];
            try
            {
                RunOperationFrame(frame);
            }
            finally
            {
                if (!_suspending && _operationFrames.Count > 0 && ReferenceEquals(_operationFrames[^1], frame))
                {
                    RemoveOperationFrame(frame);
                }
            }
        }
    }

    private void Run(Definition owner, JsonElement operation, string path, Scope scope, Combatant actor, Combatant? target, Combatant? source = null)
    {
        string op = operation.GetProperty("op").GetString()!;
        Combatant who = operation.TryGetProperty("to", out JsonElement to) && to.GetString() == "self" ? actor : target ?? actor;
        if (op == "check")
        {
            RunCheck(owner, operation, path, scope, actor, target, source);
            return;
        }

        if (op == "move")
        {
            Move(owner, operation, path, scope, actor, target);
            return;
        }

        if (op == "flee")
        {
            Escape(actor);
            return;
        }

        if (op == "if")
        {
            string branch = Evaluate(owner, $"{path}.when", scope).Boolean ? "then" : "else";
            if (operation.TryGetProperty(branch, out JsonElement operations))
            {
                RunOperations(owner, operations, $"{path}.{branch}", scope, actor, target, source);
            }

            return;
        }

        int before = _dice.Rolls.Count;
        int factsBefore = _facts.Count;
        Located(owner, path, () => Apply(owner, operation, path, scope, op, who, actor, source, before));
        if (op == "apply_condition" && _rules.Reference(owner, $"{path}.condition") is Definition applied && applied.Json.TryGetProperty("on_apply", out JsonElement onApply))
        {
            int rollsApplied = _dice.Rolls.Count;
            int factsApplied = _facts.Count;
            // The condition holder remains self/to-self for its data, while
            // source carries the creature whose action caused this nested
            // damage. This lets a hit reaction answer damage hidden inside an
            // instant condition without changing condition expression scope.
            RunOperations(applied, onApply, "$.on_apply", ConditionScope(who, applied), who, null, source ?? actor);
            if (IsInstant(applied))
            {
                who.Creature.ConditionValues.Remove(applied);

                // An instant condition records nothing of its own, so the dice its values rolled show with what it did first.
                if (rollsApplied > before && _facts.Count > factsApplied)
                {
                    _facts[factsApplied] = _facts[factsApplied] with { Rolls = [.. _dice.Rolls.Skip(before).Take(rollsApplied - before), .. _facts[factsApplied].Rolls] };
                }
            }
        }

        bool fell = CheckDefeated(who);
        Combatant effectSource = source ?? actor;
        if (op == "damage" && who != effectSource && who.Side != effectSource.Side
            && _facts.Skip(factsBefore).OfType<DamageFact>().Any(damage => damage.Who == who.Name && damage.Amount > 0))
        {
            React("damaged", who, effectSource);
        }

        // An enemy felled it: its allies still fighting may react against that enemy.
        if (fell && who.Side != effectSource.Side)
        {
            foreach (Combatant ally in Everyone.Where(member => member.Side == who.Side && member != who).ToList())
            {
                React("ally_defeated", ally, effectSource);
            }
        }
    }

    private bool Apply(Definition owner, JsonElement operation, string path, Scope scope, string op, Combatant who, Combatant actor, Combatant? source, int before)
    {
        switch (op)
        {
            case "damage":
            {
                Definition track = operation.TryGetProperty("track", out _) ? _rules.Reference(owner, $"{path}.track") : _track;
                decimal amount = Math.Max(0, Number(owner, $"{path}.amount", scope));
                Combatant damageSource = source ?? actor;
                if (amount > 0 && who != damageSource && who.Side != damageSource.Side)
                {
                    PendingDamage pending = new(who, damageSource, amount);
                    PendingDamage? previous = _pendingDamage;
                    _pendingDamage = pending;
                    try
                    {
                        bool physical = scope.ConditionValues?.TryGetValue("physical", out decimal marker) == true && marker > 0;
                        bool attack = scope.ConditionValues?.TryGetValue("attack", out decimal attackMarker) == true && attackMarker > 0;
                        BeginDamageContinuation(owner, path, actor, who, damageSource, track, amount, before, physical, attack);
                        React("hit", who, damageSource, physical: physical, attack: attack);
                        amount = pending.Amount;
                    }
                    finally
                    {
                        EndDamageContinuation();
                        _pendingDamage = previous;
                    }
                }

                TrackValue value = who.Creature.Track(track.Id);
                decimal current = value.Current ?? 0;
                decimal lowered = current - amount;
                if (_evaluator.TrackMin(who.Creature, track) is decimal floor && lowered < floor)
                {
                    lowered = Math.Min(current, floor);
                }

                value.Current = lowered;
                Record(new DamageFact(who.Name, track, current - lowered, lowered), before, damageSource, [who]);
                break;
            }

            case "heal":
            {
                Definition track = operation.TryGetProperty("track", out _) ? _rules.Reference(owner, $"{path}.track") : _track;
                decimal amount = Math.Max(0, Number(owner, $"{path}.amount", scope));
                decimal healed = TrackOperations.Heal(_evaluator, who.Creature, track, amount);
                Record(new HealFact(who.Name, track, healed, who.Creature.Track(track.Id).Current!.Value), before, who);
                break;
            }

            case "apply_condition" when IsInstant(_rules.Reference(owner, $"{path}.condition")):
            {
                // An instant condition is never held: its values last only while its on_apply runs (see Run).
                Definition condition = _rules.Reference(owner, $"{path}.condition");
                who.Creature.ConditionValues[condition] = operation.TryGetProperty("values", out JsonElement given)
                    ? given.EnumerateObject().ToDictionary(value => value.Name, value => Number(owner, $"{path}.values.{value.Name}", scope))
                    : [];
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

                Record(new ConditionFact(who.Name, condition.Name, true, rounds), before, who);
                break;
            }

            case "grant_budget":
            {
                string budget = operation.GetProperty("budget").GetString()!;
                decimal amount = Number(owner, $"{path}.amount", scope);
                decimal spend = Number(owner, $"{path}.spend", scope);
                if (amount < 0 || amount != decimal.Truncate(amount) || spend <= 0)
                {
                    throw new RuleFailure(new ModuleDiagnostic("combat.grant-budget", "grant_budget needs a whole non-negative budget amount and a positive resource spend.", owner.Module, owner.File, path));
                }

                Definition resource = _rules.Reference(owner, $"{path}.track");
                TrackValue value = who.Creature.Track(resource.Id);
                decimal left = _evaluator.TrackCurrent(who.Creature, resource);
                if (left < spend)
                {
                    throw new RuleFailure(new ModuleDiagnostic("combat.grant-budget", $"{who.Name} has {left} {resource.Name.ToLowerInvariant()} but the operation spends {spend}.", owner.Module, owner.File, path));
                }

                value.Current = left - spend;
                Record(new SpentFact(who.Name, resource, spend, value.Current.Value), before, who);
                who.Budget[budget] = who.Budget.GetValueOrDefault(budget) + (int)amount;
                break;
            }

            case "reduce_damage":
            {
                if (_pendingDamage is not PendingDamage pending || pending.Target != who)
                {
                    throw new RuleFailure(new ModuleDiagnostic("combat.reduce-damage", "reduce_damage can only run from a reaction triggered by hit while damage is pending.", owner.Module, owner.File, path));
                }

                if (operation.TryGetProperty("amount", out _))
                {
                    decimal amount = Number(owner, $"{path}.amount", scope);
                    if (amount < 0)
                    {
                        throw new RuleFailure(new ModuleDiagnostic("combat.reduce-damage", "reduce_damage amount cannot be negative.", owner.Module, owner.File, $"{path}.amount"));
                    }

                    pending.Amount = Math.Max(0, pending.Amount - amount);
                }

                if (operation.TryGetProperty("fraction", out _))
                {
                    decimal fraction = Number(owner, $"{path}.fraction", scope);
                    if (fraction < 0 || fraction > 1)
                    {
                        throw new RuleFailure(new ModuleDiagnostic("combat.reduce-damage", "reduce_damage fraction must be from 0 to 1.", owner.Module, owner.File, $"{path}.fraction"));
                    }

                    pending.Amount *= fraction;
                }

                if (operation.TryGetProperty("shield_track", out _))
                {
                    Definition shield = _rules.Reference(owner, $"{path}.shield_track");
                    decimal remaining = pending.Amount;
                    TrackValue value = who.Creature.Track(shield.Id);
                    decimal current = _evaluator.TrackCurrent(who.Creature, shield);
                    decimal lowered = current - remaining;
                    if (_evaluator.TrackMin(who.Creature, shield) is decimal floor && lowered < floor)
                    {
                        lowered = Math.Min(current, floor);
                    }

                    value.Current = lowered;
                    if (current != lowered)
                    {
                        Combatant shieldSource = source ?? actor;
                        Record(new DamageFact(who.Name, shield, current - lowered, lowered), before, shieldSource, [who]);
                    }
                }

                break;
            }

            default:
            {
                Definition condition = _rules.Reference(owner, $"{path}.condition");
                if (who.Creature.Conditions.Remove(condition))
                {
                    who.ConditionRounds.Remove(condition);
                    who.Creature.ConditionValues.Remove(condition);
                    Record(new ConditionFact(who.Name, condition.Name, false, null), subject: who);
                }

                break;
            }
        }

        return true;
    }

    private void RunCheck(Definition owner, JsonElement operation, string path, Scope scope, Combatant actor, Combatant? target, Combatant? source)
    {
        Definition check = _rules.Reference(owner, $"{path}.check");
        bool bySelf = operation.TryGetProperty("by", out JsonElement by) && by.GetString() == "self";
        Combatant roller = bySelf ? actor : target ?? actor;
        Combatant other = bySelf ? target ?? actor : actor;
        decimal extra = operation.TryGetProperty("bonus", out _) ? Number(owner, $"{path}.bonus", scope) : 0;
        CheckResult result = MakeCheck(
            check,
            roller,
            other,
            extra,
            new CheckOperationContext(owner, path, scope, actor, target, source, IsOperation: true));
        if (operation.GetProperty("outcomes").TryGetProperty(result.Tier, out JsonElement operations))
        {
            RunOperations(owner, operations, $"{path}.outcomes.{result.Tier}", scope with { Check = result, Outer = scope.Check }, actor, target, source);
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
        if (_requestedPath is not null && !_requestedPathConsumed)
        {
            _requestedPathConsumed = true;
            MoveAlongExplicitPath(owner, operation, path, scope, actor, target, _requestedPath);
            return;
        }

        bool fleeing = operation.TryGetProperty("toward", out JsonElement way) && way.GetString() == "away"
            && operation.TryGetProperty("escape", out JsonElement leaves) && leaves.GetBoolean();
        if (_field is null && fleeing && target != actor)
        {
            // Without a field there is nowhere to run but away.
            Escape(actor);
            return;
        }

        if (_field is null || actor.Creature.Position is not Cell start || target?.Creature.Position is not Cell goal || target == actor)
        {
            return;
        }

        bool away = operation.TryGetProperty("toward", out JsonElement toward) && toward.GetString() == "away";
        decimal allowed = Number(owner, $"{path}.distance", scope);
        decimal within = operation.TryGetProperty("within", out _) ? Number(owner, $"{path}.within", scope) : 1;
        decimal? beyond = operation.TryGetProperty("beyond", out _) ? Number(owner, $"{path}.beyond", scope) : null;
        bool provokes = !operation.TryGetProperty("provokes", out JsonElement provoking) || provoking.GetBoolean();
        bool escape = away && operation.TryGetProperty("escape", out JsonElement escaping) && escaping.GetBoolean();
        HashSet<Cell> blocked = _field.Zones
            ? []
            : Everyone.Where(member => member != actor && !member.Defeated && member.Creature.Position is not null)
                .Select(member => member.Creature.Position!.Value)
                .ToHashSet();
        Dictionary<Cell, int>? toGoal = away ? null : CostsToReach(goal, within, blocked);
        MovementContinuation movement = new()
        {
            Owner = owner,
            Path = path,
            Scope = scope,
            Actor = actor,
            Target = target,
            Start = start,
            Here = start,
            Allowed = allowed,
            Within = within,
            Beyond = beyond,
            Away = away,
            Provokes = provokes,
            Escape = escape,
            Blocked = blocked,
            ToGoal = toGoal,
            Enemies = Everyone.Where(member => provokes && member.Side != actor.Side && !member.Defeated && member.Creature.Position is not null).ToList(),
        };
        ContinueMovement(movement);
    }

    /// <summary>
    /// Applies the path selected at the live decision boundary. The path has
    /// already passed the no-roll legal-choice check; this method spends no
    /// generic movement pool and only performs the authored operation's steps,
    /// terrain costs, reactions and escape rule.
    /// </summary>
    private void MoveAlongExplicitPath(
        Definition owner,
        JsonElement operation,
        string path,
        Scope scope,
        Combatant actor,
        Combatant? target,
        IReadOnlyList<Cell> selected)
    {
        if (_field is null || selected.Count == 0 || actor.Creature.Position is not Cell start || target?.Creature.Position is not Cell goal || target == actor)
        {
            return;
        }

        bool away = operation.TryGetProperty("toward", out JsonElement toward) && toward.GetString() == "away";
        decimal allowed = Number(owner, $"{path}.distance", scope);
        decimal within = operation.TryGetProperty("within", out _) ? Number(owner, $"{path}.within", scope) : 1;
        decimal? beyond = operation.TryGetProperty("beyond", out _) ? Number(owner, $"{path}.beyond", scope) : null;
        bool provokes = !operation.TryGetProperty("provokes", out JsonElement provoking) || provoking.GetBoolean();
        bool escape = away && operation.TryGetProperty("escape", out JsonElement escaping) && escaping.GetBoolean();
        HashSet<Cell> blocked = BlockedCellsForMove(actor);
        MovementContinuation movement = new()
        {
            Owner = owner,
            Path = path,
            Scope = scope,
            Actor = actor,
            Target = target,
            Start = start,
            Here = start,
            Allowed = allowed,
            Within = within,
            Beyond = beyond,
            Away = away,
            Provokes = provokes,
            Escape = escape,
            Blocked = blocked,
            ToGoal = null,
            Enemies = Everyone.Where(member => provokes && member.Side != actor.Side && !member.Defeated && member.Creature.Position is not null).ToList(),
            Selected = selected,
            Explicit = true,
        };
        ContinueExplicitMovement(movement);
    }

    private HashSet<Cell> BlockedCellsForMove(Combatant actor)
    {
        return _field!.Zones
            ? []
            : Everyone.Where(member => member != actor && !member.Defeated && member.Creature.Position is not null)
                .Select(member => member.Creature.Position!.Value)
                .ToHashSet();
    }

    private bool MovementDestinationAllowedForCommit(Cell destination, Cell goal, decimal within, decimal? beyond, bool away)
    {
        if (_field!.Zones)
        {
            return away ? beyond is not decimal far || _field.Distance(destination, goal) >= far : _field.Distance(destination, goal) <= within;
        }

        if (away)
        {
            return beyond is not decimal far || _field.Distance(destination, goal) >= far;
        }

        return _field.Distance(destination, goal) <= within && _field.CanSee(destination, goal);
    }

    private bool IsEdge(Cell cell) => cell.X == 0 || cell.Y == 0 || cell.X == _field!.Width - 1 || cell.Y == _field.Height - 1;

    /// <summary>The creature leaves the fight, not felled: it can't be targeted or come back.</summary>
    private void Escape(Combatant creature)
    {
        creature.Escaped = true;
        creature.Defeated = true;
        Record(new EscapedFact(creature.Name), subject: creature);
        if (!_sides[creature.Side].Members.Any(member => !member.Defeated))
        {
            _fledSide = creature.Side;
        }
    }

    private bool ShouldFlee(Combatant actor)
    {
        if (!_combat.Json.TryGetProperty("flee", out JsonElement rules))
        {
            return false;
        }

        string side = actor.Side == 0 ? "party" : "monsters";
        int index = 0;
        foreach (JsonElement rule in rules.EnumerateArray())
        {
            if (rule.GetProperty("side").GetString() == side
                && Evaluate(_combat, $"$.flee[{index}].when", new Scope(actor.Creature, null)).Boolean)
            {
                return true;
            }

            index++;
        }

        return false;
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
                // A positive reach stops beside the target; within 0 means the
                // actor must enter the target's cell, which is valid for zones.
                if (_field.Distance(cell, goal) <= within && (within <= 0 || cell != goal) && _field.Passable(cell) && !blocked.Contains(cell) && _field.CanSee(cell, goal))
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

    private static bool IsInstant(Definition condition) => condition.Json.TryGetProperty("instant", out JsonElement instant) && instant.GetBoolean();

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
        if (combatant.Escaped)
        {
            return false;
        }

        bool defeated = Evaluate(_combat, "$.defeated", new Scope(combatant.Creature, null)).Boolean;
        if (defeated == combatant.Defeated)
        {
            return false;
        }

        combatant.Defeated = defeated;
        Record(defeated ? new DefeatedFact(combatant.Name) : new ReturnedFact(combatant.Name), subject: combatant);
        return defeated;
    }

    /// <summary>Whether the caster's tracks can pay every cost of the spell.</summary>
    private IReadOnlyDictionary<string, decimal> SpellCostValues(Combatant caster, Definition spell)
    {
        if (!spell.Json.TryGetProperty("cost", out JsonElement cost))
        {
            return new Dictionary<string, decimal>(StringComparer.Ordinal);
        }

        return cost.EnumerateObject().ToDictionary(
            entry => entry.Name,
            entry => Number(spell, $"$.cost.{entry.Name}", new Scope(caster.Creature, null)),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// Reads a spell's price through the no-dice evaluator. A null result
    /// means at least one price depends on a value that cannot be previewed;
    /// the selected action must commit that price later through
    /// <see cref="SpellCostValues"/>.
    /// </summary>
    private IReadOnlyDictionary<string, decimal>? PreviewSpellCostValues(Combatant caster, Definition spell)
    {
        if (!spell.Json.TryGetProperty("cost", out JsonElement cost))
        {
            return new Dictionary<string, decimal>(StringComparer.Ordinal);
        }

        Dictionary<string, decimal> values = new(StringComparer.Ordinal);
        foreach (JsonProperty entry in cost.EnumerateObject())
        {
            decimal? amount = PreviewNumber(spell, $"$.cost.{entry.Name}", new Scope(caster.Creature, null));
            if (amount is not decimal known)
            {
                return null;
            }

            values[entry.Name] = known;
        }

        return values;
    }

    /// <summary>Whether the caster's tracks can pay already evaluated spell costs.</summary>
    private bool SpellAffordable(
        Combatant caster,
        Definition spell,
        IReadOnlyDictionary<string, decimal>? committedCosts = null)
    {
        if (!spell.Json.TryGetProperty("cost", out JsonElement cost))
        {
            return true;
        }

        IReadOnlyDictionary<string, decimal> costs = committedCosts ?? SpellCostValues(caster, spell);
        return cost.EnumerateObject().All(entry =>
        {
            Definition track = _rules.Reference(spell, $"$.cost.{entry.Name}");
            return _evaluator.TrackCurrent(caster.Creature, track) >= costs[entry.Name];
        });
    }

    /// <summary>
    /// Commits the selected spell's price exactly once. Random prices are
    /// evaluated only here, after the command has passed action and target
    /// validation. If the committed price cannot be paid, it is retained on
    /// the pending choice so the accepted selection and later retry cannot
    /// roll a new price.
    /// </summary>
    private bool TryCommitSpellCosts(
        Combatant actor,
        UseOption use,
        CombatActionChoice choice,
        out CombatActionChoice committedChoice,
        out IReadOnlyDictionary<string, decimal>? committedCosts,
        out string? reason,
        out bool newlyCommitted,
        out bool consumedRandomness)
    {
        committedChoice = choice;
        committedCosts = choice.SpellCosts;
        reason = null;
        newlyCommitted = false;
        long randomBefore = _dice.NextRandomKey;
        int rollsBefore = _dice.Rolls.Count;

        bool affordable = TryResolveSpellCosts(actor, use, committedCosts, out committedCosts, out reason);
        if (choice.SpellCosts is null && committedCosts is not null)
        {
            committedChoice = choice with { SpellCosts = committedCosts };
            ReplacePendingActionChoice(committedChoice);
            newlyCommitted = true;
        }

        consumedRandomness = _dice.NextRandomKey != randomBefore || _dice.Rolls.Count != rollsBefore;
        return affordable;
    }

    private bool TryResolveSpellCosts(
        Combatant actor,
        UseOption use,
        IReadOnlyDictionary<string, decimal>? offeredCosts,
        out IReadOnlyDictionary<string, decimal>? committedCosts,
        out string? reason)
    {
        committedCosts = offeredCosts;
        reason = null;

        if (use.Spell is not Definition spell || actor.CastsLeft.ContainsKey(spell))
        {
            return true;
        }

        if (committedCosts is null)
        {
            try
            {
                committedCosts = SpellCostValues(actor, spell);
            }
            catch (RuleFailure)
            {
                reason = $"{actor.Name} cannot cast {use.Name}.";
                return false;
            }
        }

        try
        {
            if (!SpellAffordable(actor, spell, committedCosts))
            {
                reason = CommittedSpellCostReason(actor, use, spell, committedCosts);
                return false;
            }
        }
        catch (RuleFailure)
        {
            // The quote may already have consumed random values. The caller
            // records that changed state instead of returning a false refusal.
            reason = $"{actor.Name} cannot cast {use.Name}; its committed price could not be evaluated.";
            return false;
        }

        return true;
    }

    private string CommittedSpellCostReason(
        Combatant actor,
        UseOption use,
        Definition spell,
        IReadOnlyDictionary<string, decimal> committedCosts)
    {
        List<string> amounts = [];
        foreach ((string trackId, decimal amount) in committedCosts)
        {
            Definition track = _rules.Reference(spell, $"$.cost.{trackId}");
            decimal current = _evaluator.TrackCurrent(actor.Creature, track);
            amounts.Add($"{amount.ToString(CultureInfo.InvariantCulture)} {trackId} (has {current.ToString(CultureInfo.InvariantCulture)})");
        }

        return $"{actor.Name} cannot cast {use.Name} at its committed price ({string.Join(", ", amounts)}).";
    }

    private void ReplacePendingActionChoice(CombatActionChoice choice)
    {
        if (_pendingDecision is null)
        {
            return;
        }

        int index = _pendingDecision.Actions.ToList().FindIndex(action => action.Id == choice.Id);
        if (index < 0)
        {
            return;
        }

        List<CombatActionChoice> actions = _pendingDecision.Actions.ToList();
        actions[index] = choice;
        _pendingDecision = _pendingDecision with { Actions = actions };
    }

    private void RemovePendingActionChoice(string actionId)
    {
        if (_pendingDecision is null)
        {
            return;
        }

        List<CombatActionChoice> actions = _pendingDecision.Actions
            .Where(action => action.Id != actionId)
            .ToList();
        _pendingDecision = _pendingDecision with
        {
            Actions = actions,
            Moves = actions.SelectMany(action => action.Moves).Distinct().ToList(),
        };
    }

    /// <summary>Spends the already evaluated spell cost from the caster's tracks.</summary>
    private void PaySpell(
        Combatant caster,
        Definition spell,
        IReadOnlyDictionary<string, decimal>? committedCosts = null)
    {
        if (!spell.Json.TryGetProperty("cost", out JsonElement cost))
        {
            return;
        }

        IReadOnlyDictionary<string, decimal> costs = committedCosts ?? SpellCostValues(caster, spell);
        foreach (JsonProperty entry in cost.EnumerateObject())
        {
            Definition track = _rules.Reference(spell, $"$.cost.{entry.Name}");
            decimal amount = costs[entry.Name];
            TrackValue value = caster.Creature.Track(track.Id);
            value.Current = _evaluator.TrackCurrent(caster.Creature, track) - amount;
            Record(new SpentFact(caster.Name, track, amount, value.Current.Value), subject: caster);
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

    private void Record(
        CombatFact fact,
        int? rollsBefore = null,
        Combatant? subject = null,
        IEnumerable<Combatant>? targets = null,
        IReadOnlyList<DiceRoll>? committedRolls = null)
    {
        if (subject is not null || targets is not null)
        {
            fact = fact with
            {
                SubjectIds = subject is null ? fact.SubjectIds : [subject.Id],
                TargetIds = targets is null ? fact.TargetIds : targets.Select(target => target.Id).ToList(),
            };
        }

        if (committedRolls is not null)
        {
            fact = fact with { Rolls = [.. committedRolls, .._dice.Rolls.Skip(rollsBefore ?? _dice.Rolls.Count)] };
        }
        else if (rollsBefore is int before && _dice.Rolls.Count > before)
        {
            fact = fact with { Rolls = _dice.Rolls.Skip(before).ToList() };
        }

        _facts.Add(fact);
    }
}
