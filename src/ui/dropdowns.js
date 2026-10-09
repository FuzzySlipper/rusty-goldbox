import { element } from './dom.js';

/**
 * Opens every `<select>` under `root` (a positioned element) as a list on the page. The native window
 * draws the page offscreen and never shows the browser's own popup for a
 * select, so a click on one would do nothing there. The select stays the
 * source of truth: picking sets its value and fires `input` and `change`,
 * so the screens' own handlers run as before.
 */
export function installDropdowns(root) {
  let open = null;

  const close = () => {
    open?.list.remove();
    open = null;
  };

  // A screen may redraw while the list is open; its replacement has the same focus key.
  const current = (select) => select.isConnected || !select.dataset.focusKey
    ? select
    : root.querySelector(`select[data-focus-key="${CSS.escape(select.dataset.focusKey)}"]`) ?? select;

  const pick = (select, value) => {
    const target = current(select);
    close();
    target.focus({ preventScroll: true });
    if (target.value === value) {
      return;
    }

    target.value = value;
    target.dispatchEvent(new Event('input', { bubbles: true }));
    target.dispatchEvent(new Event('change', { bubbles: true }));
  };

  const highlight = (index) => {
    const items = [...open.list.children];
    open.active = Math.max(0, Math.min(items.length - 1, index));
    items.forEach((item, at) => item.classList.toggle('gb-active', at === open.active));
    items[open.active]?.scrollIntoView({ block: 'nearest' });
  };

  const show = (select) => {
    const options = [...select.options].filter((option) => !option.disabled && !option.hidden);
    const list = element('ul', { class: 'gb-dropdown', role: 'listbox', 'aria-label': select.getAttribute('aria-label') ?? '', 'data-rusty-ui-interactive': '' },
      ...options.map((option) => element('li', {
        role: 'option',
        'aria-selected': String(option.value === select.value),
        'data-value': option.value,
      }, option.textContent)));
    list.addEventListener('mousedown', (event) => event.preventDefault());
    list.addEventListener('click', (event) => {
      const item = event.target.closest('li');
      if (item) {
        pick(select, item.dataset.value);
      }
    });
    root.append(list);
    open = { select, list, active: 0 };

    // Below the select, or above it when there's more room there, measured in the root's own box.
    const frame = root.getBoundingClientRect();
    const box = select.getBoundingClientRect();
    const top = box.top - frame.top;
    const bottom = box.bottom - frame.top;
    const below = frame.height - bottom;
    list.style.minWidth = `${box.width}px`;
    list.style.left = `${Math.max(0, Math.min(box.left - frame.left, frame.width - list.offsetWidth))}px`;
    if (below < list.offsetHeight && top > below) {
      list.style.bottom = `${frame.height - top}px`;
      list.style.maxHeight = `${top - 4}px`;
    } else {
      list.style.top = `${bottom}px`;
      list.style.maxHeight = `${below - 4}px`;
    }

    highlight(Math.max(0, options.findIndex((option) => option.value === select.value)));
  };

  // A screen change replaces the root's children, taking an open list with it.
  const settle = () => {
    if (open && !open.list.isConnected) {
      open = null;
    }
  };

  root.addEventListener('mousedown', (event) => {
    settle();
    if (open?.list.contains(event.target)) {
      return;
    }

    const select = event.target.closest?.('select');
    const reopening = open && open.select === select;
    close();
    if (!select || select.disabled || select.multiple || reopening) {
      if (reopening) {
        event.preventDefault();
      }

      return;
    }

    event.preventDefault();
    select.focus({ preventScroll: true });
    show(select);
  }, true);

  root.addEventListener('keydown', (event) => {
    settle();
    if (open) {
      const step = { ArrowDown: 1, ArrowUp: -1, PageDown: 8, PageUp: -8 }[event.key];
      if (step) {
        highlight(open.active + step);
      } else if (event.key === 'Enter' || event.key === ' ') {
        pick(open.select, open.list.children[open.active]?.dataset.value ?? open.select.value);
      } else if (event.key === 'Escape' || event.key === 'Tab') {
        const select = open.select;
        close();
        if (event.key === 'Escape') {
          current(select).focus({ preventScroll: true });
        } else {
          return;
        }
      } else {
        return;
      }

      event.preventDefault();
      event.stopPropagation();
      return;
    }

    const select = event.target.closest?.('select');
    if (select && !select.disabled && (event.key === 'Enter' || event.key === ' ' || (event.altKey && event.key === 'ArrowDown'))) {
      event.preventDefault();
      show(select);
    }
  }, true);

  // A scroll or resize would leave the list floating away from its select.
  root.addEventListener('scroll', (event) => {
    if (open && !open.list.contains(event.target)) {
      close();
    }
  }, true);
  window.addEventListener('resize', close);
}
