import { element, picture } from '../dom.js';
import { bar } from './members.js';

/** The side column's free space: the area map in play, the other sides of the fight in combat. */
export function createMap() {
  const title = element('h2', {});
  const map = element('pre', { id: 'rusty-goldbox-map', 'aria-label': 'Map' });
  const foes = element('ul', { class: 'gb-foes' });
  const node = element('section', { class: 'gb-panel gb-map', 'data-rusty-ui-interactive': '' }, title, map, foes);

  // The map's text grows to fill its panel: as large as its widest line and its line count allow.
  const fit = () => {
    const lines = map.textContent.split('\n');
    const columns = Math.max(1, ...lines.map((line) => line.length));
    const width = map.clientWidth * 0.9;
    const height = map.clientHeight * 0.9;
    if (width <= 0 || height <= 0) {
      return;
    }

    // A monospace glyph is about 0.6em wide; lines are 1em apart.
    const size = Math.min(width / (columns * 0.6), height / lines.length, 40);
    map.style.fontSize = `${Math.max(8, Math.floor(size))}px`;
  };
  const resized = new ResizeObserver(fit);
  resized.observe(map);

  const render = (view) => {
    const combat = view.screen === 'combat';
    map.hidden = combat;
    foes.hidden = !combat;
    if (!combat) {
      title.textContent = view.position?.name ?? 'Map';
      if (map.textContent !== (view.map ?? '')) {
        map.textContent = view.map ?? '';
        fit();
      }
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

  return { node, render, dispose: () => resized.disconnect() };
}
