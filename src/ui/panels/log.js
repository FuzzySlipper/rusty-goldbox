import { element } from '../dom.js';

/**
 * The log under the view: the story so far as a page of text, the notes the
 * last action left, the waiting menu as numbered choices, and the command line.
 * In combat it shows the fight's lines.
 */
export function createLog(send) {
  const tab = element('span', { class: 'gb-tab' });
  const page = element('div', { class: 'gb-page', id: 'rusty-goldbox-log', 'aria-live': 'polite', 'data-rusty-ui-interactive': '' });
  // The command line lives outside the re-rendered page so typing survives updates.
  const command = element('input', { 'aria-label': 'Play command', placeholder: 'type a command, for example: search north', autocomplete: 'off' });
  command.addEventListener('keydown', (event) => {
    if (event.key === 'Enter' && command.value.trim().length > 0) {
      send({ action: 'play', command: command.value.trim() });
      command.value = '';
    }
  });
  const prompt = element('label', { class: 'gb-prompt' }, element('span', {}, 'Party:'), command);
  const node = element('section', { class: 'gb-panel gb-log', 'aria-label': 'Log', 'data-rusty-ui-interactive': '' }, tab, page, prompt);
  let shown = '';
  // The newest line stays in sight when the page changes size, unless the player scrolled back to read.
  let following = true;
  page.addEventListener('scroll', () => {
    following = page.scrollTop + page.clientHeight >= page.scrollHeight - 4;
  });
  const resized = new ResizeObserver(() => {
    if (following) {
      page.scrollTop = page.scrollHeight;
    }
  });
  resized.observe(page);

  const render = (view) => {
    const combat = view.screen === 'combat';
    tab.textContent = combat ? `Combat: ${view.fight?.encounter ?? ''}` : 'Log';
    prompt.hidden = combat;

    const lines = combat ? (view.fight?.log ?? []) : (view.log ?? []);
    const notes = view.notes ?? [];
    const menu = combat ? [] : (view.menu ?? []);
    const outcome = combat && view.fight?.done ? view.fight.outcome : null;
    const key = JSON.stringify([lines, notes, menu, outcome]);
    if (key === shown) {
      return;
    }

    shown = key;
    page.replaceChildren(
      ...lines.map((line, index) => element('p', { class: index < lines.length - 4 ? 'gb-old' : '' }, line)),
      ...(outcome ? [element('p', { class: 'gb-note' }, outcome)] : []),
      ...notes.map((note) => element('p', { class: 'gb-note' }, note)),
      ...menu.map((option) => {
        const choice = element('button', { type: 'button', class: 'gb-choice' }, element('b', {}, String(option.number)), option.label);
        choice.addEventListener('click', () => send({ action: 'play', command: `choose ${option.number}` }));
        return choice;
      }));
    page.scrollTop = page.scrollHeight;
    following = true;
  };

  return { node, render, dispose: () => resized.disconnect() };
}
