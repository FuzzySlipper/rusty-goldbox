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

/** Fills a select with { id, name } choices, keeping the current choice when it is still offered. */
export function fill(select, choices) {
  const current = select.value;
  select.replaceChildren(...choices.map((choice) => element('option', { value: choice.id }, choice.name)));
  if (choices.some((choice) => choice.id === current)) {
    select.value = current;
  }
}

export const currencyName = (id) => id ? id.split(':').at(-1) : 'currency';

export const formatBalances = (balances) => {
  const entries = Object.entries(balances ?? {});
  return entries.length === 0 ? 'no money' : entries.map(([id, amount]) => `${amount} ${currencyName(id)}`).join(', ');
};

/** A track as text: "Hit points 6/6". */
export const trackText = (track) => `${track.name} ${track.current ?? '-'}${track.max === null || track.max === undefined ? '' : `/${track.max}`}`;

/**
 * Any picture the projection carries ({ url, width, height, frame, animation }),
 * drawn pixel-sharp: an image whole, a sheet's frame cropped from it, playing
 * its animation. Nothing without one. Every panel picture goes through here, so
 * a new kind of media is drawn in one place.
 *
 * A number size fits the picture in a size-pixel square. The size 'fill' makes
 * it as wide as its container, keeping its shape, so it scales with the panel.
 */
export function picture(media, label, size) {
  if (!media?.url) {
    return [];
  }

  const filling = size === 'fill';
  if (!media.frame) {
    if (filling) {
      return [element('img', { src: media.url, alt: label, class: 'gb-picture gb-fill' })];
    }

    // Within a size-pixel square, keeping the image's shape.
    const fit = media.width && media.height ? size / Math.max(media.width, media.height) : 1;
    const width = media.width ? Math.round(media.width * fit) : size;
    const height = media.height ? Math.round(media.height * fit) : size;
    return [element('img', { src: media.url, alt: label, width, height, class: 'gb-picture' })];
  }

  const [frameWidth, frameHeight] = media.frame;
  const columns = Math.max(1, Math.floor(media.width / frameWidth));
  const rows = Math.max(1, Math.floor(media.height / frameHeight));
  const node = element('div', { role: 'img', 'aria-label': label, class: 'gb-picture' });
  if (filling) {
    // Percentages scale the sheet with the box; the box keeps the frame's shape.
    node.classList.add('gb-fill');
    node.style.aspectRatio = `${frameWidth} / ${frameHeight}`;
    node.style.background = `url("${media.url}") 0 0 / ${columns * 100}% ${rows * 100}% no-repeat`;
  } else {
    const scale = size / Math.max(frameWidth, frameHeight);
    node.style.width = `${frameWidth * scale}px`;
    node.style.height = `${frameHeight * scale}px`;
    node.style.background = `url("${media.url}") 0 0 / ${media.width * scale}px ${media.height * scale}px no-repeat`;
  }

  showFrame(node, columns, rows, 0);
  if (media.animation?.frames?.length) {
    // Re-renders keep a picture's clock, so the animation runs on instead of restarting.
    const key = `${media.url}|${label}`;
    if (!started.has(key)) {
      started.set(key, performance.now());
    }

    animated.set(node, { media, columns, rows, start: started.get(key) });
    startAnimating();
  }

  return [node];
}

function showFrame(node, columns, rows, frame) {
  const column = frame % columns;
  const line = Math.floor(frame / columns);
  // Percent positions put 0% at the first frame and 100% at the last.
  const x = columns > 1 ? (column / (columns - 1)) * 100 : 0;
  const y = rows > 1 ? (line / (rows - 1)) * 100 : 0;
  node.style.backgroundPosition = `${x}% ${y}%`;
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
    for (const [node, { media, columns, rows, start }] of animated) {
      if (!node.isConnected) {
        animated.delete(node);
        continue;
      }

      const { frames, fps, loop } = media.animation;
      const played = Math.floor(((now - start) / 1000) * fps);
      showFrame(node, columns, rows, frames[loop ? played % frames.length : Math.min(played, frames.length - 1)]);
    }

    if (animated.size > 0) {
      requestAnimationFrame(step);
    } else {
      ticking = false;
    }
  };
  requestAnimationFrame(step);
}
