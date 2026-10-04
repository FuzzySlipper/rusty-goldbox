import { element, picture } from '../dom.js';
import { bar } from './members.js';

/** The side column's free space: the area map in play, the other sides of the fight in combat. */
export function createMap() {
  const title = element('h2', {});
  const map = element('pre', { id: 'rusty-goldbox-map', 'aria-label': 'Map' });
  const foes = element('ul', { class: 'gb-foes' });
  const node = element('section', { class: 'gb-panel gb-map', 'data-rusty-ui-interactive': '' }, title, map, foes);

  const render = (view) => {
    const combat = view.screen === 'combat';
    map.hidden = combat;
    foes.hidden = !combat;
    if (!combat) {
      title.textContent = view.position?.name ?? 'Map';
      map.textContent = view.map ?? '';
      return;
    }

    const others = (view.fight?.members ?? []).filter((member) => member.side !== 0);
    const standing = others.filter((member) => !member.defeated).length;
    title.replaceChildren(view.fight?.encounter ?? 'Foes', element('small', {}, `${standing} standing · ${view.fight?.track ?? ''}`));
    foes.replaceChildren(...others.map((member) => element('li', {
      class: `${member.defeated ? 'gb-down' : ''}${member.acting ? ' gb-acting' : ''}`,
    },
      element('span', {}, ...picture(member.iconPicture, '', 16)),
      element('span', { class: 'gb-who' }, member.name),
      element('span', { class: 'gb-muted' }, `${member.value}${member.max === null ? '' : `/${member.max}`}`),
      bar(member.value, member.max))));
  };

  return { node, render };
}
