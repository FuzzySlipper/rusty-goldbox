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
      const done = view.fight?.done;
      commands.replaceChildren(button(done ? 'Continue' : 'Skip to the end', () => send({ action: 'continue' })));
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
