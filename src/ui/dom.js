/** Small DOM helpers every panel uses. */

export function element(tag, attributes = {}, ...children) {
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

export function fragment(...children) {
  const node = document.createDocumentFragment();
  node.append(...children);
  return node;
}

export function row(...children) {
  return element('div', { class: 'gb-row' }, ...children);
}

export function button(label, onClick, attributes = {}) {
  const node = element('button', { type: 'button', ...attributes }, label);
  node.addEventListener('click', onClick);
  return node;
}

/** Fills a select (native or custom selectBox) with { id, name, disabled? } choices, keeping the current choice when it is still offered. */
export function fill(select, choices) {
  const current = select.value;
  select.replaceChildren(...choices.map((choice) => {
    const option = element('option', { value: choice.id }, choice.name);
    if (choice.disabled) {
      option.disabled = true;
    }
    return option;
  }));
  if (choices.some((choice) => choice.id === current && !choice.disabled)) {
    select.value = current;
  }
}

const openSelectClosers = new Set();
let selectGlobalBound = false;

function ensureSelectGlobalHandlers() {
  if (selectGlobalBound) {
    return;
  }

  selectGlobalBound = true;
  document.addEventListener('pointerdown', (event) => {
    for (const close of [...openSelectClosers]) {
      close(event.target);
    }
  });
  document.addEventListener('keydown', (event) => {
    if (event.key === 'Escape' && openSelectClosers.size > 0) {
      for (const close of [...openSelectClosers]) {
        close(null);
      }
    }
  });
}

/**
 * Custom theme-matched DOM dropdown that avoids CEF/Chromium native `<select>`
 * popup offset and unstyled white OS menu bugs under UI scaling. Exposes the
 * same `.value`, `.disabled`, `.replaceChildren(...)`, `.focus()`, and `change`
 * event contract as `<select>` so panels can drop it in cleanly.
 */
export function selectBox(attributes = {}, ...initialOptions) {
  ensureSelectGlobalHandlers();
  const { class: extraClass = '', 'data-focus-key': focusKey, 'aria-label': ariaLabel, ...rest } = attributes;
  const root = element('div', { class: `gb-select${extraClass ? ` ${extraClass}` : ''}`, ...rest });
  const triggerAttrs = {
    type: 'button',
    class: 'gb-select-trigger',
    'aria-haspopup': 'listbox',
    'aria-expanded': 'false',
    ...(ariaLabel ? { 'aria-label': ariaLabel } : {}),
    ...(focusKey ? { 'data-focus-key': focusKey } : {}),
  };
  const labelSpan = element('span', { class: 'gb-select-label' }, '');
  const caretSpan = element('span', { class: 'gb-select-caret', 'aria-hidden': 'true' }, '▾');
  const trigger = element('button', triggerAttrs, labelSpan, caretSpan);
  const menu = element('div', { class: 'gb-select-menu', role: 'listbox', hidden: 'hidden' });
  root.append(trigger, menu);

  let options = [];
  let currentValue = '';
  let isOpen = false;
  let activeIndex = -1;

  const syncLabel = () => {
    const selected = options.find((opt) => opt.value === currentValue) ?? options.find((opt) => !opt.disabled) ?? options[0];
    if (selected) {
      currentValue = selected.value;
      labelSpan.textContent = selected.label;
      trigger.title = selected.label;
    } else {
      currentValue = '';
      labelSpan.textContent = '';
      trigger.title = '';
    }
  };

  const renderMenu = () => {
    menu.replaceChildren(...options.map((opt, index) => {
      const selected = opt.value === currentValue;
      const item = element('button', {
        type: 'button',
        role: 'option',
        class: `gb-select-option${selected ? ' gb-selected' : ''}${index === activeIndex ? ' gb-active' : ''}${opt.disabled ? ' gb-disabled' : ''}`,
        'aria-selected': String(selected),
        ...(opt.disabled ? { disabled: 'disabled' } : {}),
      }, opt.label);
      item.addEventListener('click', (event) => {
        event.stopPropagation();
        if (opt.disabled) {
          return;
        }
        const changed = currentValue !== opt.value;
        currentValue = opt.value;
        syncLabel();
        closeMenu();
        trigger.focus({ preventScroll: true });
        if (changed) {
          root.dispatchEvent(new Event('change', { bubbles: true }));
        }
      });
      return item;
    }));
  };

  const closeMenu = () => {
    if (!isOpen) {
      return;
    }
    isOpen = false;
    menu.hidden = true;
    root.classList.remove('gb-select-open', 'gb-select-up');
    trigger.setAttribute('aria-expanded', 'false');
    openSelectClosers.delete(handleOutside);
  };

  const handleOutside = (target) => {
    if (!target || !root.contains(target)) {
      closeMenu();
    }
  };

  const openMenu = () => {
    if (trigger.disabled || options.length === 0) {
      return;
    }
    for (const close of [...openSelectClosers]) {
      close(null);
    }
    isOpen = true;
    activeIndex = Math.max(0, options.findIndex((opt) => opt.value === currentValue));
    renderMenu();
    // Flip upward if the dropdown would overflow the bottom of the window.
    const rect = trigger.getBoundingClientRect();
    const spaceBelow = window.innerHeight - rect.bottom;
    const estimatedHeight = Math.min(240, options.length * 32 + 12);
    root.classList.toggle('gb-select-up', spaceBelow < estimatedHeight && rect.top > estimatedHeight);
    root.classList.add('gb-select-open');
    menu.hidden = false;
    trigger.setAttribute('aria-expanded', 'true');
    openSelectClosers.add(handleOutside);
    const chosenNode = menu.children[activeIndex];
    chosenNode?.scrollIntoView?.({ block: 'nearest' });
  };

  trigger.addEventListener('click', (event) => {
    event.stopPropagation();
    if (isOpen) {
      closeMenu();
    } else {
      openMenu();
    }
  });

  trigger.addEventListener('keydown', (event) => {
    if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
      event.preventDefault();
      if (!isOpen) {
        openMenu();
        return;
      }
      const step = event.key === 'ArrowDown' ? 1 : -1;
      for (let tries = 0; tries < options.length; tries++) {
        activeIndex = (activeIndex + step + options.length) % options.length;
        if (!options[activeIndex].disabled) {
          break;
        }
      }
      renderMenu();
      menu.children[activeIndex]?.scrollIntoView?.({ block: 'nearest' });
    } else if ((event.key === 'Enter' || event.key === ' ') && isOpen) {
      event.preventDefault();
      const opt = options[activeIndex];
      if (opt && !opt.disabled) {
        const changed = currentValue !== opt.value;
        currentValue = opt.value;
        syncLabel();
        closeMenu();
        if (changed) {
          root.dispatchEvent(new Event('change', { bubbles: true }));
        }
      }
    } else if (event.key === 'Escape' && isOpen) {
      event.preventDefault();
      closeMenu();
    }
  });

  Object.defineProperty(root, 'value', {
    get() {
      return currentValue;
    },
    set(next) {
      const text = next === null || next === undefined ? '' : String(next);
      if (options.some((opt) => opt.value === text)) {
        currentValue = text;
        syncLabel();
        if (isOpen) {
          renderMenu();
        }
      } else if (options.length === 0) {
        currentValue = text;
      }
    },
  });

  Object.defineProperty(root, 'disabled', {
    get() {
      return trigger.disabled;
    },
    set(flag) {
      trigger.disabled = Boolean(flag);
      if (trigger.disabled) {
        closeMenu();
      }
    },
  });

  root.replaceChildren = (...nodes) => {
    options = nodes
      .filter((node) => node && typeof node === 'object')
      .map((node) => ({
        value: node.value !== undefined ? String(node.value) : String(node.getAttribute?.('value') ?? ''),
        label: String(node.textContent ?? node.label ?? node.name ?? ''),
        disabled: Boolean(node.disabled),
      }));
    if (!options.some((opt) => opt.value === currentValue)) {
      const firstValid = options.find((opt) => !opt.disabled) ?? options[0];
      currentValue = firstValid ? firstValid.value : '';
    }
    syncLabel();
    if (isOpen) {
      renderMenu();
    }
  };

  root.focus = (opts) => trigger.focus(opts);

  if (initialOptions.length > 0) {
    root.replaceChildren(...initialOptions);
  }

  return root;
}

export const currencyName = (id) => id ? id.split(':').at(-1) : 'currency';

export const formatBalances = (balances) => {
  const entries = Object.entries(balances ?? {});
  return entries.length === 0 ? 'no money' : entries.map(([id, amount]) => `${amount} ${currencyName(id)}`).join(', ');
};

/** A track as text: "Hit points 6/6". */
export const trackText = (track) => `${track.name} ${track.current ?? '-'}${track.max === null || track.max === undefined ? '' : `/${track.max}`}`;

/**
 * Any picture the projection carries ({ url, width, height, frame, animation,
 * sampling }), drawn with the authored sampling policy: an image whole, a
 * sheet's frame cropped from it, playing its animation. Nothing without one.
 * Every panel picture goes through here, so a new kind of media is drawn in one
 * place.
 *
 * A number size fits the picture in a size-pixel square. The size 'fill' makes
 * it as wide as its container, keeping its shape, so it scales with the panel.
 */
export function picture(media, label, size) {
  if (!media?.url) {
    return [];
  }

  const sampling = media.sampling === 'linear' ? 'linear' : 'nearest';
  const imageAttributes = { 'data-gb-sampling': sampling, class: 'gb-picture' };
  const filling = size === 'fill';
  if (!media.frame) {
    if (filling) {
      return [element('img', { src: media.url, alt: label, ...imageAttributes, class: 'gb-picture gb-fill' })];
    }

    // Within a size-pixel square, keeping the image's shape.
    const fit = media.width && media.height ? size / Math.max(media.width, media.height) : 1;
    const width = media.width ? Math.round(media.width * fit) : size;
    const height = media.height ? Math.round(media.height * fit) : size;
    return [element('img', { src: media.url, alt: label, width, height, ...imageAttributes })];
  }

  const [frameWidth, frameHeight] = media.frame;
  const columns = Math.max(1, Math.floor(media.width / frameWidth));
  const rows = Math.max(1, Math.floor(media.height / frameHeight));
  const node = element('div', { role: 'img', 'aria-label': label, ...imageAttributes });
  const image = element('img', { src: media.url, alt: '', draggable: 'false', class: 'gb-picture-sheet-image' });
  node.append(image);
  node.style.position = 'relative';
  node.style.overflow = 'hidden';
  if (filling) {
    // The clipped image scales the sheet with the box; the box keeps the frame's shape.
    node.classList.add('gb-fill');
    node.style.aspectRatio = `${frameWidth} / ${frameHeight}`;
  } else {
    const scale = size / Math.max(frameWidth, frameHeight);
    node.style.width = `${frameWidth * scale}px`;
    node.style.height = `${frameHeight * scale}px`;
  }

  const frameInfo = { media, columns, rows, frameWidth, frameHeight, filling, size };
  showFrame(node, frameInfo, 0);
  if (media.animation?.frames?.length) {
    // Re-renders keep a picture's clock, so the animation runs on instead of restarting.
    const key = `${media.url}|${label}`;
    if (!started.has(key)) {
      started.set(key, performance.now());
    }

    animated.set(node, { frameInfo, start: started.get(key) });
    startAnimating();
  }

  return [node];
}

function showFrame(node, frameInfo, frame) {
  const { media, columns, rows, frameWidth, frameHeight, filling, size } = frameInfo;
  const image = node.firstElementChild;
  if (!image) {
    return;
  }

  const column = Math.max(0, Math.min(columns - 1, frame % columns));
  const line = Math.max(0, Math.min(rows - 1, Math.floor(frame / columns)));
  const x = column * frameWidth;
  const y = line * frameHeight;
  const linear = media.sampling === 'linear';
  // A half source pixel keeps an interpolated frame away from its neighbour.
  // Core rejects the only unsafe case (a one-pixel cropped axis), so these
  // interiors remain positive and the DOM follows the Engine UV policy.
  const insetX = linear && (x > 0 || frameWidth < media.width) && frameWidth > 1 ? 0.5 : 0;
  const insetY = linear && (y > 0 || frameHeight < media.height) && frameHeight > 1 ? 0.5 : 0;
  const interiorWidth = frameWidth - insetX * 2;
  const interiorHeight = frameHeight - insetY * 2;
  if (filling) {
    image.style.width = `${media.width / interiorWidth * 100}%`;
    image.style.height = `${media.height / interiorHeight * 100}%`;
    image.style.left = `${-(x + insetX) / interiorWidth * 100}%`;
    image.style.top = `${-(y + insetY) / interiorHeight * 100}%`;
  } else {
    const scale = size / Math.max(frameWidth, frameHeight);
    const scaleX = frameWidth * scale / interiorWidth;
    const scaleY = frameHeight * scale / interiorHeight;
    image.style.width = `${media.width * scaleX}px`;
    image.style.height = `${media.height * scaleY}px`;
    image.style.left = `${-(x + insetX) * scaleX}px`;
    image.style.top = `${-(y + insetY) * scaleY}px`;
  }
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
    for (const [node, { frameInfo, start }] of animated) {
      if (!node.isConnected) {
        animated.delete(node);
        continue;
      }

      const { media } = frameInfo;
      const { frames, fps, loop } = media.animation;
      const played = Math.floor(((now - start) / 1000) * fps);
      showFrame(node, frameInfo, frames[loop ? played % frames.length : Math.min(played, frames.length - 1)]);
    }

    if (animated.size > 0) {
      requestAnimationFrame(step);
    } else {
      ticking = false;
    }
  };
  requestAnimationFrame(step);
}
