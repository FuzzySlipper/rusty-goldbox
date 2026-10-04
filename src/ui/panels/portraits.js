import { element, button, picture } from '../dom.js';
import { bar, vitalTrack } from './members.js';

/**
 * The party as portrait cards, sized by party size and the portrait scale.
 * In play a card opens that member's sheet; in combat the cards show the
 * party's side of the fight and who is acting.
 */
export function createPortraits(send, ui) {
  // This is a presentation page size, not a party limit. A second page keeps
  // the cards readable on a narrow panel while still making every roster
  // member reachable through ordinary controls.
  const pageSize = 6;
  const title = element('h2', {});
  const pageLabel = element('span', { class: 'gb-roster-page', 'aria-live': 'polite' });
  const previous = button('‹', () => setPage(page - 1), { 'aria-label': 'Previous party members', title: 'Previous party members', 'data-focus-key': 'roster:previous' });
  const next = button('›', () => setPage(page + 1), { 'aria-label': 'Next party members', title: 'Next party members', 'data-focus-key': 'roster:next' });
  const pager = element('nav', { class: 'gb-roster-nav', 'aria-label': 'Party member pages' }, previous, pageLabel, next);
  const viewport = element('div', { class: 'gb-roster-scroll', tabindex: '0', role: 'region', 'aria-label': 'Party members' });
  const list = element('ol', { id: 'rusty-goldbox-roster' });
  viewport.append(list);
  const node = element('section', { class: 'gb-panel gb-portraits', 'aria-label': 'Party', 'data-rusty-ui-interactive': '' }, title, pager, viewport);
  let page = 0;
  let mode = null;

  function setPage(nextPage) {
    if (lastView === null) {
      return;
    }

    page = Math.max(0, nextPage);
    render(lastView);
  }

  let lastView = null;

  const card = (name, face, current, max, flags, onClick, index, id = null) => {
    const amount = flags.track && current !== undefined && current !== null ? `${flags.track} ${current}${max === null || max === undefined ? '' : `/${max}`}` : '';
    const label = [name, amount, flags.ready ? 'level ready' : ''].filter(Boolean).join(', ');
    const controllerText = flags.controller === 'manual'
      ? 'Manual · Let AI control'
      : flags.controller === 'automatic' ? 'Auto · Take control' : '';
    const fullLabel = [label, controllerText].filter(Boolean).join(', ');
    // Play cards open a sheet. Combat party cards are controller toggles;
    // enemy cards are never rendered in this party roster.
    const item = element(onClick ? 'button' : 'div', {
      ...(onClick ? { type: 'button' } : {}),
      class: `gb-card${flags.acting ? ' gb-acting' : ''}${flags.down ? ' gb-down' : ''}`,
      'data-roster-index': index,
      ...(id ? { 'data-combat-id': id } : {}),
      title: fullLabel,
      'aria-label': fullLabel,
    },
      element('span', { class: 'gb-face' }, ...(face.length ? face : [name[0] ?? '?'])),
      element('span', { class: 'gb-name' }, name, ...(flags.ready ? [element('span', { class: 'gb-flag' }, ' ▲')] : [])),
      ...(controllerText ? [element('span', { class: 'gb-amount' }, controllerText)] : []),
      ...(flags.showAmount && amount ? [element('span', { class: 'gb-amount' }, `${current}${max === null || max === undefined ? '' : `/${max}`}`)] : []),
      bar(current, max));
    if (onClick) {
      item.addEventListener('click', onClick);
    }

    return element('li', {}, item);
  };

  const render = (view) => {
    lastView = view;
    const currentMode = view.screen === 'combat' ? 'combat' : 'play';
    if (mode !== currentMode) {
      mode = currentMode;
      page = 0;
    }

    // Projection updates replace card nodes. Remember the scroll/focus that
    // belongs to the roster so a health tick or a note does not strand the
    // player's keyboard position at the document root.
    const scrollTop = viewport.scrollTop;
    const active = document.activeElement;
    const focusedCard = node.contains(active) ? active?.closest('[data-roster-index]') : null;
    const focused = focusedCard?.getAttribute('data-combat-id')
      ? `combat:${focusedCard.getAttribute('data-combat-id')}`
      : focusedCard?.getAttribute('data-roster-index') ?? active?.getAttribute('data-focus-key') ?? null;

    if (view.screen === 'combat') {
      const members = (view.fight?.members ?? []).filter((member) => member.side === 0);
      showPage(members.length);
      title.replaceChildren('Party ', element('small', {}, String(members.length)));
      list.replaceChildren(...members.slice(page * pageSize, (page + 1) * pageSize).map((member, index) => card(
        member.name,
        picture(member.portraitPicture ?? member.iconPicture, `${member.name}'s portrait`, 'fill'),
        member.value,
        member.max,
        {
          acting: member.acting,
          controller: member.controller,
          down: member.defeated,
          track: view.fight?.track,
          showAmount: true,
        },
        member.id && member.controller
          ? () => send({
            action: 'combat-control',
            actor: member.id,
            mode: member.controller === 'manual' ? 'auto' : 'manual',
          })
          : null,
        page * pageSize + index,
        member.id ?? null)));
      restoreRoster(scrollTop, focused);
      return;
    }

    const party = view.party ?? [];
    showPage(party.length);
    title.replaceChildren('Party ', element('small', {}, String(party.length)));
    list.replaceChildren(...party.slice(page * pageSize, (page + 1) * pageSize).map((member, index) => {
      const rosterIndex = page * pageSize + index;
      const vital = vitalTrack(member);
      return card(
        member.name,
        picture(member.portraitPicture, `${member.name}'s portrait`, 'fill'),
        vital?.current,
        vital?.max,
        { ready: member.levelReady, down: vital ? (vital.current ?? 0) <= 0 && vital.max > 0 : false, track: vital?.name },
        () => ui.toggle({ kind: 'member', index: rosterIndex }),
        rosterIndex);
    }));
    restoreRoster(scrollTop, focused);
  };

  const showPage = (count) => {
    const pages = Math.max(1, Math.ceil(count / pageSize));
    page = Math.min(page, pages - 1);
    const first = count === 0 ? 0 : page * pageSize + 1;
    const last = count === 0 ? 0 : Math.min(count, (page + 1) * pageSize);
    pageLabel.textContent = pages > 1 ? `${first}–${last} of ${count}` : count > 0 ? `${count} member${count === 1 ? '' : 's'}` : 'No members';
    pager.hidden = pages === 1;
    previous.disabled = page === 0;
    next.disabled = page >= pages - 1;
  };

  const restoreRoster = (scrollTop, focused) => {
    viewport.scrollTop = scrollTop;
    if (focused === null) {
      return;
    }

    const nextFocus = [...node.querySelectorAll('[data-roster-index], [data-focus-key]')]
      .find((candidate) => focused?.startsWith('combat:')
        ? candidate.getAttribute('data-combat-id') === focused.slice('combat:'.length)
        : candidate.getAttribute('data-roster-index') === focused || candidate.getAttribute('data-focus-key') === focused);
    nextFocus?.focus({ preventScroll: true });
  };

  return { node, render };
}
