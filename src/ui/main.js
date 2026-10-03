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
  const portrait = element('select', { 'aria-label': 'Portrait' });
  // Feature and boost choices, kept by slot so a choice survives re-renders.
  const choiceSelects = new Map();
  const choiceSelect = (key, label) => {
    if (!choiceSelects.has(key)) {
      const select = element('select', { 'aria-label': label });
      select.addEventListener('change', () => rerenderParty());
      choiceSelects.set(key, select);
    }
    return choiceSelects.get(key);
  };
  let lastView = null;
  const rerenderParty = () => {
    if (lastView?.screen === 'party') {
      body.replaceChildren(renderParty(lastView));
    }
  };
  race.addEventListener('change', () => rerenderParty());
  characterClass.addEventListener('change', () => rerenderParty());
  portrait.addEventListener('change', () => rerenderParty());
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
    lastView = view;
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
      // Extensions the player may add: drop-in content built on the campaign's ruleset.
      const choices = (campaign.extensions ?? []).map((extension) => {
        const box = element('input', { type: 'checkbox', 'aria-label': `Add ${extension.title} to ${campaign.title}` });
        return { extension, box };
      });
      list.append(element('li', {},
        `${campaign.title} (${campaign.id} ${campaign.version}) `,
        button('Open', () => send({
          action: 'open',
          campaign: campaign.bundle,
          extensions: choices.filter((choice) => choice.box.checked).map((choice) => choice.extension.id),
        })),
        ...(choices.length ? [element('div', { style: 'opacity:.8' }, 'Extensions: ',
          ...choices.map((choice) => element('label', { style: 'margin-right:8px' }, choice.box,
            ` ${choice.extension.title} (${choice.extension.id} ${choice.extension.version})`)))] : [])));
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
    fill(portrait, [{ id: '', name: '(no portrait)' }, ...(view.portraits ?? [])]);
    const members = element('ol', { style: 'padding-left:20px' });
    (view.party ?? []).forEach((member, index) => {
      const item = element('select', { 'aria-label': `Item for ${member.name}` });
      fill(item, view.items ?? []);
      members.append(element('li', {},
        element('div', { style: 'display:flex;gap:6px;align-items:center' },
          ...picture(member.portraitPicture, `${member.name}'s portrait`, 40),
          element('span', {}, `${member.name}: ${[member.race, member.class && `${member.class} ${member.level}`].filter(Boolean).join(' ')}, ${member.tracks.join(', ')}, gold ${member.gold}`)),
        element('div', { style: 'opacity:.8' }, member.attributes.join(' ')),
        ...(member.features?.length ? [element('div', {}, `Features: ${member.features.join(', ')}`)] : []),
        element('div', {}, `Equipment: ${member.equipment.map((equipment) => equipment.name).join(', ') || 'none'}`),
        ...renderSpells(member, index),
        ...renderMemorised(member, index),
        row(item,
          button('Give/take', () => send({ action: 'equip', member: index, item: item.value })),
          button('Drop', () => send({ action: 'drop', member: index })))));
    });
    const size = view.partySize ?? { min: 1, max: 1 };
    const choices = renderChoices(view);
    const chosen = (view.portraits ?? []).find((entry) => entry.id === portrait.value);
    return fragment(
      element('h2', { style: HEADING_STYLE }, `Party (${size.min} to ${size.max})`),
      ...(view.extensions?.length ? [element('div', { style: 'opacity:.8' }, `Extensions: ${view.extensions.join(', ')}`)] : []),
      members,
      row(name, ...((view.races ?? []).length > 0 ? [race] : []), ...((view.classes ?? []).length > 0 ? [characterClass] : []), portrait, ...picture(chosen?.picture, 'Chosen portrait', 32)),
      choices.rows,
      row(button('Roll', () => send({
        action: 'roll',
        name: name.value,
        // A ruleset without races or classes has none to send.
        ...((view.races ?? []).length > 0 ? { race: race.value } : {}),
        ...((view.classes ?? []).length > 0 ? { class: characterClass.value } : {}),
        ...(portrait.value ? { portrait: portrait.value } : {}),
        ...(choices.features().length > 0 ? { features: choices.features() } : {}),
        ...(choices.boosts().length > 0 ? { boosts: choices.boosts() } : {}),
      }))),
      row(button('Begin', () => send({ action: 'begin' })), button('Back', () => send({ action: 'quit' }))));
  };

  /**
   * The choices a roll needs, in the order Core takes them: a feature for each
   * slot creation and the class's first level grant, then (for creation by
   * boosts) a boost for each choice the race, the creation features, the class
   * and the creation offer. Fixed boosts need no choice.
   */
  const renderChoices = (view) => {
    const creation = view.creation;
    const chosenClass = (view.classes ?? []).find((entry) => entry.id === characterClass.value);
    const chosenRace = (view.races ?? []).find((entry) => entry.id === race.value);
    const features = view.features ?? [];
    const rows = element('div');
    const featureSlots = [];
    const creationSlots = [];
    if (!creation) {
      return { rows, features: () => [], boosts: () => [] };
    }

    const grants = [...creation.grants.map((grant) => ({ grant, creation: true })), ...(chosenClass?.grants ?? []).map((grant) => ({ grant, creation: false }))];
    grants.forEach(({ grant, creation: fromCreation }, index) => {
      for (let made = 0; made < grant.count; made++) {
        const kinds = grant.kinds.join(' or ');
        const select = choiceSelect(`feature-${index}-${made}-${grant.kinds.join('|')}`, `Choose ${kinds}`);
        fill(select, features.filter((feature) => grant.kinds.includes(feature.kind)));
        featureSlots.push(select);
        if (fromCreation) {
          creationSlots.push(select);
        }

        rows.append(row(element('span', {}, `${kinds}:`), select));
      }
    });

    const boostSlots = [];
    if (creation.method === 'boosts') {
      const sources = [
        ['race', chosenRace],
        ...creationSlots.map((select, index) => [`feature-${index}`, features.find((feature) => feature.id === select.value)]),
        ['class', chosenClass],
        ['creation', { name: 'Free', boosts: creation.boosts }],
      ];
      for (const [key, source] of sources) {
        (source?.boosts ?? []).forEach((options, index) => {
          if (options.length === 1) {
            return;
          }

          const offered = options.length === 0 ? creation.attributes : options;
          const select = choiceSelect(`boost-${key}-${source.id ?? key}-${index}`, `${source.name} boost ${index + 1}`);
          fill(select, offered.map((attribute) => ({ id: attribute, name: attribute })));
          boostSlots.push(select);
          rows.append(row(element('span', {}, `${source.name} boost ${index + 1}:`), select));
        });
      }
    }

    return {
      rows,
      features: () => featureSlots.map((select) => select.value).filter((value) => value),
      boosts: () => boostSlots.map((select) => select.value),
    };
  };

  /** The party as a roster strip: each member's portrait, name and track values. */
  const renderRoster = (party) => element('div', { id: 'rusty-goldbox-roster', style: 'display:flex;flex-wrap:wrap;gap:8px;margin:6px 0' },
    ...party.map((member) => element('div', { style: 'display:flex;gap:6px;align-items:center;padding:3px 6px;background:rgba(255,255,255,.05);border-radius:4px' },
      ...picture(member.portraitPicture, `${member.name}'s portrait`, 48),
      element('div', {},
        element('strong', {}, member.name),
        ...member.tracks.map((track) => element('div', { style: 'opacity:.85' }, track)),
        element('div', { style: 'opacity:.85' }, `${member.experience} xp${member.levelReady ? ' (level ready)' : ''}`)))));

  /** A checkbox per spell the member could know, checked for those it knows; a change sets the whole list. */
  const renderSpells = (member, index) => {
    const castable = member.castable ?? [];
    if (castable.length === 0) {
      return [];
    }

    const boxes = castable.map((spell) => {
      const box = element('input', { type: 'checkbox', 'aria-label': `${member.name} knows ${spell.name}` });
      box.checked = (member.spells ?? []).some((known) => known.id === spell.id);
      return { spell, box };
    });
    for (const { box } of boxes) {
      box.addEventListener('change', () => send({
        action: 'spells',
        member: index,
        spells: boxes.filter((entry) => entry.box.checked).map((entry) => entry.spell.id),
      }));
    }

    return [row(element('span', {}, `${member.name}'s spells:`),
      ...boxes.map(({ spell, box }) => element('label', { style: 'margin-right:8px' }, box, ` ${spell.name}`)))];
  };

  /**
   * The copies a member memorises each day, with + and − per spell it could
   * memorise; each change sends the whole list, which Core checks against its
   * slots. In play the new list is prepared at the next rest.
   */
  const renderMemorised = (member, index) => {
    const options = member.memorisable ?? [];
    if (options.length === 0) {
      return [];
    }

    const plan = (member.memorisedChosen ? member.memorised : []).map((spell) => spell.id);
    const submit = (spells) => send({ action: 'memorise', member: index, spells });
    const names = (list) => list.map((spell) => spell.name).join(', ') || 'none';
    return [
      element('div', {}, `${member.name} memorises: ${names(member.memorised ?? [])}${member.memorisedChosen ? '' : ' (known spells in order)'}; left today: ${names(member.prepared ?? [])}`),
      row(...options.flatMap((spell) => [
        // Adding to the default (known spells in order) starts a list of the member's own choosing.
        button(`+ ${spell.name}`, () => submit([...plan, spell.id])),
        button(`− ${spell.name}`, () => {
          const current = member.memorisedChosen ? [...plan] : (member.memorised ?? []).map((entry) => entry.id);
          const at = current.lastIndexOf(spell.id);
          if (at >= 0) {
            current.splice(at, 1);
            submit(current);
          }
        }),
      ])),
    ];
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
      renderRoster(view.party ?? []),
      ...(view.party ?? []).flatMap((member, index) => [...renderSpells(member, index), ...renderMemorised(member, index)]),
      // A level that needs choices is taken by typing them: level <n> --feature <id> (the refusal lists what's open).
      row(...(view.party ?? []).flatMap((member, index) => member.levelReady
        ? [button(`Level up ${member.name}`, () => send({ action: 'play', command: `level ${index + 1}` }))]
        : [])),
      // A dual-classed member may call on its waiting former class, forfeiting the adventure's experience.
      row(...(view.party ?? []).flatMap((member, index) => member.formerClasses === 'waiting'
        ? [button(`${member.name}: call on former class`, () => send({ action: 'play', command: `former ${index + 1} on` }))]
        : member.formerClasses === 'called'
          ? [button(`${member.name}: set former class aside`, () => send({ action: 'play', command: `former ${index + 1} off` }))]
          : [])),
      row(slot, button('Save', () => send({ action: 'save', slot: slot.value })), button('Quit', () => send({ action: 'quit' }))));
  };

  // The fight plays back in the view window; Enter or Space (or this button) skips to the end, then returns to play.
  const renderCombat = (view) => {
    const fight = view.fight ?? { members: [], log: [] };
    const side = (index) => element('ul', { style: 'padding-left:16px;margin:2px 0' },
      ...fight.members.filter((member) => member.side === index).map((member) => element('li',
        { style: member.defeated ? 'opacity:.45;text-decoration:line-through' : (member.acting ? 'color:#f6c177' : '') },
        ...picture(member.iconPicture, '', 16),
        ` ${member.name}: ${fight.track} ${member.value}${member.max === null ? '' : '/' + member.max}`)));
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

/**
 * Any picture the projection carries ({ url, width, height, frame }), drawn
 * pixel-sharp within a size-pixel square: an image whole, a sheet's first
 * frame cropped from it. Nothing without one. Every panel picture goes
 * through here, so a new kind of media is drawn in one place.
 */
function picture(media, label, size) {
  if (!media?.url) {
    return [];
  }

  if (!media.frame) {
    return [element('img', { src: media.url, alt: label, width: size, height: size, style: 'image-rendering:pixelated;object-fit:contain' })];
  }

  const [frameWidth, frameHeight] = media.frame;
  const scale = size / Math.max(frameWidth, frameHeight);
  return [element('div', {
    role: 'img',
    'aria-label': label,
    style: `width:${frameWidth * scale}px;height:${frameHeight * scale}px;image-rendering:pixelated;`
      + `background:url("${media.url}") 0 0 / ${media.width * scale}px ${media.height * scale}px no-repeat`,
  })];
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
