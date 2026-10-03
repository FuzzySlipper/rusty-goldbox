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

  const currencyName = (id) => id ? id.split(':').at(-1) : 'currency';
  const formatBalances = (balances) => {
    const entries = Object.entries(balances ?? {});
    return entries.length === 0 ? 'no money' : entries.map(([id, amount]) => `${amount} ${currencyName(id)}`).join(', ');
  };

  const panel = element('aside', { 'aria-label': 'Rusty Goldbox', 'data-goldbox-panel': '', style: PANEL_STYLE });
  const title = element('h1', { style: 'margin:0 0 4px;font-size:16px' }, 'Rusty Goldbox');
  // The look lives in a stylesheet of theme variables, so a skin restyles every panel at once.
  const skinStyle = element('style', {}, BASE_LOOK);
  let appliedSkin = null;
  const applySkin = (skin) => {
    const key = JSON.stringify(skin ?? null);
    if (key === appliedSkin) {
      return;
    }

    appliedSkin = key;
    skinStyle.textContent = BASE_LOOK + skinLook(skin);
    title.replaceChildren(...(skin?.title?.url ? picture(skin.title, 'Rusty Goldbox', 480) : ['Rusty Goldbox']));
  };
  const status = element('output', { id: 'rusty-goldbox-status', 'aria-live': 'polite', style: 'display:block;margin-bottom:6px' });
  const notes = element('ul', { id: 'rusty-goldbox-notes', style: 'color:var(--gb-accent);margin:0 0 6px;padding-left:16px' });
  const body = element('div');

  // Inputs live outside the re-rendered body so typing survives updates.
  const slot = element('input', { value: 'slot-1', size: '10', 'aria-label': 'Save slot' });
  const name = element('input', { value: 'Ada', size: '10', 'aria-label': 'Character name' });
  const race = element('select', { 'aria-label': 'Race' });
  const characterClass = element('select', { 'aria-label': 'Class' });
  const creation = element('select', { 'aria-label': 'Character creation' });
  const portrait = element('select', { 'aria-label': 'Portrait' });
  const lifepathCareer = element('select', { 'aria-label': 'Career' });
  const lifepathTerms = element('input', { type: 'number', min: '1', value: '1', size: '3', 'aria-label': 'Career terms' });
  const lifepathTables = element('select', { 'aria-label': 'Skill table' });
  const lifepathBenefits = element('select', { 'aria-label': 'Benefit kind' });
  // Feature and boost choices, kept by slot so a choice survives re-renders.
  const choiceSelects = new Map();
  // Skill amounts are a local draft until the player submits the Core action.
  const skillDrafts = new Map();
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
  creation.addEventListener('change', () => {
    choiceSelects.clear();
    rerenderParty();
  });
  portrait.addEventListener('change', () => rerenderParty());
  lifepathCareer.addEventListener('change', () => rerenderParty());
  const command = element('input', { size: '14', placeholder: 'command', 'aria-label': 'Play command' });
  command.addEventListener('keydown', (event) => {
    if (event.key === 'Enter' && command.value.trim().length > 0) {
      send({ action: 'play', command: command.value.trim() });
      command.value = '';
    }
  });

  // Volume sliders stay put across renders, so dragging one isn't interrupted; the projection sets them when idle.
  const slider = (bus, label) => {
    const input = element('input', { type: 'range', min: '0', max: '1', step: '0.05', 'aria-label': `${label} volume` });
    input.addEventListener('change', () => send({ action: 'volume', bus, volume: Number(input.value) }));
    return input;
  };
  const musicVolume = slider('music', 'Music');
  const soundVolume = slider('sound', 'Sound');
  const skinPick = element('select', { 'aria-label': 'Skin' });
  skinPick.addEventListener('change', () => send({ action: 'skin', skin: skinPick.value || null }));
  const volumes = element('div', { style: 'display:flex;gap:8px;align-items:center;margin-top:8px;opacity:.8' },
    'Music', musicVolume, 'Sound', soundVolume, 'Skin', skinPick);

  panel.append(title, status, notes, body, volumes);
  root.append(skinStyle, panel);

  const render = (view) => {
    lastView = view;
    status.textContent = view.status ?? '';
    notes.replaceChildren(...(view.notes ?? []).map((note) => element('li', {}, note)));
    applySkin(view.skin);
    if (document.activeElement !== skinPick) {
      fill(skinPick, [{ id: '', name: "(the campaign's own)" }, ...(view.skins ?? [])]);
      skinPick.value = view.skinPicked ?? '';
    }

    for (const [input, value] of [[musicVolume, view.volumes?.music], [soundVolume, view.volumes?.sound]]) {
      if (value !== undefined && document.activeElement !== input) {
        input.value = String(value);
      }
    }
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
    const creationChoices = view.creations?.length ? view.creations : (view.creation ? [view.creation] : []);
    const currentCreation = creation.value;
    fill(creation, creationChoices);
    const creationId = creationChoices.some((choice) => choice.id === currentCreation)
      ? currentCreation
      : view.creation?.id ?? creationChoices[0]?.id;
    if (creationId) {
      creation.value = creationId;
    }

    const selectedCreation = creationChoices.find((choice) => choice.id === creation.value) ?? view.creation;
    fill(portrait, [{ id: '', name: '(no portrait)' }, ...(view.portraits ?? [])]);
    const members = element('ol', { style: 'padding-left:20px' });
    (view.party ?? []).forEach((member, index) => {
      const item = element('select', { 'aria-label': `Item for ${member.name}` });
      fill(item, view.items ?? []);
      members.append(element('li', {},
        element('div', { style: 'display:flex;gap:6px;align-items:center' },
          ...picture(member.portraitPicture, `${member.name}'s portrait`, 40),
          element('span', {}, `${member.name}: ${[member.race, member.class && `${member.class} ${member.level}`].filter(Boolean).join(' ')}, ${member.tracks.join(', ')}, ${formatBalances(member.balances)}`)),
        element('div', { style: 'opacity:.8' }, member.attributes.join(' ')),
        ...(member.careerTerms?.length ? [element('div', { style: 'opacity:.8' }, `Prior history age ${member.age}: `,
          ...member.careerTerms.map((term) => element('div', {}, `Term ${term.number} ${term.career} (${term.results.join(', ') || 'no result'}${term.benefitsLost ? '; benefits lost' : ''})`)))] : []),
        ...(member.features?.length ? [element('div', {}, `Features: ${member.features.join(', ')}`)] : []),
        ...renderSkillPoints(member, index),
        element('div', {}, `Equipment: ${member.equipment.map((equipment) => equipment.name).join(', ') || 'none'}`),
        ...renderSpells(member, index),
        ...renderMemorised(member, index),
        row(item,
          button('Give/take', () => send({ action: 'equip', member: index, item: item.value })),
          button('Drop', () => send({ action: 'drop', member: index })))));
    });
    const size = view.partySize ?? { min: 1, max: 1 };
    const choices = renderChoices(view, selectedCreation);
    const chosen = (view.portraits ?? []).find((entry) => entry.id === portrait.value);
    const creationControl = creationChoices.length > 1
      ? [element('span', {}, 'Creation:'), creation]
      : selectedCreation ? [element('span', {}, `Creation: ${selectedCreation.name}`)] : [];
    return fragment(
      element('h2', { style: HEADING_STYLE }, `Party (${size.min} to ${size.max})`),
      ...(view.extensions?.length ? [element('div', { style: 'opacity:.8' }, `Extensions: ${view.extensions.join(', ')}`)] : []),
      members,
      row(name, ...creationControl, ...((view.races ?? []).length > 0 ? [race] : []), ...((view.classes ?? []).length > 0 ? [characterClass] : []), portrait, ...picture(chosen?.picture, 'Chosen portrait', 32)),
      choices.rows,
      ...(view.lifepath ? [row(element('span', {}, 'Career:'), lifepathCareer,
        element('span', {}, 'Terms:'), lifepathTerms,
        element('span', {}, 'Skill table:'), lifepathTables,
        element('span', {}, 'Benefit:'), lifepathBenefits)] : []),
      row(button('Roll', () => send({
        action: 'roll',
        name: name.value,
        ...(selectedCreation?.id ? { creation: selectedCreation.id } : {}),
        // A ruleset without races or classes has none to send.
        ...((view.races ?? []).length > 0 ? { race: race.value } : {}),
        ...((view.classes ?? []).length > 0 ? { class: characterClass.value } : {}),
        ...(portrait.value ? { portrait: portrait.value } : {}),
        ...(choices.features().length > 0 ? { features: choices.features() } : {}),
        ...(choices.boosts().length > 0 ? { boosts: choices.boosts() } : {}),
        ...(view.lifepath ? {
          lifepath: view.lifepath.id,
          careers: lifepathCareer.value ? [lifepathCareer.value] : [],
          terms: Number(lifepathTerms.value) || 1,
          skillTables: lifepathTables.value ? [lifepathTables.value] : [],
          benefits: lifepathBenefits.value ? [lifepathBenefits.value] : [],
        } : {}),
      }))),
      row(button('Begin', () => send({ action: 'begin' })), button('Back', () => send({ action: 'quit' }))));
  };

  /**
   * The choices a roll needs, in the order Core takes them: a feature for each
   * slot creation and the class's first level grant, then (for creation by
   * boosts) a boost for each choice the race, the creation features, the class
   * and the creation offer. Fixed boosts need no choice.
   */
  const renderChoices = (view, selectedCreation) => {
    const creation = selectedCreation ?? view.creation;
    const chosenClass = (view.classes ?? []).find((entry) => entry.id === characterClass.value);
    const chosenRace = (view.races ?? []).find((entry) => entry.id === race.value);
    const features = view.features ?? [];
    const rows = element('div');
    const featureSlots = [];
    const creationSlots = [];
    if (!creation) {
      return { rows, features: () => [], boosts: () => [] };
    }

    if (view.lifepath) {
      fill(lifepathCareer, view.lifepath.careers ?? []);
      if (!lifepathCareer.value && view.lifepath.careers?.length) {
        lifepathCareer.value = view.lifepath.careers[0].id;
      }
      const selected = (view.lifepath.careers ?? []).find((career) => career.id === lifepathCareer.value);
      const tableChoices = (selected?.skillTables ?? []).map((table) => typeof table === 'string' ? { id: table, name: table } : table);
      fill(lifepathTables, tableChoices);
      const benefitChoices = (selected?.benefitKinds ?? view.lifepath.benefitKinds ?? [
        { id: 'cash', name: 'Cash' },
        { id: 'material', name: 'Material' },
      ]).map((benefit) => typeof benefit === 'string' ? { id: benefit, name: benefit } : benefit);
      fill(lifepathBenefits, benefitChoices);
      rows.append(element('div', { style: 'opacity:.8' }, `${view.lifepath.name}: choose one career, skill table and benefit kind; the selected choices repeat for the policy's actual rolls.`));
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

  /**
   * Shows the actual post-roll staged skill values and keeps only the numeric
   * input draft in the DOM. Core owns budgets, profession limits and the final
   * allocation check when the existing `skills` action is submitted.
   */
  const renderSkillPoints = (member, index) => {
    const skillPoints = member.skillPoints;
    if (!skillPoints?.skills?.length) {
      return [];
    }

    const memberKey = `${index}:${member.name}:${(member.attributes ?? []).join('|')}`;
    const committed = new Map((member.skillAllocations ?? []).map((allocation) => [allocation.id, allocation]));
    const hasCommitted = Boolean(skillPoints.committed) || committed.size > 0;
    const fields = [];
    const rows = skillPoints.skills.map((skill) => {
      const saved = committed.get(skill.id) ?? {};
      const professionKey = `${memberKey}:${skill.id}:profession`;
      const personalKey = `${memberKey}:${skill.id}:personal`;
      const profession = pointInput(professionKey, saved.profession ?? 0, skill.profession && !hasCommitted);
      const personal = pointInput(personalKey, saved.personal ?? 0, !hasCommitted);
      fields.push({ skill: skill.id, profession, personal });
      return row(
        element('span', { style: 'min-width:210px' }, `${skill.id}: base ${skill.base}, current ${skill.current}`),
        element('label', {}, skill.profession ? 'Profession ' : 'Profession (not allowed) ', profession),
        element('label', {}, 'Personal ', personal));
    });

    return [element('section', { style: 'margin:5px 0;padding:4px;background:var(--gb-inset)' },
      element('strong', {}, `${member.name}'s staged skill points`),
      element('div', { style: 'opacity:.85' }, `Profession budget: ${skillPoints.profession}; Personal budget: ${skillPoints.personal}. Base/current values are from this roll.`),
      ...rows,
      hasCommitted
        ? element('div', { style: 'opacity:.85' }, 'Skill points committed.')
        : row(button(`Spend ${member.name}'s skill points`, () => send({
          action: 'skills',
          member: index,
          skills: fields.map((field) => ({
            skill: field.skill,
            profession: Number(field.profession.value),
            personal: Number(field.personal.value),
          })),
        })))]

    function pointInput(key, initial, allowed) {
      const input = element('input', { type: 'number', min: '0', step: 'any', value: String(skillDrafts.get(key) ?? initial), size: '5' });
      input.disabled = !allowed;
      input.addEventListener('input', () => skillDrafts.set(key, input.value));
      return input;
    }
  };

  /** The party as a roster strip: each member's portrait, name and track values. */
  const renderRoster = (party) => element('div', { id: 'rusty-goldbox-roster', style: 'display:flex;flex-wrap:wrap;gap:8px;margin:6px 0' },
    ...party.map((member) => element('div', { style: 'display:flex;gap:6px;align-items:center;padding:3px 6px;background:var(--gb-inset);border-radius:4px' },
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
    const moves = row(...((view.shop || view.temple || view.training) ? ['look', 'status'] : ['left', 'forward', 'right', 'back', 'around', 'look', 'status']).map((move) =>
      button(move, () => send({ action: 'play', command: move }))));
    const training = view.training ? fragment(element('p', {}, view.training),
      row(...view.party.map((member, index) => button(`Train ${member.name}`, () => send({ action: 'play', command: `train ${index + 1}` })))),
      row(button('Leave trainer', () => send({ action: 'play', command: 'leave' })))) : '';
    const temple = view.temple ? fragment(
      element('p', {}, view.temple.text),
      ...view.temple.services.map((service) => row(element('span', {}, service.label),
        ...service.prices.map((price, member) => button(`${view.party[member].name} — ${price} ${currencyName(service.currency)}`,
          () => send({ action: 'play', command: `serve ${service.number} ${member + 1}` }))))),
      row(button('Leave temple', () => send({ action: 'play', command: 'leave' })))) : '';
    const shop = view.shop ? fragment(
      element('h2', { style: HEADING_STYLE }, view.shop.text),
      element('div', {}, `Party balances: ${formatBalances(view.shop.balances)}`),
      element('div', {}, 'Buy'),
      row(...view.shop.stock.map((offer) => button(`${offer.number}. ${offer.name} — ${offer.price} ${currencyName(offer.currency)}`,
        () => send({ action: 'play', command: `buy ${offer.number}` })))),
      element('div', {}, 'Sell'),
      row(...view.shop.carried.map((offer) => button(`${offer.number}. ${offer.name}${offer.holder ? ` (${offer.holder})` : ''} — ${offer.price} ${currencyName(offer.currency)}`,
        () => send({ action: 'play', command: `sell ${offer.number}` })))),
      row(button('Leave shop', () => send({ action: 'play', command: 'leave' })))) : '';
    const log = element('pre', { id: 'rusty-goldbox-log', style: LOG_STYLE }, (view.log ?? []).join('\n'));
    queueMicrotask(() => { log.scrollTop = log.scrollHeight; });
    return fragment(
      element('pre', { id: 'rusty-goldbox-map', style: 'margin:0 0 6px;line-height:1.05' }, view.map ?? ''),
      menu, shop, temple, training, moves, row(command),
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
        { style: member.defeated ? 'opacity:.45;text-decoration:line-through' : (member.acting ? 'color:var(--gb-accent)' : '') },
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
  + 'padding:10px;font:13px/1.35 ui-monospace,monospace';

// The Game's own look, as theme variables a skin's colours override.
const BASE_LOOK = '[data-goldbox-panel]{--gb-background:rgba(16,16,24,.92);--gb-text:#e0def4;--gb-muted:#908caa;'
  + '--gb-accent:#f6c177;--gb-border:transparent;--gb-inset:rgba(255,255,255,.05);'
  + 'background:var(--gb-background);color:var(--gb-text);border:1px solid var(--gb-border);border-radius:6px}';

/** A skin's stylesheet: its colours as theme variables, its panel tile under them, its frame and button faces cut into nine. */
function skinLook(skin) {
  if (!skin) {
    return '';
  }

  const colors = Object.entries(skin.colors ?? {}).map(([name, value]) => `--gb-${name.replace('_', '-')}:${value};`).join('');
  let css = `[data-goldbox-panel]{${colors}}`;
  if (skin.panel?.url) {
    css += '[data-goldbox-panel]{background:linear-gradient(var(--gb-background),var(--gb-background)),'
      + `url("${skin.panel.url}") 0 0 / ${skin.panel.width * 2}px ${skin.panel.height * 2}px repeat;image-rendering:pixelated}`;
  }

  if (skin.frame?.picture?.url) {
    const width = skin.frame.slice * 2;
    css += `[data-goldbox-panel]{border:${width}px solid transparent;border-radius:0;`
      + `border-image:url("${skin.frame.picture.url}") ${skin.frame.slice} / ${width}px stretch}`;
  }

  if (skin.colors?.button || skin.colors?.button_text || skin.button) {
    css += '[data-goldbox-panel] button{background:var(--gb-button,buttonface);color:var(--gb-button-text,buttontext);font:inherit;padding:1px 6px';
    if (skin.button?.picture?.url) {
      const width = skin.button.slice;
      css += `;border:${width}px solid transparent;border-image:url("${skin.button.picture.url}") ${skin.button.slice} fill / ${width}px stretch;image-rendering:pixelated`;
    }

    css += '}';
  }

  return css;
}
const HEADING_STYLE = 'font-size:14px;margin:8px 0 4px';
const LOG_STYLE = 'max-height:220px;overflow:auto;white-space:pre-wrap;margin:6px 0;padding:4px;background:var(--gb-inset)';

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
 * Any picture the projection carries ({ url, width, height, frame, animation }),
 * drawn pixel-sharp within a size-pixel square: an image whole, a sheet's
 * frame cropped from it, playing its animation. Nothing without one. Every
 * panel picture goes through here, so a new kind of media is drawn in one place.
 */
function picture(media, label, size) {
  if (!media?.url) {
    return [];
  }

  if (!media.frame) {
    // Within a size-pixel square, keeping the image's shape.
    const fit = media.width && media.height ? size / Math.max(media.width, media.height) : 1;
    const width = media.width ? Math.round(media.width * fit) : size;
    const height = media.height ? Math.round(media.height * fit) : size;
    return [element('img', { src: media.url, alt: label, width, height, style: 'image-rendering:pixelated;object-fit:contain;max-width:100%;height:auto' })];
  }

  const [frameWidth, frameHeight] = media.frame;
  const scale = size / Math.max(frameWidth, frameHeight);
  const node = element('div', {
    role: 'img',
    'aria-label': label,
    style: `width:${frameWidth * scale}px;height:${frameHeight * scale}px;image-rendering:pixelated;`
      + `background:url("${media.url}") 0 0 / ${media.width * scale}px ${media.height * scale}px no-repeat`,
  });
  if (media.animation?.frames?.length) {
    // Re-renders keep a picture's clock, so the animation runs on instead of restarting.
    const key = `${media.url}|${label}`;
    if (!started.has(key)) {
      started.set(key, performance.now());
    }

    animated.set(node, { media, scale, start: started.get(key) });
    startAnimating();
  }

  return [node];
}

// Animated panel pictures and what each plays; one ticker steps them all while any is on the page.
const animated = new Map();
const started = new Map();
let ticking = false;

function startAnimating() {
  if (ticking) {
    return;
  }

  ticking = true;
  const step = (now) => {
    for (const [node, { media, scale, start }] of animated) {
      if (!node.isConnected) {
        animated.delete(node);
        continue;
      }

      const { frames, fps, loop } = media.animation;
      const played = Math.floor(((now - start) / 1000) * fps);
      const frame = frames[loop ? played % frames.length : Math.min(played, frames.length - 1)];
      const [frameWidth, frameHeight] = media.frame;
      const columns = Math.floor(media.width / frameWidth);
      node.style.backgroundPosition = `${-(frame % columns) * frameWidth * scale}px ${-Math.floor(frame / columns) * frameHeight * scale}px`;
    }

    if (animated.size > 0) {
      requestAnimationFrame(step);
    } else {
      ticking = false;
    }
  };
  requestAnimationFrame(step);
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
