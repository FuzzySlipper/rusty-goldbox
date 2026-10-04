import { element, picture } from '../dom.js';
import { bar, vitalTrack } from './members.js';

/**
 * The party as portrait cards, sized by party size and the portrait scale.
 * In play a card opens that member's sheet; in combat the cards show the
 * party's side of the fight and who is acting.
 */
export function createPortraits(ui) {
  const title = element('h2', {});
  const list = element('ol', { id: 'rusty-goldbox-roster' });
  const node = element('section', { class: 'gb-panel gb-portraits', 'aria-label': 'Party', 'data-rusty-ui-interactive': '' }, title, list);

  const card = (name, face, current, max, flags, onClick) => {
    const amount = flags.track && current !== undefined && current !== null ? `${flags.track} ${current}${max === null || max === undefined ? '' : `/${max}`}` : '';
    const label = [name, amount, flags.ready ? 'level ready' : ''].filter(Boolean).join(', ');
    // Only a card that opens something is a button; a combat card is a plain picture.
    const item = element(onClick ? 'button' : 'div', {
      ...(onClick ? { type: 'button' } : {}),
      class: `gb-card${flags.acting ? ' gb-acting' : ''}${flags.down ? ' gb-down' : ''}`,
      title: label,
      'aria-label': label,
    },
      element('span', { class: 'gb-face' }, ...(face.length ? face : [name[0] ?? '?'])),
      element('span', { class: 'gb-name' }, name, ...(flags.ready ? [element('span', { class: 'gb-flag' }, ' ▲')] : [])),
      ...(flags.showAmount && amount ? [element('span', { class: 'gb-amount' }, `${current}${max === null || max === undefined ? '' : `/${max}`}`)] : []),
      bar(current, max));
    if (onClick) {
      item.addEventListener('click', onClick);
    }

    return element('li', {}, item);
  };

  const render = (view) => {
    if (view.screen === 'combat') {
      const members = (view.fight?.members ?? []).filter((member) => member.side === 0);
      title.textContent = 'Party';
      list.replaceChildren(...members.map((member) => card(
        member.name,
        picture(member.portraitPicture ?? member.iconPicture, `${member.name}'s portrait`, 'fill'),
        member.value,
        member.max,
        { acting: member.acting, down: member.defeated, track: view.fight?.track, showAmount: true })));
      return;
    }

    const party = view.party ?? [];
    title.replaceChildren('Party ', element('small', {}, String(party.length)));
    list.replaceChildren(...party.map((member, index) => {
      const vital = vitalTrack(member);
      return card(
        member.name,
        picture(member.portraitPicture, `${member.name}'s portrait`, 'fill'),
        vital?.current,
        vital?.max,
        { ready: member.levelReady, down: vital ? (vital.current ?? 0) <= 0 && vital.max > 0 : false, track: vital?.name },
        () => ui.toggle({ kind: 'member', index }));
    }));
  };

  return { node, render };
}
