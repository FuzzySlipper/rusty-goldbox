import { element, fragment, row, button, fill, picture, formatBalances, trackText } from '../dom.js';
import { renderSpells, renderMemorised } from '../panels/members.js';

/**
 * The party screen: the members made so far, and what a roll needs from the
 * projection's creation, classes, features and lifepath. Core checks the roll;
 * its notes say what to fix. `rerender` redraws the screen after a local choice.
 */
export function createParty(send, rerender) {
  // Inputs live outside the re-rendered body so typing survives updates.
  const name = element('input', { value: 'Ada', size: '10', 'aria-label': 'Character name', 'data-focus-key': 'party:new:name' });
  const race = element('select', { 'aria-label': 'Race', 'data-focus-key': 'party:new:race' });
  const characterClass = element('select', { 'aria-label': 'Class', 'data-focus-key': 'party:new:class' });
  const creation = element('select', { 'aria-label': 'Character creation', 'data-focus-key': 'party:new:creation' });
  const portrait = element('select', { 'aria-label': 'Portrait', 'data-focus-key': 'party:new:portrait' });
  const lifepathCareer = element('select', { 'aria-label': 'Career', 'data-focus-key': 'party:new:career' });
  const lifepathTerms = element('input', { type: 'number', min: '1', value: '1', size: '3', 'aria-label': 'Career terms', 'data-focus-key': 'party:new:terms' });
  const lifepathTables = element('select', { 'aria-label': 'Skill table', 'data-focus-key': 'party:new:skill-table' });
  const lifepathBenefits = element('select', { 'aria-label': 'Benefit kind', 'data-focus-key': 'party:new:benefit' });
  // Feature and boost choices, kept by slot so a choice survives re-renders.
  const choiceSelects = new Map();
  // Skill amounts are a local draft until the player submits the Core action.
  const skillDrafts = new Map();
  // Creation scores and priorities are local intent drafts until the Roll action.
  const scoreDrafts = new Map();
  const priorityDrafts = new Map();
  const choiceSelect = (key, label) => {
    if (!choiceSelects.has(key)) {
      const select = element('select', { 'aria-label': label, 'data-focus-key': `party:choice:${key}` });
      select.addEventListener('change', () => rerender());
      choiceSelects.set(key, select);
    }
    return choiceSelects.get(key);
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
    const members = element('ol', {});
    (view.party ?? []).forEach((member, index) => {
      const item = element('select', {
        'aria-label': `Item for ${member.name}`,
        'data-focus-key': `party:${index}:item`,
      });
      fill(item, view.items ?? []);
      members.append(element('li', {},
        element('div', { class: 'gb-row' },
          ...picture(member.portraitPicture, `${member.name}'s portrait`, 40),
          element('span', {}, `${member.name}: ${[member.race, member.class && `${member.class} ${member.level}`].filter(Boolean).join(' ')}, ${(member.tracks ?? []).map(trackText).join(', ')}, ${formatBalances(member.balances)}`)),
        element('div', { class: 'gb-muted' }, member.attributes.join(' ')),
        ...(member.careerTerms?.length ? [element('div', { class: 'gb-muted' }, `Prior history age ${member.age}: `,
          ...member.careerTerms.map((term) => element('div', {}, `Term ${term.number} ${term.career} (${term.results.join(', ') || 'no result'}${term.benefitsLost ? '; benefits lost' : ''})`)))] : []),
        ...(member.features?.length ? [element('div', {}, `Features: ${member.features.join(', ')}`)] : []),
        ...renderSkillPoints(member, index),
        element('div', {}, `Equipment: ${member.equipment.map((equipment) => equipment.name).join(', ') || 'none'}`),
        ...renderSpells(send, member, index),
        ...renderMemorised(send, member, index),
        row(item,
          button('Give/take', () => send({ action: 'equip', member: index, item: item.value }), { 'data-focus-key': `party:${index}:equip` }),
          button('Drop', () => send({ action: 'drop', member: index }), { 'data-focus-key': `party:${index}:drop` }))));
    });
    const size = view.partySize ?? { min: 1, max: 1 };
    const choices = renderChoices(view, selectedCreation);
    const creationValues = () => {
      const attributes = choices.attributes();
      const priority = choices.priority();
      return {
        ...(attributes ? { attributes } : {}),
        ...(priority ? { priority } : {}),
      };
    };
    const roll = button('Roll', () => send({
      action: 'roll',
      name: name.value,
      ...(selectedCreation?.id ? { creation: selectedCreation.id } : {}),
      // A ruleset without races or classes has none to send.
      ...((view.races ?? []).length > 0 ? { race: race.value } : {}),
      ...((view.classes ?? []).length > 0 ? { class: characterClass.value } : {}),
      ...(portrait.value ? { portrait: portrait.value } : {}),
      ...(choices.features().length > 0 ? { features: choices.features() } : {}),
      ...(choices.boosts().length > 0 ? { boosts: choices.boosts() } : {}),
      ...creationValues(),
      ...(view.lifepath ? {
        lifepath: view.lifepath.id,
        careers: lifepathCareer.value ? [lifepathCareer.value] : [],
        terms: Number(lifepathTerms.value) || 1,
        skillTables: lifepathTables.value ? [lifepathTables.value] : [],
        benefits: lifepathBenefits.value ? [lifepathBenefits.value] : [],
      } : {}),
    }), { 'data-focus-key': 'party:new:roll' });
    if ((view.party ?? []).length >= size.max) {
      roll.disabled = true;
      roll.title = 'Campaign maximum reached';
    }
    const chosen = (view.portraits ?? []).find((entry) => entry.id === portrait.value);
    const creationControl = creationChoices.length > 1
      ? [element('span', {}, 'Creation:'), creation]
      : selectedCreation ? [element('span', {}, `Creation: ${selectedCreation.name}`)] : [];
    return fragment(
      element('h1', {}, view.status ?? 'Party'),
      element('h2', {}, `Party (${size.min} to ${size.max})`),
      ...(view.extensions?.length ? [element('div', { class: 'gb-muted' }, `Extensions: ${view.extensions.join(', ')}`)] : []),
      members,
      row(name, ...creationControl, ...((view.races ?? []).length > 0 ? [race] : []), ...((view.classes ?? []).length > 0 ? [characterClass] : []), portrait, ...picture(chosen?.picture, 'Chosen portrait', 32)),
      choices.rows,
      ...(view.lifepath ? [row(element('span', {}, 'Career:'), lifepathCareer,
        element('span', {}, 'Terms:'), lifepathTerms,
        element('span', {}, 'Skill table:'), lifepathTables,
        element('span', {}, 'Benefit:'), lifepathBenefits)] : []),
      row(roll),
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
      return { rows, features: () => [], boosts: () => [], attributes: () => null, priority: () => null };
    }

    const attributeDetails = (creation.attributeDetails?.length
      ? creation.attributeDetails
      : (creation.attributes ?? []).map((id) => ({ id, name: id })));
    const arrangeableDetails = Array.isArray(creation.arrangeableAttributes)
      ? creation.arrangeableAttributes
      : attributeDetails;
    const attributeIds = arrangeableDetails.map((attribute) => attribute.id);
    let attributeValues = null;
    let priorityValues = null;
    if (creation.method === 'point-buy') {
      const costRows = creation.costs?.rows ?? [];
      const costText = costRows.length > 0
        ? ` Costs: ${costRows.map((cost) => `${cost[0]}=${cost[cost.length - 1]}`).join(', ')}.`
        : '';
      rows.append(element('div', { class: 'gb-muted' }, `Point buy: base ${creation.base ?? '-'}, budget ${creation.budget ?? '-'}${costText}`));
      const fields = attributeDetails.map((attribute) => {
        const key = `${creation.id}:score:${attribute.id}`;
        const initial = scoreDrafts.get(key) ?? creation.base ?? attribute.min ?? '';
        const inputAttributes = {
          type: 'number',
          step: 'any',
          value: String(initial),
          size: '5',
          'aria-label': `${attribute.name} score`,
          'data-focus-key': `party:new:score:${attribute.id}`,
        };
        if (attribute.min !== undefined) {
          inputAttributes.min = String(attribute.min);
        }

        if (attribute.max !== undefined) {
          inputAttributes.max = String(attribute.max);
        }

        const input = element('input', inputAttributes);
        input.addEventListener('input', () => scoreDrafts.set(key, input.value));
        rows.append(row(element('span', {}, `${attribute.name} (${attribute.id}):`), input));
        return { id: attribute.id, input };
      });
      attributeValues = () => Object.fromEntries(fields.map((field) => [field.id, Number(field.input.value)]));
    } else if (creation.method === 'array' || creation.assignment === 'arrange') {
      const array = creation.array ?? [];
      rows.append(element('div', { class: 'gb-muted' }, creation.method === 'array'
        ? 'Assign each standard score to an attribute.'
        : 'Assign each rolled score to an attribute.'));
      const fixedDetails = attributeDetails.filter((attribute) => !attributeIds.includes(attribute.id));
      if (fixedDetails.length > 0) {
        rows.append(element('div', { class: 'gb-muted' }, `Fixed authored rolls: ${fixedDetails.map((attribute) => attribute.name).join(', ')}.`));
      }

      const selects = arrangeableDetails.map((attribute, index) => {
        const key = `${creation.id}:priority:${index}`;
        const current = priorityDrafts.get(key);
        const selected = attributeIds.includes(current) ? current : attributeIds[index] ?? attributeIds[0];
        const select = element('select', {
          'aria-label': `${creation.method === 'array' ? `Score ${array[index] ?? index + 1}` : `Rolled score ${index + 1}`} priority`,
          'data-focus-key': `party:new:priority:${index}`,
        });
        fill(select, arrangeableDetails.map((detail) => ({ id: detail.id, name: detail.name })));
        if (selected) {
          select.value = selected;
        }

        select.addEventListener('change', () => {
          priorityDrafts.set(key, select.value);
          rerender();
        });
        rows.append(row(element('span', {}, `${creation.method === 'array' ? `Score ${array[index] ?? index + 1}` : `Rolled score ${index + 1}`} →`), select));
        return select;
      });
      priorityValues = selects.length > 0 ? () => selects.map((select) => select.value) : () => null;
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
      rows.append(element('div', { class: 'gb-muted' }, `${view.lifepath.name}: choose one career, skill table and benefit kind; the selected choices repeat for the policy's actual rolls.`));
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
      attributes: () => attributeValues?.() ?? null,
      priority: () => priorityValues?.() ?? null,
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
        element('span', { style: 'min-width:16em' }, `${skill.id}: base ${skill.base}, current ${skill.current}`),
        element('label', {}, skill.profession ? 'Profession ' : 'Profession (not allowed) ', profession),
        element('label', {}, 'Personal ', personal));
    });

    return [element('section', { style: 'margin:.4em 0;padding:.3em;background:var(--gb-inset)' },
      element('strong', {}, `${member.name}'s staged skill points`),
      element('div', { class: 'gb-muted' }, `Profession budget: ${skillPoints.profession}; Personal budget: ${skillPoints.personal}. Base/current values are from this roll.`),
      ...rows,
      hasCommitted
        ? element('div', { class: 'gb-muted' }, 'Skill points committed.')
        : row(button(`Spend ${member.name}'s skill points`, () => send({
          action: 'skills',
          member: index,
          skills: fields.map((field) => ({
            skill: field.skill,
            profession: Number(field.profession.value),
            personal: Number(field.personal.value),
          })),
        }, { 'data-focus-key': `party:${index}:skills` }))))];

    function pointInput(key, initial, allowed) {
      const input = element('input', {
        type: 'number',
        min: '0',
        step: 'any',
        value: String(skillDrafts.get(key) ?? initial),
        size: '5',
        'data-focus-key': key,
      });
      input.disabled = !allowed;
      input.addEventListener('input', () => skillDrafts.set(key, input.value));
      return input;
    }
  };

  return render;
}
