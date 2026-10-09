/**
 * The Game's DOM panels. Engine owns the canvas, input delivery and projection
 * transport. This module shows the product's rusty.goldbox.session projection
 * and claims goldbox.command intents with goldbox.command.v1 payloads; it owns
 * no game state. Arrow keys / WASD move, digits choose menu options, through
 * the product's key mappings.
 *
 * Play and combat use one panel frame whose arrangement follows the window's
 * shape (layout.js); each panel module renders its own part of the projection.
 * The Engine draws the world in the view panel, which the frame leaves clear.
 */
import { element } from './dom.js';
import { installDropdowns } from './dropdowns.js';
import { BASE_LOOK, skinLook } from './look.js';
import { applyLayout, arrangementFor } from './layout.js';
import { createStatus } from './panels/status.js';
import { createLog } from './panels/log.js';
import { createPortraits } from './panels/portraits.js';
import { createMap } from './panels/map.js';
import { createControls } from './panels/controls.js';
import { createOverlay } from './panels/overlays.js';
import { createTitle } from './screens/title.js';
import { createParty } from './screens/party.js';

const COMMAND_INTENT = 'goldbox.command';
const COMMAND_CONTRACT = 'goldbox.command.v1';

export function mountProductUi(root, context) {
  const send = (data) => {
    context?.intents?.claim(COMMAND_INTENT, { kind: 'product-payload', contract: COMMAND_CONTRACT, data });
  };

  // The overlay the player opened (the game menu or a member's sheet), if any: presentation only.
  let open = null;
  let lastView = null;
  const rerender = () => {
    if (lastView) {
      render(lastView);
    }
  };
  const ui = {
    open: (what) => {
      open = what;
      rerender();
    },
    toggle: (what) => {
      open = JSON.stringify(open) === JSON.stringify(what) ? null : what;
      rerender();
    },
    close: () => {
      open = null;
      rerender();
    },
    // Shows proportions while the player drags a layout slider; the projection's layout returns on the next update.
    preview: (layout) => showLayout(layout),
    // Sets the interface scale while the player drags its slider; letting go saves it.
    scale: (scale) => {
      savedUiScale = scale;
      context?.ui?.setScale?.(scale);
    },
  };

  // The look lives in a stylesheet of theme variables, so a skin restyles every panel at once.
  const skinStyle = element('style', {}, BASE_LOOK);
  let appliedSkin = null;
  const applySkin = (skin) => {
    const key = JSON.stringify(skin ?? null);
    if (key !== appliedSkin) {
      appliedSkin = key;
      skinStyle.textContent = BASE_LOOK + skinLook(skin);
    }
  };

  const status = createStatus(ui);
  const log = createLog(send);
  const portraits = createPortraits(send, ui);
  const map = createMap();
  const controls = createControls(send, ui);
  const overlay = createOverlay(send, ui);
  // The view panel is left clear: the Engine draws the world inside it.
  const viewSurface = element('div', { class: 'gb-view-surface' });
  const view = element('section', { class: 'gb-panel gb-view', 'aria-label': 'View' }, viewSurface);
  const side = element('div', { class: 'gb-side' }, portraits.node, map.node, controls.node);
  const playPanels = [status.node, view, log.node, side, overlay.node];

  // Title and party screens: one sheet, its notes, and the menu's sound and look settings (made once, never moved).
  // Notes (a campaign's load errors can run to dozens) scroll inside their own box, so the sheet below stays put.
  const sheetNotes = element('ul', { id: 'rusty-goldbox-notes', class: 'gb-notes', role: 'status' });
  let shownNotes = '';
  const sheetBody = element('div');
  const sheet = element('section', { class: 'gb-panel gb-sheet', 'data-rusty-ui-interactive': '' }, sheetNotes, sheetBody, overlay.settings);
  const renderTitle = createTitle(send);
  const renderParty = createParty(send, rerender);

  const frame = element('div', { class: 'gb-frame' });
  const panel = element('div', { 'aria-label': 'Rusty Goldbox', 'data-goldbox-panel': '' }, frame);
  root.append(skinStyle, panel);
  installDropdowns(frame);

  // The player's interface scale is the Engine's UI scale, which the stylesheet multiplies every size by.
  // The saved one is applied when it arrives; the menu slider sets it live and saves it.
  let savedUiScale = null;
  const applyUiScale = (scale) => {
    if (typeof scale !== 'number' || scale === savedUiScale) {
      return;
    }

    savedUiScale = scale;
    context?.ui?.setScale?.(scale);
  };

  // The arrangement follows the window's shape, at the projection's layout proportions.
  let layout = null;
  let shownLayout = '';
  const arrange = () => {
    frame.dataset.arrangement = arrangementFor(panel.clientWidth, panel.clientHeight, layout);
  };
  const showLayout = (next) => {
    const key = JSON.stringify(next ?? null);
    if (!next || key === shownLayout) {
      return;
    }

    shownLayout = key;
    layout = next;
    applyLayout(panel, layout);
    arrange();
  };

  // The Engine's camera draws over the view panel's inside, following it on
  // every resize and layout change (SceneView anchors the camera to "hero").
  const removeAnchor = context?.viewport?.anchor?.('hero', viewSurface);
  const resized = new ResizeObserver(arrange);

  resized.observe(panel);
  arrange();

  let shownScreen = null;
  const render = (projection) => {
    lastView = projection;
    applySkin(projection.skin);
    showLayout(projection.layout);
    applyUiScale(projection.uiScale);
    const screen = projection.screen;
    const playing = screen === 'play' || screen === 'combat';
    if (screen !== shownScreen) {
      shownScreen = screen;
      frame.dataset.screen = screen;
      // The menu may stay open over a fight; anything else closes with its screen.
      open = screen === 'combat' && open?.kind === 'menu' ? open : null;

      // The settings move back into the menu when play starts; nothing is focused on them at a screen change.
      overlay.node.replaceChildren();
      overlay.node.dataset.showing = '';
      frame.replaceChildren(...(playing ? playPanels : [sheet]));
      if (!playing) {
        sheet.append(overlay.settings);
      }
    }

    if (playing) {
      status.render(projection);
      log.render(projection);
      portraits.render(projection);
      map.render(projection);
      controls.render(projection);
      overlay.render(projection, open);
      return;
    }

    const sheetState = shownScreen === screen
      ? {
        scrollTop: sheet.scrollTop,
        focusKey: sheet.contains(document.activeElement) ? document.activeElement?.getAttribute('data-focus-key') : null,
      }
      : null;
    const notes = projection.notes ?? [];
    const notesKey = JSON.stringify(notes);
    if (notesKey !== shownNotes) {
      shownNotes = notesKey;
      sheetNotes.replaceChildren(...notes.map((note) => element('li', {}, note)));
      sheetNotes.hidden = notes.length === 0;
    }
    overlay.render(projection, null);
    const body = screen === 'title' ? renderTitle(projection) : screen === 'party' ? renderParty(projection) : '';
    if (body !== null) {
      sheetBody.replaceChildren(body);
    }
    if (sheetState) {
      sheet.scrollTop = sheetState.scrollTop;
      if (sheetState.focusKey) {
        const target = [...sheet.querySelectorAll('[data-focus-key]')]
          .find((candidate) => candidate.getAttribute('data-focus-key') === sheetState.focusKey);
        target?.focus({ preventScroll: true });
      }
    }
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
      resized.disconnect();
      removeAnchor?.();
      log.dispose();
      map.dispose();
      panel.remove();
      skinStyle.remove();
    },
  });
}
