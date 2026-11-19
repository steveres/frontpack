// Small DOM and math helpers shared by the components.

/** The element matching `selector` inside `root`; throws if the markup is missing it. */
export function query<T extends Element = HTMLElement>(root: ParentNode, selector: string): T {
  const el = root.querySelector<T>(selector);
  if (!el) throw new Error(`Missing element ${selector}`);
  return el;
}

/**
 * Shows that `button`'s action is under way: disabled, with a spinner and `text` (e.g. "Signing out…").
 * Returns a function that puts the button back, for when the action fails.
 */
export function showBusy(button: HTMLButtonElement, text: string): () => void {
  const before = [...button.childNodes];
  const spinner = Object.assign(document.createElement('span'), { className: 'spinner' });
  spinner.setAttribute('aria-hidden', 'true');
  button.replaceChildren(spinner, ` ${text}`);
  button.disabled = true;
  button.setAttribute('aria-busy', 'true');
  return () => {
    button.replaceChildren(...before);
    button.disabled = false;
    button.removeAttribute('aria-busy');
  };
}

export const sleep = (ms: number) => new Promise<void>(r => setTimeout(r, ms));
export const clamp = (v: number, lo: number, hi: number) => Math.min(hi, Math.max(lo, v));
export const lerp = (a: number, b: number, t: number) => a + (b - a) * t;
export const ease = (t: number) => t * t * (3 - 2 * t);
