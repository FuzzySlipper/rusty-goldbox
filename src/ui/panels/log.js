import { element, button } from '../dom.js';
import { leading, votersFor } from '../table.js';

/**
 * The log under the view: the story so far as a page of text, the notes the
 * last action left, the waiting menu as numbered choices, and the command line.
 * In combat it shows the fight's lines. At a hosted table a Chat tab sits
 * beside it, and the menu shows who voted for what.
 */
export function createLog(send) {
  let showing = 'log';
  let read = 0;
  let lastView = null;
  const tab = element('button', { type: 'button', class: 'gb-tab' });
  const chatTab = element('button', { type: 'button', class: 'gb-tab', hidden: '' });
  const tabs = element('div', { class: 'gb-tabs' }, tab, chatTab);
  const switchTo = (which) => {
    showing = which;
    shown = '';
    if (lastView) {
      render(lastView);
    }
  };
  tab.addEventListener('click', () => switchTo('log'));
  chatTab.addEventListener('click', () => switchTo('chat'));
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
  const say = element('input', { 'aria-label': 'Say to the table', placeholder: 'say something to everyone', autocomplete: 'off', maxlength: '1000' });
  say.addEventListener('keydown', (event) => {
    if (event.key === 'Enter' && say.value.trim().length > 0) {
      send({ action: 'chat', text: say.value.trim() });
      say.value = '';
    }
  });
  const sayPrompt = element('label', { class: 'gb-prompt', hidden: '' }, element('span', {}, 'Say:'), say);
  const node = element('section', { class: 'gb-panel gb-log', 'aria-label': 'Log', 'data-rusty-ui-interactive': '' }, tabs, page, prompt, sayPrompt);
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
    lastView = view;
    const combat = view.screen === 'combat';
    const chat = view.hosting?.chat ?? null;
    if (!chat && showing === 'chat') {
      showing = 'log';
    }

    tab.textContent = combat ? `Combat: ${view.fight?.encounter ?? ''}` : 'Log';
    tab.setAttribute('aria-pressed', String(showing === 'log'));
    chatTab.hidden = !chat;
    if (showing === 'chat') {
      read = chat.length;
    }
    const unread = chat ? Math.max(0, chat.length - read) : 0;
    chatTab.textContent = unread > 0 ? `Chat (${unread})` : 'Chat';
    chatTab.setAttribute('aria-pressed', String(showing === 'chat'));
    prompt.hidden = combat || showing === 'chat';
    sayPrompt.hidden = showing !== 'chat';

    if (showing === 'chat') {
      const chatKey = JSON.stringify(['chat', chat]);
      if (chatKey === shown) {
        return;
      }

      shown = chatKey;
      // Chat is shown as text only: textContent through element(), never HTML.
      page.replaceChildren(...chat.map((line) => element('p', { class: line.state === 'failed' ? 'gb-note' : '' },
        element('b', {}, `${line.from}: `), line.text,
        ...(line.state === 'pending' ? [element('span', { class: 'gb-muted' }, ' (sending)')] : line.state === 'failed' ? [element('span', {}, ' (not delivered)')] : []))));
      page.scrollTop = page.scrollHeight;
      following = true;
      return;
    }

    const lines = combat ? (view.fight?.log ?? []) : (view.log ?? []);
    const notes = view.notes ?? [];
    const menu = combat ? [] : (view.menu ?? []);
    const outcome = combat && view.fight?.done ? view.fight.outcome : null;
    const votes = view.table?.votes ?? null;
    const key = JSON.stringify([lines, notes, menu, outcome, votes, view.table?.leader]);
    if (key === shown) {
      return;
    }

    shown = key;
    const decide = view.table && leading(view) && menu.length && Object.keys(votes ?? {}).length
      ? [button('Decide now', () => send({ action: 'decide' }), { class: 'gb-choice', 'data-focus-key': 'log:decide' })]
      : [];
    page.replaceChildren(
      ...lines.map((line, index) => element('p', { class: index < lines.length - 4 ? 'gb-old' : '' }, line)),
      ...(outcome ? [element('p', { class: 'gb-note' }, outcome)] : []),
      ...notes.map((note) => element('p', { class: 'gb-note' }, note)),
      ...menu.map((option) => {
        // At a hosted table a choice is a vote; who chose what shows beside it.
        const voters = view.table ? votersFor(view, option.number) : [];
        const mine = view.table && votes?.[String(view.you)] === option.number;
        const choice = element('button', { type: 'button', class: `gb-choice${mine ? ' gb-selected' : ''}` },
          element('b', {}, String(option.number)), option.label,
          ...(voters.length ? [element('span', { class: 'gb-muted' }, ` · ${voters.join(', ')}`)] : []));
        choice.addEventListener('click', () => send({ action: 'play', command: `choose ${option.number}` }));
        return choice;
      }),
      ...decide);
    page.scrollTop = page.scrollHeight;
    following = true;
  };

  return { node, render, dispose: () => resized.disconnect() };
}
