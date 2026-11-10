// The trip page's Music section: the trip's tracks in play order (reorder, remove), the music library to add
// from (search, sort, filter by tag, preview), and a table of how long the slideshow runs at each speed
// against the music. Every change is saved straight away.

import { MusicApi, TRACKS_PER_TRIP, type LibraryTrack, type MediaInfo } from '../api/albums-api.js';
import { MAX_DURATION } from '../slideshow-config.js';
import { query } from '../utils/dom.js';
import { errorMessage, plural } from '../utils/format.js';

/** "3:07", or "1:02:07" from an hour. */
export function formatLength(seconds: number): string {
  const s = Math.round(seconds);
  const h = Math.floor(s / 3600), m = Math.floor((s % 3600) / 60), r = String(s % 60).padStart(2, '0');
  return h ? `${h}:${String(m).padStart(2, '0')}:${r}` : `${m}:${r}`;
}

type Sort = 'title' | 'artist' | 'shortest' | 'longest' | 'newest';
const SORTS: Record<Sort, (a: LibraryTrack, b: LibraryTrack) => number> = {
  title: (a, b) => a.title.localeCompare(b.title),
  artist: (a, b) => a.artist.localeCompare(b.artist) || a.title.localeCompare(b.title),
  shortest: (a, b) => a.durationSeconds - b.durationSeconds,
  longest: (a, b) => b.durationSeconds - a.durationSeconds,
  newest: (a, b) => b.id.localeCompare(a.id), // ids are time-ordered (UUID v7)
};

function button(text: string, className: string, label: string, onclick: () => void): HTMLButtonElement {
  const b = document.createElement('button');
  b.type = 'button';
  b.className = className;
  b.textContent = text;
  b.setAttribute('aria-label', label);
  b.onclick = onclick;
  return b;
}

export function mountMusicPicker(root: HTMLElement, albumId: string) {
  const chosenList = query<HTMLOListElement>(root, '.music__chosen');
  const errorEl = query(root, '.music__error');
  const tableBody = query(root, '.music__table tbody');
  const search = query<HTMLInputElement>(root, '.music__search');
  const sortSelect = query<HTMLSelectElement>(root, '.music__sort-select');
  const tagBar = query(root, '.music__tags');

  let library: LibraryTrack[] = [];
  let chosen: LibraryTrack[] = [];
  let slides: MediaInfo[] = [];
  const tags = new Set<string>(); // tags to show only (none: everything)
  const preview = new Audio();
  let previewing: string | null = null;

  const showError = (err: unknown) => { errorEl.textContent = errorMessage(err); errorEl.hidden = false; };

  // ───────── Preview: one track at a time ─────────

  function togglePreview(t: LibraryTrack): void {
    if (previewing === t.id) { preview.pause(); previewing = null; }
    else {
      preview.src = MusicApi.address(t.url);
      void preview.play().catch(() => { previewing = null; renderLibrary(); });
      previewing = t.id;
    }
    renderLibrary();
  }
  preview.onended = () => { previewing = null; renderLibrary(); };

  // ───────── Saving ─────────

  async function save(next: LibraryTrack[]): Promise<void> {
    errorEl.hidden = true;
    const before = chosen;
    chosen = next; // show it at once; put it back if the save fails
    renderAll();
    try { chosen = await MusicApi.setForTrip(albumId, chosen.map(t => t.id)); }
    catch (err) { chosen = before; showError(err); }
    renderAll();
  }

  const move = (i: number, by: number) => {
    const next = [...chosen];
    [next[i], next[i + by]] = [next[i + by], next[i]];
    void save(next);
  };

  // ───────── The trip's music ─────────

  function renderChosen(): void {
    chosenList.replaceChildren(...chosen.map((t, i) => {
      const li = document.createElement('li');
      li.className = 'music__chosen-item';
      const text = document.createElement('span');
      text.className = 'music__chosen-text';
      const title = document.createElement('strong');
      title.textContent = t.title;
      text.append(title, ` · ${t.artist}`);
      const length = document.createElement('span');
      length.className = 'music__length';
      length.textContent = formatLength(t.durationSeconds);
      const up = button('↑', 'button button--quiet music__move', `Move ${t.title} up`, () => move(i, -1));
      const down = button('↓', 'button button--quiet music__move', `Move ${t.title} down`, () => move(i, 1));
      up.disabled = i === 0;
      down.disabled = i === chosen.length - 1;
      const remove = button('Remove', 'button button--quiet music__remove', `Remove ${t.title}`, () => void save(chosen.filter(c => c.id !== t.id)));
      li.append(text, length, up, down, remove);
      return li;
    }));
    query(root, '.music__none').hidden = chosen.length > 0;
    const total = chosen.reduce((s, t) => s + t.durationSeconds, 0);
    query(root, '.music__total').textContent = chosen.length ? `(${plural(chosen.length, 'track')}, ${formatLength(total)})` : '';
  }

  // ───────── Will the music last? ─────────

  function renderFit(): void {
    const photos = slides.filter(m => m.playable && m.kind === 'image').length;
    const videos = slides.filter(m => m.playable && m.kind === 'video');
    const videoSeconds = videos.reduce((s, v) => s + (v.durationSeconds ?? 0), 0);
    const untimed = videos.filter(v => !v.durationSeconds).length;
    const music = chosen.reduce((s, t) => s + t.durationSeconds, 0);
    const summary = query(root, '.music__fit-summary');
    const table = query<HTMLTableElement>(root, '.music__fit-table');
    const note = query(root, '.music__fit-note');

    if (photos + videos.length === 0) {
      summary.textContent = 'Add photos or videos to see how long the slideshow runs.';
      table.hidden = true;
      note.textContent = '';
      return;
    }
    // Each photo shows for its seconds (the cross-fade overlaps them); videos play to the end.
    const length = (perPhoto: number) => photos * perPhoto + videoSeconds;
    const speeds = Array.from({ length: MAX_DURATION }, (_, i) => i + 1);
    const lasting = speeds.filter(s => music >= length(s));
    const closest = speeds.reduce((best, s) => Math.abs(length(s) - music) < Math.abs(length(best) - music) ? s : best, speeds[0]);

    query(root, '.music__fit-music').textContent = music ? `(${formatLength(music)})` : '';
    table.tBodies[0].replaceChildren(...speeds.map(s => {
      const tr = document.createElement('tr');
      if (music && s === closest) tr.className = 'music__fit-best';
      const fits = music >= length(s);
      const verdict = !music ? '—' : fits ? '✓ Lasts the whole show' : `Starts again after ${formatLength(music)}`;
      for (const text of [`${s} s`, formatLength(length(s)), verdict]) {
        const td = document.createElement('td');
        td.textContent = text;
        tr.append(td);
      }
      if (music && !fits) tr.lastElementChild!.className = 'music__fit-repeats';
      return tr;
    }));
    table.hidden = false;

    summary.textContent = !music
      ? 'Add music to see how it fits. The slideshow lengths below are for each speed.'
      : lasting.length === speeds.length ? `Your music lasts the whole slideshow at every speed. It fits best at ${closest} s per photo.`
      : lasting.length ? `Your music lasts the whole slideshow at up to ${Math.max(...lasting)} s per photo, and fits best at ${closest} s.`
      : 'The slideshow is longer than your music at every speed, so the music starts again from the first track. Add tracks to cover it.';
    note.textContent = [
      `${plural(photos, 'photo')}${videos.length ? ` and ${plural(videos.length, 'video')}` : ''}.`,
      untimed ? `${untimed === 1 ? "One video's" : `${untimed} videos'`} length isn't known, so the slideshow may run longer.` : '',
      'Viewers can change the speed while they watch.',
    ].filter(Boolean).join(' ');
  }

  // ───────── The library ─────────

  function renderTags(): void {
    const all = [...new Set(library.flatMap(t => t.tags))].sort();
    tagBar.replaceChildren(...all.map(tag => {
      const b = button(tag, 'music__tag', `Show only tracks tagged ${tag}`, () => {
        if (tags.has(tag)) tags.delete(tag); else tags.add(tag);
        renderTags();
        renderLibrary();
      });
      b.setAttribute('aria-pressed', String(tags.has(tag)));
      return b;
    }));
    tagBar.hidden = all.length === 0;
  }

  function renderLibrary(): void {
    const words = search.value.trim().toLowerCase().split(/\s+/).filter(Boolean);
    const shown = library
      .filter(t => [...tags].every(tag => t.tags.includes(tag)))
      .filter(t => words.every(w => `${t.title} ${t.artist}`.toLowerCase().includes(w)))
      .sort(SORTS[sortSelect.value as Sort] ?? SORTS.title);
    const picked = new Set(chosen.map(t => t.id));
    const full = chosen.length >= TRACKS_PER_TRIP;

    tableBody.replaceChildren(...shown.map(t => {
      const tr = document.createElement('tr');
      const cell = (content: string | Node, className = '') => {
        const td = document.createElement('td');
        td.append(content);
        if (className) td.className = className;
        return td;
      };
      const playing = previewing === t.id;
      const play = button(playing ? '❚❚' : '▶', 'button button--quiet music__preview', `${playing ? 'Stop' : 'Preview'} ${t.title}`, () => togglePreview(t));
      const tagText = document.createElement('span');
      tagText.className = 'music__row-tags';
      tagText.textContent = t.tags.join(', ');
      const add = button(picked.has(t.id) ? 'Added' : 'Add', 'button music__add', `Add ${t.title} to this trip's music`, () => void save([...chosen, t]));
      add.disabled = picked.has(t.id) || full;
      if (full && !picked.has(t.id)) add.title = `A trip can have at most ${TRACKS_PER_TRIP} tracks.`;
      const title = document.createElement('span');
      title.textContent = t.title;
      const byline = document.createElement('span');
      byline.className = 'music__byline'; // the artist, under the title, on phones (where the artist column goes)
      byline.textContent = t.artist;
      const titleCell = cell(title, 'music__col-title');
      titleCell.append(byline);
      tr.append(
        cell(play, 'music__col-play'),
        titleCell,
        cell(t.artist, 'music__col-artist'),
        cell(tagText, 'music__col-tags'),
        cell(formatLength(t.durationSeconds), 'music__col-length'),
        cell(add));
      return tr;
    }));
    query(root, '.music__table').hidden = library.length === 0;
    const empty = query(root, '.music__empty');
    empty.hidden = shown.length > 0 || library.length === 0;
  }

  function renderAll(): void { renderChosen(); renderFit(); renderLibrary(); }

  search.oninput = () => renderLibrary();
  sortSelect.onchange = () => renderLibrary();

  return {
    /** Loads the library and the trip's music. */
    async load(): Promise<void> {
      try {
        [library, chosen] = await Promise.all([MusicApi.library(), MusicApi.forTrip(albumId)]);
        renderTags();
        renderAll();
        if (library.length === 0) query(root, '.music__empty').hidden = true;
        query(root, '.music__loading').textContent = library.length ? '' : 'The music library is empty for now.';
        query(root, '.music__loading').hidden = library.length > 0;
      } catch (err) {
        query(root, '.music__loading').hidden = true;
        showError(err);
      }
    },
    /** The trip's photos and videos changed: the slideshow's length did too. */
    setSlides(media: MediaInfo[]): void { slides = media; renderFit(); },
  };
}
