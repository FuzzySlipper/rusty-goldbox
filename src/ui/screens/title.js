import { element, fragment, row, button, picture } from '../dom.js';

/** The title screen: the campaigns to open (with the extensions the player may add) and a save to load. */
export function createTitle(send) {
  // Inputs live outside the re-rendered body so typing survives updates.
  const slot = element('input', { value: 'slot-1', size: '10', 'aria-label': 'Save slot', 'data-focus-key': 'title:slot' });
  // Ticked extensions, by campaign bundle and extension ID, so a projection
  // update that redraws the list doesn't clear them before Open.
  const ticked = new Set();

  return (view) => {
    const list = element('ul', {});
    for (const campaign of view.campaigns ?? []) {
      // Extensions the player may add: drop-in content built on the campaign's ruleset.
      const choices = (campaign.extensions ?? []).map((extension) => {
        const key = `${campaign.bundle}|${extension.id}`;
        const box = element('input', { type: 'checkbox', 'aria-label': `Add ${extension.title} to ${campaign.title}` });
        box.checked = ticked.has(key);
        box.addEventListener('change', () => (box.checked ? ticked.add(key) : ticked.delete(key)));
        return { extension, box };
      });
      list.append(element('li', {},
        `${campaign.title} (${campaign.id} ${campaign.version}) `,
        button('Open', () => send({
          action: 'open',
          campaign: campaign.bundle,
          extensions: choices.filter((choice) => choice.box.checked).map((choice) => choice.extension.id),
        })),
        ...(choices.length ? [element('div', { class: 'gb-muted' }, 'Extensions: ',
          ...choices.map((choice) => element('label', { style: 'margin-right:8px' }, choice.box,
            ` ${choice.extension.title} (${choice.extension.id} ${choice.extension.version})`)))] : [])));
    }

    return fragment(
      element('h1', {}, ...(view.skin?.title?.url ? picture(view.skin.title, 'Rusty Goldbox', 480) : ['Rusty Goldbox'])),
      element('p', { class: 'gb-muted' }, view.status ?? ''),
      element('h2', {}, 'Campaigns'), list,
      row(button('Refresh', () => send({ action: 'refresh' }))),
      element('h2', {}, 'Load'),
      row(slot, button('Load', () => send({ action: 'load', slot: slot.value }))));
  };
}
