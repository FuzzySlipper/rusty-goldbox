import { element, button, row } from '../dom.js';

/** The track a portrait's bar shows: the one a fight is fought on, else the first with a maximum. */
export function vitalTrack(member) {
  const tracks = member.tracks ?? [];
  return tracks.find((track) => track.vital) ?? tracks.find((track) => track.max !== null && track.max !== undefined) ?? null;
}

/** A bar for current/max, coloured by how full it is. */
export function bar(current, max) {
  if (max === null || max === undefined || max <= 0) {
    return element('span', { class: 'gb-bar' });
  }

  const share = Math.max(0, Math.min(1, (current ?? 0) / max));
  const color = current <= 0 ? 'var(--gb-muted)' : share > .6 ? 'var(--gb-good)' : share > .3 ? 'var(--gb-warn)' : 'var(--gb-bad)';
  return element('span', { class: 'gb-bar' }, element('i', { style: `width:${Math.round(share * 100)}%;background:${color}` }));
}

/** A checkbox per spell the member could know, checked for those it knows; a change sets the whole list. */
export function renderSpells(send, member, index) {
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

  return [row(element('span', {}, 'Spells known:'),
    ...boxes.map(({ spell, box }) => element('label', {}, box, spell.name)))];
}

/**
 * The copies a member memorises each day, with + and − per spell it could
 * memorise; each change sends the whole list, which Core checks against its
 * slots. In play the new list is prepared at the next rest.
 */
export function renderMemorised(send, member, index) {
  const options = member.memorisable ?? [];
  if (options.length === 0) {
    return [];
  }

  const plan = (member.memorisedChosen ? member.memorised : []).map((spell) => spell.id);
  const submit = (spells) => send({ action: 'memorise', member: index, spells });
  const names = (list) => list.map((spell) => spell.name).join(', ') || 'none';
  return [
    element('div', {}, `Memorises: ${names(member.memorised ?? [])}${member.memorisedChosen ? '' : ' (known spells in order)'}; left today: ${names(member.prepared ?? [])}`),
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
}
