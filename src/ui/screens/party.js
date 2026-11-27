import { element, fragment, row, button, fill, selectBox, picture, formatBalances, trackText } from '../dom.js';
import { renderSpells, renderMemorised } from '../panels/members.js';
import { invitation } from '../table.js';

const SUGGESTED_NAMES = [
  'Ada', 'Bram', 'Cora', 'Dain', 'Elen', 'Finn',
  'Garrick', 'Hilda', 'Iris', 'Joren', 'Kael', 'Lyra',
  'Maren', 'Nolan', 'Orin', 'Petra', 'Rowan', 'Sera',
];

const formatSigned = (value) => (value > 0 ? `+${value}` : String(value));
const titleCase = (text) => text ? text.replace(/[_-]+/g, ' ').replace(/\b\w/g, (char) => char.toUpperCase()) : '';

/**
 * Computes the cost of a score from an authored point-buy table (matching Core's
 * row intervals or exact score rows). Returns null if out of table range.
 */
function lookupPointCost(costRows, score) {
  if (!Array.isArray(costRows) || costRows.length === 0 || Number.isNaN(score)) {
    return null;
  }

  for (const entry of costRows) {
    if (!Array.isArray(entry) || entry.length < 2) {
      continue;
    }

    const key = entry[0];
    const cost = Number(entry[entry.length - 1]);
    if (typeof key === 'number' && key === score) {
      return cost;
    }

    if (typeof key === 'string') {
      if (key.endsWith('+')) {
        const low = Number(key.slice(0, -1));
        if (!Number.isNaN(low) && score >= low) {
          return cost;
        }
      } else if (key.includes('-')) {
        const [lowText, highText] = key.split('-');
        const low = Number(lowText);
        const high = Number(highText);
        if (!Number.isNaN(low) && !Number.isNaN(high) && score >= low && score <= high) {
          return cost;
        }
      } else if (Number(key) === score) {
        return cost;
      }
    }
  }

  return null;
}

/** Bounds of a point-buy table, falling back to attribute min/max. */
function pointBuyRange(costRows, attribute) {
  let min = attribute.min;
  let max = attribute.max;
  if (Array.isArray(costRows) && costRows.length > 0) {
    const numericKeys = costRows
      .map((entry) => Number(entry[0]))
      .filter((value) => !Number.isNaN(value));
    if (numericKeys.length > 0) {
      const tableMin = Math.min(...numericKeys);
      const tableMax = Math.max(...numericKeys);
      min = min === undefined ? tableMin : Math.max(min, tableMin);
      max = max === undefined ? tableMax : Math.min(max, tableMax);
    }
  }

  return { min, max };
}

/**
 * The party screen: two-column Character Forge + Party Roster supporting
 * every ruleset creation method (in-order roll, arranged roll, standard array,
 * point buy, attribute boosts, lifepath, and post-roll staged skill points).
 * Core remains the sole rule authority; the UI surfaces hints and keeps local drafts.
 */
export function createParty(send, rerender) {
  // Inputs live outside the re-rendered body so typing survives updates.
  const name = element('input', { value: SUGGESTED_NAMES[0], size: '14', placeholder: 'Character name', 'aria-label': 'Character name', 'data-focus-key': 'party:new:name' });
  const race = selectBox({ 'aria-label': 'Race', 'data-focus-key': 'party:new:race' });
  const characterClass = selectBox({ 'aria-label': 'Class', 'data-focus-key': 'party:new:class' });
  const creation = selectBox({ 'aria-label': 'Character creation', 'data-focus-key': 'party:new:creation' });
  const portrait = selectBox({ 'aria-label': 'Portrait', 'data-focus-key': 'party:new:portrait' });
  const lifepathCareer = selectBox({ 'aria-label': 'Career', 'data-focus-key': 'party:new:career' });
  const lifepathTerms = element('input', { type: 'number', min: '1', value: '1', size: '3', 'aria-label': 'Career terms', 'data-focus-key': 'party:new:terms' });
  const lifepathTables = selectBox({ 'aria-label': 'Skill table', 'data-focus-key': 'party:new:skill-table' });
  const lifepathBenefits = selectBox({ 'aria-label': 'Benefit kind', 'data-focus-key': 'party:new:benefit' });
  const importJson = element('textarea', { rows: '7', placeholder: 'Paste character JSON exported from Rusty Tales or `tales character new`…', 'aria-label': 'Character JSON to import', 'data-focus-key': 'party:import:json' });
  let workbenchTab = 'forge';
  let requestedDraftFor = null;
  // Feature and boost choices, kept by slot so a choice survives re-renders.
  const choiceSelects = new Map();
  // Skill amounts are a local draft until the player submits the Core action.
  const skillDrafts = new Map();
  // Creation scores and priorities are local intent drafts until the Roll action.
  const scoreDrafts = new Map();
  const priorityDrafts = new Map();
  let lastPartyCount = 0;
  let lastAutoName = SUGGESTED_NAMES[0];
  let nameCursor = 0;

  const choiceSelect = (key, label) => {
    if (!choiceSelects.has(key)) {
      const select = selectBox({ 'aria-label': label, 'data-focus-key': `party:choice:${key}` });
      select.addEventListener('change', () => rerender());
      choiceSelects.set(key, select);
    }
    return choiceSelects.get(key);
  };

  const pickNextName = (usedNames) => {
    const used = new Set(usedNames.map((entry) => entry.toLowerCase()));
    for (let offset = 1; offset <= SUGGESTED_NAMES.length; offset++) {
      const candidate = SUGGESTED_NAMES[(nameCursor + offset) % SUGGESTED_NAMES.length];
      if (!used.has(candidate.toLowerCase())) {
        nameCursor = (nameCursor + offset) % SUGGESTED_NAMES.length;
        return candidate;
      }
    }

    return `Hero ${usedNames.length + 1}`;
  };

  const pickNextPortrait = (portraits, party) => {
    if (!portraits?.length) {
      return '';
    }

    const used = new Set((party ?? []).map((member) => member.portrait).filter(Boolean));
    const unused = portraits.find((entry) => !used.has(entry.id));
    return (unused ?? portraits[party.length % portraits.length]).id;
  };

  race.addEventListener('change', () => rerender());
  characterClass.addEventListener('change', () => rerender());
  creation.addEventListener('change', () => {
    choiceSelects.clear();
    rerender();
  });
  portrait.addEventListener('change', () => rerender());
  lifepathCareer.addEventListener('change', () => rerender());

  const render = (view) => {
    const party = view.party ?? [];
    const portraits = view.portraits ?? [];
    const races = view.races ?? [];
    const classes = view.classes ?? [];

    // When a new character joins the party and the name still holds the previous auto-filled name,
    // advance to the next unused name and portrait so rolling a full party feels effortless.
    if (party.length > lastPartyCount) {
      const usedNames = party.map((member) => member.name);
      if (name.value === lastAutoName || usedNames.includes(name.value)) {
        const nextName = pickNextName(usedNames);
        name.value = nextName;
        lastAutoName = nextName;
      }

      if (portraits.length > 0) {
        portrait.value = pickNextPortrait(portraits, party);
      }
    } else if (party.length === 0 && lastPartyCount === 0 && !portrait.value && portraits.length > 0) {
      portrait.value = portraits[0].id;
    }

    lastPartyCount = party.length;

    fill(race, races);
    const chosenRace = races.find((entry) => entry.id === race.value) ?? races[0];
    const allowedClasses = Array.isArray(chosenRace?.classes) ? new Set(chosenRace.classes) : null;

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
    const isRollMethod = selectedCreation && (!selectedCreation.method || selectedCreation.method === 'roll');
    const draftMatches = Boolean(
      isRollMethod
      && view.draftScores?.scores
      && (!view.draftScores.creationId || view.draftScores.creationId === selectedCreation.id),
    );
    if (isRollMethod && !draftMatches && requestedDraftFor !== selectedCreation.id) {
      requestedDraftFor = selectedCreation.id;
      send({ action: 'roll-scores', creation: selectedCreation.id });
    } else if (draftMatches) {
      requestedDraftFor = null;
    }

    // Compute live adjusted scores for roll-in-order so the Class dropdown can show exact qualification right away.
    const liveAdjustedInOrder = (() => {
      if (!draftMatches || selectedCreation?.assignment === 'arrange') {
        return null;
      }
      const raw = view.draftScores.scores;
      const adj = chosenRace?.abilityAdjustments ?? {};
      const out = {};
      for (const [stat, value] of Object.entries(raw)) {
        out[stat] = Number(value) + Number(adj[stat] ?? 0);
      }
      return out;
    })();

    const missingForClass = (cls, scoresMap) => {
      if (!scoresMap || !cls?.requirements) {
        return [];
      }
      const missing = [];
      for (const [stat, minVal] of Object.entries(cls.requirements)) {
        const have = scoresMap[stat];
        if (have !== undefined && Number(have) < Number(minVal)) {
          missing.push(`${stat.toUpperCase()} ${minVal}+`);
        }
      }
      return missing;
    };

    // Populate class dropdown with race compatibility + live stat qualification awareness.
    const previousClass = characterClass.value;
    characterClass.replaceChildren(...classes.map((entry) => {
      const disallowed = allowedClasses !== null && !allowedClasses.has(entry.id);
      const missing = !disallowed ? missingForClass(entry, liveAdjustedInOrder) : [];
      const label = disallowed
        ? `${entry.name} (not for ${chosenRace.name})`
        : missing.length > 0
          ? `${entry.name} (needs ${missing.join(', ')})`
          : liveAdjustedInOrder
            ? `${entry.name} ✓`
            : entry.name;
      const option = element('option', { value: entry.id }, label);
      option.disabled = disallowed;
      return option;
    }));
    if (classes.some((entry) => entry.id === previousClass && (!allowedClasses || allowedClasses.has(entry.id)))) {
      characterClass.value = previousClass;
    } else {
      const firstQualified = classes.find((entry) => (!allowedClasses || allowedClasses.has(entry.id)) && missingForClass(entry, liveAdjustedInOrder).length === 0);
      const firstValid = firstQualified ?? classes.find((entry) => !allowedClasses || allowedClasses.has(entry.id)) ?? classes[0];
      if (firstValid) {
        characterClass.value = firstValid.id;
      }
    }

    const chosenClass = classes.find((entry) => entry.id === characterClass.value) ?? classes[0];
    const activeLifepath = selectedCreation?.lifepath ?? view.lifepath ?? null;
    const formatPortraitName = (raw) => titleCase(String(raw ?? '').replace(/_?portrait$/i, '') || raw);
    fill(portrait, [{ id: '', name: '(no portrait)' }, ...portraits.map((entry) => ({ id: entry.id, name: formatPortraitName(entry.name) }))]);
    if (!portrait.value && portraits.length > 0 && party.length === 0) {
      portrait.value = portraits[0].id;
    }

    const size = view.partySize ?? { min: 1, max: 1 };
    const readyToBegin = party.length >= size.min && party.length <= size.max;
    const partyFull = party.length >= size.max;

    const choices = renderChoices(view, selectedCreation, chosenRace, chosenClass, activeLifepath);
    const creationValues = () => {
      const attributes = choices.attributes();
      const priority = choices.priority();
      return {
        ...(attributes ? { attributes } : {}),
        ...(priority ? { priority } : {}),
      };
    };

    const roll = button('Roll character', () => send({
      action: 'roll',
      name: name.value,
      ...(selectedCreation?.id ? { creation: selectedCreation.id } : {}),
      ...(races.length > 0 ? { race: race.value } : {}),
      ...(classes.length > 0 ? { class: characterClass.value } : {}),
      ...(portrait.value ? { portrait: portrait.value } : {}),
      ...(choices.features().length > 0 ? { features: choices.features() } : {}),
      ...(choices.boosts().length > 0 ? { boosts: choices.boosts() } : {}),
      ...creationValues(),
      ...(activeLifepath ? {
        lifepath: activeLifepath.id,
        careers: lifepathCareer.value ? [lifepathCareer.value] : [],
        terms: Number(lifepathTerms.value) || 1,
        skillTables: lifepathTables.value ? [lifepathTables.value] : [],
        benefits: lifepathBenefits.value ? [lifepathBenefits.value] : [],
      } : {}),
    }), { class: 'gb-btn-primary gb-roll-btn', 'data-focus-key': 'party:new:roll' });
    if (partyFull) {
      roll.disabled = true;
      roll.title = 'Campaign maximum reached';
    }

    const randomNameButton = button('🎲', () => {
      const next = pickNextName(party.map((member) => member.name));
      name.value = next;
      lastAutoName = next;
      name.focus();
    }, { title: 'Suggest another name', 'aria-label': 'Suggest another name', class: 'gb-icon-btn' });

    // Visual portrait picker synchronized with the accessible select.
    const portraitStrip = portraits.length > 0
      ? element('div', { class: 'gb-portrait-strip', role: 'listbox', 'aria-label': 'Portrait gallery' },
        ...portraits.map((entry) => {
          const active = portrait.value === entry.id;
          const tile = button('', () => {
            portrait.value = entry.id;
            rerender();
          }, {
            class: `gb-portrait-tile${active ? ' gb-selected' : ''}`,
            title: titleCase(entry.name),
            'aria-label': `Choose portrait ${titleCase(entry.name)}`,
            'aria-pressed': String(active),
          });
          tile.append(...picture(entry.picture, entry.name, 48));
          return tile;
        }),
        (() => {
          const noneActive = portrait.value === '';
          return button('None', () => {
            portrait.value = '';
            rerender();
          }, {
            class: `gb-portrait-tile gb-portrait-none${noneActive ? ' gb-selected' : ''}`,
            'aria-pressed': String(noneActive),
          });
        })())
      : null;

    // Rule hints under Race & Class selectors.
    const raceHints = [];
    if (chosenRace?.abilityAdjustments) {
      for (const [stat, delta] of Object.entries(chosenRace.abilityAdjustments)) {
        if (delta !== 0) {
          raceHints.push(element('span', { class: `gb-pill ${delta > 0 ? 'gb-pill-good' : 'gb-pill-bad'}` }, `${formatSigned(delta)} ${stat.toUpperCase()}`));
        }
      }
    }

    if (allowedClasses && classes.length > 0) {
      const names = classes.filter((entry) => allowedClasses.has(entry.id)).map((entry) => entry.name);
      if (names.length < classes.length) {
        raceHints.push(element('span', { class: 'gb-pill' }, `Classes: ${names.join(', ')}`));
      }
    }

    const classHints = [];
    if (chosenClass?.primeRequisites?.length) {
      classHints.push(element('span', { class: 'gb-pill gb-pill-accent' }, `Prime: ${chosenClass.primeRequisites.map((stat) => stat.toUpperCase()).join(', ')}`));
    }

    if (chosenClass?.requirements) {
      const reqs = Object.entries(chosenClass.requirements)
        .filter(([, min]) => Number(min) > 3)
        .map(([stat, min]) => `${stat.toUpperCase()} ${min}+`);
      if (reqs.length > 0) {
        classHints.push(element('span', { class: 'gb-pill' }, `Requires: ${reqs.join(', ')}`));
      }
    }

    const creationBadge = describeCreationMethod(selectedCreation, activeLifepath);
    const stable = view.stable ?? [];
    const importPolicy = view.importPolicy ?? { allowHigherLevel: false, maxLevel: null };
    const policyLabel = importPolicy.allowHigherLevel
      ? (importPolicy.maxLevel ? `Imports up to Lv.${importPolicy.maxLevel}` : 'Allows veteran imports')
      : 'Imports start at Lv.1';

    const tabButton = (id, label) => button(label, () => {
      workbenchTab = id;
      rerender();
    }, {
      class: `gb-tab-btn${workbenchTab === id ? ' gb-selected' : ''}`,
      'aria-pressed': String(workbenchTab === id),
      'data-focus-key': `party:tab:${id}`,
    });

    const tabBar = element('div', { class: 'gb-tab-bar' },
      tabButton('forge', 'Forge new'),
      tabButton('stable', `Character stable (${stable.length})`),
      tabButton('import', 'Import JSON'));

    const forgeBody = [
      element('div', { class: 'gb-section-head' },
        element('h2', { class: 'gb-card-heading' }, 'Character forge'),
        ...(creationBadge ? [element('span', { class: 'gb-pill gb-pill-accent' }, creationBadge)] : [])),
      element('div', { class: 'gb-field-grid' },
        element('div', { class: 'gb-field' },
          element('span', { class: 'gb-field-label' }, 'Name'),
          element('div', { class: 'gb-inline-input' }, name, randomNameButton)),
        ...(creationChoices.length > 1
          ? [element('label', { class: 'gb-field' }, element('span', { class: 'gb-field-label' }, 'Creation method'), creation)]
          : []),
        ...(races.length > 0
          ? [element('div', { class: 'gb-field' },
            element('span', { class: 'gb-field-label' }, 'Race / Ancestry'),
            race,
            ...(raceHints.length ? [element('div', { class: 'gb-badge-row' }, ...raceHints)] : []))]
          : []),
        ...(classes.length > 0
          ? [element('div', { class: 'gb-field' },
            element('span', { class: 'gb-field-label' }, 'Class'),
            characterClass,
            ...(classHints.length ? [element('div', { class: 'gb-badge-row' }, ...classHints)] : []))]
          : [])),
      ...(portraits.length > 0 ? [element('div', { class: 'gb-field' },
        element('div', { class: 'gb-section-head gb-portrait-head' },
          element('span', { class: 'gb-field-label' }, 'Portrait'),
          portrait),
        portraitStrip)] : []),
      choices.rows,
      ...(activeLifepath ? [renderLifepathSection(activeLifepath)] : []),
      element('div', { class: 'gb-forge-foot' },
        ...(choices.warning ? [element('div', { class: 'gb-inline-warn' }, choices.warning)] : []),
        roll),
    ];

    const stableBody = [
      element('div', { class: 'gb-section-head' },
        element('h2', { class: 'gb-card-heading' }, `Ready characters · ${view.rulesetTitle ?? view.rulesetId ?? 'Ruleset'}`),
        element('span', { class: `gb-pill ${importPolicy.allowHigherLevel ? 'gb-pill-good' : 'gb-pill-accent'}` }, policyLabel)),
      element('div', { class: 'gb-hint-bar' }, importPolicy.allowHigherLevel
        ? 'Characters import with their levels, items, and spells on a best-effort basis; any rule or item differences are logged.'
        : 'This campaign starts imported heroes at Level 1. Higher-level heroes scale down cleanly with a full import log.'),
      ...(stable.length > 0
        ? [element('div', { class: 'gb-stable-list' }, ...stable.map((entry) => {
          const importBtn = button('Import', () => send({ action: 'stable-import', id: entry.id }), {
            class: 'gb-btn-primary',
            'data-focus-key': `party:stable:import:${entry.id}`,
          });
          if (partyFull) {
            importBtn.disabled = true;
            importBtn.title = 'Campaign maximum reached';
          }
          return element('div', { class: 'gb-stable-row' },
            element('div', { class: 'gb-stable-portrait' },
              ...(entry.portraitPicture ? picture(entry.portraitPicture, `${entry.name}'s portrait`, 40) : [element('span', { class: 'gb-portrait-fallback' }, entry.name.slice(0, 1).toUpperCase())])),
            element('div', { class: 'gb-stable-info' },
              element('div', {},
                element('strong', {}, entry.name),
                element('span', { class: 'gb-muted' }, ` · ${[entry.race, entry.classText].filter(Boolean).join(' ')}`)),
              ...(entry.originCampaign ? [element('div', { class: 'gb-muted gb-stable-origin' }, `Last in ${entry.originCampaign}`)] : [])),
            element('div', { class: 'gb-badge-row' },
              importBtn,
              button('Copy JSON', () => navigator.clipboard?.writeText(entry.json ?? ''), { class: 'gb-btn-xs' }),
              button('Remove', () => send({ action: 'stable-remove', id: entry.id }), { class: 'gb-btn-xs' })));
        }))]
        : [element('p', { class: 'gb-muted' }, `No saved characters for ${view.rulesetTitle ?? 'this ruleset'} yet. Roll a character in Forge new — it is automatically added to your stable for reuse across modules.`)]),
    ];

    const importJsonButton = button('Import character JSON', () => {
      send({ action: 'stable-import-json', json: importJson.value });
      importJson.value = '';
    }, { class: 'gb-btn-primary gb-roll-btn', 'data-focus-key': 'party:import:submit' });
    if (partyFull) {
      importJsonButton.disabled = true;
      importJsonButton.title = 'Campaign maximum reached';
    }

    const importBody = [
      element('div', { class: 'gb-section-head' },
        element('h2', { class: 'gb-card-heading' }, 'Import character file (JSON)'),
        element('span', { class: `gb-pill ${importPolicy.allowHigherLevel ? 'gb-pill-good' : 'gb-pill-accent'}` }, policyLabel)),
      element('div', { class: 'gb-hint-bar' }, 'Paste JSON from `tales character new` or another campaign using the same baseline ruleset. Missing items or tweaked rules are mapped best-effort and logged.'),
      element('div', { class: 'gb-field' },
        element('span', { class: 'gb-field-label' }, 'Character JSON'),
        importJson),
      importJsonButton,
    ];

    const forgeCard = element('section', { class: 'gb-card-box gb-forge' },
      tabBar,
      ...(workbenchTab === 'stable' ? stableBody : workbenchTab === 'import' ? importBody : forgeBody));

    // Party roster column (right column on desktop).
    const slotPips = element('div', { class: 'gb-slot-pips', 'aria-hidden': 'true' },
      ...Array.from({ length: size.max }, (_, idx) => element('span', {
        class: `gb-slot-pip${idx < party.length ? ' gb-filled' : idx < size.min ? ' gb-required' : ''}`,
      })));

    const memberCards = party.map((member, index) => renderMemberCard(view, member, index));
    const rosterSection = element('section', { class: 'gb-roster-col' },
      element('div', { class: 'gb-card-box gb-roster-summary' },
        element('div', { class: 'gb-section-head' },
          element('h2', { class: 'gb-card-heading' }, `Party roster (${party.length} / ${size.max})`),
          element('div', { class: 'gb-badge-row' },
            slotPips,
            ...(party.length > 0 ? [button('Export party', () => send({ action: 'stable-export-party' }), { class: 'gb-btn-xs', title: 'Save all party members to your character stable' })] : []))),
        element('div', { class: 'gb-muted' }, readyToBegin
          ? `Party ready (${size.min}–${size.max} allowed). Equip gear, prepare spells, or begin the adventure.`
          : `Roll at least ${size.min - party.length} more character${size.min - party.length === 1 ? '' : 's'} to begin.`)),
      ...(memberCards.length > 0
        ? memberCards
        : [element('div', { class: 'gb-card-box gb-empty-roster' },
          element('p', { class: 'gb-muted' }, 'No characters rolled yet. Choose your options on the left and click Roll character.'))]));

    const beginButton = button('Begin adventure', () => send({ action: 'begin' }), {
      class: 'gb-btn-primary',
      'data-focus-key': 'party:begin',
    });
    if (!readyToBegin) {
      beginButton.disabled = true;
      beginButton.title = `Requires ${size.min} to ${size.max} party members`;
    }

    const header = element('header', { class: 'gb-sheet-header' },
      element('div', { class: 'gb-sheet-title' },
        element('h1', {}, view.campaignName ?? view.status ?? 'Assemble Party'),
        element('div', { class: 'gb-badge-row' },
          element('span', { class: `gb-pill ${readyToBegin ? 'gb-pill-good' : 'gb-pill-accent'}` },
            readyToBegin ? `${party.length}/${size.max} Ready` : `${party.length}/${size.max} · Need ${size.min}+`),
          ...(view.extensions?.length ? [element('span', { class: 'gb-pill' }, `Extensions: ${view.extensions.join(', ')}`)] : []))),
      element('div', { class: 'gb-header-actions' },
        button('Back', () => send({ action: 'quit' })),
        beginButton));

    // At a hosted table: who is here and which characters each has made.
    const seats = (view.table?.seats ?? []).map((seat) => element('li', {},
      `${seat.name}${seat.member === view.you ? ' (you)' : ''}${seat.member === view.table.leader ? ' · leads' : ''}${seat.connected ? '' : ' · away'}: `,
      seat.characters.length ? seat.characters.map((index) => party[index]?.name ?? '?').join(', ') : element('span', { class: 'gb-muted' }, 'no character yet')));
    const tableBanner = seats.length
      ? [element('section', { class: 'gb-card-box' },
        element('h2', { class: 'gb-card-heading' }, 'At the table'),
        element('ul', { class: 'gb-compact-list' }, ...seats),
        ...invitation(view.hosting))]
      : [];

    return fragment(
      header,
      ...tableBanner,
      element('div', { class: 'gb-party-grid' }, forgeCard, rosterSection));
  };

  const renderMemberCard = (view, member, index) => {
    const allItems = view.items ?? [];
    const allowedSet = Array.isArray(member.equippableItems) ? new Set(member.equippableItems) : null;
    const items = (allowedSet ? allItems.filter((entry) => allowedSet.has(entry.id)) : allItems)
      .slice()
      .sort((a, b) => {
        const rank = (kind) => kind === 'weapon' ? 0 : kind === 'armour' || kind === 'armor' || kind === 'shield' ? 1 : 2;
        return rank(a.kind) - rank(b.kind) || a.name.localeCompare(b.name);
      });
    const item = selectBox({
      'aria-label': `Item for ${member.name}`,
      'data-focus-key': `party:${index}:item`,
    });
    item.replaceChildren(...items.map((entry) => element('option', { value: entry.id },
      entry.kind ? `${entry.name} (${entry.kind})` : entry.name)));

    const subtitle = [member.race, member.class && `${member.class} Lv.${member.level}`].filter(Boolean).join(' · ');
    const statChips = (member.attributes ?? []).map((entry) => {
      const parts = entry.split(/\s+/);
      const label = parts[0]?.toUpperCase() ?? entry;
      const val = parts.slice(1).join(' ');
      return element('span', { class: 'gb-stat-chip' },
        element('small', {}, label),
        element('strong', {}, val));
    });
    const equippedTags = (member.equipment ?? []).map((eq) =>
      button(`${eq.name} ×`, () => send({ action: 'equip', member: index, item: eq.id }), {
        class: 'gb-tag-btn',
        title: `Unequip ${eq.name}`,
      }));

    return element('article', { class: 'gb-card-box gb-member-card' },
      element('div', { class: 'gb-member-head' },
        element('div', { class: 'gb-member-portrait' },
          ...(member.portraitPicture ? picture(member.portraitPicture, `${member.name}'s portrait`, 56) : [element('span', { class: 'gb-portrait-fallback' }, member.name.slice(0, 1).toUpperCase())])),
        element('div', { class: 'gb-member-ident' },
          element('div', { class: 'gb-member-name-row' },
            element('strong', { class: 'gb-member-name' }, member.name),
            element('span', { class: 'gb-muted' }, subtitle)),
          element('div', { class: 'gb-badge-row' },
            ...(member.tracks ?? []).map((track) => element('span', { class: 'gb-pill gb-pill-good' }, trackText(track))),
            element('span', { class: 'gb-pill' }, formatBalances(member.balances)))),
        element('div', { class: 'gb-badge-row' },
          button('Save to stable', () => send({ action: 'stable-save', member: index }), { class: 'gb-btn-subtle', 'data-focus-key': `party:${index}:stable` }),
          button('Drop', () => send({ action: 'drop', member: index }), { class: 'gb-btn-subtle', 'data-focus-key': `party:${index}:drop` }))),
      element('div', { class: 'gb-stat-strip' }, ...statChips),
      ...(member.features?.length ? [element('div', { class: 'gb-badge-row' },
        element('span', { class: 'gb-field-label' }, 'Features:'),
        ...member.features.map((feat) => element('span', { class: 'gb-pill' }, feat)))] : []),
      ...(member.careerTerms?.length ? [element('div', { class: 'gb-subbox' },
        element('div', { class: 'gb-field-label' }, `Prior history · Age ${member.age}`),
        ...member.careerTerms.map((term) => element('div', { class: 'gb-muted' },
          `Term ${term.number} ${term.career} (${term.results.join(', ') || 'no result'}${term.benefitsLost ? '; benefits lost' : ''})`)))] : []),
      ...renderSkillPoints(member, index),
      ...(items.length > 0 ? [element('div', { class: 'gb-member-gear' },
        element('div', { class: 'gb-badge-row' },
          element('span', { class: 'gb-field-label' }, 'Gear:'),
          ...(equippedTags.length ? equippedTags : [element('span', { class: 'gb-muted' }, 'none')])),
        row(item,
          button('Give/take', () => send({ action: 'equip', member: index, item: item.value }), { 'data-focus-key': `party:${index}:equip` })))] : []),
      ...renderSpells(send, member, index),
      ...renderMemorised(send, member, index));
  };

  const renderLifepathSection = (lifepath) => {
    const stepTerms = (delta) => {
      const next = Math.max(1, Math.min(lifepath.maxTerms || 99, (Number(lifepathTerms.value) || 1) + delta));
      lifepathTerms.value = String(next);
      rerender();
    };
    return element('div', { class: 'gb-subbox' },
      element('div', { class: 'gb-section-head' },
        element('span', { class: 'gb-field-label' }, lifepath.name),
        element('span', { class: 'gb-muted' }, `Starts age ${lifepath.startAge} · up to ${lifepath.maxTerms} terms`)),
      element('div', { class: 'gb-field-grid' },
        element('label', { class: 'gb-field' }, element('span', { class: 'gb-field-label' }, 'Career'), lifepathCareer),
        element('div', { class: 'gb-field' },
          element('span', { class: 'gb-field-label' }, 'Terms'),
          element('div', { class: 'gb-stepper' },
            button('−', () => stepTerms(-1), { class: 'gb-step-btn', 'aria-label': 'Fewer terms' }),
            lifepathTerms,
            button('+', () => stepTerms(1), { class: 'gb-step-btn', 'aria-label': 'More terms' }))),
        element('label', { class: 'gb-field' }, element('span', { class: 'gb-field-label' }, 'Skill table'), lifepathTables),
        element('label', { class: 'gb-field' }, element('span', { class: 'gb-field-label' }, 'Benefit kind'), lifepathBenefits)));
  };

  /**
   * The choices a roll needs, in the order Core takes them: a feature for each
   * slot creation and the class's first level grant, then (for creation by
   * boosts) a boost for each choice the race, the creation features, the class
   * and the creation offer. Fixed boosts need no choice.
   */
  const renderChoices = (view, selectedCreation, chosenRace, chosenClass, activeLifepath) => {
    const creation = selectedCreation ?? view.creation;
    const features = view.features ?? [];
    const rows = element('div', { class: 'gb-forge-sections' });
    const featureSlots = [];
    const creationSlots = [];
    let warning = null;
    if (!creation) {
      return { rows, features: () => [], boosts: () => [], attributes: () => null, priority: () => null, warning };
    }

    const primeSet = new Set(chosenClass?.primeRequisites ?? []);
    const attributeDetails = (creation.attributeDetails?.length
      ? creation.attributeDetails
      : (creation.attributes ?? []).map((id) => ({ id, name: id })));
    const arrangeableDetails = Array.isArray(creation.arrangeableAttributes)
      ? creation.arrangeableAttributes
      : attributeDetails;
    const attributeIds = arrangeableDetails.map((attribute) => attribute.id);
    const adjustments = chosenRace?.abilityAdjustments ?? {};
    let attributeValues = null;
    let priorityValues = null;

    if (creation.method === 'point-buy') {
      const costRows = creation.costs?.rows ?? [];
      const baseScore = Number(creation.base ?? 8);
      const budget = Number(creation.budget ?? 0);

      const getScore = (attribute) => {
        const key = `${creation.id}:score:${attribute.id}`;
        const raw = scoreDrafts.get(key);
        const val = raw !== undefined && raw !== '' ? Number(raw) : (creation.base ?? attribute.min ?? 8);
        return Number.isNaN(val) ? baseScore : val;
      };

      const setScore = (attribute, next) => {
        const { min, max } = pointBuyRange(costRows, attribute);
        let clamped = next;
        if (min !== undefined) {
          clamped = Math.max(min, clamped);
        }

        if (max !== undefined) {
          clamped = Math.min(max, clamped);
        }

        scoreDrafts.set(`${creation.id}:score:${attribute.id}`, String(clamped));
        rerender();
      };

      let spent = 0;
      let anyUnknown = false;
      for (const attribute of attributeDetails) {
        const cost = lookupPointCost(costRows, getScore(attribute));
        if (cost === null) {
          anyUnknown = true;
        } else {
          spent += cost;
        }
      }

      const remaining = budget - spent;
      if (remaining < 0) {
        warning = `Over point-buy budget by ${Math.abs(remaining)} point${Math.abs(remaining) === 1 ? '' : 's'}.`;
      }

      const resetScores = () => {
        for (const attribute of attributeDetails) {
          scoreDrafts.set(`${creation.id}:score:${attribute.id}`, String(baseScore));
        }
        rerender();
      };

      const autoDistribute = () => {
        const ordered = [...attributeDetails].sort((a, b) => Number(primeSet.has(b.id)) - Number(primeSet.has(a.id)));
        const draft = new Map(ordered.map((attr) => [attr.id, baseScore]));
        let budgetLeft = budget - ordered.reduce((sum, attr) => sum + (lookupPointCost(costRows, baseScore) ?? 0), 0);
        let progressed = true;
        while (progressed && budgetLeft > 0) {
          progressed = false;
          for (const attr of ordered) {
            const current = draft.get(attr.id);
            const { max } = pointBuyRange(costRows, attr);
            if (max !== undefined && current >= max) {
              continue;
            }

            const curCost = lookupPointCost(costRows, current);
            const nextCost = lookupPointCost(costRows, current + 1);
            if (curCost !== null && nextCost !== null && nextCost - curCost <= budgetLeft) {
              draft.set(attr.id, current + 1);
              budgetLeft -= (nextCost - curCost);
              progressed = true;
            }
          }
        }

        for (const [id, value] of draft) {
          scoreDrafts.set(`${creation.id}:score:${id}`, String(value));
        }
        rerender();
      };

      const fields = [];
      const statList = element('div', { class: 'gb-stat-rows' }, ...attributeDetails.map((attribute) => {
        const key = `${creation.id}:score:${attribute.id}`;
        const score = getScore(attribute);
        const { min, max } = pointBuyRange(costRows, attribute);
        const cost = lookupPointCost(costRows, score);
        const nextCost = lookupPointCost(costRows, score + 1);
        const stepDelta = cost !== null && nextCost !== null ? nextCost - cost : null;

        const inputAttributes = {
          type: 'number',
          step: 'any',
          value: String(score),
          size: '4',
          'aria-label': `${attribute.name} score`,
          'data-focus-key': `party:new:score:${attribute.id}`,
        };
        if (min !== undefined) {
          inputAttributes.min = String(min);
        }

        if (max !== undefined) {
          inputAttributes.max = String(max);
        }

        const input = element('input', inputAttributes);
        input.addEventListener('change', () => {
          scoreDrafts.set(key, input.value);
          rerender();
        });
        input.addEventListener('input', () => scoreDrafts.set(key, input.value));
        fields.push({ id: attribute.id, input });

        const dec = button('−', () => setScore(attribute, score - 1), {
          class: 'gb-step-btn',
          'aria-label': `Decrease ${attribute.name}`,
        });
        if (min !== undefined && score <= min) {
          dec.disabled = true;
        }

        const inc = button('+', () => setScore(attribute, score + 1), {
          class: 'gb-step-btn',
          'aria-label': `Increase ${attribute.name}`,
        });
        if ((max !== undefined && score >= max) || (stepDelta !== null && stepDelta > remaining)) {
          inc.disabled = true;
        }

        const adj = adjustments[attribute.id];
        return element('div', { class: 'gb-stat-row' },
          element('div', { class: 'gb-stat-row-label' },
            element('strong', {}, attribute.name),
            element('span', { class: 'gb-muted' }, ` ${attribute.id.toUpperCase()}`),
            ...(primeSet.has(attribute.id) ? [element('span', { class: 'gb-pill gb-pill-accent' }, 'Prime')] : []),
            ...(adj ? [element('span', { class: `gb-pill ${adj > 0 ? 'gb-pill-good' : 'gb-pill-bad'}` }, formatSigned(adj))] : [])),
          element('div', { class: 'gb-stepper' }, dec, input, inc),
          element('span', { class: 'gb-muted gb-cost-note' }, cost !== null ? `${cost} pt${cost === 1 ? '' : 's'}${stepDelta !== null ? ` (+${stepDelta})` : ''}` : 'out of range'));
      }));

      const pbRaw = Object.fromEntries(attributeDetails.map((attr) => [attr.id, getScore(attr)]));
      const pbPreview = buildStatPreview(attributeDetails, pbRaw, chosenRace, chosenClass, primeSet);
      if (pbPreview.warning && !warning) {
        warning = pbPreview.warning;
      }

      const budgetTone = remaining < 0 ? 'gb-pill-bad' : remaining === 0 ? 'gb-pill-good' : 'gb-pill-accent';
      rows.append(element('div', { class: 'gb-subbox' },
        element('div', { class: 'gb-section-head' },
          element('span', { class: 'gb-field-label' }, 'Attribute point buy'),
          element('div', { class: 'gb-badge-row' },
            element('span', { class: `gb-pill ${budgetTone}` }, anyUnknown ? `Budget ${budget}` : `${remaining} left (${spent}/${budget})`),
            button('Auto-fill', autoDistribute, { class: 'gb-btn-xs' }),
            button('Reset', resetScores, { class: 'gb-btn-xs' }))),
        pbPreview.grid,
        statList));
      attributeValues = () => Object.fromEntries(fields.map((field) => [field.id, Number(field.input.value)]));
    } else if (creation.method === 'array' || creation.assignment === 'arrange') {
      const array = creation.array ?? [];
      const fixedDetails = attributeDetails.filter((attribute) => !attributeIds.includes(attribute.id));

      // Initialize unique priority order cleanly on first visit.
      const currentOrder = arrangeableDetails.map((_, index) => {
        const saved = priorityDrafts.get(`${creation.id}:priority:${index}`);
        return attributeIds.includes(saved) ? saved : null;
      });
      const used = new Set(currentOrder.filter(Boolean));
      const missing = attributeIds.filter((id) => !used.has(id));
      let fillCursor = 0;
      const order = currentOrder.map((entry) => entry ?? missing[fillCursor++]);
      order.forEach((id, index) => priorityDrafts.set(`${creation.id}:priority:${index}`, id));

      const applyOrder = (nextOrder) => {
        nextOrder.forEach((id, index) => priorityDrafts.set(`${creation.id}:priority:${index}`, id));
        rerender();
      };

      const orderForClass = () => {
        const ordered = [...attributeIds].sort((a, b) => Number(primeSet.has(b)) - Number(primeSet.has(a)));
        applyOrder(ordered);
      };

      const swapSlots = (first, second) => {
        if (first < 0 || second < 0 || first >= order.length || second >= order.length) {
          return;
        }

        const next = [...order];
        [next[first], next[second]] = [next[second], next[first]];
        applyOrder(next);
      };

      const selects = [];
      const draftRaw = (creation.method === 'roll' || !creation.method)
        && view.draftScores?.scores
        && (!view.draftScores.creationId || view.draftScores.creationId === creation.id)
        ? view.draftScores.scores
        : null;
      const arrangedPool = draftRaw
        ? arrangeableDetails
          .map((attr) => Number(draftRaw[attr.id]))
          .filter((v) => !Number.isNaN(v))
          .sort((a, b) => b - a)
        : null;

      const priorityRows = element('div', { class: 'gb-stat-rows' }, ...arrangeableDetails.map((_, index) => {
        const selected = order[index];
        const select = selectBox({
          'aria-label': `${creation.method === 'array' ? `Score ${array[index] ?? index + 1}` : `Rolled score ${index + 1}`} priority`,
          'data-focus-key': `party:new:priority:${index}`,
        });
        fill(select, arrangeableDetails.map((detail) => ({
          id: detail.id,
          name: primeSet.has(detail.id) ? `${detail.name} ★` : detail.name,
        })));
        select.value = selected;
        // Auto-swap with the slot that previously held the newly picked attribute to prevent duplicates.
        select.addEventListener('change', () => {
          const picked = select.value;
          const next = [...order];
          const other = next.indexOf(picked);
          if (other >= 0 && other !== index) {
            next[other] = next[index];
          }
          next[index] = picked;
          applyOrder(next);
        });
        selects.push(select);

        const up = button('↑', () => swapSlots(index, index - 1), { class: 'gb-step-btn', 'aria-label': 'Move higher' });
        if (index === 0) {
          up.disabled = true;
        }
        const down = button('↓', () => swapSlots(index, index + 1), { class: 'gb-step-btn', 'aria-label': 'Move lower' });
        if (index === arrangeableDetails.length - 1) {
          down.disabled = true;
        }

        const badgeText = creation.method === 'array'
          ? String(array[index] ?? index + 1)
          : arrangedPool?.[index] !== undefined
            ? String(arrangedPool[index])
            : `#${index + 1} ${index === 0 ? 'Highest' : index === arrangeableDetails.length - 1 ? 'Lowest' : 'Roll'}`;
        return element('div', { class: 'gb-stat-row gb-priority-row' },
          element('span', { class: 'gb-pill gb-pill-accent gb-priority-badge' }, badgeText),
          select,
          element('div', { class: 'gb-stepper' }, up, down));
      }));

      // Live final score card preview for Standard Array or Arranged Roll pool.
      const mappedRaw = {};
      if (creation.method === 'array' && array.length > 0) {
        order.forEach((id, idx) => { mappedRaw[id] = Number(array[idx] ?? 10); });
      } else if (arrangedPool) {
        order.forEach((id, idx) => { mappedRaw[id] = arrangedPool[idx]; });
        for (const fixed of fixedDetails) {
          if (draftRaw && draftRaw[fixed.id] !== undefined) {
            mappedRaw[fixed.id] = Number(draftRaw[fixed.id]);
          }
        }
      }
      const statPreview = Object.keys(mappedRaw).length > 0
        ? buildStatPreview(attributeDetails, mappedRaw, chosenRace, chosenClass, primeSet)
        : null;
      if (statPreview?.warning && !warning) {
        warning = statPreview.warning;
      }

      rows.append(element('div', { class: 'gb-subbox' },
        element('div', { class: 'gb-section-head' },
          element('span', { class: 'gb-field-label' }, creation.method === 'array' ? 'Standard array assignment' : 'Rolled pool · assign scores'),
          element('div', { class: 'gb-badge-row' },
            ...(arrangedPool ? [button('🎲 Re-roll pool', () => send({ action: 'roll-scores', creation: creation.id }), { class: 'gb-btn-xs', 'data-focus-key': 'party:new:reroll' })] : []),
            ...(primeSet.size > 0 ? [button('Prioritize for class', orderForClass, { class: 'gb-btn-xs' })] : []))),
        element('div', { class: 'gb-muted' }, creation.method === 'array'
          ? 'Picking an attribute swaps automatically so every score stays unique.'
          : arrangedPool
            ? `Rolled ${creation.attributeRoll || 'scores'} — arrange them across attributes below.`
            : `Highest rolls go to the top slots${creation.attributeRoll ? ` (${creation.attributeRoll})` : ''}.`),
        ...(fixedDetails.length > 0 ? [element('div', { class: 'gb-muted' }, `Fixed authored rolls: ${fixedDetails.map((attribute) => `${attribute.name}${draftRaw?.[attribute.id] !== undefined ? ` (${draftRaw[attribute.id]})` : ''}`).join(', ')}.`)] : []),
        ...(statPreview ? [statPreview.grid] : []),
        priorityRows));
      if (arrangedPool && Object.keys(mappedRaw).length === attributeDetails.length) {
        attributeValues = () => ({ ...mappedRaw });
      } else {
        priorityValues = selects.length > 0 ? () => selects.map((select) => select.value) : () => null;
      }
    } else if (creation.method === 'roll' || !creation.method) {
      const draftRaw = view.draftScores?.scores
        && (!view.draftScores.creationId || view.draftScores.creationId === creation.id)
        ? view.draftScores.scores
        : null;
      if (draftRaw) {
        const preview = buildStatPreview(attributeDetails, draftRaw, chosenRace, chosenClass, primeSet);
        if (preview.warning && !warning) {
          warning = preview.warning;
        }
        rows.append(element('div', { class: 'gb-subbox' },
          element('div', { class: 'gb-section-head' },
            element('span', { class: 'gb-field-label' }, `Rolled stats${creation.attributeRoll ? ` (${creation.attributeRoll})` : ''}`),
            element('div', { class: 'gb-badge-row' },
              element('span', { class: `gb-pill ${preview.ok ? 'gb-pill-good' : 'gb-pill-bad'}` },
                preview.ok ? `✓ Qualifies for ${chosenClass?.name ?? 'class'}` : `✗ Does not qualify for ${chosenClass?.name ?? 'class'}`),
              button('🎲 Re-roll stats', () => send({ action: 'roll-scores', creation: creation.id }), {
                class: 'gb-btn-xs gb-btn-primary',
                'data-focus-key': 'party:new:reroll',
              }))),
          preview.grid));
        attributeValues = () => Object.fromEntries(attributeDetails.map((attr) => [attr.id, Number(draftRaw[attr.id])]));
      } else {
        const rollNote = creation.attributeRoll
          ? `Rolls ${creation.attributeRoll} in order across ${attributeDetails.map((attr) => attr.id.toUpperCase()).join(', ')}.`
          : `Rolls attributes across ${attributeDetails.map((attr) => attr.id.toUpperCase()).join(', ')}.`;
        rows.append(element('div', { class: 'gb-hint-bar' },
          rollNote,
          ' ',
          button('🎲 Roll stats now', () => send({ action: 'roll-scores', creation: creation.id }), { class: 'gb-btn-xs' })));
      }
    }

    if (activeLifepath) {
      fill(lifepathCareer, activeLifepath.careers ?? []);
      if (!lifepathCareer.value && activeLifepath.careers?.length) {
        lifepathCareer.value = activeLifepath.careers[0].id;
      }
      const selected = (activeLifepath.careers ?? []).find((career) => career.id === lifepathCareer.value);
      const tableChoices = (selected?.skillTables ?? []).map((table) => typeof table === 'string' ? { id: table, name: table } : table);
      fill(lifepathTables, tableChoices);
      const benefitChoices = (selected?.benefitKinds ?? activeLifepath.benefitKinds ?? [
        { id: 'cash', name: 'Cash' },
        { id: 'material', name: 'Material' },
      ]).map((benefit) => typeof benefit === 'string' ? { id: benefit, name: benefit } : benefit);
      fill(lifepathBenefits, benefitChoices);
    }

    // Feature grants (heritage, background, feats, class choices).
    const grants = [...(creation.grants ?? []).map((grant) => ({ grant, creation: true })), ...(chosenClass?.grants ?? []).map((grant) => ({ grant, creation: false }))];
    if (grants.length > 0) {
      const featureFields = [];
      grants.forEach(({ grant, creation: fromCreation }, index) => {
        for (let made = 0; made < grant.count; made++) {
          const kinds = grant.kinds.join(' or ');
          const select = choiceSelect(`feature-${index}-${made}-${grant.kinds.join('|')}`, `Choose ${kinds}`);
          const offered = features.filter((feature) => grant.kinds.includes(feature.kind));
          fill(select, offered);
          featureSlots.push(select);
          if (fromCreation) {
            creationSlots.push(select);
          }

          const picked = offered.find((feature) => feature.id === select.value);
          featureFields.push(element('div', { class: 'gb-field' },
            element('span', { class: 'gb-field-label' }, titleCase(kinds)),
            select,
            ...(picked?.description ? [element('span', { class: 'gb-muted' }, picked.description)] : []),
            ...(picked?.requirements ? [element('span', { class: 'gb-pill' }, `Requires: ${picked.requirements}`)] : [])));
        }
      });

      rows.append(element('div', { class: 'gb-subbox' },
        element('div', { class: 'gb-field-label' }, 'Choices & features'),
        element('div', { class: 'gb-field-grid' }, ...featureFields)));
    }

    // Boosts method (PF2e-style): live computed preview + non-duplicate smart defaults + duplicate conflict detection.
    const boostSlots = [];
    if (creation.method === 'boosts') {
      const baseValue = Number(creation.base ?? 10);
      const boostStep = Number(creation.boost ?? 2);
      const totals = Object.fromEntries(attributeDetails.map((attr) => [attr.id, baseValue]));
      const sources = [
        ['race', chosenRace],
        ...creationSlots.map((select, index) => [`feature-${index}`, features.find((feature) => feature.id === select.value)]),
        ['class', chosenClass],
        ['creation', { id: 'free', name: 'Free', boosts: creation.boosts ?? [] }],
      ];

      const boostControls = [];
      let duplicateFound = false;

      for (const [key, source] of sources) {
        if (!source?.boosts?.length) {
          continue;
        }

        const sourceTaken = new Set();
        const fixedLabels = [];
        for (const options of source.boosts) {
          if (options.length === 1) {
            sourceTaken.add(options[0]);
            totals[options[0]] = (totals[options[0]] ?? baseValue) + boostStep;
            fixedLabels.push(`+${boostStep} ${options[0].toUpperCase()}`);
          }
        }

        const sourcePickers = [];
        source.boosts.forEach((options, index) => {
          if (options.length === 1) {
            return;
          }

          const offered = options.length === 0 ? (creation.attributes ?? []) : options;
          const slotKey = `boost-${key}-${source.id ?? key}-${index}`;
          const isNew = !choiceSelects.has(slotKey);
          const select = choiceSelect(slotKey, `${source.name} boost ${index + 1}`);
          fill(select, offered.map((attribute) => ({ id: attribute, name: attribute.toUpperCase() })));

          // On first render of a boost slot, auto-pick an unused attribute in this source (preferring class primes).
          if (isNew || !offered.includes(select.value)) {
            const preferred = offered.find((attr) => primeSet.has(attr) && !sourceTaken.has(attr))
              ?? offered.find((attr) => !sourceTaken.has(attr))
              ?? offered[0];
            if (preferred) {
              select.value = preferred;
            }
          }

          const conflict = sourceTaken.has(select.value);
          if (conflict) {
            duplicateFound = true;
          } else if (select.value) {
            sourceTaken.add(select.value);
            totals[select.value] = (totals[select.value] ?? baseValue) + boostStep;
          }

          boostSlots.push(select);
          sourcePickers.push(element('label', { class: `gb-boost-slot${conflict ? ' gb-conflict' : ''}` },
            element('span', { class: 'gb-muted' }, `#${index + 1}`),
            select));
        });

        if (fixedLabels.length > 0 || sourcePickers.length > 0) {
          boostControls.push(element('div', { class: 'gb-boost-group' },
            element('div', { class: 'gb-boost-head' },
              element('strong', {}, source.name),
              ...fixedLabels.map((label) => element('span', { class: 'gb-pill gb-pill-good' }, label))),
            ...(sourcePickers.length > 0 ? [element('div', { class: 'gb-boost-row' }, ...sourcePickers)] : [])));
        }
      }

      if (duplicateFound) {
        warning = 'A boost source cannot boost the same attribute twice — change the highlighted slot.';
      }

      const previewStrip = element('div', { class: 'gb-stat-strip' },
        ...attributeDetails.map((attr) => element('span', {
          class: `gb-stat-chip${primeSet.has(attr.id) ? ' gb-stat-prime' : ''}`,
        },
          element('small', {}, attr.id.toUpperCase()),
          element('strong', {}, String(totals[attr.id] ?? baseValue)))));

      rows.append(element('div', { class: 'gb-subbox' },
        element('div', { class: 'gb-section-head' },
          element('span', { class: 'gb-field-label' }, 'Attribute boosts preview'),
          element('span', { class: 'gb-muted' }, `Base ${baseValue}, +${boostStep} per boost`)),
        previewStrip,
        ...boostControls));
    }

    return {
      rows,
      warning,
      features: () => featureSlots.map((select) => select.value).filter((value) => value),
      boosts: () => boostSlots.map((select) => select.value),
      attributes: () => attributeValues?.() ?? null,
      priority: () => priorityValues?.() ?? null,
    };
  };

  /**
   * Shows the actual post-roll staged skill values with live remaining Profession
   * and Personal budgets and stepper buttons. Core owns final allocation checks.
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
    let profSpent = 0;
    let persSpent = 0;

    const getVal = (key, fallback) => {
      const raw = skillDrafts.get(key);
      const parsed = raw !== undefined && raw !== '' ? Number(raw) : Number(fallback ?? 0);
      return Number.isNaN(parsed) ? 0 : Math.max(0, parsed);
    };

    for (const skill of skillPoints.skills) {
      const saved = committed.get(skill.id) ?? {};
      profSpent += skill.profession ? getVal(`${memberKey}:${skill.id}:profession`, saved.profession ?? 0) : 0;
      persSpent += getVal(`${memberKey}:${skill.id}:personal`, saved.personal ?? 0);
    }

    const profLeft = Number(skillPoints.profession ?? 0) - profSpent;
    const persLeft = Number(skillPoints.personal ?? 0) - persSpent;

    const rows = skillPoints.skills.map((skill) => {
      const saved = committed.get(skill.id) ?? {};
      const professionKey = `${memberKey}:${skill.id}:profession`;
      const personalKey = `${memberKey}:${skill.id}:personal`;
      const profVal = skill.profession ? getVal(professionKey, saved.profession ?? 0) : 0;
      const persVal = getVal(personalKey, saved.personal ?? 0);
      const profession = pointInput(professionKey, profVal, skill.profession && !hasCommitted);
      const personal = pointInput(personalKey, persVal, !hasCommitted);
      fields.push({ skill: skill.id, profession, personal });
      const previewTotal = Number(skill.base ?? 0) + profVal + persVal;

      const step = (key, cur, delta) => {
        skillDrafts.set(key, String(Math.max(0, cur + delta)));
        rerender();
      };

      const profControl = skill.profession
        ? element('div', { class: 'gb-stepper' },
          ...(!hasCommitted ? [button('−', () => step(professionKey, profVal, -5), { class: 'gb-step-btn', 'aria-label': `Decrease ${skill.id} profession` })] : []),
          profession,
          ...(!hasCommitted ? [button('+', () => step(professionKey, profVal, 5), { class: 'gb-step-btn', 'aria-label': `Increase ${skill.id} profession` })] : []))
        : element('span', { class: 'gb-muted' }, '—');

      const persControl = element('div', { class: 'gb-stepper' },
        ...(!hasCommitted ? [button('−', () => step(personalKey, persVal, -5), { class: 'gb-step-btn', 'aria-label': `Decrease ${skill.id} personal` })] : []),
        personal,
        ...(!hasCommitted ? [button('+', () => step(personalKey, persVal, 5), { class: 'gb-step-btn', 'aria-label': `Increase ${skill.id} personal` })] : []));

      return element('div', { class: 'gb-skill-row' },
        element('span', {},
          element('strong', {}, titleCase(skill.id)),
          element('span', { class: 'gb-muted' }, ` ${hasCommitted ? skill.current : `${skill.base} → ${previewTotal}`}`)),
        element('div', { class: 'gb-skill-cell' }, element('small', { class: 'gb-muted' }, 'Prof'), profControl),
        element('div', { class: 'gb-skill-cell' }, element('small', { class: 'gb-muted' }, 'Pers'), persControl));
    });

    return [element('section', { class: 'gb-subbox' },
      element('div', { class: 'gb-section-head' },
        element('span', { class: 'gb-field-label' }, `${member.name}'s skill points`),
        hasCommitted
          ? element('span', { class: 'gb-pill gb-pill-good' }, 'Committed')
          : element('div', { class: 'gb-badge-row' },
            element('span', { class: `gb-pill ${profLeft < 0 ? 'gb-pill-bad' : profLeft === 0 ? 'gb-pill-good' : 'gb-pill-accent'}` }, `Prof: ${profLeft} left`),
            element('span', { class: `gb-pill ${persLeft < 0 ? 'gb-pill-bad' : persLeft === 0 ? 'gb-pill-good' : 'gb-pill-accent'}` }, `Pers: ${persLeft} left`))),
      ...rows,
      ...(hasCommitted ? [] : [row(button(`Spend ${member.name}'s skill points`, () => send({
        action: 'skills',
        member: index,
        skills: fields.map((field) => ({
          skill: field.skill,
          profession: Number(field.profession.value),
          personal: Number(field.personal.value),
        })),
      }), { class: 'gb-btn-primary', 'data-focus-key': `party:${index}:skills` }))]))];

    function pointInput(key, initial, allowed) {
      const input = element('input', {
        type: 'number',
        min: '0',
        step: 'any',
        value: String(skillDrafts.get(key) ?? initial),
        size: '4',
        'data-focus-key': key,
      });
      input.disabled = !allowed;
      input.addEventListener('change', () => {
        skillDrafts.set(key, input.value);
        rerender();
      });
      input.addEventListener('input', () => skillDrafts.set(key, input.value));
      return input;
    }
  };

  return render;
}

function buildStatPreview(attributeDetails, rawScores, chosenRace, chosenClass, primeSet) {
  const adjustments = chosenRace?.abilityAdjustments ?? {};
  const limits = chosenRace?.abilityLimits ?? {};
  const reqs = chosenClass?.requirements ?? {};
  const reasons = [];
  const cards = [];

  for (const attr of attributeDetails) {
    const raw = Number(rawScores[attr.id]);
    const adj = Number(adjustments[attr.id] ?? 0);
    const finalVal = Number.isNaN(raw) ? 0 : raw + adj;
    const minReq = reqs[attr.id] !== undefined ? Number(reqs[attr.id]) : null;
    const lim = limits[attr.id];
    const minLim = lim?.min !== undefined ? Number(lim.min) : null;
    const maxLim = lim?.max !== undefined ? Number(lim.max) : null;

    let failText = null;
    if (minReq !== null && finalVal < minReq) {
      failText = `need ${minReq}+`;
      reasons.push(`${attr.id.toUpperCase()} ${finalVal} (needs ${minReq}+ for ${chosenClass?.name ?? 'class'})`);
    } else if (minLim !== null && finalVal < minLim) {
      failText = `min ${minLim}`;
      reasons.push(`${attr.id.toUpperCase()} ${finalVal} (below ${chosenRace?.name ?? 'race'} min ${minLim})`);
    } else if (maxLim !== null && finalVal > maxLim) {
      failText = `max ${maxLim}`;
      reasons.push(`${attr.id.toUpperCase()} ${finalVal} (above ${chosenRace?.name ?? 'race'} max ${maxLim})`);
    }

    const cls = failText
      ? 'gb-stat-card gb-stat-fail'
      : primeSet.has(attr.id)
        ? 'gb-stat-card gb-stat-prime'
        : minReq !== null && minReq > 3
          ? 'gb-stat-card gb-stat-ok'
          : 'gb-stat-card';

    const subText = failText
      ?? (adj !== 0 ? `${raw}${formatSigned(adj)}` : minReq !== null && minReq > 3 ? `✓ ${minReq}+` : primeSet.has(attr.id) ? 'Prime' : '\u00A0');

    cards.push(element('div', { class: cls, title: `${attr.name}: ${finalVal}${adj !== 0 ? ` (${raw} ${formatSigned(adj)} racial)` : ''}` },
      element('small', {}, attr.id.toUpperCase()),
      element('strong', {}, String(finalVal)),
      element('span', { class: 'gb-stat-sub' }, subText)));
  }

  return {
    ok: reasons.length === 0,
    warning: reasons.length > 0 ? `Stats do not meet requirements: ${reasons.join('; ')}. Re-roll stats or pick another class/race.` : null,
    grid: element('div', { class: 'gb-stat-card-grid', 'aria-label': 'Ability scores' }, ...cards),
  };
}

function describeCreationMethod(creation, lifepath) {
  if (!creation) {
    return null;
  }

  if (lifepath) {
    return `${creation.name} · Lifepath`;
  }

  if (creation.method === 'point-buy') {
    return `Point buy · ${creation.budget ?? '?'} pts`;
  }

  if (creation.method === 'array') {
    return `Array (${(creation.array ?? []).join('/')})`;
  }

  if (creation.method === 'boosts') {
    return `Boosts (+${creation.boost ?? 2})`;
  }

  if (creation.assignment === 'arrange') {
    return creation.attributeRoll ? `${creation.attributeRoll} arrange` : 'Arranged rolls';
  }

  return creation.attributeRoll ? `${creation.attributeRoll} in order` : creation.name;
}
