using System.Text.Json;
using RustyGoldbox.Core.Definitions;
using RustyGoldbox.Core.Expressions;
using RustyGoldbox.Core.Rules;

namespace RustyGoldbox.Core.Combat;

/// <summary>Serializable continuation state for one authored behavior assignment.</summary>
public sealed class CombatBehaviorState
{
    public string? BehaviorId { get; set; }

    public int RuleIndex { get; set; }

    public int StepIndex { get; set; }

    public bool Committed { get; set; }

    /// <summary>Whether the committed state is waiting for a destination move before the authored step.</summary>
    public bool MovementOnly { get; set; }

    public CombatBehaviorState Clone() => new()
    {
        BehaviorId = BehaviorId,
        RuleIndex = RuleIndex,
        StepIndex = StepIndex,
        Committed = Committed,
        MovementOnly = MovementOnly,
    };
}

/// <summary>A proposed action or explicit fallback from an authored behavior.</summary>
public sealed record CombatBehaviorAlternativeTrace(
    int RuleIndex,
    int StepIndex,
    string RulePath,
    string StepPath,
    string? Guard,
    bool? GuardResult,
    string? Priority,
    decimal? PriorityValue,
    string? Score,
    decimal? ScoreValue,
    string Status,
    string? Reason,
    string? ActionId,
    string? TargetId,
    string? DestinationKind,
    decimal? DestinationDistance);

/// <summary>
/// Primitive diagnostics for one behavior proposal. It is safe to expose to a
/// transcript or CLI because it contains expression text and evaluated values,
/// never evaluator objects, executable definitions or random state.
/// </summary>
public sealed record CombatBehaviorTrace(
    string ActorId,
    string? BehaviorId,
    int? RuleIndex,
    int? StepIndex,
    string? StepPath,
    string? ActionId,
    string? TargetId,
    string? DestinationKind,
    decimal? DestinationDistance,
    decimal? Rank,
    bool MovementOnly,
    CombatBehaviorFallback? Fallback,
    string? Reason,
    IReadOnlyList<CombatBehaviorAlternativeTrace> Alternatives)
{
    /// <summary>The live combat round at which this proposal was inspected.</summary>
    public int Round { get; init; }

    /// <summary>Module source identity for the behavior definition.</summary>
    public string? BehaviorModule { get; init; }

    /// <summary>Source file for the behavior definition.</summary>
    public string? BehaviorFile { get; init; }

    /// <summary>Whether the trace ended a committed rule or step because it was no longer legal.</summary>
    public bool Abandoned { get; init; }
}

/// <summary>A proposed action or explicit fallback from an authored behavior.</summary>
public sealed record CombatBehaviorProposal(
    string ActorId,
    CombatBehaviorProfile? Behavior,
    int? RuleIndex,
    int? StepIndex,
    CombatCommand? Command,
    CombatBehaviorFallback? Fallback,
    string? Reason,
    bool MovementOnly = false)
{
    public bool EndsTurn => Fallback == CombatBehaviorFallback.EndTurn;

    public bool Flees => Fallback == CombatBehaviorFallback.Flee;

    /// <summary>Read-only primitive diagnostics suitable for CLI trace output.</summary>
    public CombatBehaviorTrace? Trace { get; init; }
}

/// <summary>
/// Evaluates module-authored combat behavior against the current legal choices.
/// It never resolves a command and never mutates combat state while proposing one.
/// The live combat owner commits the returned command through its normal resolver,
/// then calls <see cref="Commit"/> to advance the short authored sequence.
/// </summary>
public sealed class CombatBehaviorController
{
    private readonly RuleSet _rules;
    private readonly Definition? _combat;
    private readonly CombatField? _field;
    private readonly Evaluator _evaluator;
    private readonly Dictionary<string, CombatBehaviorProfile?> _overrides = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CombatBehaviorState> _states = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Combatant> _actors = new(StringComparer.Ordinal);

    public CombatBehaviorController(RuleSet rules, Definition? combat = null, CombatField? field = null)
    {
        _rules = rules;
        _combat = combat;
        _field = field;
        _evaluator = new Evaluator(rules, null);
    }

    /// <summary>Current per-actor state, copied so observation cannot mutate it.</summary>
    public IReadOnlyDictionary<string, CombatBehaviorState> States => _states.ToDictionary(entry => entry.Key, entry => entry.Value.Clone(), StringComparer.Ordinal);

    /// <summary>
    /// Explicit assignments, including null opt-outs, keyed by stable actor ID.
    /// The live save owner can persist these qualified IDs separately from the
    /// transient rule and step state.
    /// </summary>
    public IReadOnlyDictionary<string, string?> ExplicitAssignments => _overrides.ToDictionary(
        entry => entry.Key,
        entry => entry.Value?.Definition.QualifiedId,
        StringComparer.Ordinal);

    /// <summary>Assigns an explicit policy, including null to disable an inherited policy.</summary>
    public void Assign(string actorId, CombatBehaviorProfile? behavior)
    {
        _overrides[actorId] = behavior;
        Reset(actorId);
    }

    /// <summary>Registers the live combatants whose creatures target expressions may read.</summary>
    public void Register(IEnumerable<Combatant> actors)
    {
        foreach (Combatant actor in actors)
        {
            _actors[actor.Id] = actor;
        }
    }

    /// <summary>Assigns a policy by definition, resolving it through the checked rule set.</summary>
    public void Assign(string actorId, Definition behavior)
    {
        if (behavior.Type != DefinitionTypes.CombatBehavior)
        {
            throw new ArgumentException($"'{behavior.QualifiedId}' is not a combat-behavior definition.", nameof(behavior));
        }

        if (_rules.CombatBehaviorOf(behavior) is not CombatBehaviorProfile profile)
        {
            throw new ArgumentException($"'{behavior.QualifiedId}' is not a checked combat behavior.", nameof(behavior));
        }

        Assign(actorId, profile);
    }

    /// <summary>Removes an explicit override so the actor can inherit its module policy again.</summary>
    public void ClearAssignment(string actorId)
    {
        _overrides.Remove(actorId);
        Reset(actorId);
    }

    /// <summary>Resets a policy's rule and step commitment at a combat boundary.</summary>
    public void Reset(string actorId)
    {
        _states.Remove(actorId);
    }

    /// <summary>Replaces state restored by the live combat save owner.</summary>
    public void RestoreState(string actorId, CombatBehaviorState state)
    {
        _states[actorId] = state.Clone();
    }

    /// <summary>
    /// Returns a policy for an actor according to explicit override, monster
    /// assignment, and combat default precedence. A party or companion can be
    /// opted into a policy with <see cref="Assign(string, CombatBehaviorProfile?)"/>.
    /// </summary>
    public CombatBehaviorProfile? ProfileFor(Combatant actor)
    {
        if (_overrides.TryGetValue(actor.Id, out CombatBehaviorProfile? overrideProfile))
        {
            return overrideProfile;
        }

        if (actor.Creature.Monster is Definition monster
            && monster.Json.TryGetProperty("behavior", out _)
            && _rules.References.TryGetValue((monster, "$.behavior"), out Definition? assigned)
            && _rules.CombatBehaviorOf(assigned) is CombatBehaviorProfile monsterProfile)
        {
            return monsterProfile;
        }

        if (actor.Character?.Npc is Definition npc
            && npc.Json.TryGetProperty("behavior", out _)
            && _rules.References.TryGetValue((npc, "$.behavior"), out Definition? npcBehavior)
            && _rules.CombatBehaviorOf(npcBehavior) is CombatBehaviorProfile npcProfile)
        {
            return npcProfile;
        }

        if (_combat is not null
            && _combat.Json.TryGetProperty("behavior", out _)
            && _rules.References.TryGetValue((_combat, "$.behavior"), out Definition? defaultDefinition)
            && _rules.CombatBehaviorOf(defaultDefinition) is CombatBehaviorProfile defaultProfile)
        {
            return defaultProfile;
        }

        return null;
    }

    /// <summary>
    /// Proposes the next legal command for an automatic actor. It evaluates only
    /// deterministic policy expressions and reads the supplied observation; no
    /// action cost, dice, target mutation or state transition occurs here.
    /// </summary>
    public CombatBehaviorProposal? Propose(Combatant actor, CombatObservation observation)
    {
        if (actor.Controller != CombatControlMode.Automatic
            || observation.PendingDecision is not CombatDecision pending
            || pending.Kind != CombatDecisionKind.Action
            || pending.ActorId != actor.Id)
        {
            return null;
        }

        _actors[actor.Id] = actor;
        SetCombatMoment(observation);

        CombatBehaviorProfile? behavior = ProfileFor(actor);
        if (behavior is null)
        {
            return null;
        }

        CombatBehaviorState state = _states.TryGetValue(actor.Id, out CombatBehaviorState? saved)
            ? saved
            : new CombatBehaviorState();
        if (state.BehaviorId is not null && state.BehaviorId != behavior.Definition.QualifiedId)
        {
            state = new CombatBehaviorState();
        }

        List<CombatBehaviorAlternativeTrace> alternatives = [];
        List<(CombatBehaviorRule Rule, int Index, int StepIndex, CombatCommand Command, bool MovementOnly, decimal Rank, int TraceIndex)> candidates = [];
        foreach ((CombatBehaviorRule rule, int ruleIndex) in CandidateRules(behavior, state))
        {
            CombatBehaviorStep? step = state.Committed && state.RuleIndex == ruleIndex && state.StepIndex < rule.Steps.Count
                ? rule.Steps[state.StepIndex]
                : rule.Steps.FirstOrDefault();
            int stepIndex = state.Committed && state.RuleIndex == ruleIndex ? state.StepIndex : 0;
            if (step is null)
            {
                continue;
            }

            UseOption? intendedUse = FindUse(actor, step);
            CombatActionChoice? choice = intendedUse is not null ? FindAction(actor, pending, step, intendedUse) : null;
            IReadOnlyList<CombatTargetChoice> selectedTargets = [];
            CombatTargetChoice? target = null;
            if (choice is not null)
            {
                selectedTargets = ChooseTargets(actor, choice, step, behavior);
                target = selectedTargets.FirstOrDefault();
            }
            else if (step.Destination is not null && intendedUse is not null)
            {
                target = ChooseTarget(actor, RegisteredTargets(pending), step, behavior, intendedUse);
                selectedTargets = target is null ? [] : [target];
            }
            if (target is null)
            {
                alternatives.Add(new CombatBehaviorAlternativeTrace(
                    ruleIndex,
                    stepIndex,
                    $"$.rules[{ruleIndex}]",
                    step.Path,
                    rule.When?.Text,
                    null,
                    rule.Priority?.Text,
                    null,
                    rule.Score?.Text,
                    null,
                    "unavailable",
                    intendedUse is null
                        ? "The authored action use is unavailable to this actor."
                        : choice is null
                            ? "The authored action is not a current legal choice."
                            : "No legal target matches the step's target group.",
                    choice?.Id ?? intendedUse?.Action.QualifiedId,
                    null,
                    step.Destination?.Kind,
                    null));
                continue;
            }

            Creature? targetCreature = FindCreature(target.Id);
            if (rule.When is not null && !EvaluateBoolean(rule.When, actor, targetCreature, behavior))
            {
                alternatives.Add(new CombatBehaviorAlternativeTrace(
                    ruleIndex,
                    stepIndex,
                    $"$.rules[{ruleIndex}]",
                    step.Path,
                    rule.When.Text,
                    false,
                    rule.Priority?.Text,
                    null,
                    rule.Score?.Text,
                    null,
                    "guarded",
                    "The rule guard evaluated false.",
                    choice?.Id ?? intendedUse?.Action.QualifiedId,
                    target.Id,
                    step.Destination?.Kind,
                    null));
                continue;
            }

            decimal? priorityValue = rule.Priority is not null
                ? EvaluateNumber(rule.Priority, actor, targetCreature, behavior)
                : null;
            decimal? scoreValue = rule.Score is not null
                ? EvaluateNumber(rule.Score, actor, targetCreature, behavior)
                : null;
            decimal rank = priorityValue ?? scoreValue ?? 0;

            IReadOnlyList<Cell>? path = choice is not null
                ? ChoosePath(actor, target, choice, step, behavior)
                : null;
            bool destinationSatisfied = step.Destination is null
                || DestinationSatisfied(actor, target, step.Destination, behavior);
            bool actionNeedsMovement = choice is null
                && intendedUse is not null
                && target.Position is Cell
                && actor.Creature.Position is Cell actorPosition
                && !ActionLegalAt(intendedUse, actor, target, behavior, actorPosition);
            decimal? destinationDistance = step.Destination is CombatBehaviorDestination destinationForTrace && targetCreature is not null
                ? EvaluateNumber(destinationForTrace.Distance, actor, targetCreature, behavior)
                : null;
            if ((!destinationSatisfied || actionNeedsMovement)
                && path is null
                && step.Destination is CombatBehaviorDestination destination
                && FindMovement(pending, actor, target, destination, behavior, intendedUse, actionNeedsMovement) is (CombatActionChoice movement, IReadOnlyList<Cell> movementPath))
            {
                int traceIndex = alternatives.Count;
                alternatives.Add(new CombatBehaviorAlternativeTrace(
                    ruleIndex,
                    stepIndex,
                    $"$.rules[{ruleIndex}]",
                    step.Path,
                    rule.When?.Text,
                    rule.When is null ? null : true,
                    rule.Priority?.Text,
                    priorityValue,
                    rule.Score?.Text,
                    scoreValue,
                    "eligible",
                    "A legal authored movement use reaches the preferred destination before the intended step.",
                    movement.Id,
                    target.Id,
                    destination.Kind,
                    destinationDistance));
                candidates.Add((
                    rule,
                    ruleIndex,
                    stepIndex,
                    new CombatCommand.UseAction(actor.Id, movement.Id, [target.Id], movementPath),
                    true,
                    rank,
                    traceIndex));
                continue;
            }

            if (!destinationSatisfied && path is null && step.Destination is not null)
            {
                alternatives.Add(new CombatBehaviorAlternativeTrace(
                    ruleIndex,
                    stepIndex,
                    $"$.rules[{ruleIndex}]",
                    step.Path,
                    rule.When?.Text,
                    rule.When is null ? null : true,
                    rule.Priority?.Text,
                    priorityValue,
                    rule.Score?.Text,
                    scoreValue,
                    "blocked",
                    "The destination is not satisfied and no legal authored movement choice reaches it.",
                    choice?.Id ?? intendedUse?.Action.QualifiedId,
                    target.Id,
                    step.Destination.Kind,
                    destinationDistance));
                continue;
            }

            if (choice is null)
            {
                alternatives.Add(new CombatBehaviorAlternativeTrace(
                    ruleIndex,
                    stepIndex,
                    $"$.rules[{ruleIndex}]",
                    step.Path,
                    rule.When?.Text,
                    rule.When is null ? null : true,
                    rule.Priority?.Text,
                    priorityValue,
                    rule.Score?.Text,
                    scoreValue,
                    "unavailable",
                    "The authored action is not a current legal choice.",
                    intendedUse?.Action.QualifiedId,
                    target.Id,
                    step.Destination?.Kind,
                    destinationDistance));
                continue;
            }

            int normalTraceIndex = alternatives.Count;
            alternatives.Add(new CombatBehaviorAlternativeTrace(
                ruleIndex,
                stepIndex,
                $"$.rules[{ruleIndex}]",
                step.Path,
                rule.When?.Text,
                rule.When is null ? null : true,
                rule.Priority?.Text,
                priorityValue,
                rule.Score?.Text,
                scoreValue,
                "eligible",
                "The intended action is currently legal.",
                choice.Id,
                target.Id,
                step.Destination?.Kind,
                destinationDistance));
            if (choice is not null)
            {
                candidates.Add((
                    rule,
                    ruleIndex,
                    stepIndex,
                    new CombatCommand.UseAction(actor.Id, choice.Id, selectedTargets.Select(candidate => candidate.Id).ToArray(), path),
                    false,
                    rank,
                    normalTraceIndex));
            }
        }

        if (candidates.Count > 0)
        {
            (CombatBehaviorRule _, int ruleIndex, int stepIndex, CombatCommand command, bool movementOnly, decimal rank, int traceIndex) = candidates
                .OrderByDescending(candidate => candidate.Rank)
                .ThenBy(candidate => candidate.Index)
                .First();
            alternatives[traceIndex] = alternatives[traceIndex] with { Status = "selected" };
            CombatBehaviorAlternativeTrace selected = alternatives[traceIndex];
            return new CombatBehaviorProposal(
                actor.Id,
                behavior,
                ruleIndex,
                stepIndex,
                command,
                null,
                null,
                movementOnly)
            {
                Trace = new CombatBehaviorTrace(
                    actor.Id,
                    behavior.Definition.QualifiedId,
                    ruleIndex,
                    stepIndex,
                    selected.StepPath,
                    selected.ActionId,
                    selected.TargetId,
                    selected.DestinationKind,
                    selected.DestinationDistance,
                    rank,
                    movementOnly,
                    null,
                    selected.Reason,
                    alternatives.ToArray())
                {
                    BehaviorModule = behavior.Definition.Module,
                    BehaviorFile = behavior.Definition.File,
                },
            };
        }

        CombatBehaviorFallback fallback = state.Committed && state.RuleIndex >= 0 && state.RuleIndex < behavior.Rules.Count
            ? behavior.Rules[state.RuleIndex].Fallback
            : behavior.Fallback;
        const string reason = "No authored step is currently legal.";
        bool abandoned = state.Committed && state.RuleIndex >= 0 && state.RuleIndex < behavior.Rules.Count;
        int? abandonedRule = abandoned ? state.RuleIndex : null;
        int? abandonedStep = abandoned ? state.StepIndex : null;
        string? abandonedPath = abandoned
            && state.StepIndex >= 0
            && state.StepIndex < behavior.Rules[state.RuleIndex].Steps.Count
            ? behavior.Rules[state.RuleIndex].Steps[state.StepIndex].Path
            : null;
        return new CombatBehaviorProposal(actor.Id, behavior, null, null, null, fallback, reason)
        {
            Trace = new CombatBehaviorTrace(
                actor.Id,
                behavior.Definition.QualifiedId,
                abandonedRule,
                abandonedStep,
                abandonedPath,
                null,
                null,
                null,
                null,
                null,
                false,
                fallback,
                reason,
                alternatives.ToArray())
            {
                BehaviorModule = behavior.Definition.Module,
                BehaviorFile = behavior.Definition.File,
                    Abandoned = abandoned,
            },
        };
    }

    /// <summary>Commits a proposal after the shared resolver accepted its command.</summary>
    public void Commit(CombatBehaviorProposal proposal)
    {
        if (proposal.Behavior is null || proposal.RuleIndex is not int ruleIndex || proposal.StepIndex is not int stepIndex)
        {
            return;
        }

        CombatBehaviorRule rule = proposal.Behavior.Rules[ruleIndex];
        CombatBehaviorState state = _states.TryGetValue(proposal.ActorId, out CombatBehaviorState? current)
            ? current
            : new CombatBehaviorState();
        if (proposal.MovementOnly)
        {
            state.BehaviorId = proposal.Behavior.Definition.QualifiedId;
            state.RuleIndex = ruleIndex;
            state.StepIndex = stepIndex;
            state.Committed = true;
            state.MovementOnly = true;
            _states[proposal.ActorId] = state;
            return;
        }

        if (rule.Commitment is CombatBehaviorCommitment.Plan or CombatBehaviorCommitment.Step
            && stepIndex + 1 < rule.Steps.Count)
        {
            state.BehaviorId = proposal.Behavior.Definition.QualifiedId;
            state.RuleIndex = ruleIndex;
            state.StepIndex = stepIndex + 1;
            state.Committed = true;
            state.MovementOnly = false;
            _states[proposal.ActorId] = state;
        }
        else
        {
            Reset(proposal.ActorId);
        }
    }

    private IEnumerable<(CombatBehaviorRule Rule, int Index)> CandidateRules(CombatBehaviorProfile behavior, CombatBehaviorState state)
    {
        IEnumerable<(CombatBehaviorRule Rule, int Index)> rules = behavior.Rules.Select((rule, index) => (rule, index));
        if (state.Committed && state.RuleIndex >= 0 && state.RuleIndex < behavior.Rules.Count)
        {
            CombatBehaviorRule committed = behavior.Rules[state.RuleIndex];
            if (state.MovementOnly || committed.Commitment == CombatBehaviorCommitment.Plan)
            {
                return [(committed, state.RuleIndex)];
            }
        }
        return rules;
    }

    private static CombatActionChoice? FindAction(
        Combatant actor,
        CombatDecision pending,
        CombatBehaviorStep step,
        UseOption intendedUse)
    {
        foreach (CombatActionChoice choice in pending.Actions)
        {
            if (!ChoiceMatchesStep(choice, step))
            {
                continue;
            }

            if (TryExactUse(actor, choice, out UseOption? choiceUse))
            {
                if (ReferenceEquals(choiceUse, intendedUse))
                {
                    return choice;
                }

                continue;
            }

            // Older synthetic observations do not carry the live use ID. Their
            // action/name fields are still enough when there is no exact identity
            // to disambiguate, and the normal live snapshot always has one.
            return choice;
        }

        return null;
    }

    private IReadOnlyList<CombatTargetChoice> ChooseTargets(Combatant actor, CombatActionChoice choice, CombatBehaviorStep step, CombatBehaviorProfile behavior)
    {
        UseOption? use = FindUse(actor, choice, step);
        return ChooseTargets(actor, choice.Targets, step, behavior, use);
    }

    private IReadOnlyList<CombatTargetChoice> ChooseTargets(
        Combatant actor,
        IEnumerable<CombatTargetChoice> available,
        CombatBehaviorStep step,
        CombatBehaviorProfile behavior,
        UseOption? use)
    {
        IEnumerable<CombatTargetChoice> candidates = FilterTargets(actor, available, step);
        List<CombatTargetChoice> list = candidates.ToList();
        if (list.Count == 0)
        {
            return [];
        }

        // A max_targets expression is resolved only after the action commits.
        // Inspecting it here would consume dice and would also guess a target
        // count that the shared resolver has not committed yet. Automatic
        // behavior therefore chooses one target for ordinary target kinds;
        // capped and whole-side actions retain their legal ranked set and let
        // the resolver apply any committed cap.
        bool multiple = (use?.Action ?? step.Action).Json.TryGetProperty("target", out JsonElement targetKind)
            && targetKind.GetString() is "all_enemies" or "all_allies"
            || (use?.Action ?? step.Action).Json.TryGetProperty("max_targets", out _);

        if (step.TargetScore is CompiledExpression score)
        {
            IEnumerable<CombatTargetChoice> ranked = list
                .Select((candidate, index) => (Candidate: candidate, Score: EvaluateNumber(score, actor, FindCreature(candidate.Id), behavior), Index: index))
                .OrderByDescending(entry => entry.Score)
                .ThenBy(entry => entry.Index)
                .Select(entry => entry.Candidate);
            return multiple ? ranked.ToArray() : [ranked.First()];
        }

        if (use is not null
            && _rules.TryExpression(step.Action, "$.prefer", out CompiledExpression? preference)
            && preference is not null)
        {
            try
            {
                IEnumerable<CombatTargetChoice> ranked = list
                    .Select((candidate, index) => (Candidate: candidate, Score: EvaluateActionNumber(preference, actor, FindCreature(candidate.Id), behavior, use), Index: index))
                    .OrderByDescending(entry => entry.Score)
                    .ThenBy(entry => entry.Index)
                    .Select(entry => entry.Candidate);
                return multiple ? ranked.ToArray() : [ranked.First()];
            }
            catch (ExpressionException)
            {
                // A legacy action preference that depends on unavailable preview
                // data cannot make an authored proposal illegal; its legal target
                // list remains the read-only fallback.
            }
        }

        if (multiple)
        {
            return list;
        }

        CombatBehaviorTarget? target = step.Target ?? ActionTarget(step.Action);
        return [target switch
        {
            CombatBehaviorTarget.Enemy => list.OrderBy(candidate => candidate.Track ?? decimal.MaxValue).First(),
            CombatBehaviorTarget.HurtAlly => list.OrderByDescending(candidate => (candidate.MaximumTrack ?? 0) - (candidate.Track ?? 0)).First(),
            _ => list[0],
        }];
    }

    private CombatTargetChoice? ChooseTarget(
        Combatant actor,
        IEnumerable<CombatTargetChoice> available,
        CombatBehaviorStep step,
        CombatBehaviorProfile behavior,
        UseOption? use)
    {
        IReadOnlyList<CombatTargetChoice> targets = ChooseTargets(actor, available, step, behavior, use);
        return targets.FirstOrDefault();
    }

    private IEnumerable<CombatTargetChoice> FilterTargets(
        Combatant actor,
        IEnumerable<CombatTargetChoice> available,
        CombatBehaviorStep step)
    {
        IEnumerable<CombatTargetChoice> candidates = available;
        CombatBehaviorTarget? target = step.Target ?? ActionTarget(step.Action);
        if (target is CombatBehaviorTarget targetKind)
        {
            candidates = candidates.Where(candidate => targetKind switch
            {
                CombatBehaviorTarget.Self => candidate.Id == actor.Id,
                CombatBehaviorTarget.Enemy => candidate.Side != actor.Side && !candidate.Defeated,
                CombatBehaviorTarget.Ally => candidate.Side == actor.Side && !candidate.Defeated,
                CombatBehaviorTarget.HurtAlly => candidate.Side == actor.Side && !candidate.Defeated && (candidate.Track is decimal left && candidate.MaximumTrack is decimal max && left < max),
                CombatBehaviorTarget.FallenAlly => candidate.Side == actor.Side && candidate.Defeated && !candidate.Escaped,
                _ => true,
            });
        }
        return candidates;
    }

    private IReadOnlyList<CombatTargetChoice> RegisteredTargets(CombatDecision pending)
    {
        Dictionary<string, CombatTargetChoice> targets = pending.Actions
            .SelectMany(action => action.Targets)
            .GroupBy(target => target.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        foreach (Combatant actor in _actors.Values)
        {
            targets.TryAdd(actor.Id, new CombatTargetChoice(
                actor.Id,
                actor.Name,
                actor.Side,
                actor.Defeated,
                actor.Escaped,
                actor.Creature.Position,
                null,
                null));
        }

        return targets.Values.ToList();
    }

    private static CombatBehaviorTarget? ActionTarget(Definition action)
    {
        if (!action.Json.TryGetProperty("target", out JsonElement target) || target.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return target.GetString() switch
        {
            "self" => CombatBehaviorTarget.Self,
            "enemy" or "all_enemies" => CombatBehaviorTarget.Enemy,
            "ally" or "all_allies" => CombatBehaviorTarget.Ally,
            "hurt_ally" => CombatBehaviorTarget.HurtAlly,
            "fallen_ally" => CombatBehaviorTarget.FallenAlly,
            _ => null,
        };
    }

    private static UseOption? FindUse(Combatant actor, CombatActionChoice choice, CombatBehaviorStep step)
    {
        if (TryExactUse(actor, choice, out UseOption? exact))
        {
            return exact is not null && MatchesStep(exact, step) ? exact : null;
        }

        return actor.Uses.FirstOrDefault(use => MatchesChoice(use, choice) && MatchesStep(use, step));
    }

    private static UseOption? FindUse(Combatant actor, CombatBehaviorStep step)
    {
        return actor.Uses.FirstOrDefault(use => MatchesStep(use, step));
    }

    private static bool ChoiceMatchesStep(CombatActionChoice choice, CombatBehaviorStep step)
    {
        return (choice.ActionId == step.Action.QualifiedId || choice.ActionId == step.Action.Id)
            && (step.Name is null || string.Equals(choice.Name, step.Name, StringComparison.Ordinal))
            && (step.Spell is null || choice.SpellId == step.Spell.QualifiedId || choice.SpellId == step.Spell.Id);
    }

    private static bool MatchesChoice(UseOption use, CombatActionChoice choice)
    {
        return (choice.ActionId == use.Action.QualifiedId || choice.ActionId == use.Action.Id)
            && string.Equals(use.Name, choice.Name, StringComparison.Ordinal)
            && (choice.SpellId is null
                ? use.Spell is null
                : use.Spell is Definition spell && (choice.SpellId == spell.QualifiedId || choice.SpellId == spell.Id));
    }

    private static bool MatchesStep(UseOption use, CombatBehaviorStep step)
    {
        if (use.Action != step.Action
            || (step.Name is not null && !string.Equals(use.Name, step.Name, StringComparison.Ordinal))
            || (step.Spell is not null && use.Spell != step.Spell)
            || (step.FromItem is not null && !string.Equals(use.FromItem, step.FromItem, StringComparison.Ordinal)))
        {
            return false;
        }

        return step.Parameters.All(parameter =>
            use.Parameters.TryGetValue(parameter.Key, out CompiledExpression? supplied)
            && string.Equals(supplied.Text, parameter.Value.Text, StringComparison.Ordinal));
    }

    private static bool TryExactUse(Combatant actor, CombatActionChoice choice, out UseOption? use)
    {
        use = null;
        const string marker = "/use/";
        int markerIndex = choice.Id.IndexOf(marker, StringComparison.Ordinal);
        if (markerIndex <= 0 || !choice.Id.StartsWith(actor.Id, StringComparison.Ordinal))
        {
            return false;
        }

        int indexStart = markerIndex + marker.Length;
        int separator = choice.Id.IndexOf('/', indexStart);
        if (separator <= indexStart
            || !int.TryParse(choice.Id[indexStart..separator], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int index)
            || index < 0
            || index >= actor.Uses.Count
            || !MatchesChoice(actor.Uses[index], choice))
        {
            return false;
        }

        use = actor.Uses[index];
        return true;
    }

    private (CombatActionChoice Choice, IReadOnlyList<Cell> Path)? FindMovement(
        CombatDecision pending,
        Combatant actor,
        CombatTargetChoice target,
        CombatBehaviorDestination destination,
        CombatBehaviorProfile behavior,
        UseOption? intendedUse,
        bool requiresIntendedAction)
    {
        if (target.Position is not Cell targetCell)
        {
            return null;
        }

        decimal preferred = EvaluateNumber(destination.Distance, actor, FindCreature(target.Id), behavior);
        foreach (CombatActionChoice choice in pending.Actions)
        {
            Definition? action = _rules.Find(DefinitionTypes.Action, choice.ActionId, out _);
            if (action is null || !ContainsOperation(action.Json, "move"))
            {
                continue;
            }

            if (!choice.Targets.Any(candidate => candidate.Id == target.Id))
            {
                continue;
            }

            CombatMoveChoice? move = choice.Moves
                .Select(candidate => (Move: candidate, Distance: Distance(candidate.Destination, targetCell)))
                .Where(candidate => Meets(candidate.Distance, preferred, destination.Kind))
                .Where(candidate => !requiresIntendedAction
                    || intendedUse is not null
                    && ActionLegalAt(intendedUse, actor, target, behavior, candidate.Move.Destination))
                .OrderBy(candidate => candidate.Move.Cost)
                .ThenBy(candidate => candidate.Distance)
                .Select(candidate => (CombatMoveChoice?)candidate.Move)
                .FirstOrDefault();
            if (move is CombatMoveChoice selected)
            {
                return (choice, selected.Path);
            }
        }

        return null;
    }

    /// <summary>
    /// Checks the field-owned part of an action's target legality from a
    /// hypothetical endpoint. The live resolver remains the authority for the
    /// actual choice; this read-only check only lets a behavior choose a move
    /// that can make a currently screened ranged action legal.
    /// </summary>
    private bool ActionLegalAt(
        UseOption use,
        Combatant actor,
        CombatTargetChoice target,
        CombatBehaviorProfile behavior,
        Cell endpoint)
    {
        if (_field is null || target.Position is not Cell targetCell)
        {
            return true;
        }

        if (ActionCostUnavailable(actor, use.Action)
            || (use.Spell is Definition spell
                && (!actor.CanCast(spell) || !SpellAffordableAt(actor, spell, target, behavior, use, endpoint))))
        {
            return false;
        }

        try
        {
            if (_rules.TryExpression(use.Action, "$.available", out CompiledExpression? available)
                && available is not null
                && !EvaluateActionBooleanAt(available, actor, target, behavior, use, endpoint))
            {
                return false;
            }

            if (_rules.TryExpression(use.Action, "$.valid_target", out CompiledExpression? validTarget)
                && validTarget is not null
                && !EvaluateActionBooleanAt(validTarget, actor, target, behavior, use, endpoint))
            {
                return false;
            }
        }
        catch (ExpressionException)
        {
            return false;
        }

        if (!_rules.TryExpression(use.Action, "$.range", out CompiledExpression? range)
            || range is null)
        {
            return true;
        }

        decimal knownRange;
        try
        {
            knownRange = EvaluateActionNumberAt(range, actor, target, behavior, use, endpoint);
        }
        catch (ExpressionException)
        {
            return false;
        }

        return Distance(endpoint, targetCell) <= knownRange && _field.CanSee(endpoint, targetCell);
    }

    private bool EvaluateActionBooleanAt(
        CompiledExpression expression,
        Combatant actor,
        CombatTargetChoice target,
        CombatBehaviorProfile behavior,
        UseOption use,
        Cell endpoint)
    {
        Creature? targetCreature = FindCreature(target.Id);
        return CreateEndpointEvaluator(actor, target, endpoint).Evaluate(
            expression,
            ScopeFor(actor, targetCreature, behavior, use.Parameters)).Boolean;
    }

    private decimal EvaluateActionNumberAt(
        CompiledExpression expression,
        Combatant actor,
        CombatTargetChoice target,
        CombatBehaviorProfile behavior,
        UseOption use,
        Cell endpoint)
    {
        Creature? targetCreature = FindCreature(target.Id);
        Scope scope = ScopeFor(actor, targetCreature, behavior, use.Parameters);
        return CreateEndpointEvaluator(actor, target, endpoint).Evaluate(expression, scope).Number;
    }

    private Evaluator CreateEndpointEvaluator(Combatant actor, CombatTargetChoice target, Cell endpoint)
    {
        return new Evaluator(_rules, null)
        {
            Combat = new CombatMoment(
                _evaluator.Combat?.Round ?? 0,
                _evaluator.Combat?.SurpriseRound ?? false,
                (from, to) => from == actor.Creature && to.Position is Cell toCell
                    ? Distance(endpoint, toCell)
                    : CombatDistance(from, to),
                creature => creature == actor.Creature
                    ? _actors.Values
                        .Where(member => member != actor && member.Side != actor.Side && !member.Defeated && !member.Escaped && member.Creature.Position is Cell)
                        .Select(member => Distance(endpoint, member.Creature.Position!.Value))
                        .DefaultIfEmpty(0)
                        .Min()
                    : Nearest(creature),
                (from, to) => from == actor.Creature && to.Position is Cell toCell
                    ? _field!.CanSee(endpoint, toCell)
                    : CanSee(from, to),
                (from, to) => from == actor.Creature && to.Position is Cell toCell
                    ? _actors.Values.Count(member => member != actor
                        && member.Side == actor.Side
                        && !member.Defeated
                        && !member.Escaped
                        && member.Creature != to
                        && member.Creature.Position is Cell allyCell
                        && Distance(allyCell, toCell) <= 1)
                    : AlliesNear(from, to)),
        };
    }

    private bool SpellAffordableAt(
        Combatant actor,
        Definition spell,
        CombatTargetChoice target,
        CombatBehaviorProfile behavior,
        UseOption use,
        Cell endpoint)
    {
        if (!spell.Json.TryGetProperty("cost", out JsonElement costs)
            || actor.CastsLeft.ContainsKey(spell))
        {
            return true;
        }

        foreach (JsonProperty cost in costs.EnumerateObject())
        {
            if (!_rules.References.TryGetValue((spell, $"$.cost.{cost.Name}"), out Definition? track)
                || !_rules.TryExpression(spell, $"$.cost.{cost.Name}", out CompiledExpression? expression)
                || expression is null)
            {
                return false;
            }

            decimal amount;
            try
            {
                amount = CreateEndpointEvaluator(actor, target, endpoint)
                    .Evaluate(expression, ScopeFor(actor, FindCreature(target.Id), behavior, use.Parameters))
                    .Number;
            }
            catch (ExpressionException)
            {
                return false;
            }

            decimal current = actor.Creature.Tracks.TryGetValue(track.Id, out TrackValue? value) && value.Current is decimal present
                ? present
                : 0;
            if (current < amount)
            {
                return false;
            }
        }

        return true;
    }

    private static bool ActionCostUnavailable(Combatant actor, Definition action)
    {
        if (actor.Budget.Count == 0)
        {
            return false;
        }

        return action.Json.GetProperty("cost").EnumerateObject()
            .Any(cost => !actor.Budget.TryGetValue(cost.Name, out int left) || left < cost.Value.GetInt32());
    }

    private static bool ContainsOperation(JsonElement element, string name)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("op", out JsonElement operation)
                && operation.ValueKind == JsonValueKind.String
                && operation.GetString() == name)
            {
                return true;
            }

            return element.EnumerateObject().Any(property => ContainsOperation(property.Value, name));
        }

        return element.ValueKind == JsonValueKind.Array
            && element.EnumerateArray().Any(item => ContainsOperation(item, name));
    }

    private IReadOnlyList<Cell>? ChoosePath(Combatant actor, CombatTargetChoice target, CombatActionChoice choice, CombatBehaviorStep step, CombatBehaviorProfile behavior)
    {
        if (step.Destination is not CombatBehaviorDestination destination)
        {
            return null;
        }

        if (target.Position is not Cell targetCell)
        {
            return null;
        }

        decimal preferred = EvaluateNumber(destination.Distance, actor, FindCreature(target.Id), behavior);
        return choice.Moves
            .Select(move => (Move: move, Distance: Distance(move.Destination, targetCell)))
            .Where(entry => Meets(entry.Distance, preferred, destination.Kind))
            .OrderBy(entry => entry.Move.Cost)
            .ThenBy(entry => entry.Distance)
            .Select(entry => (IReadOnlyList<Cell>)entry.Move.Path)
            .FirstOrDefault();
    }

    private bool DestinationSatisfied(Combatant actor, CombatTargetChoice target, CombatBehaviorDestination destination, CombatBehaviorProfile behavior)
    {
        if (actor.Creature.Position is not Cell actorCell || target.Position is not Cell targetCell)
        {
            return false;
        }

        decimal preferred = EvaluateNumber(destination.Distance, actor, FindCreature(target.Id), behavior);
        return Meets(Distance(actorCell, targetCell), preferred, destination.Kind);
    }

    private bool Meets(decimal distance, decimal preferred, string kind)
    {
        return kind switch
        {
            "away" or "outside" => distance >= preferred,
            _ => distance <= preferred,
        };
    }

    private decimal Distance(Cell from, Cell to)
    {
        return _field?.Distance(from, to) ?? Math.Abs(from.X - to.X) + Math.Abs(from.Y - to.Y);
    }

    private void SetCombatMoment(CombatObservation observation)
    {
        _evaluator.Combat = new CombatMoment(
            observation.Round,
            observation.Combatants.Any(combatant => combatant.SurprisedRounds > 0),
            CombatDistance,
            Nearest,
            CanSee,
            AlliesNear);
    }

    /// <summary>Cells apart on the field; without positions or a field, everyone is in reach.</summary>
    private decimal CombatDistance(Creature from, Creature to)
    {
        return _field is not null && from.Position is Cell a && to.Position is Cell b
            ? _field.Distance(a, b)
            : 1;
    }

    private bool CanSee(Creature from, Creature to)
    {
        return _field is null || from.Position is not Cell a || to.Position is not Cell b || _field.CanSee(a, b);
    }

    private decimal Nearest(Creature creature)
    {
        Combatant? self = _actors.Values.FirstOrDefault(actor => actor.Creature == creature);
        return _actors.Values
            .Where(actor => self is not null && actor.Side != self.Side && !actor.Defeated && !actor.Escaped)
            .Select(actor => CombatDistance(creature, actor.Creature))
            .DefaultIfEmpty(0)
            .Min();
    }

    private decimal AlliesNear(Creature creature, Creature target)
    {
        Combatant? self = _actors.Values.FirstOrDefault(actor => actor.Creature == creature);
        return _actors.Values.Count(actor => self is not null
            && actor != self
            && actor.Side == self.Side
            && !actor.Defeated
            && !actor.Escaped
            && actor.Creature != target
            && CombatDistance(actor.Creature, target) <= 1);
    }

    private bool EvaluateBoolean(CompiledExpression expression, Combatant? actor, Creature? target, CombatBehaviorProfile behavior)
    {
        return _evaluator.Evaluate(expression, ScopeFor(actor, target, behavior)).Boolean;
    }

    private decimal EvaluateNumber(CompiledExpression expression, Combatant? actor, Creature? target, CombatBehaviorProfile behavior)
    {
        return _evaluator.Evaluate(expression, ScopeFor(actor, target, behavior)).Number;
    }

    private decimal EvaluateActionNumber(CompiledExpression expression, Combatant actor, Creature? target, CombatBehaviorProfile behavior, UseOption use)
    {
        return _evaluator.Evaluate(expression, ScopeFor(actor, target, behavior, use.Parameters)).Number;
    }

    private Scope ScopeFor(Combatant? actor, Creature? target, CombatBehaviorProfile behavior, IReadOnlyDictionary<string, CompiledExpression>? use = null)
    {
        Dictionary<string, Value> parameters = [];
        HashSet<string> evaluating = new(StringComparer.Ordinal);
        foreach ((string name, CompiledExpression expression) in behavior.Parameters)
        {
            EvaluateBehaviorParameter(name, expression, actor, target, behavior, parameters, evaluating);
        }

        return new Scope(actor?.Creature, target, Use: use, BehaviorValues: parameters);
    }

    private Value EvaluateBehaviorParameter(
        string name,
        CompiledExpression expression,
        Combatant? actor,
        Creature? target,
        CombatBehaviorProfile behavior,
        Dictionary<string, Value> values,
        HashSet<string> evaluating)
    {
        if (values.TryGetValue(name, out Value value))
        {
            return value;
        }

        if (!evaluating.Add(name))
        {
            throw new ExpressionException(
                $"behavior.{name} depends on itself through another behavior parameter. Break the parameter cycle.",
                expression.Root.Column);
        }

        try
        {
            foreach (string dependency in BehaviorReferences(expression.Root))
            {
                if (behavior.Parameters.TryGetValue(dependency, out CompiledExpression? dependencyExpression))
                {
                    EvaluateBehaviorParameter(dependency, dependencyExpression, actor, target, behavior, values, evaluating);
                }
            }

            value = _evaluator.Evaluate(expression, new Scope(actor?.Creature, target, BehaviorValues: values));
            values[name] = value;
            return value;
        }
        finally
        {
            evaluating.Remove(name);
        }
    }

    private static IEnumerable<string> BehaviorReferences(Expr expression)
    {
        return expression switch
        {
            PathExpr path when path.Root == "behavior" => [path.Name],
            UnaryExpr unary => BehaviorReferences(unary.Operand),
            BinaryExpr binary => BehaviorReferences(binary.Left).Concat(BehaviorReferences(binary.Right)),
            ConditionalExpr conditional => BehaviorReferences(conditional.Condition)
                .Concat(BehaviorReferences(conditional.Then))
                .Concat(BehaviorReferences(conditional.Else)),
            CallExpr call => call.Arguments.SelectMany(BehaviorReferences),
            _ => [],
        };
    }

    private Creature? FindCreature(string id)
    {
        // Candidate creatures are registered by callers through the combatant
        // object passed to Propose. Target expressions therefore use the target
        // only when it is the actor; the live owner can call Register for richer
        // target data without adding a second state authority.
        return _actors.TryGetValue(id, out Combatant? actor) ? actor.Creature : null;
    }
}
