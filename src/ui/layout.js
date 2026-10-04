/**
 * Which arrangement the play screen uses, and the proportions it uses them
 * with. The projection's `layout` (the Game's defaults, the skin's, then the
 * player's) gives the proportions; the window's aspect ratio picks the
 * arrangement. The proportions become CSS variables on the UI root, so one
 * stylesheet serves every window shape.
 */

export function arrangementFor(width, height, layout) {
  const aspect = width / Math.max(1, height);
  if (layout && aspect >= layout.ultrawide_from) {
    return 'ultrawide';
  }

  return layout && aspect < layout.tall_below ? 'tall' : 'standard';
}

/** Sets the layout's proportions as the CSS variables the stylesheet reads. */
export function applyLayout(node, layout) {
  const log = Math.round(layout.log_share * 100);
  node.style.setProperty('--gb-main-rows', `minmax(0, ${100 - log}fr) minmax(0, ${log}fr)`);
  node.style.setProperty('--gb-side-width', String(layout.side_width));
  node.style.setProperty('--gb-text-scale', String(layout.text_scale));
  node.style.setProperty('--gb-control-scale', String(layout.control_scale));
  node.style.setProperty('--gb-portrait-scale', String(layout.portrait_scale));
}
