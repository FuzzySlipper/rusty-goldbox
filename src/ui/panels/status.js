import { element, button } from '../dom.js';

/** The status line: where the party is, which way it faces, the day, and the game menu. */
export function createStatus(ui) {
  const compass = element('span', { class: 'gb-compass', 'aria-hidden': 'true' });
  const where = element('output', { class: 'gb-where', id: 'rusty-goldbox-status', 'aria-live': 'polite' });
  const meta = element('span', { class: 'gb-muted' });
  const menu = button('≡ Menu', () => ui.toggle({ kind: 'menu' }), { title: 'Save, quit, skin and volume' });
  const node = element('header', { class: 'gb-panel gb-status', 'data-rusty-ui-interactive': '' },
    compass, where, meta, element('span', { class: 'gb-spacer' }), menu);

  const render = (view) => {
    where.textContent = view.status ?? '';
    const facing = view.position?.facing;
    compass.hidden = !facing;
    compass.textContent = facing ? facing[0].toUpperCase() : '';
    if (view.screen === 'play') {
      meta.textContent = `Day ${(view.elapsedDays ?? 0) + 1}`;
    } else {
      meta.textContent = view.fight?.done ? 'Fight over' : '';
    }
  };

  return { node, render };
}
