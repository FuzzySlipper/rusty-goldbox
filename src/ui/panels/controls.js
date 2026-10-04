import { element, button } from '../dom.js';

/** The movement pad and the commands that fit what the party is doing. Keys do the same through the product's key mappings. */
export function createControls(send, ui) {
  const play = (command) => () => send({ action: 'play', command });
  const pad = element('div', { class: 'gb-pad', role: 'group', 'aria-label': 'Movement' },
    ...[['↶', 'left', 'Turn left'], ['↑', 'forward', 'Forward'], ['↷', 'right', 'Turn right'],
      ['⟲', 'around', 'Turn around'], ['↓', 'back', 'Back'], ['◎', 'look', 'Look']]
      .map(([glyph, command, label]) => button(glyph, play(command), { title: label, 'aria-label': label })));
  const commands = element('div', { class: 'gb-commands' });
  const node = element('nav', { class: 'gb-panel gb-controls', 'aria-label': 'Controls', 'data-rusty-ui-interactive': '' }, pad, commands);
  let selectedAction = null;
  // Keep target IDs in click order.  Portion actions use that order to map
  // each authored portion to a target, and may intentionally repeat an ID.
  let selectedTargets = [];
  let selectedMove = null;
  let selectedDecision = null;

  const text = (value, fallback = '') => value === undefined || value === null ? fallback : String(value);
  const cellText = (cell) => cell ? `(${cell.x},${cell.y})` : '';
  const budgetText = (budget) => Object.entries(budget ?? {})
    .map(([name, amount]) => `${name.split(':').at(-1)} ${amount}`)
    .join(' · ');
  const trackText = (member, track) => {
    const tracks = member?.tracks ?? {};
    const value = track && tracks[track] !== undefined ? tracks[track] : member?.value;
    const max = member?.max ?? (track && member?.trackMaximums?.[track]);
    return value === undefined || value === null ? '' : `${value}${max === undefined || max === null ? '' : `/${max}`}`;
  };

  const submitAction = (actorId, action, targets, path = undefined) => {
    send({ action: 'combat-action', actor: actorId, choice: action.id, targets, ...(path ? { path } : {}) });
  };

  const renderCombat = (view) => {
    const fight = view.fight ?? {};
    const decision = fight.decision ?? null;
    const members = fight.members ?? [];
    const actorId = decision?.actorId ?? fight.activeActorId ?? null;
    const actor = members.find((member) => member.id === actorId) ?? fight.activeActor ?? null;
    const turnActor = members.find((member) => member.id === fight.activeActorId) ?? null;
    const actions = decision?.actions ?? [];

    // A projection update may replace every button. Keep the presentation
    // selection only while it still belongs to this actor and decision.
    if (selectedDecision !== decision?.id || selectedAction && !actions.some((action) => action.id === selectedAction)) {
      selectedDecision = decision?.id ?? null;
      selectedAction = actions[0]?.id ?? null;
      selectedTargets = [];
      selectedMove = null;
    }
    const action = actions.find((choice) => choice.id === selectedAction) ?? null;
    const targets = action?.targets ?? [];
    const targetMode = action?.targetMode ?? 'one';
    const targetPhase = Number.isInteger(decision?.maximumTargets);
    // A normal action is one-target by default. Whole-side and authored
    // multi-target actions advertise their selection mode in the projection;
    // Core still commits the random cap and validates the final IDs.
    const maximumTargets = decision?.maximumTargets
      ?? (targetMode === 'all' || targetMode === 'maximum' ? targets.length
        : targetMode === 'portions' && action?.portionCount > 1 ? action.portionCount
          : 1);
    const commitsTargetCount = targetMode === 'maximum' && !targetPhase;
    const requiresAllTargets = targetMode === 'all';
    const fixedPortions = targetMode === 'portions' && action?.portionCount > 1;
    const autoSubmitSingle = maximumTargets <= 1
      && !commitsTargetCount
      && !requiresAllTargets
      && !fixedPortions
      && !(action?.moves?.length);
    selectedTargets = selectedTargets.filter((target) => targets.some((choice) => choice.id === target));

    const summary = element('div', { class: 'gb-combat-summary', 'aria-live': 'polite' },
      element('strong', {}, actor ? `${decision ? 'Choose: ' : 'Turn: '}${actor.name}` : 'Waiting for combat'),
      element('span', { class: 'gb-muted' }, actorId ? ` · ${actorId}` : ''),
      ...(decision?.kind ? [element('span', { class: 'gb-muted' }, ` · ${decision.kind}`)] : []),
      ...(turnActor && turnActor.id !== actorId ? [element('span', { class: 'gb-muted' }, ` · turn ${turnActor.name}`)] : []),
      element('span', {}, actor ? ` · ${trackText(actor, fight.track)}` : ''),
      ...(actor && budgetText(actor.budget) ? [element('span', { class: 'gb-muted' }, ` · ${budgetText(actor.budget)}`)] : []),
      ...(actor ? [element('span', { class: 'gb-muted' }, ` · ${text(actor.controller, 'manual')}`)] : []));

    const control = actor && actor.side === 0 && actor.controller
      ? button(actor.controller === 'manual' ? 'Let AI control' : 'Take control', () => send({
        action: 'combat-control', actor: actor.id, mode: actor.controller === 'manual' ? 'auto' : 'manual',
      }), { 'data-focus-key': `combat:control:${actor.id}` })
      : null;
    const actionButtons = actions.map((choice) => button(
      `${choice.name}${Object.keys(choice.cost ?? {}).length ? ` · ${budgetText(choice.cost)}` : ''}`,
      () => {
        selectedAction = choice.id;
        selectedTargets = [];
        selectedMove = null;
        render(view);
      },
      {
        'aria-pressed': String(choice.id === selectedAction),
        class: choice.id === selectedAction ? 'gb-selected' : '',
        'data-focus-key': `combat:action:${choice.id}`,
        title: choice.actionId ?? choice.id,
      }));

    const targetButtons = action && targets.length > 0
      ? [element('span', { class: 'gb-combat-label' },
          `${fixedPortions
            ? 'Portion targets'
            : targetMode === 'all' ? 'All legal targets' : targetMode === 'maximum' && commitsTargetCount ? 'Potential targets' : 'Targets'}${maximumTargets ? ` (${selectedTargets.length}/${maximumTargets}${fixedPortions ? '' : ' max'})` : ''}`),
        ...targets.map((target) => button(
          `${target.name}${target.track === null || target.track === undefined ? '' : ` · ${target.track}`}`,
          () => {
            const selectedIndex = selectedTargets.lastIndexOf(target.id);
            if (selectedIndex >= 0) {
              // Remove one allocation.  For portions, a repeated target is
              // valid and each click represents one authored portion.
              selectedTargets = [
                ...selectedTargets.slice(0, selectedIndex),
                ...selectedTargets.slice(selectedIndex + 1),
              ];
            } else if (maximumTargets <= 1) {
              selectedTargets = [target.id];
            } else if (selectedTargets.length < maximumTargets) {
              selectedTargets = [...selectedTargets, target.id];
            }

            if (autoSubmitSingle && selectedTargets.length > 0) {
              submitAction(actorId, action, [...selectedTargets]);
              return;
            }

            render(view);
          },
          {
            'aria-pressed': String(selectedTargets.includes(target.id)),
            class: `${target.defeated ? 'gb-down ' : ''}${selectedTargets.includes(target.id) ? 'gb-selected' : ''}`,
            'data-focus-key': `combat:target:${target.id}`,
            title: target.id,
          }))]
      : [];
    const allTargetsReady = !requiresAllTargets || selectedTargets.length === targets.length;
    const portionsReady = !fixedPortions || selectedTargets.length === maximumTargets;
    const useReady = action
      && (commitsTargetCount || targets.length === 0 || (selectedTargets.length > 0 && allTargetsReady && portionsReady));
    const submittedTargets = commitsTargetCount ? [] : [...selectedTargets];
    const selectedPath = action?.moves?.[selectedMove]?.path;
    const useLabel = commitsTargetCount
      ? `Commit ${action?.name ?? 'action'} (roll target count)`
      : targets.length === 0 ? `Use ${action?.name ?? 'action'}`
        : `Use ${action?.name ?? 'action'} (${selectedTargets.length}${requiresAllTargets || fixedPortions ? `/${fixedPortions ? maximumTargets : targets.length}` : ''})`;
    const use = useReady
      ? [button(useLabel, () => submitAction(actorId, action, submittedTargets, selectedPath), {
        'data-focus-key': `combat:use:${action.id}`,
      })]
      : [];
    const moves = (action?.moves ?? (decision?.kind === 'movement' ? decision.moves : [])).map((move, index) => button(
      `${action ? 'Path' : 'Move'} ${cellText(move.destination)}${move.cost === undefined ? '' : ` · ${move.cost}`}`,
      () => {
        if (action) {
          selectedMove = selectedMove === index ? null : index;
          render(view);
          return;
        }

        send({
          action: 'combat-move', actor: actorId, choice: decision?.actionId ?? 'move',
          target: selectedTargets[0] ?? actorId, path: move.path ?? [],
        });
      },
      {
        'aria-pressed': String(action && selectedMove === index),
        class: action && selectedMove === index ? 'gb-selected' : '',
        'data-focus-key': `combat:move:${index}:${cellText(move.destination)}`,
      }));
    const options = (decision?.options ?? []).map((option) => button(option.name, () => send({
      action: 'combat-decide', decision: decision.id, option: option.id,
    }), { 'data-focus-key': `combat:option:${option.id}` }));
    const decline = decision?.options?.length ? [button('Decline', () => send({ action: 'combat-decide', decision: decision.id }), { 'data-focus-key': `combat:decline:${decision.id}` })] : [];
    const end = decision?.canEndTurn && actorId
      ? [button('End turn', () => send({ action: 'combat-end-turn', actor: actorId }), { 'data-focus-key': `combat:end:${actorId}` })]
      : [];
    const done = Boolean(fight.done || fight.phase === 'ended');
    const skip = fight.live && !done
      ? element('span', { class: 'gb-muted' }, decision ? 'Choose a combat decision.' : 'Combat is advancing.')
      : button(done ? 'Continue' : 'Show committed facts', () => send({ action: 'continue' }), { 'data-focus-key': 'combat:continue' });

    const body = [summary,
      ...(control ? [control] : []),
      ...(actionButtons.length ? [element('span', { class: 'gb-combat-label' }, 'Actions'), ...actionButtons] : []),
      ...targetButtons,
      ...use,
      ...(moves.length ? [element('span', { class: 'gb-combat-label' }, 'Movement'), ...moves] : []),
      ...(options.length ? [element('span', { class: 'gb-combat-label' }, 'Choices'), ...options, ...decline] : []),
      ...end,
      skip];
    const active = document.activeElement;
    const focusKey = commands.contains(active) ? active?.getAttribute('data-focus-key') : null;
    commands.replaceChildren(...body);
    if (focusKey) {
      [...commands.querySelectorAll('[data-focus-key]')]
        .find((candidate) => candidate.getAttribute('data-focus-key') === focusKey)
        ?.focus({ preventScroll: true });
    }
  };

  const render = (view) => {
    const combat = view.screen === 'combat';
    const busy = Boolean(view.shop || view.temple || view.training);
    // A waiting menu, shop, temple or trainer takes a choice first; only Look still works.
    const choosing = (view.menu ?? []).length > 0;
    for (const control of pad.children) {
      const looking = control.getAttribute('aria-label') === 'Look';
      control.disabled = combat || Boolean(view.ended) || ((busy || choosing) && !looking);
    }

    if (combat) {
      renderCombat(view);
      return;
    }

    commands.replaceChildren(
      button('Status', play('status')),
      ...(busy || choosing ? [] : [button('Search', play('search'))]),
      ...(view.party?.length ? [button('Party', () => ui.toggle({ kind: 'member', index: 0 }))] : []),
      ...(busy ? [button('Leave', play('leave'))] : []));
  };

  return { node, render };
}
