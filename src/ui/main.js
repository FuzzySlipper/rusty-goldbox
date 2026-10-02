/**
 * DOM-only debug readout. Engine owns the canvas, input delivery and
 * projection transport. This module shows the product's
 * rusty.goldbox.session projection and claims goldbox.command intents with
 * goldbox.command.v1 payloads; it owns no game state. Arrow keys / WASD move,
 * digits choose menu options, through the product's key mappings.
 */
const COMMAND_INTENT = 'goldbox.command';
const COMMAND_CONTRACT = 'goldbox.command.v1';

export function mountProductUi(root, context) {
  const send = (data) => {
    context?.intents?.claim(COMMAND_INTENT, { kind: 'product-payload', contract: COMMAND_CONTRACT, data });
  };

  const panel = element('aside', { 'aria-label': 'Rusty Goldbox', style: PANEL_STYLE });
  const title = element('h1', { style: 'margin:0 0 4px;font-size:16px' }, 'Rusty Goldbox');
  const status = element('output', { id: 'rusty-goldbox-status', 'aria-live': 'polite', style: 'display:block;margin-bottom:6px' });
  const notes = element('ul', { id: 'rusty-goldbox-notes', style: 'color:#f6c177;margin:0 0 6px;padding-left:16px' });
  const body = element('div');

  // Inputs live outside the re-rendered body so typing survives updates.
  const slot = element('input', { value: 'slot-1', size: '10', 'aria-label': 'Save slot' });
  const name = element('input', { value: 'Ada', size: '10', 'aria-label': 'Character name' });
  const race = element('select', { 'aria-label': 'Race' });
  const characterClass = element('select', { 'aria-label': 'Class' });
  const command = element('input', { size: '14', placeholder: 'command', 'aria-label': 'Play command' });
  command.addEventListener('keydown', (event) => {
    if (event.key === 'Enter' && command.value.trim().length > 0) {
      send({ action: 'play', command: command.value.trim() });
      command.value = '';
    }
  });

  panel.append(title, status, notes, body);
  root.append(panel);

  const render = (view) => {
    status.textContent = view.status ?? '';
    notes.replaceChildren(...(view.notes ?? []).map((note) => element('li', {}, note)));
    if (view.screen === 'title') {
      body.replaceChildren(renderTitle(view));
    } else if (view.screen === 'party') {
      body.replaceChildren(renderParty(view));
    } else if (view.screen === 'play') {
      body.replaceChildren(renderPlay(view));
    } else if (view.screen === 'combat') {
      body.replaceChildren(renderCombat(view));
    } else {
      body.replaceChildren();
    }
  };

  const renderTitle = (view) => {
    const list = element('ul', { style: 'padding-left:16px' });
    for (const campaign of view.campaigns ?? []) {
      list.append(element('li', {},
        `${campaign.title} (${campaign.id} ${campaign.version}) `,
        button('Open', () => send({ action: 'open', campaign: campaign.bundle }))));
    }
    return fragment(
      element('h2', { style: HEADING_STYLE }, 'Campaigns'), list,
      row(button('Refresh', () => send({ action: 'refresh' }))),
      element('h2', { style: HEADING_STYLE }, 'Load'),
      row(slot, button('Load', () => send({ action: 'load', slot: slot.value }))));
  };

  const renderParty = (view) => {
    fill(race, view.races ?? []);
    fill(characterClass, view.classes ?? []);
    const members = element('ol', { style: 'padding-left:20px' });
    (view.party ?? []).forEach((member, index) => {
      const item = element('select', { 'aria-label': `Item for ${member.name}` });
      fill(item, view.items ?? []);
      members.append(element('li', {},
        element('div', {}, `${member.name}: ${member.race} ${member.class} ${member.level}, ${member.tracks.join(', ')}, gold ${member.gold}`),
        element('div', { style: 'opacity:.8' }, member.attributes.join(' ')),
        element('div', {}, `Equipment: ${member.equipment.map((equipment) => equipment.name).join(', ') || 'none'}`),
        row(item,
          button('Give/take', () => send({ action: 'equip', member: index, item: item.value })),
          button('Drop', () => send({ action: 'drop', member: index })))));
    });
    const size = view.partySize ?? { min: 1, max: 1 };
    return fragment(
      element('h2', { style: HEADING_STYLE }, `Party (${size.min} to ${size.max})`), members,
      row(name, race, characterClass,
        button('Roll', () => send({ action: 'roll', name: name.value, race: race.value, class: characterClass.value }))),
      row(button('Begin', () => send({ action: 'begin' })), button('Back', () => send({ action: 'quit' }))));
  };

  const renderPlay = (view) => {
    const menu = row(...(view.menu ?? []).map((option) =>
      button(`${option.number}. ${option.label}`, () => send({ action: 'play', command: `choose ${option.number}` }))));
    const moves = row(...['left', 'forward', 'right', 'back', 'around', 'look', 'status'].map((move) =>
      button(move, () => send({ action: 'play', command: move }))));
    const log = element('pre', { id: 'rusty-goldbox-log', style: LOG_STYLE }, (view.log ?? []).join('\n'));
    queueMicrotask(() => { log.scrollTop = log.scrollHeight; });
    return fragment(
      element('pre', { id: 'rusty-goldbox-map', style: 'margin:0 0 6px;line-height:1.05' }, view.map ?? ''),
      menu, moves, row(command),
      log,
      element('div', { style: 'opacity:.8' }, (view.party ?? []).map((member) => `${member.name} ${member.tracks.join(' ')}`).join(' · ')),
      row(slot, button('Save', () => send({ action: 'save', slot: slot.value })), button('Quit', () => send({ action: 'quit' }))));
  };

  // The fight plays back in the view window; Enter or Space (or this button) skips to the end, then returns to play.
  const renderCombat = (view) => {
    const fight = view.fight ?? { members: [], log: [] };
    const side = (index) => element('ul', { style: 'padding-left:16px;margin:2px 0' },
      ...fight.members.filter((member) => member.side === index).map((member) => element('li',
        { style: member.defeated ? 'opacity:.45;text-decoration:line-through' : (member.acting ? 'color:#f6c177' : '') },
        `${member.name}: ${fight.track} ${member.value}${member.max === null ? '' : '/' + member.max}`)));
    return fragment(
      element('h2', { style: HEADING_STYLE }, `Combat: ${fight.encounter ?? ''}`),
      element('div', { style: 'display:flex;gap:16px' },
        element('div', {}, element('strong', {}, 'Party'), side(0)),
        element('div', {}, element('strong', {}, fight.encounter ?? 'Foes'), side(1))),
      element('pre', { id: 'rusty-goldbox-fight', style: LOG_STYLE }, fight.log.join('\n')),
      fight.done ? element('div', { style: 'margin:4px 0' }, fight.outcome ?? '') : '',
      row(button(fight.done ? 'Continue' : 'Skip', () => send({ action: 'continue' }))));
  };

  let unsubscribe;
  const projection = context?.projection;
  if (projection?.subscribe !== undefined) {
    unsubscribe = projection.subscribe((envelope) => {
      if (envelope?.value !== undefined && envelope?.value !== null) {
        render(envelope.value);
      }
    });
  }

  return Object.freeze({
    dispose: () => {
      unsubscribe?.();
      panel.remove();
    },
  });
}

// The first-person view is drawn by the Engine in the top-left window (SceneView.Window); the panel sits to its right.
const PANEL_STYLE = 'position:absolute;top:1%;left:49%;right:1%;max-height:98%;overflow:auto;'
  + 'padding:10px;background:rgba(16,16,24,.92);color:#e0def4;font:13px/1.35 ui-monospace,monospace;border-radius:6px';
const HEADING_STYLE = 'font-size:14px;margin:8px 0 4px';
const LOG_STYLE = 'max-height:220px;overflow:auto;white-space:pre-wrap;margin:6px 0;padding:4px;background:rgba(255,255,255,.05)';

function element(tag, attributes = {}, ...children) {
  const node = document.createElement(tag);
  for (const [key, value] of Object.entries(attributes)) {
    if (key === 'value') {
      node.value = value;
    } else {
      node.setAttribute(key, value);
    }
  }
  node.append(...children);
  return node;
}

function fragment(...children) {
  const node = document.createDocumentFragment();
  node.append(...children);
  return node;
}

function row(...children) {
  return element('div', { style: 'display:flex;flex-wrap:wrap;gap:4px;margin:4px 0' }, ...children);
}

function button(label, onClick) {
  const node = element('button', { type: 'button' }, label);
  node.addEventListener('click', onClick);
  return node;
}

/** Fills a select with { id, name } choices, keeping the current choice when it is still offered. */
function fill(select, choices) {
  const current = select.value;
  select.replaceChildren(...choices.map((choice) => element('option', { value: choice.id }, choice.name)));
  if (choices.some((choice) => choice.id === current)) {
    select.value = current;
  }
}
