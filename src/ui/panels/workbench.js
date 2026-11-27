import { element, button, selectBox } from '../dom.js';

/**
 * Optional in-game Workbench debug surface: hosts the CLI Workbench SPA in an
 * overlay drawer, streams live game state (area, cell, facing, active event,
 * combatant) into it, handles click-to-jump debug commands back into the Game,
 * and lets the player drop review pins directly from gameplay.
 */
export function createWorkbench(send) {
  let isOpen = false;
  let lastLive = null;
  let currentUrl = 'http://127.0.0.1:4173';
  let connected = false;
  let pinMode = 'cell'; // 'cell' | 'event'

  const backdrop = element('div', {
    class: 'gb-workbench-backdrop',
    'data-rusty-ui-interactive': '',
    hidden: 'hidden',
  });

  const urlInput = element('input', {
    type: 'url',
    class: 'gb-wb-input',
    value: currentUrl,
    'aria-label': 'Workbench URL',
  });
  const liveLabel = element('span', { class: 'gb-wb-live' }, 'Not in play');
  const statusChip = element('span', { class: 'gb-wb-status' }, 'Disconnected');

  const pinTargetSelect = selectBox({ class: 'gb-wb-select', 'aria-label': 'Pin target' });
  const pinNoteInput = element('input', {
    type: 'text',
    class: 'gb-wb-note',
    placeholder: 'Quick bug note for the agent (optional)…',
    'aria-label': 'Review pin note',
  });
  const pinFeedback = element('span', { class: 'gb-wb-feedback' }, '');

  const iframe = element('iframe', {
    class: 'gb-wb-iframe',
    title: 'Rusty Tales Workbench',
    hidden: 'hidden',
  });

  const offlineHint = element('div', { class: 'gb-wb-offline' });

  const buildPayload = () => {
    if (!lastLive) return null;
    return {
      type: 'tales.liveState',
      screen: lastLive.screen ?? null,
      module: lastLive.module ?? null,
      campaign: lastLive.campaign ?? null,
      area: lastLive.area ?? null,
      areaName: lastLive.areaName ?? null,
      x: lastLive.x ?? null,
      y: lastLive.y ?? null,
      facing: lastLive.facing ?? null,
      activeEvent: lastLive.activeEvent ?? null,
      activeActor: lastLive.activeActor ?? null,
    };
  };

  const pushLiveState = () => {
    const payload = buildPayload();
    if (!payload) return;
    if (connected && iframe.contentWindow) {
      iframe.contentWindow.postMessage(payload, '*');
    }
    if (connected) {
      fetch(`${currentUrl}/api/live-state`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ ok: true, connected: true, ...payload }),
      }).catch(() => {});
    }
  };

  const renderOffline = (reason) => {
    connected = false;
    statusChip.textContent = 'Offline';
    statusChip.dataset.state = 'offline';
    iframe.hidden = true;
    offlineHint.hidden = false;
    const modHint = lastLive?.module ? `modules/${lastLive.module}` : 'modules/<campaign>';
    offlineHint.replaceChildren(
      element('h3', {}, 'CLI Workbench is not reachable'),
      element('p', {}, reason || `Could not reach ${currentUrl}/api/health.`),
      element('p', {}, 'Start the local Workbench server in a terminal, then click Retry:'),
      element('pre', { class: 'gb-wb-code' }, `tales workbench ${modHint}`),
      button('↻ Retry Connection', () => connect(urlInput.value)),
    );
  };

  const connect = async (rawUrl) => {
    const clean = (rawUrl || '').trim().replace(/\/+$/, '');
    if (!/^https?:\/\//i.test(clean)) {
      renderOffline(`'${clean}' is not a valid http:// or https:// URL.`);
      return;
    }
    currentUrl = clean;
    urlInput.value = clean;
    statusChip.textContent = 'Connecting…';
    statusChip.dataset.state = 'connecting';
    try {
      const res = await fetch(`${currentUrl}/api/health`, { cache: 'no-store' });
      if (!res.ok) throw new Error(`HTTP ${res.status}`);
      const health = await res.json();
      connected = true;
      statusChip.textContent = `Connected · ${health.module || 'workbench'}`;
      statusChip.dataset.state = 'online';
      offlineHint.hidden = true;
      const targetSrc = `${currentUrl}/?embed=1`;
      if (iframe.src !== targetSrc) {
        iframe.src = targetSrc;
      }
      iframe.hidden = false;
      pushLiveState();
    } catch (err) {
      renderOffline(`Could not connect to ${currentUrl} (${err.message}).`);
    }
  };

  iframe.addEventListener('load', () => {
    pushLiveState();
  });

  const syncPinTargets = () => {
    const options = [];
    if (lastLive?.area && lastLive.x !== null && lastLive.x !== undefined) {
      options.push({
        value: 'cell',
        label: `Cell ${lastLive.area}@[${lastLive.x},${lastLive.y}]`,
      });
    }
    if (lastLive?.activeEvent) {
      options.push({
        value: 'event',
        label: `Event ${lastLive.activeEvent}`,
      });
    }
    if (lastLive?.area) {
      options.push({
        value: 'area',
        label: `Area ${lastLive.area}`,
      });
    }
    if (options.length === 0) {
      options.push({ value: 'none', label: 'No active gameplay target' });
    }
    pinTargetSelect.replaceChildren(...options.map((opt) => element('option', { value: opt.value }, opt.label)));
    if (options.some((o) => o.value === pinMode)) {
      pinTargetSelect.value = pinMode;
    } else {
      pinMode = options[0].value;
      pinTargetSelect.value = pinMode;
    }
  };
  pinTargetSelect.addEventListener('change', () => {
    pinMode = pinTargetSelect.value;
  });

  const submitQuickPin = async (status) => {
    if (!connected) {
      pinFeedback.textContent = 'Connect Workbench first.';
      return;
    }
    let targetKind = null;
    let target = null;
    if (pinMode === 'cell' && lastLive?.area && lastLive.x !== null && lastLive.x !== undefined) {
      targetKind = 'cell';
      target = `${lastLive.area}@[${lastLive.x},${lastLive.y}]`;
    } else if (pinMode === 'event' && lastLive?.activeEvent) {
      targetKind = 'event';
      target = lastLive.activeEvent;
    } else if (pinMode === 'area' && lastLive?.area) {
      targetKind = 'area';
      target = lastLive.area;
    }
    if (!targetKind || !target) {
      pinFeedback.textContent = 'Enter play to drop a live pin.';
      return;
    }

    pinFeedback.textContent = 'Saving…';
    try {
      const res = await fetch(`${currentUrl}/api/reviews`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          targetKind,
          target,
          status,
          note: pinNoteInput.value.trim(),
          author: 'human',
        }),
      });
      if (!res.ok) throw new Error(`HTTP ${res.status}`);
      const data = await res.json();
      pinNoteInput.value = '';
      pinFeedback.textContent = `Saved ${data.pin?.id || ''} [${status}]`;
      // Refresh iframe review overlays cleanly without losing tab or selection
      if (iframe.contentWindow) {
        iframe.contentWindow.postMessage({ type: 'tales.refreshReviews' }, '*');
      }
    } catch (err) {
      pinFeedback.textContent = `Failed: ${err.message}`;
    }
  };

  const onMessage = (ev) => {
    if (!ev.data || typeof ev.data !== 'object') return;
    if (ev.data.type === 'tales.debugGoto' && ev.data.area) {
      send({
        action: 'debug-goto',
        area: ev.data.area,
        x: Number(ev.data.x ?? 0),
        y: Number(ev.data.y ?? 0),
        facing: ev.data.facing || undefined,
      });
    } else if (ev.data.type === 'tales.debugEvent' && ev.data.event) {
      send({
        action: 'debug-event',
        event: ev.data.event,
      });
    }
  };
  window.addEventListener('message', onMessage);

  const onKeyDown = (ev) => {
    if (ev.key === 'F9') {
      ev.preventDefault();
      toggle();
    } else if (ev.key === 'Escape' && isOpen) {
      ev.preventDefault();
      close();
    }
  };
  window.addEventListener('keydown', onKeyDown);

  const header = element('div', { class: 'gb-wb-header' },
    element('strong', { class: 'gb-wb-title' }, '⚙ Workbench Debug Surface'),
    liveLabel,
    statusChip,
    element('span', { class: 'gb-spacer' }),
    urlInput,
    button('Connect', () => {
      const next = urlInput.value.trim().replace(/\/+$/, '');
      if (next) send({ action: 'workbench-url', url: next });
      connect(next);
    }),
    button('✕ Close', () => close(), { title: 'Close Workbench (Esc or F9)' }),
  );

  const quickPinBar = element('div', { class: 'gb-wb-pinbar' },
    element('span', { class: 'gb-wb-pinlabel' }, 'Quick Pin:'),
    pinTargetSelect,
    pinNoteInput,
    button('✗ Needs Fix', () => submitQuickPin('needs-fix'), { class: 'gb-wb-btn-fix' }),
    button('? Question', () => submitQuickPin('question'), { class: 'gb-wb-btn-q' }),
    button('✓ Approve', () => submitQuickPin('approved'), { class: 'gb-wb-btn-ok' }),
    pinFeedback,
  );

  const body = element('div', { class: 'gb-wb-body' }, offlineHint, iframe);
  const shell = element('section', {
    class: 'gb-wb-shell',
    role: 'dialog',
    'aria-label': 'Rusty Tales Workbench Debug Surface',
  }, header, quickPinBar, body);

  backdrop.append(shell);
  backdrop.addEventListener('click', (ev) => {
    if (ev.target === backdrop) close();
  });

  const open = () => {
    isOpen = true;
    backdrop.hidden = false;
    syncPinTargets();
    connect(currentUrl);
  };

  const close = () => {
    isOpen = false;
    backdrop.hidden = true;
  };

  const toggle = () => {
    if (isOpen) close();
    else open();
  };

  const render = (projection) => {
    const wb = projection?.workbench;
    if (!wb) return;
    if (wb.url && wb.url !== currentUrl && document.activeElement !== urlInput) {
      currentUrl = wb.url;
      urlInput.value = wb.url;
    }
    const prevKey = JSON.stringify(lastLive);
    lastLive = wb;
    const nextKey = JSON.stringify(lastLive);

    if (wb.area) {
      const pos = wb.x !== null && wb.x !== undefined ? ` [${wb.x}, ${wb.y}] ${wb.facing || ''}` : '';
      const evPart = wb.activeEvent ? ` · event ${wb.activeEvent.split(':').pop()}` : '';
      const actPart = wb.activeActor ? ` · actor ${wb.activeActor}` : '';
      liveLabel.textContent = `${wb.areaName || wb.area}${pos}${evPart}${actPart}`;
    } else {
      liveLabel.textContent = `${wb.screen || 'title'}${wb.module ? ` · ${wb.module}` : ''}`;
    }

    if (prevKey !== nextKey) {
      if (isOpen) {
        syncPinTargets();
      }
      pushLiveState();
    }
  };

  return {
    node: backdrop,
    open,
    close,
    toggle,
    render,
    dispose: () => {
      window.removeEventListener('message', onMessage);
      window.removeEventListener('keydown', onKeyDown);
      backdrop.remove();
    },
  };
}
