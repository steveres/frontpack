// album.html?id=…: one album — upload files, see which will be shown, view them full size, select and
// delete them, rename the album, play, share, delete.

import { AlbumsApi, collectionFor, MediaApi, type AlbumInfo, type Collection, type MediaInfo } from '../api/albums-api.js';
import { requireSignIn } from '../auth/session.js';
import { createMediaViewer } from '../components/media-viewer.js';
import { mountNavigation } from '../components/navigation.js';
import { mountSharePanel } from '../components/share-panel.js';
import { thumbnail } from '../components/thumbnail.js';
import { query } from '../utils/dom.js';
import { albumSummary, errorMessage, formatBytes, formatTaken } from '../utils/format.js';

const UPLOAD_CONCURRENCY = 3;

await requireSignIn();
void mountNavigation();

const albumId = new URLSearchParams(location.search).get('id') ?? '';
const page = query(document, '.album');
const errorEl = query(page, '.album__error');
const showError = (err: unknown) => { errorEl.textContent = errorMessage(err); errorEl.hidden = false; };
const viewer = createMediaViewer();
let album: AlbumInfo | null = null;

// ───────── Selecting files ─────────

/** The checkboxes of one table, the "select all" box and the bar that acts on the selection. */
function selectionFor(collection: Collection) {
  const table = query(page, collection === 'images' ? '.album__images-table' : '.album__music-table');
  const bar = query(page, collection === 'images' ? '.album__bulk--images' : '.album__bulk--music');
  const all = query<HTMLInputElement>(table, '.media-table__select-all');
  const count = query(bar, '.bulk__count');
  const deleteButton = query<HTMLButtonElement>(bar, '.bulk__delete');
  const selected = new Set<string>();
  let boxes: HTMLInputElement[] = [];
  let anchor = -1; // last box clicked, for shift-click ranges
  const noun = collection === 'images' ? ['photo or video', 'photos and videos'] : ['track', 'tracks'];

  function refresh(): void {
    for (const box of boxes) {
      box.checked = selected.has(box.value);
      box.closest('tr')!.classList.toggle('media-table__row--selected', box.checked);
    }
    all.checked = boxes.length > 0 && selected.size === boxes.length;
    all.indeterminate = selected.size > 0 && selected.size < boxes.length;
    bar.hidden = selected.size === 0;
    count.textContent = `${selected.size} ${selected.size === 1 ? noun[0] : noun[1]} selected`;
  }

  all.onchange = () => {
    if (all.checked) boxes.forEach(b => selected.add(b.value)); else selected.clear();
    refresh();
  };
  query<HTMLButtonElement>(bar, '.bulk__clear').onclick = () => { selected.clear(); refresh(); };
  deleteButton.onclick = async () => {
    const n = selected.size;
    if (!confirm(`Delete ${n === 1 ? `1 ${noun[0]}` : `${n} ${noun[1]}`} from this album? This can't be undone.`)) return;
    deleteButton.disabled = true;
    deleteButton.textContent = 'Deleting…';
    try {
      await MediaApi.removeMany(albumId, collection, [...selected]);
      selected.clear();
      await load();
    } catch (err) {
      showError(err);
    } finally {
      deleteButton.disabled = false;
      deleteButton.textContent = 'Delete selected…';
    }
  };

  return {
    /** A checkbox for a row; clicking with Shift selects (or clears) everything from the last one clicked. */
    checkbox(m: MediaInfo): HTMLTableCellElement {
      const box = document.createElement('input');
      box.type = 'checkbox';
      box.value = m.id;
      box.setAttribute('aria-label', `Select ${m.fileName}`);
      box.addEventListener('click', e => {
        const i = boxes.indexOf(box);
        if (e.shiftKey && anchor >= 0) {
          const [from, to] = i < anchor ? [i, anchor] : [anchor, i];
          for (const b of boxes.slice(from, to + 1)) box.checked ? selected.add(b.value) : selected.delete(b.value);
        } else if (box.checked) selected.add(m.id);
        else selected.delete(m.id);
        anchor = i;
        refresh();
      });
      const td = document.createElement('td');
      td.className = 'media-table__check';
      td.append(box);
      return td;
    },
    /** After the rows are (re)built: forget files that are gone, show the rest as selected. */
    attach(): void {
      boxes = Array.from(table.querySelectorAll<HTMLInputElement>('tbody input[type="checkbox"]'));
      const present = new Set(boxes.map(b => b.value));
      for (const id of selected) if (!present.has(id)) selected.delete(id);
      anchor = -1;
      refresh();
    },
  };
}

const selections = { images: selectionFor('images'), music: selectionFor('music') };

// ───────── Rendering ─────────

/** A <td> with text (never HTML: file names come from users). */
function cell(text: string, className?: string): HTMLTableCellElement {
  const td = document.createElement('td');
  td.textContent = text;
  if (className) td.className = className;
  return td;
}

function deleteCell(m: MediaInfo, collection: Collection): HTMLTableCellElement {
  const button = document.createElement('button');
  button.type = 'button';
  button.className = 'button button--quiet media-table__delete';
  button.textContent = 'Delete';
  button.setAttribute('aria-label', `Delete ${m.fileName}`);
  button.onclick = async () => {
    if (!confirm(`Delete "${m.fileName}" from this album?`)) return;
    button.disabled = true;
    try { await MediaApi.remove(albumId, collection, m.id); await load(); }
    catch (err) { showError(err); button.disabled = false; }
  };
  const td = document.createElement('td');
  td.append(button);
  return td;
}

const missingText = (missing: MediaInfo['missing']) =>
  missing.length === 2 ? 'No date or location' : missing[0] === 'date' ? 'No capture date' : 'No location';

/** A row of the photos-and-videos table; its preview and name open the viewer at this file. */
function imageRow(m: MediaInfo, list: MediaInfo[], index: number): HTMLTableRowElement {
  const tr = document.createElement('tr');
  if (!m.playable) tr.className = 'media-table__row--excluded';
  const open = () => viewer.open(list, index);

  const viewButton = document.createElement('button');
  viewButton.type = 'button';
  viewButton.className = 'media-table__view';
  viewButton.setAttribute('aria-label', `View ${m.fileName}`);
  viewButton.append(thumbnail(m.thumbnailUrl, m.kind, 'thumb--row'));
  viewButton.onclick = open;
  const preview = document.createElement('td');
  preview.className = 'media-table__preview';
  preview.append(viewButton);

  const nameButton = document.createElement('button');
  nameButton.type = 'button';
  nameButton.className = 'media-table__name-button';
  nameButton.textContent = `${m.fileName}${m.kind === 'video' ? ' (video)' : ''}`;
  nameButton.onclick = open;
  const name = document.createElement('td');
  name.className = 'media-table__name';
  name.append(nameButton);

  const status = document.createElement('td');
  const badge = document.createElement('span');
  badge.className = m.playable ? 'badge badge--ok' : 'badge badge--warning';
  badge.textContent = m.playable ? 'Shown' : `Not shown: ${missingText(m.missing)}`;
  status.append(badge);

  tr.append(
    selections.images.checkbox(m),
    preview,
    name,
    cell(m.taken ? formatTaken(m.taken) : '—', m.taken ? '' : 'media-table__missing'),
    cell(m.location ? `${m.location.lat.toFixed(4)}, ${m.location.lon.toFixed(4)}` : '—', m.location ? '' : 'media-table__missing'),
    status,
    cell(formatBytes(m.sizeBytes), 'media-table__size'),
    deleteCell(m, 'images'));
  return tr;
}

function musicRow(m: MediaInfo): HTMLTableRowElement {
  const tr = document.createElement('tr');
  tr.append(selections.music.checkbox(m), cell(m.fileName, 'media-table__name'), cell(formatBytes(m.sizeBytes), 'media-table__size'), deleteCell(m, 'music'));
  return tr;
}

/** Slideshow order first (by capture time), then the ones that won't be shown. */
function slideshowOrder(a: MediaInfo, b: MediaInfo): number {
  if (a.playable !== b.playable) return a.playable ? -1 : 1;
  return (a.taken ?? '').localeCompare(b.taken ?? '') || a.fileName.localeCompare(b.fileName, undefined, { numeric: true });
}

function renderAlbum(a: AlbumInfo): void {
  album = a;
  document.title = `${a.name} - Slideshow`;
  query(page, '.album__name').textContent = a.name;
  const description = query(page, '.album__description');
  description.textContent = a.description ?? '';
  description.hidden = !a.description;
  query(page, '.album__summary').textContent = albumSummary(a);

  const shown = a.imageCount + a.videoCount - a.excludedCount;
  const play = query<HTMLAnchorElement>(page, '.album__play');
  if (shown > 0) {
    play.href = `/slideshow.html?album=${encodeURIComponent(a.id)}`;
    play.removeAttribute('aria-disabled');
    play.title = '';
  } else {
    play.removeAttribute('href');
    play.setAttribute('aria-disabled', 'true');
    play.title = 'Add photos or videos that have a capture date and a location first.';
  }

  const excluded = query(page, '.album__excluded');
  excluded.hidden = a.excludedCount === 0;
  const n = a.excludedCount;
  excluded.textContent = n === 1
    ? "1 photo or video won't be shown in the slideshow because it is missing a capture date or a location (GPS). It's marked below."
    : `${n} photos or videos won't be shown in the slideshow because they are missing a capture date or a location (GPS). They're marked below.`;
}

function renderList(collection: Collection, media: MediaInfo[]): void {
  const images = collection === 'images';
  const body = query(page, images ? '.album__images' : '.album__music');
  if (images) {
    const sorted = [...media].sort(slideshowOrder);
    body.replaceChildren(...sorted.map((m, i) => imageRow(m, sorted, i)));
  } else {
    body.replaceChildren(...media.map(musicRow));
  }
  selections[collection].attach();
  query(page, images ? '.album__images-table' : '.album__music-table').hidden = media.length === 0;
  query(page, images ? '.album__none--images' : '.album__none--music').hidden = media.length > 0;
}

async function load(): Promise<void> {
  try {
    const [info, images, music] = await Promise.all([
      AlbumsApi.info(albumId), MediaApi.list(albumId, 'images'), MediaApi.list(albumId, 'music'),
    ]);
    renderAlbum(info);
    renderList('images', images);
    renderList('music', music);
    query(page, '.album__body').hidden = false;
  } catch (err) {
    showError(err);
  } finally {
    query(page, '.album__loading').hidden = true;
  }
}

// ───────── Edit name and description ─────────

const editForm = query<HTMLFormElement>(page, '.album__edit');
const editToggle = query<HTMLButtonElement>(page, '.album__edit-toggle');
const editName = query<HTMLInputElement>(editForm, '.album__edit-name');
const editDescription = query<HTMLTextAreaElement>(editForm, '.album__edit-description');
const editError = query(editForm, '.album__edit-error');

function showEdit(open: boolean): void {
  editForm.hidden = !open;
  editToggle.setAttribute('aria-expanded', String(open));
  editError.hidden = true;
  if (open && album) {
    editName.value = album.name;
    editDescription.value = album.description ?? '';
    editName.focus();
  }
}
editToggle.onclick = () => showEdit(editForm.hidden !== false);
query<HTMLButtonElement>(editForm, '.album__edit-cancel').onclick = () => showEdit(false);
editForm.addEventListener('keydown', e => { if (e.key === 'Escape') showEdit(false); });
editForm.onsubmit = async e => {
  e.preventDefault();
  const save = query<HTMLButtonElement>(editForm, 'button[type="submit"]');
  save.disabled = true;
  try {
    // An empty description removes it.
    renderAlbum(await AlbumsApi.update(albumId, { name: editName.value.trim(), description: editDescription.value.trim() }));
    showEdit(false);
  } catch (err) {
    editError.textContent = errorMessage(err);
    editError.hidden = false;
  } finally {
    save.disabled = false;
  }
};

// ───────── Upload ─────────

const queueEl = query<HTMLUListElement>(page, '.upload__queue');

/** One line in the upload list, with progress and the outcome. */
function queueItem(file: File) {
  const li = document.createElement('li');
  li.className = 'upload__item';
  const name = document.createElement('span');
  name.className = 'upload__name';
  name.textContent = file.name;
  const progress = document.createElement('progress');
  progress.max = 1;
  progress.value = 0;
  const status = document.createElement('span');
  status.className = 'upload__status';
  status.textContent = 'Waiting…';
  li.append(name, progress, status);
  queueEl.prepend(li);
  return {
    progress: (fraction: number) => { progress.value = fraction; status.textContent = `${Math.round(fraction * 100)}%`; },
    done(m: MediaInfo) {
      progress.remove();
      li.classList.add(m.playable ? 'upload__item--ok' : 'upload__item--warning');
      status.textContent = m.playable ? 'Added' : `Added, but not shown: ${missingText(m.missing).toLowerCase()}`;
      if (m.playable) setTimeout(() => li.remove(), 4000); // keep the ones that need attention
    },
    failed(err: unknown) {
      progress.remove();
      li.classList.add('upload__item--error');
      status.textContent = errorMessage(err);
    },
  };
}

async function uploadAll(files: File[]): Promise<void> {
  if (!files.length) return;
  errorEl.hidden = true;
  const pending = files.map(file => ({ file, ui: queueItem(file) }));
  const worker = async () => {
    for (let next = pending.shift(); next; next = pending.shift()) {
      try { next.ui.done(await MediaApi.upload(albumId, collectionFor(next.file), next.file, next.ui.progress)); }
      catch (err) { next.ui.failed(err); }
    }
  };
  await Promise.all(Array.from({ length: Math.min(UPLOAD_CONCURRENCY, files.length) }, worker));
  await load();
}

const drop = query(page, '.upload__drop');
const input = query<HTMLInputElement>(page, '.upload__input');
input.onchange = () => { void uploadAll(Array.from(input.files ?? [])); input.value = ''; };
drop.addEventListener('dragover', e => { e.preventDefault(); drop.classList.add('upload__drop--over'); });
drop.addEventListener('dragleave', () => drop.classList.remove('upload__drop--over'));
drop.addEventListener('drop', e => {
  e.preventDefault();
  drop.classList.remove('upload__drop--over');
  void uploadAll(Array.from(e.dataTransfer?.files ?? []));
});

// ───────── Share ─────────

if (albumId) mountSharePanel(query(page, '.share'), query<HTMLButtonElement>(page, '.album__share-toggle'), albumId,
  () => query(page, '.album__name').textContent ?? 'Slideshow');

// ───────── Delete album ─────────

query<HTMLButtonElement>(page, '.album__delete').onclick = async () => {
  const name = query(page, '.album__name').textContent;
  if (!confirm(`Delete the album "${name}" and all its photos, videos and music? This can't be undone.`)) return;
  try { await AlbumsApi.remove(albumId); location.assign('/albums.html'); }
  catch (err) { showError(err); }
};

if (!albumId) location.replace('/albums.html');
else await load();

