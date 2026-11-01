// Full-screen view of an album's photos and videos: opens on the one clicked, steps through the rest
// with the arrow keys, the on-screen buttons or a swipe, and closes with Esc. A photo is shown whole,
// as large as the window allows, over a slowly drifting blurred copy of itself. Files need the access
// token, so each is downloaded and shown from memory; the last few are kept for stepping back.

import { MediaApi, type MediaInfo } from '../api/albums-api.js';
import { errorMessage, formatBytes, formatTaken } from '../utils/format.js';

const KEEP = 4; // files kept in memory

export interface MediaViewer {
  /** Shows items[index]; the arrows move through `items`. */
  open(items: MediaInfo[], index: number): void;
}

export function createMediaViewer(): MediaViewer {
  const dialog = document.createElement('dialog');
  dialog.className = 'viewer';
  dialog.setAttribute('aria-label', 'Photo viewer');
  dialog.innerHTML = `
    <div class="viewer__stage"><p class="viewer__status" role="status"></p></div>
    <button type="button" class="viewer__nav viewer__prev" aria-label="Previous">‹</button>
    <button type="button" class="viewer__nav viewer__next" aria-label="Next">›</button>
    <button type="button" class="viewer__close" aria-label="Close">✕</button>
    <p class="viewer__caption"><span class="viewer__name"></span><span class="viewer__meta"></span></p>`;
  document.body.append(dialog);

  const stage = dialog.querySelector<HTMLElement>('.viewer__stage')!;
  const status = dialog.querySelector<HTMLElement>('.viewer__status')!;
  const prev = dialog.querySelector<HTMLButtonElement>('.viewer__prev')!;
  const next = dialog.querySelector<HTMLButtonElement>('.viewer__next')!;
  const nameEl = dialog.querySelector<HTMLElement>('.viewer__name')!;
  const metaEl = dialog.querySelector<HTMLElement>('.viewer__meta')!;

  const cache = new Map<string, Promise<string>>(); // file URL -> object URL, oldest first
  let items: MediaInfo[] = [];
  let index = 0;
  let showing = 0; // bumping this abandons a file still loading

  function fileUrl(m: MediaInfo): Promise<string> {
    let p = cache.get(m.url);
    if (p) cache.delete(m.url); // re-added below as the newest
    else {
      p = MediaApi.download(m.url).then(blob => URL.createObjectURL(blob));
      p.catch(() => cache.delete(m.url));
    }
    cache.set(m.url, p);
    while (cache.size > KEEP) {
      const [oldest, url] = cache.entries().next().value!;
      cache.delete(oldest);
      void url.then(u => URL.revokeObjectURL(u), () => undefined);
    }
    return p;
  }

  async function show(i: number): Promise<void> {
    index = i;
    const m = items[i];
    const mine = ++showing;
    prev.hidden = i === 0;
    next.hidden = i === items.length - 1;
    nameEl.textContent = m.fileName;
    const where = m.location ? `${m.location.lat.toFixed(4)}, ${m.location.lon.toFixed(4)}` : 'no location';
    metaEl.textContent = ` · ${m.taken ? formatTaken(m.taken) : 'no capture date'} · ${where} · ${formatBytes(m.sizeBytes)} · ${i + 1} of ${items.length}`;
    clearStage();
    status.textContent = 'Loading…';
    status.hidden = false;

    try {
      const url = await fileUrl(m);
      if (mine !== showing) return;
      let media: HTMLImageElement | HTMLVideoElement;
      if (m.kind === 'video') {
        const v = document.createElement('video');
        v.controls = true;
        v.autoplay = true;
        v.playsInline = true;
        v.src = url;
        media = v;
      } else {
        const img = new Image();
        img.alt = m.fileName;
        img.src = url;
        await img.decode().catch(() => undefined);
        if (mine !== showing) return;
        media = img;
      }
      media.className = 'viewer__media';
      if (m.kind !== 'video') {
        // The rest of the window: the same photo, blurred and darkened, drifting slowly.
        const backdrop = new Image();
        backdrop.alt = '';
        backdrop.className = 'viewer__backdrop';
        backdrop.src = url;
        stage.append(backdrop);
      }
      stage.append(media);
      status.hidden = true;
      // Get the next one ready for stepping forward.
      if (items[i + 1]?.kind === 'image') void fileUrl(items[i + 1]).catch(() => undefined);
    } catch (err) {
      if (mine === showing) status.textContent = `Couldn't load this file: ${errorMessage(err)}`;
    }
  }

  const step = (delta: number) => {
    const i = index + delta;
    if (i >= 0 && i < items.length) void show(i);
  };
  prev.onclick = () => step(-1);
  next.onclick = () => step(1);
  dialog.querySelector<HTMLButtonElement>('.viewer__close')!.onclick = () => dialog.close();
  // On the document, not the dialog: the keys work wherever the focus is while it's open.
  document.addEventListener('keydown', e => {
    if (!dialog.open || (e.target as Element).closest?.('video')) return;
    if (e.key === 'ArrowLeft') { e.preventDefault(); step(-1); }
    else if (e.key === 'ArrowRight') { e.preventDefault(); step(1); }
  });
  function clearStage(): void {
    stage.querySelector('video')?.pause();
    stage.querySelectorAll('.viewer__media, .viewer__backdrop').forEach(el => el.remove());
  }

  // Clicking around the photo (not on it) closes the viewer, like most photo viewers. The photo's
  // element fills the window, so work out where the picture itself is drawn inside it.
  stage.addEventListener('click', e => {
    const img = stage.querySelector<HTMLImageElement>('img.viewer__media');
    if (!img) { if (e.target === stage) dialog.close(); return; }
    if ((e.target as Element).closest('video')) return;
    const box = img.getBoundingClientRect();
    const scale = Math.min(box.width / img.naturalWidth, box.height / img.naturalHeight);
    const w = img.naturalWidth * scale, h = img.naturalHeight * scale;
    const left = box.left + (box.width - w) / 2, top = box.top + (box.height - h) / 2;
    if (e.clientX < left || e.clientX > left + w || e.clientY < top || e.clientY > top + h) dialog.close();
  });
  // (The close event comes a moment after closing; if it was opened again meanwhile, leave it be.)
  dialog.addEventListener('close', () => {
    if (dialog.open) return;
    showing++;
    clearStage();
  });

  // Swipe left/right on touch screens.
  let touchX: number | null = null;
  dialog.addEventListener('touchstart', e => { touchX = e.touches.length === 1 ? e.touches[0].clientX : null; }, { passive: true });
  dialog.addEventListener('touchend', e => {
    if (touchX === null || (e.target as Element).closest('video')) return;
    const dx = e.changedTouches[0].clientX - touchX;
    if (Math.abs(dx) > 50) step(dx < 0 ? 1 : -1);
    touchX = null;
  });

  return {
    open(list, i) {
      items = list;
      if (!dialog.open) dialog.showModal();
      void show(i);
    },
  };
}
