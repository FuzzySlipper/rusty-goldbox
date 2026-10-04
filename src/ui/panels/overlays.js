import { element, button, row, fill, currencyName, formatBalances, trackText, picture } from '../dom.js';
import { renderSpells, renderMemorised } from './members.js';

/**
 * What covers the view for a while: the game menu or a member's sheet when the
 * player opens one, else the shop, temple or trainer the party is at. None of
 * them has permanent space.
 */
export function createOverlay(send, ui) {
  const node = element('section', { class: 'gb-panel gb-overlay', role: 'dialog', 'aria-modal': 'false' });
  node.hidden = true;
  const play = (command) => () => send({ action: 'play', command });

  // Menu inputs stay put across renders, so typing or dragging isn't interrupted; the projection sets them when idle.
  const slot = element('input', { value: 'slot-1', size: '10', 'aria-label': 'Save slot' });
  const slider = (bus, label) => {
    const input = element('input', { type: 'range', min: '0', max: '1', step: '0.05', 'aria-label': `${label} volume` });
    input.addEventListener('change', () => send({ action: 'volume', bus, volume: Number(input.value) }));
    return input;
  };
  const musicVolume = slider('music', 'Music');
  const soundVolume = slider('sound', 'Sound');
  const skinPick = element('select', { 'aria-label': 'Skin' });
  skinPick.addEventListener('change', () => send({ action: 'skin', skin: skinPick.value || null }));
  const uiScale = element('input', { type: 'range', min: '0.5', max: '2.5', step: '0.05', value: '1', 'aria-label': 'Interface scale' });
  const uiScaleShown = element('output', { class: 'gb-muted' }, '1.00');
  uiScale.addEventListener('input', () => {
    uiScaleShown.textContent = Number(uiScale.value).toFixed(2);
    ui.scale(Number(uiScale.value));
  });
  uiScale.addEventListener('change', () => send({ action: 'ui-scale', scale: Number(uiScale.value) }));

  // Layout sliders, one per part the projection says may be set, made once the parts arrive.
  // Dragging previews the proportions; letting go sends the whole layout, which the Game checks and keeps.
  const layoutSliders = new Map();
  const layoutRows = element('div', { class: 'gb-columns' });
  const layoutValues = () => Object.fromEntries([...layoutSliders].map(([id, { input }]) => [id, Number(input.value)]));
  const layoutReset = button('Reset to the skin\'s', () => send({ action: 'layout-config', layout: null }));
  const buildLayoutSliders = (parts) => {
    for (const part of parts) {
      const input = element('input', { type: 'range', min: String(part.min), max: String(part.max), step: String((part.max - part.min) / 100), 'aria-label': part.name });
      const shown = element('output', { class: 'gb-muted' });
      input.addEventListener('input', () => {
        shown.textContent = Number(input.value).toFixed(2);
        ui.preview({ ...lastLayout, ...layoutValues() });
      });
      input.addEventListener('change', () => send({ action: 'layout-config', layout: layoutValues() }));
      layoutSliders.set(part.id, { input, shown });
      layoutRows.append(element('label', {}, part.name, input, shown));
    }
  };
  let lastLayout = {};

  /** Keeps the menu's inputs in step with the projection; called on every screen, as the title screen shows them too. */
  const syncSettings = (view) => {
    if (layoutSliders.size === 0 && view.layoutParts?.length) {
      buildLayoutSliders(view.layoutParts);
    }

    if (typeof view.uiScale === 'number' && document.activeElement !== uiScale) {
      uiScale.value = String(view.uiScale);
      uiScaleShown.textContent = view.uiScale.toFixed(2);
    }

    lastLayout = view.layout ?? lastLayout;
    layoutReset.disabled = !view.layoutPicked;
    for (const [id, { input, shown }] of layoutSliders) {
      if (document.activeElement !== input && lastLayout[id] !== undefined) {
        input.value = String(lastLayout[id]);
        shown.textContent = Number(lastLayout[id]).toFixed(2);
      }
    }

    if (document.activeElement !== skinPick) {
      fill(skinPick, [{ id: '', name: "(the campaign's own)" }, ...(view.skins ?? [])]);
      skinPick.value = view.skinPicked ?? '';
    }

    for (const [input, value] of [[musicVolume, view.volumes?.music], [soundVolume, view.volumes?.sound]]) {
      if (value !== undefined && document.activeElement !== input) {
        input.value = String(value);
      }
    }
  };

  // Built once: moving a control between containers would drop a drag or a focus in progress.
  const settings = element('div', {},
    element('h3', {}, 'Sound and look'),
    row(element('label', {}, 'Music', musicVolume), element('label', {}, 'Sound', soundVolume), element('label', {}, 'Skin', skinPick)),
    row(element('label', {}, 'Interface scale', uiScale, uiScaleShown)),
    element('h3', {}, 'Layout'),
    layoutRows,
    row(layoutReset));
  const saving = element('div', {},
    element('h3', {}, 'Save'),
    row(slot, button('Save', () => send({ action: 'save', slot: slot.value }))),
    element('h3', {}, 'Leave'),
    row(button('Quit to title', () => send({ action: 'quit' }))));

  const frame = (heading, detail, ...body) => {
    const close = button('✕', () => ui.close(), { class: 'gb-close', 'aria-label': 'Close', 'data-focus-key': 'overlay:close' });
    return [
      element('h2', {}, element('span', {}, heading), element('small', {}, detail ?? '', ' ', close)),
      element('div', { class: 'gb-body' }, ...body),
    ];
  };

  const menuColumns = element('div', { class: 'gb-columns' });
  const menu = frame('Game', '', menuColumns);

  const member = (view, index) => {
    const who = view.party?.[index];
    if (!who) {
      return null;
    }

    const party = view.party;
    const step = (by) => () => ui.open({ kind: 'member', index: (index + by + party.length) % party.length });
    const kind = [who.race, who.class && `${who.class} ${who.level}`].filter(Boolean).join(' ');
    const former = who.formerClasses === 'waiting'
      ? [button('Call on former class', play(`former ${index + 1} on`), { 'data-focus-key': `member:${index}:former:on` })]
      : who.formerClasses === 'called' ? [button('Set former class aside', play(`former ${index + 1} off`), { 'data-focus-key': `member:${index}:former:off` })] : [];
    const viewButton = who.perception ? [button(`View as ${who.name}`, play(`view ${index + 1}`), { 'data-focus-key': `member:${index}:view` })] : [];
    const gearWaiting = Boolean(view.ended || view.menu?.length || view.shop || view.temple || view.training);
    const gearChoice = (verb, item) => {
      const action = verb.toLowerCase();
      const node = button(verb, play(`${action} ${index + 1} ${item.id}`), { 'data-focus-key': `member:${index}:${action}:${item.id}` });
      node.disabled = gearWaiting;
      return { node };
    };
    const equipment = [
      ...(who.equipment ?? []).map((item) => [item.name, 'Equipped', gearChoice('Unequip', item)]),
      ...(view.inventory ?? []).map((item) => [`${item.name}${item.count > 1 ? ` × ${item.count}` : ''}`, 'Carried', gearChoice(item.usable ? 'Use' : 'Equip', item)]),
    ];
    return frame(who.name, `${kind} · ${who.experience} xp`,
      row(...(party.length > 1 ? [button('◀ Previous', step(-1), { 'data-focus-key': `member:${index}:previous` }), button('Next ▶', step(1), { 'data-focus-key': `member:${index}:next` })] : []),
        ...viewButton,
        // A level that needs choices is taken by typing them: level <n> --feature <id> (the refusal lists what's open).
        ...(who.levelReady ? [button('Level up', play(`level ${index + 1}`), { 'data-focus-key': `member:${index}:level` })] : []),
        ...former),
      element('div', { class: 'gb-columns' },
        element('div', {},
          ...picture(who.portraitPicture, `${who.name}'s portrait`, 96),
          element('h3', {}, 'Tracks'), table((who.tracks ?? []).map((track) => [trackText(track)])),
          ...(who.conditions?.length ? [element('div', {}, who.conditions.join(', '))] : []),
          element('h3', {}, 'Money'), element('div', {}, formatBalances(who.balances))),
        element('div', {},
          element('h3', {}, 'Attributes'), table((who.attributes ?? []).map((attribute) => [attribute])),
          ...(who.derived?.length ? [table(who.derived.map((stat) => [stat.name, stat.value]))] : []),
          ...(who.features?.length ? [element('h3', {}, 'Features'), element('div', {}, who.features.join(', '))] : [])),
        element('div', {},
          element('h3', {}, 'Equipment'), table(equipment),
          ...((who.castable?.length || who.memorisable?.length) ? [element('h3', {}, 'Spells')] : []),
          ...renderSpells(send, who, index),
          ...renderMemorised(send, who, index))));
  };

  const shop = (view) => {
    const offers = (list, verb) => table(list.map((offer) => [
      `${offer.number}. ${offer.name}${offer.holder ? ` (${offer.holder})` : ''}${offer.remaining === null || offer.remaining === undefined ? '' : ` · ${offer.remaining} left`}${verb === 'Sell' && offer.sellable === false && offer.refusalReason ? ` · unavailable: ${offer.refusalReason}` : ''}`,
      { text: `${offer.price} ${currencyName(offer.currency)}`, number: true },
      { node: (() => {
        const node = button(verb, play(`${verb.toLowerCase()} ${offer.number}`));
        const rejected = verb === 'Sell' && offer.sellable === false;
        node.disabled = (verb === 'Buy' && offer.remaining === 0) || rejected;
        if (rejected && offer.refusalReason) {
          node.title = offer.refusalReason;
        }
        return node;
      })() },
    ]));
    return frame(view.shop.text, `Party: ${formatBalances(view.shop.balances)}`,
      ...(view.shop.buyingMaxValue === null || view.shop.buyingMaxValue === undefined ? [] : [element('p', {}, `Buys goods worth at most ${view.shop.buyingMaxValue} ${currencyName(view.shop.buyingCurrency)} each.`)]),
      element('div', { class: 'gb-columns' },
        element('div', {}, element('h3', {}, 'For sale'), offers(view.shop.stock, 'Buy')),
        element('div', {}, element('h3', {}, 'Carried'), offers(view.shop.carried, 'Sell'))),
      row(button('Leave shop', play('leave'))));
  };

  const temple = (view) => frame('Temple', '',
    element('p', {}, view.temple.text),
    ...view.temple.services.map((service) => element('div', {},
      element('h3', {}, service.label),
      row(...service.prices.map((price, index) => button(`${view.party[index]?.name ?? index + 1}: ${price} ${currencyName(service.currency)}`,
        play(`serve ${service.number} ${index + 1}`), { 'data-focus-key': `temple:${service.number}:${index}` }))))),
    row(button('Leave temple', play('leave'))));

  const training = (view) => frame('Trainer', '',
    element('p', {}, view.training),
    row(...(view.party ?? []).map((who, index) => button(`Train ${who.name}`, play(`train ${index + 1}`), { 'data-focus-key': `training:${index}` }))),
    row(button('Leave trainer', play('leave'))));

  const render = (view, open) => {
    syncSettings(view);
    if (open?.kind === 'menu') {
      // The menu is the same nodes every time; leave it in place so its controls keep their state.
      // A pending live combat is a save boundary too. Keep the menu usable
      // over the fight so saving never requires resolving a player's choice.
      saving.hidden = view.screen !== 'play' && view.screen !== 'combat';
      if (node.dataset.showing !== 'menu') {
        node.dataset.showing = 'menu';
        // The title screen borrows the settings; take them back.
        menuColumns.replaceChildren(saving, settings);
        node.replaceChildren(...menu);
      }

      node.hidden = false;
      return;
    }

    const showing = open?.kind === 'member' && view.screen === 'play'
      ? `member:${open.index}`
      : view.screen === 'play' && view.shop ? 'shop'
        : view.screen === 'play' && view.temple ? 'temple'
          : view.screen === 'play' && view.training ? 'training' : '';
    const previousShowing = node.dataset.showing;
    const previousBody = node.querySelector('.gb-body');
    const previousFocus = document.activeElement;
    const previousState = previousShowing === showing && previousBody
      ? {
        scrollTop: previousBody.scrollTop,
        focusKey: node.contains(previousFocus) ? previousFocus?.getAttribute('data-focus-key') : null,
      }
      : null;
    node.dataset.showing = showing;
    let content = null;
    if (open?.kind === 'member' && view.screen === 'play') {
      content = member(view, open.index);
    }

    if (!content && view.screen === 'play') {
      content = view.shop ? shop(view) : view.temple ? temple(view) : view.training ? training(view) : null;
    }

    node.hidden = !content;
    node.replaceChildren(...(content ?? []));
    if (content && previousState) {
      const body = node.querySelector('.gb-body');
      if (body) {
        body.scrollTop = previousState.scrollTop;
      }
      if (previousState.focusKey) {
        const target = [...node.querySelectorAll('[data-focus-key]')]
          .find((candidate) => candidate.getAttribute('data-focus-key') === previousState.focusKey);
        target?.focus({ preventScroll: true });
      }
    }
  };

  return { node, render, settings };
}

/** A small table: each row's cells are text, { text, number } or { node }. */
function table(rows) {
  return element('table', {}, ...rows.map((cells) => element('tr', {}, ...cells.map((cell) => {
    if (cell?.node) {
      return element('td', { class: 'gb-act' }, cell.node);
    }

    return cell?.number ? element('td', { class: 'gb-num' }, cell.text) : element('td', {}, String(cell));
  }))));
}
