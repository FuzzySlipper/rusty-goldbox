/**
 * DOM-only debug readout. Engine owns the canvas, input delivery and
 * projection transport; this module shows the product's status projection.
 */
export function mountProductUi(root, context) {
  const panel = document.createElement('aside');
  panel.setAttribute('aria-label', 'Rusty Goldbox status');

  const title = document.createElement('h1');
  title.textContent = 'Rusty Goldbox';
  panel.append(title);

  const status = document.createElement('output');
  status.id = 'rusty-goldbox-status';
  status.setAttribute('aria-live', 'polite');
  panel.append(status);
  root.append(panel);

  let unsubscribe;
  const projection = context?.projection;
  if (projection?.subscribe !== undefined) {
    unsubscribe = projection.subscribe((envelope) => {
      const nextStatus = envelope?.value?.status;
      if (typeof nextStatus === 'string') {
        status.textContent = nextStatus;
      }
    });
  }

  return Object.freeze({
    dispose: () => {
      unsubscribe?.();
      panel.remove();
    },
  });
}
