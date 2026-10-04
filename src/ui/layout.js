/**
 * Which arrangement the play screen uses, and the ratios it uses them with.
 * The window's aspect ratio picks the arrangement; the ratios are CSS
 * variables on the UI root, so one stylesheet serves every window shape.
 */

export const DEFAULT_LAYOUT = Object.freeze({
  logShare: 0.36,
  sideWidth: 32,
  textScale: 1,
  controlScale: 1,
  portraitScale: 1,
  ultrawideFrom: 2.1,
  tallBelow: 1.1,
});

export function arrangementFor(width, height, layout) {
  const aspect = width / Math.max(1, height);
  if (aspect >= layout.ultrawideFrom) {
    return 'ultrawide';
  }

  return aspect < layout.tallBelow ? 'tall' : 'standard';
}

/** Sets the layout's ratios as the CSS variables the stylesheet reads. */
export function applyLayout(node, layout) {
  const log = Math.round(layout.logShare * 100);
  node.style.setProperty('--gb-main-rows', `minmax(0, ${100 - log}fr) minmax(0, ${log}fr)`);
  node.style.setProperty('--gb-side-width', String(layout.sideWidth));
  node.style.setProperty('--gb-text-scale', String(layout.textScale));
  node.style.setProperty('--gb-control-scale', String(layout.controlScale));
  node.style.setProperty('--gb-portrait-scale', String(layout.portraitScale));
}
