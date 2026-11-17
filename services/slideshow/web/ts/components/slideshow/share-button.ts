// The player's Share button (top right, next to the gear): hands out the trip's share link
// (https://s.thefrontpack.com/<code>, which apps preview with the trip's name and cover) rather than the
// player's own address, which the address bar shows and which previews as just "Slideshow".
// Phones and tablets: the device's share sheet (Messages, Mail, …). Elsewhere: copies the link.

import { shareLink } from '../../api/albums-api.js';
import { query } from '../../utils/dom.js';

const NOTE_MS = 2500;

export function showShareButton(root: HTMLElement, token: string, tripName: string): void {
  const button = query<HTMLButtonElement>(root, '.slideshow__share');
  const note = query(root, '.slideshow__share-note');
  const url = shareLink(token);
  let noteTimer: number | undefined;

  const say = (text: string) => {
    note.textContent = text;
    note.hidden = false;
    clearTimeout(noteTimer);
    noteTimer = window.setTimeout(() => { note.hidden = true; }, NOTE_MS);
  };

  const copy = async () => {
    try {
      await navigator.clipboard.writeText(url);
      say('Link copied');
    } catch {
      window.prompt('Copy this link to share the slideshow:', url); // no clipboard access
    }
  };

  // Desktop browsers may offer a share sheet too, but copying the link is what people expect there.
  const useSheet = typeof navigator.share === 'function' && matchMedia('(pointer: coarse)').matches;

  button.onclick = async () => {
    if (!useSheet) { await copy(); return; }
    try {
      await navigator.share({ title: tripName, text: `Watch "${tripName}"`, url });
    } catch (err) {
      if (!(err instanceof DOMException && err.name === 'AbortError')) await copy(); // closed without sharing: nothing to do
    }
  };
  button.hidden = false;
}
