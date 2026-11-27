import { element, fragment, row, button, picture } from '../dom.js';
import { invitation } from '../table.js';

/** The title screen: campaign launcher cards, quick load, and collapsible co-op/module utilities. */
export function createTitle(send) {
  // Inputs live outside the re-rendered body so typing survives updates.
  const slot = element('input', { value: 'slot-1', size: '12', 'aria-label': 'Save slot', 'data-focus-key': 'title:slot' });
  // Ticked extensions, by campaign bundle and extension ID, so a projection
  // update that redraws the list doesn't clear them before Open.
  const ticked = new Set();
  const source = element('input', { size: '30', placeholder: 'https://github.com/owner/repo', 'aria-label': 'Module source', 'data-focus-key': 'title:module-source' });
  // The running activity's row stays in place while only its byte count
  // changes, so its Cancel button can be clicked during a download.
  const activity = element('span', {});
  const progress = element('span', { class: 'gb-muted' });
  const busyRow = row(activity, progress, button('Cancel', () => send({ action: 'module-cancel' })));
  let shown = null;
  let togetherOpen = false;
  let modulesOpen = false;
  let stableOpen = false;
  const importJson = element('textarea', { rows: '3', placeholder: 'Paste character JSON to add to your stable…', 'aria-label': 'Character JSON', 'data-focus-key': 'title:stable-json' });
  // Playing together: the player's name (also how they rejoin), the relay and a pasted invitation.
  const playerName = element('input', { size: '14', placeholder: 'your name', 'aria-label': 'Your name', 'data-focus-key': 'title:player-name' });
  const relay = element('input', { size: '18', value: 'n0', 'aria-label': 'Relay', title: 'n0 (free test relays), a relay URL, or empty for the same network only', 'data-focus-key': 'title:relay' });
  const pasted = element('input', { size: '24', placeholder: 'paste an invitation', 'aria-label': 'Invitation', 'data-focus-key': 'title:invitation' });

  const renderTogether = (hosting) => {
    const active = Boolean(hosting && !hosting.ended);
    const shouldOpen = togetherOpen || active || Boolean(hosting?.ended);
    const body = !active
      ? [
        ...(hosting?.ended ? [element('p', { class: 'gb-note' }, hosting.ended)] : []),
        element('div', { class: 'gb-field-grid' },
          element('label', { class: 'gb-field' }, element('span', { class: 'gb-field-label' }, 'Name'), playerName),
          element('label', { class: 'gb-field' }, element('span', { class: 'gb-field-label' }, 'Relay'), relay)),
        row(button('Host', () => send({ action: 'host', name: playerName.value, relay: relay.value }), { class: 'gb-btn-primary' })),
        element('div', { class: 'gb-divider' }),
        element('div', { class: 'gb-field' },
          element('span', { class: 'gb-field-label' }, 'Join by invitation'),
          row(pasted, button('Join', () => send({ action: 'join', name: playerName.value, invitation: pasted.value })))),
      ]
      : [
        element('div', { class: 'gb-badge-row' },
          element('span', { class: 'gb-pill gb-pill-accent' }, hosting.role === 'host' ? `Hosting as ${hosting.name}` : `Joined as ${hosting.name}`),
          element('span', { class: 'gb-muted' }, hosting.state === 'open'
            ? hosting.role === 'host' ? 'Open a campaign to bring joined players in.' : 'Waiting for the host to open a campaign…'
            : 'Connecting…')),
        // Shown as selectable text, so it can be copied by hand as well as with the button.
        ...invitation(hosting),
        element('ul', { class: 'gb-compact-list' }, ...(hosting.members ?? []).map((member) => element('li', {},
          `${member.name}${member.you ? ' (you)' : ''}${member.host ? ' · host' : ''}`,
          element('span', { class: 'gb-muted' }, member.connected ? ` · ${member.path}${member.ms ? `, ${member.ms} ms` : ''}` : ' · away')))),
        row(button('Leave', () => send({ action: 'leave' }))),
      ];
    const drawer = element('details', { class: 'gb-drawer' },
      element('summary', {}, active ? `Play together · ${hosting.role === 'host' ? 'Hosting' : 'Joined'}` : 'Play together (co-op)'),
      element('div', { class: 'gb-drawer-body' }, ...body));
    drawer.open = shouldOpen;
    drawer.addEventListener('toggle', () => { togetherOpen = drawer.open; });
    return drawer;
  };

  // Installing published modules: preview a source, install with progress, update and remove.
  const renderModules = (state) => {
    const off = state.activity ? { disabled: '' } : {};
    const megabytes = (bytes) => `${(Number(bytes) / 1048576).toFixed(1)} MB`;
    activity.textContent = state.activity ?? '';
    progress.textContent = state.expected ? ` ${megabytes(state.received)} of ${megabytes(state.expected)} ` : state.received ? ` ${megabytes(state.received)} ` : ' ';
    const busy = state.activity ? [busyRow] : [];
    const preview = (state.preview ?? []).map((offer) => element('li', {},
      element('div', { class: 'gb-row' },
        element('strong', {}, offer.title),
        element('span', { class: 'gb-pill' }, `${offer.id} ${offer.version}`),
        element('span', { class: 'gb-pill' }, offer.kind),
        button(`Install ${offer.id}`, () => send({ action: 'module-install', source: state.previewSource, id: offer.id, version: offer.version }), off)),
      element('div', { class: 'gb-muted' }, offer.provenance),
      ...(offer.requires?.length ? [element('div', { class: 'gb-muted' }, `Requires ${offer.requires.join(', ')}`)] : [])));
    const updates = (state.updates ?? []).map((update) => element('li', {},
      `${update.id}: ${update.installed} → ${update.available} `,
      button('Install', () => send({ action: 'module-install', source: update.source, id: update.id, version: update.available }), off)));
    const installed = (state.installed ?? []).map((module) => element('li', {},
      `${module.id} ${module.version}`, element('span', { class: 'gb-muted' }, module.source ? ` from ${module.source} ` : ' '),
      button('Remove', () => send({ action: 'module-remove', id: module.id, version: module.version }), off)));
    const hasDetails = Boolean(state.activity || preview.length || updates.length || (state.messages ?? []).length);
    const countLabel = installed.length ? ` (${installed.length} installed)` : '';
    const drawer = element('details', { class: 'gb-drawer' },
      element('summary', {}, `Modules${countLabel}`),
      element('div', { class: 'gb-drawer-body' },
        element('div', { class: 'gb-field' },
          element('span', { class: 'gb-field-label' }, 'Release source URL'),
          row(source,
            button('Preview', () => send({ action: 'module-preview', source: source.value }), off),
            button('Check for updates', () => send({ action: 'module-updates' }), off))),
        ...busy,
        ...(state.messages ?? []).map((message) => element('p', { class: 'gb-note' }, message)),
        ...(preview.length ? [element('p', { class: 'gb-muted' }, `${state.previewSource} offers:`), element('ul', { class: 'gb-compact-list' }, ...preview)] : []),
        ...(updates.length ? [element('p', { class: 'gb-muted' }, 'Updates available:'), element('ul', { class: 'gb-compact-list' }, ...updates)] : []),
        ...(installed.length ? [element('p', { class: 'gb-muted' }, 'Installed packages:'), element('ul', { class: 'gb-compact-list' }, ...installed)] : [])));
    drawer.open = modulesOpen || hasDetails;
    drawer.addEventListener('toggle', () => { modulesOpen = drawer.open; });
    return drawer;
  };

  const renderStable = (entries) => {
    const list = entries ?? [];
    const grouped = new Map();
    for (const entry of list) {
      const groupKey = `${entry.rulesetTitle ?? entry.rulesetId} (${entry.rulesetId} ${entry.rulesetVersion})`;
      if (!grouped.has(groupKey)) {
        grouped.set(groupKey, []);
      }
      grouped.get(groupKey).push(entry);
    }

    const groups = [...grouped.entries()].map(([rulesetLabel, heroes]) => element('div', { class: 'gb-subbox' },
      element('span', { class: 'gb-field-label' }, rulesetLabel),
      ...heroes.map((hero) => element('div', { class: 'gb-stable-row' },
        element('div', { class: 'gb-stable-info' },
          element('strong', {}, hero.name),
          element('span', { class: 'gb-muted' }, ` ${[hero.race, hero.classText].filter(Boolean).join(' · ')}`),
          ...(hero.originCampaign ? [element('div', { class: 'gb-muted gb-stable-origin' }, `Last in ${hero.originCampaign}`)] : [])),
        element('div', { class: 'gb-badge-row' },
          button('Copy JSON', () => navigator.clipboard?.writeText(hero.json ?? ''), { class: 'gb-btn-xs', title: 'Copy character JSON to clipboard' }),
          button('Remove', () => send({ action: 'stable-remove', id: hero.id }), { class: 'gb-btn-xs' }))))));

    const drawer = element('details', { class: 'gb-drawer' },
      element('summary', {}, `Character stable (${list.length})`),
      element('div', { class: 'gb-drawer-body' },
        ...(groups.length > 0 ? groups : [element('p', { class: 'gb-muted' }, 'No characters saved yet. Roll or export characters in any campaign to reuse them across modules sharing the same ruleset.')]),
        element('div', { class: 'gb-divider' }),
        element('div', { class: 'gb-field' },
          element('span', { class: 'gb-field-label' }, 'Import character JSON'),
          importJson,
          row(button('Add to stable', () => {
            send({ action: 'stable-import-json', json: importJson.value });
            importJson.value = '';
          }, { class: 'gb-btn-xs' })))));
    drawer.open = stableOpen;
    drawer.addEventListener('toggle', () => { stableOpen = drawer.open; });
    return drawer;
  };

  /** The title body for <view>, or null when only download progress changed and was updated in place. */
  return (view) => {
    const key = JSON.stringify({ ...view, modules: { ...(view.modules ?? {}), received: 0, expected: 0 } });
    if (key === shown) {
      renderModules(view.modules ?? {});
      return null;
    }

    shown = key;
    const campaigns = view.campaigns ?? [];
    const cards = element('div', { class: 'gb-campaign-grid' });
    for (const campaign of campaigns) {
      const choices = (campaign.extensions ?? []).map((extension) => {
        const extKey = `${campaign.bundle}|${extension.id}`;
        const box = element('input', { type: 'checkbox', 'aria-label': `Add ${extension.title} to ${campaign.title}` });
        box.checked = ticked.has(extKey);
        box.addEventListener('change', () => (box.checked ? ticked.add(extKey) : ticked.delete(extKey)));
        return { extension, box };
      });
      const openButton = button('Open', () => send({
        action: 'open',
        campaign: campaign.bundle,
        extensions: choices.filter((choice) => choice.box.checked).map((choice) => choice.extension.id),
      }), { class: 'gb-btn-primary' });
      const meta = [
        ...(campaign.ruleset ? [element('span', { class: 'gb-pill gb-pill-accent' }, campaign.ruleset)] : []),
        element('span', { class: 'gb-pill' }, `${campaign.id} v${campaign.version}`),
      ];
      cards.append(element('article', { class: 'gb-card-box gb-campaign-card' },
        element('div', { class: 'gb-campaign-head' },
          element('h3', { class: 'gb-campaign-title' }, campaign.title),
          element('div', { class: 'gb-badge-row' }, ...meta)),
        ...(campaign.provenance ? [element('p', { class: 'gb-campaign-desc' }, campaign.provenance)] : []),
        ...(choices.length ? [element('div', { class: 'gb-campaign-ext' },
          element('span', { class: 'gb-field-label' }, 'Optional extensions'),
          element('div', { class: 'gb-row' },
            ...choices.map((choice) => element('label', { class: 'gb-check-pill' }, choice.box,
              ` ${choice.extension.title}`))))] : []),
        element('div', { class: 'gb-campaign-foot' }, openButton)));
    }

    const header = element('header', { class: 'gb-sheet-header' },
      element('div', { class: 'gb-sheet-title' },
        element('h1', {}, ...(view.skin?.title?.url ? picture(view.skin.title, 'Rusty Tales', 420) : ['Rusty Tales'])),
        element('div', { class: 'gb-sheet-sub' }, view.status ?? `${campaigns.length} campaign(s) available`)),
      element('div', { class: 'gb-header-actions' },
        button('Refresh', () => send({ action: 'refresh' }))));

    const loadCard = element('section', { class: 'gb-card-box' },
      element('h2', { class: 'gb-card-heading' }, 'Continue saved game'),
      element('div', { class: 'gb-field' },
        element('span', { class: 'gb-field-label' }, 'Save slot'),
        element('div', { class: 'gb-inline-input' }, slot, button('Load', () => send({ action: 'load', slot: slot.value }), { class: 'gb-btn-primary' }))));

    const dashboard = element('div', { class: 'gb-title-grid' },
      element('section', { class: 'gb-title-main' },
        element('div', { class: 'gb-section-head' },
          element('h2', {}, 'Campaigns'),
          element('span', { class: 'gb-muted' }, 'Choose a module to begin or assemble a party')),
        campaigns.length > 0 ? cards : element('p', { class: 'gb-muted' }, 'No campaign modules found.')),
      element('aside', { class: 'gb-title-side' },
        loadCard,
        renderStable(view.stable),
        renderTogether(view.hosting),
        renderModules(view.modules ?? {})));

    return fragment(header, dashboard);
  };
}
