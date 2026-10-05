import { element, fragment, row, button, picture } from '../dom.js';

/** The title screen: the campaigns to open (with the extensions the player may add) and a save to load. */
export function createTitle(send) {
  // Inputs live outside the re-rendered body so typing survives updates.
  const slot = element('input', { value: 'slot-1', size: '10', 'aria-label': 'Save slot', 'data-focus-key': 'title:slot' });
  // Ticked extensions, by campaign bundle and extension ID, so a projection
  // update that redraws the list doesn't clear them before Open.
  const ticked = new Set();
  const source = element('input', { size: '40', placeholder: 'https://github.com/owner/repo', 'aria-label': 'Module source', 'data-focus-key': 'title:module-source' });
  // The running activity's row stays in place while only its byte count
  // changes, so its Cancel button can be clicked during a download.
  const activity = element('span', {});
  const progress = element('span', { class: 'gb-muted' });
  const busyRow = row(activity, progress, button('Cancel', () => send({ action: 'module-cancel' })));
  let shown = null;

  // Installing published modules: preview a source, install with progress, update and remove.
  const modules = (state) => {
    // Buttons that start work wait while something is running.
    const off = state.activity ? { disabled: '' } : {};
    const megabytes = (bytes) => `${(Number(bytes) / 1048576).toFixed(1)} MB`;
    activity.textContent = state.activity ?? '';
    progress.textContent = state.expected ? ` ${megabytes(state.received)} of ${megabytes(state.expected)} ` : state.received ? ` ${megabytes(state.received)} ` : ' ';
    const busy = state.activity ? [busyRow] : [];
    const preview = (state.preview ?? []).map((offer) => element('li', {},
      element('strong', {}, offer.title), ` (${offer.id} ${offer.version}, ${offer.kind}) `,
      button(`Install ${offer.id}`, () => send({ action: 'module-install', source: state.previewSource, id: offer.id, version: offer.version }), off),
      element('div', { class: 'gb-muted' }, offer.provenance),
      ...(offer.requires?.length ? [element('div', { class: 'gb-muted' }, `Requires ${offer.requires.join(', ')}`)] : [])));
    const updates = (state.updates ?? []).map((update) => element('li', {},
      `${update.id}: ${update.installed} installed, ${update.available} available `,
      button('Install', () => send({ action: 'module-install', source: update.source, id: update.id, version: update.available }), off)));
    const installed = (state.installed ?? []).map((module) => element('li', {},
      `${module.id} ${module.version}`, element('span', { class: 'gb-muted' }, module.source ? ` from ${module.source} ` : ' '),
      button('Remove', () => send({ action: 'module-remove', id: module.id, version: module.version }), off)));
    return [
      element('h2', {}, 'Modules'),
      row(source,
        button('Preview', () => send({ action: 'module-preview', source: source.value }), off),
        button('Check for updates', () => send({ action: 'module-updates' }), off)),
      ...busy,
      ...(state.messages ?? []).map((message) => element('p', { class: 'gb-note' }, message)),
      ...(preview.length ? [element('p', {}, `${state.previewSource} offers:`), element('ul', {}, ...preview)] : []),
      ...(updates.length ? [element('p', {}, 'Updates:'), element('ul', {}, ...updates)] : []),
      ...(installed.length ? [element('p', {}, 'Installed:'), element('ul', {}, ...installed)] : []),
    ];
  };

  /** The title body for <view>, or null when only download progress changed and was updated in place. */
  return (view) => {
    const key = JSON.stringify({ ...view, modules: { ...(view.modules ?? {}), received: 0, expected: 0 } });
    if (key === shown) {
      modules(view.modules ?? {});
      return null;
    }

    shown = key;
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
      row(slot, button('Load', () => send({ action: 'load', slot: slot.value }))),
      ...modules(view.modules ?? {}));
  };
}
