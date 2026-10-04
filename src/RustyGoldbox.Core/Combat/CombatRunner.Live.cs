using System.Text.Json;
using RustyGoldbox.Core.Definitions;
using RustyGoldbox.Core.Expressions;
using RustyGoldbox.Core.Rules;

namespace RustyGoldbox.Core.Combat;

public sealed partial class CombatRunner
{
    private int _maxRounds;
    private int _round;
    private bool _started;
    private bool _roundOpen;
    private bool _turnPrepared;
    private int _turnIndex;
    private List<Combatant> _order = [];
    private HashSet<Combatant> _tookTurns = [];
    private CombatPhase _phase = CombatPhase.NotStarted;
    private CombatDecision? _pendingDecision;
    private Combatant? _activeActor;
    private IReadOnlyList<Cell>? _requestedPath;
    private bool _requestedPathConsumed;
    private IReadOnlyList<Combatant>? _requestedTargets;
    private UseOption? _pendingTargetUse;
    private Combatant? _pendingTargetActor;
    private List<Combatant> _pendingTargetCandidates = [];
    private int? _pendingTargetCap;
    private int? _pendingTargetRollStart;
    private string? _lastActorId;
    private readonly Dictionary<Creature, Combatant> _previewOwners = [];

    /// <summary>Current phase without advancing the fight.</summary>
    public CombatPhase Phase => _phase;

    /// <summary>Current round; zero means the opening setup has not started a round.</summary>
    public int Round => _round;

    /// <summary>The actor whose turn is active, if a turn is in progress.</summary>
    public Combatant? ActiveActor => _activeActor;

    /// <summary>The pending decision, if a manual or optional controller must answer.</summary>
    public CombatDecision? PendingDecision => _pendingDecision;

    /// <summary>The one live state owner exposes its current fact list read-only.</summary>
    public IReadOnlyList<CombatFact> Facts => _facts;

    /// <summary>Creates the persistence snapshot through the shared persistence partial.</summary>
    public CombatContinuationState CaptureContinuation() => Capture();

    /// <summary>Resumes a live owner through the shared persistence partial.</summary>
    public static CombatRunner Resume(
        RuleSet rules,
        Definition combat,
        IReadOnlyList<CombatSide> sides,
        DiceRoller dice,
        CombatContinuationState state,
        Definition? encounter = null,
        CombatSetup? setup = null) => Restore(rules, combat, sides, dice, state, encounter, setup);

    private void AssignStableIds()
    {
        HashSet<string> used = new(StringComparer.Ordinal);
        for (int side = 0; side < _sides.Count; side++)
        {
            for (int index = 0; index < _sides[side].Members.Count; index++)
            {
                Combatant member = _sides[side].Members[index];
                string candidate = string.IsNullOrWhiteSpace(member.Id) ? $"side-{side + 1}-member-{index + 1}" : member.Id;
                string stable = candidate;
                int suffix = 2;
                while (!used.Add(stable))
                {
                    stable = $"{candidate}-{suffix++}";
                }

                member.Id = stable;
            }
        }
    }

    /// <summary>Starts a live owner and advances only through automatic work.</summary>
    public CombatObservation Start(int maxRounds)
    {
        if (_started)
        {
            return Observe();
        }

        if (maxRounds < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxRounds), "A combat needs at least one round.");
        }

        _maxRounds = maxRounds;
        InitializeCombat();
        _started = true;
        _phase = CombatPhase.Advancing;
        return Advance();
    }

    public CombatObservation Start() => Start(RoundLimit(_combat));

    private void InitializeCombat()
    {
        if (_combatInitialized)
        {
            return;
        }

        foreach (Combatant combatant in Everyone)
        {
            CheckDefeated(combatant);
        }

        RollSurprise();
        SetCombatMoment(0);
        foreach (Combatant combatant in Everyone)
        {
            Refill(combatant);
        }

        _combatInitialized = true;
    }

    public CombatObservation Advance()
    {
        if (!_started)
        {
            return Start();
        }

        try
        {
            while (_phase is CombatPhase.Advancing)
            {
                int? winner = Winner();
                if (winner is not null || StandingSides() <= 1 || (_round >= _maxRounds && !_roundOpen))
                {
                    EndCombat(winner);
                    break;
                }

                if (!_roundOpen)
                {
                    BeginRound();
                }

                Combatant? next = NextActor();
                if (next is null)
                {
                    FinishRound();
                    continue;
                }

                BeginTurn(next);
            }
        }
        catch (CombatSuspendedException)
        {
            // The optional decision has already committed every value needed
            // to resume. Observation itself is side-effect free.
        }

        return Observe();
    }

    public CombatObservation Observe()
    {
        return new CombatObservation(
            _phase,
            _round,
            _activeActor?.Id,
            _pendingDecision,
            Winner(),
            _fledSide,
            _facts.ToList(),
            Everyone.Select(member => new CombatantObservation(
                member.Id,
                member.Name,
                member.Side,
                member.Controller,
                member.Defeated,
                member.Escaped,
                member.SurprisedRounds,
                member.Creature.Position,
                member.Budget.ToDictionary(entry => entry.Key, entry => entry.Value),
                member.Creature.Tracks.ToDictionary(entry => entry.Key, entry => entry.Value.Current))).ToList(),
            _lastBehaviorTrace is null || !CollectBehaviorTraces ? null : _lastBehaviorTrace) with
        {
            BehaviorTraces = CollectBehaviorTraces ? _behaviorTraces.ToArray() : [],
        };
    }

    public bool SetController(string actorId, CombatControlMode mode)
    {
        Combatant? actor = Find(actorId);
        if (actor is null)
        {
            return false;
        }

        actor.Controller = mode;
        if (_pendingDecision is not null && _pendingDecision.ActorId == actorId && mode == CombatControlMode.Automatic && _phase == CombatPhase.AwaitingInterrupt)
        {
            try
            {
                SubmitDecision(new CombatCommand.Decide(_pendingDecision.Id));
            }
            catch (CombatSuspendedException)
            {
                // A nested optional choice remains visible to the caller.
            }

            return true;
        }

        if (_pendingDecision is not null && _pendingDecision.ActorId == actorId && mode == CombatControlMode.Automatic)
        {
            _pendingDecision = null;
            _phase = CombatPhase.Advancing;
            Advance();
        }

        return true;
    }

    public CombatCommandResult Submit(CombatCommand command)
    {
        if (_phase is CombatPhase.NotStarted)
        {
            return CombatCommandResult.Refused("Combat has not started.", Observe());
        }

        if (command is CombatCommand.Decide decision)
        {
            if (_phase is not CombatPhase.AwaitingInterrupt || _pendingDecision is null)
            {
                return CombatCommandResult.Refused("Combat is not waiting for an interrupt decision.", Observe());
            }

            try
            {
                return SubmitDecision(decision);
            }
            catch (CombatSuspendedException)
            {
                // A reaction or post-roll can itself expose one nested
                // decision. The outer answer remains accepted exactly once.
                return CombatCommandResult.AcceptedResult(Observe());
            }
        }

        if (_phase is not CombatPhase.AwaitingAction and not CombatPhase.AwaitingMovement || _pendingDecision is null)
        {
            return CombatCommandResult.Refused("Combat is not waiting for an action command.", Observe());
        }

        if (_pendingTargetUse is not null && command is CombatCommand.EndTurn)
        {
            return Refused("Choose the committed action's targets before ending the turn.");
        }

        try
        {
            return command switch
            {
                CombatCommand.EndTurn end => SubmitEndTurn(end),
                CombatCommand.Move move => SubmitMove(move),
                CombatCommand.UseAction use => SubmitAction(use),
                _ => CombatCommandResult.Refused("The combat command is not supported.", Observe()),
            };
        }
        catch (CombatSuspendedException)
        {
            return CombatCommandResult.AcceptedResult(Observe());
        }
    }

    private CombatCommandResult SubmitEndTurn(CombatCommand.EndTurn command)
    {
        if (!IsActiveActor(command.ActorId))
        {
            return Refused("That actor does not own the pending turn.");
        }

        FinishCurrentTurn();
        _phase = CombatPhase.Advancing;
        return CombatCommandResult.AcceptedResult(Advance());
    }

    private CombatCommandResult SubmitMove(CombatCommand.Move command)
    {
        return SubmitAction(new CombatCommand.UseAction(command.ActorId, command.ActionId, [command.TargetId], command.Path));
    }

    private CombatCommandResult SubmitAction(CombatCommand.UseAction command)
    {
        if (!IsActiveActor(command.ActorId))
        {
            return Refused("That actor does not own the pending turn.");
        }

        if (_pendingTargetUse is not null)
        {
            return SubmitCommittedTargets(command);
        }

        Combatant actor = _activeActor!;
        CombatActionChoice? choice = _pendingDecision!.Actions.FirstOrDefault(candidate => candidate.Id == command.ActionId)
            ?? _pendingDecision.Actions.FirstOrDefault(candidate => candidate.ActionId == command.ActionId);
        if (choice is null)
        {
            return Refused($"Action '{command.ActionId}' is not a legal action for {actor.Name}.");
        }

        UseOption? use = FindUse(actor, choice.Id);
        if (use is null)
        {
            return Refused("That action is no longer available to the active actor.");
        }

        List<Combatant> candidates = Targets(actor, use, preview: true);
        List<Combatant> targets = [];
        foreach (string targetId in command.TargetIds)
        {
            Combatant? target = candidates.FirstOrDefault(candidate => candidate.Id == targetId);
            if (target is null)
            {
                return Refused($"Target '{targetId}' is not legal for {use.Name}.");
            }

            targets.Add(target);
        }

        if (targets.Count == 0)
        {
            if (HasMaximumTargets(use.Action))
            {
                return CommitTargetSelection(actor, use, choice, candidates);
            }

            return Refused($"Action '{use.Name}' needs at least one target.");
        }

        if (!TryValidateExplicitTargets(actor, use, targets, candidates, out string? cardinalityReason))
        {
            return Refused(cardinalityReason!);
        }

        if (!Affordable(actor, use.Action))
        {
            return Refused($"{actor.Name} cannot afford {use.Name}.");
        }

        if (use.Spell is Definition spell && (!actor.CanCast(spell) || (!actor.CastsLeft.ContainsKey(spell) && !SpellAffordable(actor, spell))))
        {
            return Refused($"{actor.Name} cannot cast {use.Name}.");
        }

        if (command.Path is not null && !ActionHasMove(use.Action))
        {
            return Refused($"Action '{use.Name}' does not move.");
        }

        if (command.Path is not null && !PathIsLegal(actor, use, targets[0], command.Path))
        {
            return Refused("The requested movement path is obstructed, out of range or unaffordable.");
        }

        if (HasMaximumTargets(use.Action))
        {
            return CommitExplicitTargetSelection(actor, use, targets, command.Path);
        }

        ResolveChosenAction(actor, use, targets, command.Path, explicitTargets: true);
        _pendingDecision = null;
        return CombatCommandResult.AcceptedResult(AdvanceAfterAction());
    }

    private CombatCommandResult CommitTargetSelection(Combatant actor, UseOption use, CombatActionChoice choice, IReadOnlyList<Combatant> candidates)
    {
        if (!Affordable(actor, use.Action))
        {
            return Refused($"{actor.Name} cannot afford {use.Name}.");
        }

        int rollsBefore = _dice.Rolls.Count;
        Spend(actor, use.Action);
        PayUse(actor, use);
        decimal maximum = Number(use.Action, "$.max_targets", new Scope(actor.Creature, null, use.Parameters));
        int cap = (int)Math.Clamp(decimal.Floor(maximum), 0, candidates.Count);
        if (cap == 0)
        {
            _pendingDecision = null;
            return CombatCommandResult.AcceptedResult(AdvanceAfterAction());
        }

        _pendingTargetUse = use;
        _pendingTargetActor = actor;
        _pendingTargetCandidates = candidates.ToList();
        _pendingTargetCap = cap;
        _pendingTargetRollStart = rollsBefore;
        _pendingDecision = new CombatDecision(
            $"targets:{actor.Id}:{_round}:{_facts.Count}",
            CombatDecisionKind.Targets,
            actor.Id,
            _round,
            [choice with { Targets = candidates.Select(ToTargetChoice).ToList() }],
            [],
            false,
            ActionId: choice.Id,
            MaximumTargets: cap);
        _phase = CombatPhase.AwaitingAction;
        return CombatCommandResult.AcceptedResult(Observe());
    }

    private CombatCommandResult CommitExplicitTargetSelection(
        Combatant actor,
        UseOption use,
        List<Combatant> targets,
        IReadOnlyList<Cell>? path)
    {
        int rollsBefore = _dice.Rolls.Count;
        Spend(actor, use.Action);
        PayUse(actor, use);
        decimal maximum = Number(use.Action, "$.max_targets", new Scope(actor.Creature, null, use.Parameters));
        int cap = (int)Math.Clamp(decimal.Floor(maximum), 0, targets.Count);
        if (cap == 0)
        {
            _pendingDecision = null;
            return CombatCommandResult.AcceptedResult(AdvanceAfterAction());
        }

        ResolveChosenAction(
            actor,
            use,
            targets,
            path,
            alreadyPaid: true,
            committedMaxTargets: cap,
            rollsBefore: rollsBefore,
            explicitTargets: true);
        _pendingDecision = null;
        return CombatCommandResult.AcceptedResult(AdvanceAfterAction());
    }

    private CombatCommandResult SubmitCommittedTargets(CombatCommand.UseAction command)
    {
        if (_pendingTargetActor is null || _pendingTargetUse is null || _pendingTargetCap is not int cap || _pendingDecision is null)
        {
            return Refused("There is no committed target choice to answer.");
        }

        if (_pendingDecision.ActionId is not null && command.ActionId != _pendingDecision.ActionId)
        {
            return Refused("That target choice belongs to another action.");
        }

        if (command.TargetIds.Count == 0 || command.TargetIds.Count > cap)
        {
            return Refused($"Choose between one and {cap} target{(cap == 1 ? "" : "s")}.");
        }

        List<Combatant> targets = [];
        foreach (string targetId in command.TargetIds)
        {
            Combatant? target = _pendingTargetCandidates.FirstOrDefault(candidate => candidate.Id == targetId);
            if (target is null || targets.Contains(target))
            {
                return Refused($"Target '{targetId}' is not legal for {_pendingTargetUse.Name}.");
            }

            targets.Add(target);
        }

        if (command.Path is not null && !ActionHasMove(_pendingTargetUse.Action))
        {
            return Refused($"Action '{_pendingTargetUse.Name}' does not move.");
        }

        if (command.Path is not null && !PathIsLegal(_pendingTargetActor, _pendingTargetUse, targets[0], command.Path))
        {
            return Refused("The requested movement path is obstructed, out of range or unaffordable.");
        }

        Combatant actor = _pendingTargetActor;
        UseOption use = _pendingTargetUse;
        int? rollsBefore = _pendingTargetRollStart;
        ClearPendingTargetSelection();
        ResolveChosenAction(actor, use, targets, command.Path, alreadyPaid: true, committedMaxTargets: cap, rollsBefore: rollsBefore, explicitTargets: true);
        _pendingDecision = null;
        return CombatCommandResult.AcceptedResult(AdvanceAfterAction());
    }

    private void ClearPendingTargetSelection()
    {
        _pendingTargetUse = null;
        _pendingTargetActor = null;
        _pendingTargetCandidates = [];
        _pendingTargetCap = null;
        _pendingTargetRollStart = null;
    }

    private bool TryValidateExplicitTargets(
        Combatant actor,
        UseOption use,
        IReadOnlyList<Combatant> targets,
        IReadOnlyList<Combatant> candidates,
        out string? reason)
    {
        Definition action = use.Action;
        string kind = action.Json.GetProperty("target").GetString()!;
        bool hasMaximum = HasMaximumTargets(action);
        bool hasPortions = action.Json.TryGetProperty("portions", out _);

        if (!hasPortions && targets.Select(target => target.Id).Distinct(StringComparer.Ordinal).Count() != targets.Count)
        {
            reason = $"Action '{use.Name}' cannot target the same combatant more than once.";
            return false;
        }

        if (hasMaximum)
        {
            reason = null;
            return true;
        }

        if (hasPortions)
        {
            int? knownPortions = PreviewPortionCount(actor, use);
            if (knownPortions is int count && targets.Count > count)
            {
                reason = $"Action '{use.Name}' has only {count} portion{(count == 1 ? "" : "s")}; choose at most one target per portion.";
                return false;
            }

            reason = null;
            return true;
        }

        if (kind is "all_enemies" or "all_allies")
        {
            HashSet<string> legal = candidates.Select(candidate => candidate.Id).ToHashSet(StringComparer.Ordinal);
            if (targets.Count != legal.Count || targets.Any(target => !legal.Contains(target.Id)))
            {
                reason = $"Action '{use.Name}' must name every legal target exactly once.";
                return false;
            }

            reason = null;
            return true;
        }

        if (targets.Count != 1)
        {
            reason = $"Action '{use.Name}' accepts exactly one target.";
            return false;
        }

        reason = null;
        return true;
    }

    private int? PreviewPortionCount(Combatant actor, UseOption use)
    {
        decimal? value = PreviewNumber(use.Action, "$.portions", new Scope(actor.Creature, null, use.Parameters));
        return value is decimal count
            ? (int)Math.Clamp(decimal.Floor(count), 0, int.MaxValue)
            : null;
    }

    private CombatCommandResult Refused(string reason) => CombatCommandResult.Refused(reason, Observe());

    private CombatDecision BuildDecision(Combatant actor)
    {
        List<CombatActionChoice> actions = [];
        foreach ((UseOption use, List<Combatant> candidates) in Options(actor, preview: true))
        {
            int index = actor.Uses.IndexOf(use);
            if (index < 0)
            {
                continue;
            }

            List<CombatMoveChoice> moves = [];
            if (ActionHasMove(use.Action))
            {
                foreach (Combatant target in candidates)
                {
                    moves.AddRange(MoveChoices(actor, use, target));
                }
            }

            actions.Add(new CombatActionChoice(
                ActionUseId(actor, index, use),
                use.Action.QualifiedId,
                use.Name,
                use.Spell?.QualifiedId,
                use.Action.Json.GetProperty("cost").EnumerateObject().ToDictionary(entry => entry.Name, entry => entry.Value.GetInt32()),
                candidates.Select(ToTargetChoice).ToList(),
                moves,
                TargetKind(use.Action),
                TargetMode(use.Action),
                use.Action.Json.TryGetProperty("portions", out _)
                    ? PreviewPortionCount(actor, use)
                    : null));
        }

        return new CombatDecision(
            $"decision:{actor.Id}:{_round}:{_facts.Count}",
            CombatDecisionKind.Action,
            actor.Id,
            _round,
            actions,
            actions.SelectMany(action => action.Moves).Distinct().ToList(),
            true);
    }

    private CombatTargetChoice ToTargetChoice(Combatant target)
    {
        decimal? current = target.Creature.Tracks.TryGetValue(_track.Id, out TrackValue? value) ? value.Current : null;
        decimal? maximum = PreviewMaximum(target);
        return new CombatTargetChoice(target.Id, target.Name, target.Side, target.Defeated, target.Escaped, target.Creature.Position, current, maximum);
    }

    private static string ActionUseId(Combatant actor, int index, UseOption use) =>
        $"{actor.Id}/use/{index}/{use.Action.QualifiedId}";

    private UseOption? FindUse(Combatant actor, string id)
    {
        for (int index = 0; index < actor.Uses.Count; index++)
        {
            UseOption use = actor.Uses[index];
            if (ActionUseId(actor, index, use) == id)
            {
                return use;
            }
        }

        List<UseOption> actionMatches = actor.Uses.Where(use => use.Action.QualifiedId == id || use.Action.Id == id).ToList();
        return actionMatches.Count == 1 ? actionMatches[0] : null;
    }

    private bool HasPreviewAction(Combatant actor) => Options(actor, preview: true).Any();

    private static bool HasMaximumTargets(Definition action) => action.Json.TryGetProperty("max_targets", out _);

    private static string? TargetKind(Definition action) =>
        action.Json.TryGetProperty("target", out JsonElement target) ? target.GetString() : null;

    private static string TargetMode(Definition action)
    {
        if (HasMaximumTargets(action))
        {
            return "maximum";
        }

        if (TargetKind(action) is "all_enemies" or "all_allies")
        {
            return "all";
        }

        return action.Json.TryGetProperty("portions", out _) ? "portions" : "one";
    }

    private static bool ActionHasMove(Definition action) => ContainsOperation(action.Json, "move");

    private static bool ContainsOperation(JsonElement element, string name)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("op", out JsonElement operation) && operation.GetString() == name)
            {
                return true;
            }

            return element.EnumerateObject().Any(property => ContainsOperation(property.Value, name));
        }

        return element.ValueKind == JsonValueKind.Array && element.EnumerateArray().Any(item => ContainsOperation(item, name));
    }

    private JsonElement? FindMoveOperation(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("op", out JsonElement operation) && operation.GetString() == "move")
            {
                return element;
            }

            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (FindMoveOperation(property.Value) is JsonElement found)
                {
                    return found;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in element.EnumerateArray())
            {
                if (FindMoveOperation(item) is JsonElement found)
                {
                    return found;
                }
            }
        }

        return null;
    }

    private List<CombatMoveChoice> MoveChoices(Combatant actor, UseOption use, Combatant target)
    {
        if (_field is null || actor.Creature.Position is not Cell start || target.Creature.Position is not Cell goal)
        {
            return [];
        }

        if (!TryMovementSpec(use, actor, target, preview: true, out _, out _, out decimal allowed, out decimal within, out decimal? beyond, out bool away))
        {
            return [];
        }

        HashSet<Cell> blocked = BlockedCells(actor);
        Dictionary<Cell, (int Cost, IReadOnlyList<Cell> Path)> paths = Reachable(actor, start, (int)Math.Floor(allowed), blocked);
        return paths
            .Where(entry => entry.Key != start && MovementDestinationAllowed(entry.Key, goal, within, beyond, away))
            .OrderBy(entry => entry.Value.Cost)
            .ThenBy(entry => entry.Key.X)
            .ThenBy(entry => entry.Key.Y)
            .Take(64)
            .Select(entry => new CombatMoveChoice(entry.Key, entry.Value.Path, entry.Value.Cost))
            .ToList();
    }

    private string? FindMovePath(JsonElement element, string path = "$")
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("op", out JsonElement operation) && operation.GetString() == "move")
            {
                return path;
            }

            foreach (JsonProperty property in element.EnumerateObject())
            {
                string? found = FindMovePath(property.Value, $"{path}.{property.Name}");
                if (found is not null)
                {
                    return found;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            int index = 0;
            foreach (JsonElement item in element.EnumerateArray())
            {
                string? found = FindMovePath(item, $"{path}[{index++}]");
                if (found is not null)
                {
                    return found;
                }
            }
        }

        return null;
    }

    private bool TryMovementSpec(UseOption use, Combatant actor, Combatant target, bool preview, out JsonElement operation, out string movePath, out decimal allowed, out decimal within, out decimal? beyond, out bool away)
    {
        operation = FindMoveOperation(use.Action.Json) ?? default;
        movePath = FindMovePath(use.Action.Json) ?? "";
        allowed = 0;
        within = 1;
        beyond = null;
        away = false;
        if (operation.ValueKind == JsonValueKind.Undefined || movePath.Length == 0 || !operation.TryGetProperty("distance", out _))
        {
            return false;
        }

        Scope scope = new(actor.Creature, target.Creature, use.Parameters);
        decimal? distance = preview ? PreviewNumber(use.Action, $"{movePath}.distance", scope) : Number(use.Action, $"{movePath}.distance", scope);
        if (distance is not decimal knownDistance || knownDistance < 0)
        {
            return false;
        }

        allowed = knownDistance;
        away = operation.TryGetProperty("toward", out JsonElement toward) && toward.GetString() == "away";
        if (operation.TryGetProperty("within", out _))
        {
            decimal? value = preview ? PreviewNumber(use.Action, $"{movePath}.within", scope) : Number(use.Action, $"{movePath}.within", scope);
            if (value is not decimal knownWithin)
            {
                return false;
            }

            within = knownWithin;
        }

        if (operation.TryGetProperty("beyond", out _))
        {
            beyond = preview ? PreviewNumber(use.Action, $"{movePath}.beyond", scope) : Number(use.Action, $"{movePath}.beyond", scope);
            if (beyond is null)
            {
                return false;
            }
        }

        return true;
    }

    private bool MovementDestinationAllowed(Cell destination, Cell goal, decimal within, decimal? beyond, bool away)
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

    private HashSet<Cell> BlockedCells(Combatant actor)
    {
        return _field!.Zones
            ? []
            : Everyone.Where(member => member != actor && !member.Defeated && member.Creature.Position is not null)
                .Select(member => member.Creature.Position!.Value)
                .ToHashSet();
    }

    private Dictionary<Cell, (int Cost, IReadOnlyList<Cell> Path)> Reachable(Combatant actor, Cell start, int budget, HashSet<Cell> blocked)
    {
        Dictionary<Cell, (int Cost, IReadOnlyList<Cell> Path)> paths = new() { [start] = (0, []) };
        PriorityQueue<Cell, int> frontier = new();
        frontier.Enqueue(start, 0);
        while (frontier.TryDequeue(out Cell cell, out int cost))
        {
            if (paths[cell].Cost != cost)
            {
                continue;
            }

            foreach (Cell next in _field!.Neighbours(cell))
            {
                if (!_field.Passable(next) || blocked.Contains(next))
                {
                    continue;
                }

                int total = cost + _field.Cost(next);
                if (total > budget || paths.TryGetValue(next, out (int Cost, IReadOnlyList<Cell> Path) known) && known.Cost <= total)
                {
                    continue;
                }

                List<Cell> path = [.. paths[cell].Path, next];
                paths[next] = (total, path);
                frontier.Enqueue(next, total);
            }
        }

        return paths;
    }

    private bool PathIsLegal(Combatant actor, UseOption use, Combatant target, IReadOnlyList<Cell> path)
    {
        if (_field is null || actor.Creature.Position is not Cell start || path.Count == 0 || !ActionHasMove(use.Action) || target.Creature.Position is not Cell goal
            || !TryMovementSpec(use, actor, target, preview: true, out _, out _, out decimal allowed, out decimal within, out decimal? beyond, out bool away))
        {
            return false;
        }

        HashSet<Cell> blocked = BlockedCells(actor);
        Cell current = start;
        decimal spent = 0;
        foreach (Cell next in path)
        {
            if (!_field.Neighbours(current).Contains(next) || !_field.Passable(next) || blocked.Contains(next))
            {
                return false;
            }

            spent += _field.Cost(next);
            if (spent > allowed)
            {
                return false;
            }

            current = next;
        }

        return MovementDestinationAllowed(current, goal, within, beyond, away);
    }

    private decimal PreviewMissing(Combatant combatant)
    {
        decimal current = combatant.Creature.Tracks.TryGetValue(_track.Id, out TrackValue? value) && value.Current is decimal present ? present : 0;
        return (PreviewMaximum(combatant) ?? current) - current;
    }

    private decimal? PreviewMaximum(Combatant combatant)
    {
        if (combatant.Creature.Tracks.TryGetValue(_track.Id, out TrackValue? value) && value.Max is decimal own)
        {
            return own;
        }

        return _rules.TryExpression(_track, "$.max", out _)
            ? PreviewNumber(_track, "$.max", new Scope(combatant.Creature, null))
            : null;
    }

    private bool PreviewSpellAffordable(Combatant caster, Definition spell)
    {
        if (!spell.Json.TryGetProperty("cost", out JsonElement cost))
        {
            return true;
        }

        return cost.EnumerateObject().All(entry =>
        {
            Definition track = _rules.Reference(spell, $"$.cost.{entry.Name}");
            decimal? amount = PreviewNumber(spell, $"$.cost.{entry.Name}", new Scope(caster.Creature, null));
            decimal current = caster.Creature.Tracks.TryGetValue(track.Id, out TrackValue? value) && value.Current is decimal present ? present : 0;
            return amount is null || current >= amount.Value;
        });
    }

    private decimal? PreviewNumber(Definition owner, string path, Scope scope)
    {
        try
        {
            return _previewEvaluator.Evaluate(_rules.Expression(owner, path), PreviewScope(scope)).Number;
        }
        catch (Exception exception) when (exception is ExpressionException or OverflowException)
        {
            return null;
        }
    }

    private bool? PreviewBoolean(Definition owner, string path, Scope scope)
    {
        try
        {
            return _previewEvaluator.Evaluate(_rules.Expression(owner, path), PreviewScope(scope)).Boolean;
        }
        catch (Exception exception) when (exception is ExpressionException or OverflowException)
        {
            return null;
        }
    }

    private Scope PreviewScope(Scope scope)
    {
        Creature? self = scope.Self is Creature selfCreature ? CloneForPreview(selfCreature) : null;
        Creature? target = scope.Target is Creature targetCreature ? CloneForPreview(targetCreature) : null;
        return scope with { Self = self, Target = target };
    }

    private Creature CloneForPreview(Creature source)
    {
        Creature clone = new(source.Label)
        {
            Class = source.Class,
            Race = source.Race,
            Monster = source.Monster,
            Level = source.Level,
            Position = source.Position,
            AdvancingClasses = source.AdvancingClasses,
            FormerLevel = source.FormerLevel,
        };
        foreach ((Definition key, int value) in source.ClassLevels)
        {
            clone.ClassLevels[key] = value;
        }

        clone.LevelsTaken.AddRange(source.LevelsTaken);
        foreach ((string key, TrackValue value) in source.Tracks)
        {
            clone.Tracks[key] = new TrackValue { Current = value.Current, Max = value.Max };
        }

        foreach ((string key, decimal value) in source.Values)
        {
            clone.Values[key] = value;
        }

        foreach ((string key, decimal value) in source.AdvancementBonuses)
        {
            clone.AdvancementBonuses[key] = value;
        }

        clone.Conditions.AddRange(source.Conditions);
        foreach ((Definition condition, Dictionary<string, decimal> values) in source.ConditionValues)
        {
            clone.ConditionValues[condition] = new Dictionary<string, decimal>(values);
        }

        foreach ((string key, int value) in source.Rolled)
        {
            clone.Rolled[key] = value;
        }

        clone.Features.AddRange(source.Features);
        clone.Equipment.AddRange(source.Equipment);
        if (Everyone.FirstOrDefault(member => member.Creature == source) is Combatant owner)
        {
            _previewOwners[clone] = owner;
        }

        return clone;
    }

    private CombatObservation AdvanceAfterAction()
    {
        if (StandingSides() <= 1 || _activeActor is null || _activeActor.Defeated || !HasPreviewAction(_activeActor))
        {
            FinishCurrentTurn();
            _phase = CombatPhase.Advancing;
            return _resolvingBehavior ? Observe() : Advance();
        }

        _pendingDecision = BuildDecision(_activeActor);
        _phase = CombatPhase.AwaitingAction;
        return Observe();
    }

    private bool IsActiveActor(string actorId) => _pendingDecision?.ActorId == actorId && _activeActor?.Id == actorId;

    private Combatant? Find(string id) => Everyone.FirstOrDefault(member => member.Id == id);

    private Combatant FindUseActor(string id) => Find(id) ?? throw new InvalidOperationException($"Unknown combatant '{id}'.");

    private void BeginRound()
    {
        _round++;
        SetCombatMoment(_round);
        Record(new RoundFact(_round));
        if (RolledByRound)
        {
            foreach (Combatant member in Everyone)
            {
                member.Creature.Rolled.Clear();
            }
        }

        bool rollEachRound = _combat.Json.TryGetProperty("initiative_each", out JsonElement initiativeEach) && initiativeEach.GetString() == "round";
        if (!ElectiveInitiative && (_order.Count == 0 || rollEachRound))
        {
            _order = TurnOrder();
        }

        _tookTurns.Clear();
        _turnIndex = 0;
        _roundOpen = true;
    }

    private Combatant? NextActor()
    {
        if (ElectiveInitiative)
        {
            if (TakeResumedInitiative(out Combatant? resumed))
            {
                return resumed;
            }

            Combatant? next = NextElective(_lastActor, _tookTurns);
            if (next is not null)
            {
                _lastActor = next;
                _lastActorId = next.Id;
            }

            return next;
        }

        while (_turnIndex < _order.Count)
        {
            Combatant next = _order[_turnIndex++];
            if (!_tookTurns.Contains(next))
            {
                return next;
            }
        }

        return null;
    }

    private void BeginTurn(Combatant actor)
    {
        _activeActor = actor;
        _turn = actor;
        if (ElectiveInitiative)
        {
            _lastActorId = actor.Id;
        }

        if (actor.Defeated)
        {
            if (DownedConditions && !actor.Escaped)
            {
                DownedTurn(actor);
            }

            FinishCurrentTurn();
            return;
        }

        _turnPrepared = true;
        if (!RolledByRound)
        {
            actor.Creature.Rolled.Clear();
        }

        CountDown(actor, atStart: true);
        if (actor.SurprisedRounds > 0)
        {
            Record(new TurnSkippedFact(actor.Name, "surprised"), subject: actor);
            FinishCurrentTurn();
            return;
        }

        foreach (Definition condition in actor.Creature.Conditions.ToList())
        {
            if (condition.Json.TryGetProperty("each_turn", out JsonElement operations))
            {
                RunOperations(condition, operations, "$.each_turn", ConditionScope(actor, condition), actor, null);
                if (actor.Defeated)
                {
                    FinishCurrentTurn();
                    return;
                }
            }
        }

        if (ShouldFlee(actor))
        {
            Escape(actor);
            FinishCurrentTurn();
            return;
        }

        Definition? preventing = actor.Creature.Conditions.FirstOrDefault(condition =>
            condition.Json.TryGetProperty("prevents_actions", out JsonElement prevents) && prevents.GetBoolean());
        if (preventing is not null)
        {
            Record(new TurnSkippedFact(actor.Name, preventing.Name.ToLowerInvariant()), subject: actor);
            FinishCurrentTurn();
            return;
        }

        Refill(actor);
        if (actor.Controller == CombatControlMode.Automatic)
        {
            ResolveAutomaticTurn(actor);
            FinishCurrentTurn();
            return;
        }

        CombatDecision decision = BuildDecision(actor);
        if (decision.Actions.Count == 0 && decision.Moves.Count == 0)
        {
            Record(new TurnSkippedFact(actor.Name, "no action it can take"), subject: actor);
            FinishCurrentTurn();
            return;
        }

        _pendingDecision = decision;
        _phase = CombatPhase.AwaitingAction;
    }

    private void ResolveAutomaticTurn(Combatant actor)
    {
        if (HasBehavior(actor))
        {
            ResolveBehaviorAutomaticTurn(actor);
            return;
        }

        bool acted = false;
        while (!actor.Defeated && StandingSides() > 1 && Choose(actor) is (UseOption use, List<Combatant> targets))
        {
            ResolveChosenAction(actor, use, targets, null, explicitTargets: false);
            acted = true;
        }

        if (!acted && !actor.Defeated)
        {
            Record(new TurnSkippedFact(actor.Name, "no action it can take"), subject: actor);
        }
    }

    private void ResolveChosenAction(
        Combatant actor,
        UseOption use,
        List<Combatant> targets,
        IReadOnlyList<Cell>? path,
        bool alreadyPaid = false,
        int? committedMaxTargets = null,
        int? rollsBefore = null,
        bool explicitTargets = false)
    {
        if (!alreadyPaid)
        {
            Spend(actor, use.Action);
        }

        _requestedPath = path;
        _requestedPathConsumed = false;
        _requestedTargets = targets;
        try
        {
            Act(actor, use, targets, explicitTargets ? targets : null, committedMaxTargets, rollsBefore, alreadyPaid);
        }
        finally
        {
            _requestedPath = null;
            _requestedPathConsumed = false;
            _requestedTargets = null;
        }
    }

    private void FinishCurrentTurn()
    {
        if (_activeActor is null)
        {
            return;
        }

        if (_turnPrepared)
        {
            EndTurn(_activeActor);
        }

        _tookTurns.Add(_activeActor);
        _turn = null;
        _turnPrepared = false;
        _activeActor = null;
        _pendingDecision = null;
    }

    private void FinishRound()
    {
        RunDownedTurnsForLiveRound();
        _roundOpen = false;
        _order = ElectiveInitiative ? [] : _order;
        _tookTurns.Clear();
        int? winner = Winner();
        if (winner is not null || StandingSides() <= 1 || _round >= _maxRounds)
        {
            EndCombat(winner);
        }
    }

    private void RunDownedTurnsForLiveRound()
    {
        if (!DownedConditions || StandingSides() <= 1)
        {
            return;
        }

        foreach (Combatant downed in Everyone.Where(member => member.Defeated && !member.Escaped && !_tookTurns.Contains(member)).ToList())
        {
            _activeActor = downed;
            _turn = downed;
            DownedTurn(downed);
            _turn = null;
            _activeActor = null;
            _tookTurns.Add(downed);
        }
    }

    private void EndCombat(int? winner)
    {
        if (_phase == CombatPhase.Ended)
        {
            return;
        }

        _pendingDecision = null;
        _turn = null;
        _activeActor = null;
        _phase = CombatPhase.Ended;
        Record(new EndFact(winner is int side ? _sides[side].Name : null, _round));
    }

    private void SetCombatMoment(int round)
    {
        CombatMoment moment = new(round, Everyone.Any(member => member.SurprisedRounds > 0), Distance, Nearest, CanSee, AlliesNear);
        _evaluator.Combat = moment;
        _previewEvaluator.Combat = moment;
    }

    private CombatResult RunAllAutomatic(int maxRounds)
    {
        foreach (Combatant member in Everyone)
        {
            member.Controller = CombatControlMode.Automatic;
        }

        Start(maxRounds);
        while (_phase != CombatPhase.Ended)
        {
            if (_pendingDecision is null)
            {
                Advance();
                continue;
            }

            throw new InvalidOperationException("Automatic combat reached an unresolved decision.");
        }

        return new CombatResult(
            _facts,
            Winner(),
            _round,
            _sides,
            _track,
            _fledSide,
            CollectBehaviorTraces ? _behaviorTraces.ToArray() : null);
    }
}
