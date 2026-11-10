// music-admin.html: the music library, for music admins (the API's Music.Admin app role). Add a track (its MP3
// and license certificate; the form fills itself in from a Pixabay certificate), change or delete one, preview
// it, and download a backup of the catalog. The API checks the role on every call; this page only hides itself.

import { MusicAdminApi, MusicApi, type AdminTrack } from '../api/albums-api.js';
import { requireSignIn } from '../auth/session.js';
import { formatLength } from '../components/music-picker.js';
import { mountNavigation } from '../components/navigation.js';
import { query } from '../utils/dom.js';
import { errorMessage, formatBytes } from '../utils/format.js';

await requireSignIn();
void mountNavigation();

const page = query(document, '.admin');
const errorEl = query(page, '.admin__error');
const showError = (err: unknown) => { errorEl.textContent = errorMessage(err); errorEl.hidden = false; };
const form = query<HTMLFormElement>(page, '.admin__form');
const formError = query(form, '.admin__form-error');
const mp3Input = query<HTMLInputElement>(form, '.admin__mp3');
const licenseInput = query<HTMLInputElement>(form, '.admin__license');
const progress = query<HTMLProgressElement>(form, '.admin__progress');
const field = (name: string) => query<HTMLInputElement>(form, `[name="${name}"]`);

let tracks: AdminTrack[] = [];
let editing: AdminTrack | null = null; // null: adding
let mp3Seconds: number | null = null;  // the chosen MP3's length, read in the browser
const preview = new Audio();
let previewing: string | null = null;

// ───────── Reading the chosen files ─────────

/** The MP3's length, from the browser's own reading of it. */
function readLength(file: File): Promise<number> {
  return new Promise((resolve, reject) => {
    const audio = new Audio();
    const url = URL.createObjectURL(file);
    audio.preload = 'metadata';
    audio.onloadedmetadata = () => { URL.revokeObjectURL(url); Number.isFinite(audio.duration) ? resolve(audio.duration) : reject(new Error("Couldn't read the MP3's length.")); };
    audio.onerror = () => { URL.revokeObjectURL(url); reject(new Error("This file doesn't play as an MP3.")); };
    audio.src = url;
  });
}

/**
 * The details a Pixabay license certificate gives: lines like "Audio File Title:" followed by the value on the
 * next line. Other certificates fill in nothing; the fields are then typed in.
 */
export function readCertificate(text: string): { title?: string; artist?: string; artistUrl?: string; sourceUrl?: string; licenseName?: string } {
  const lines = text.split(/\r?\n/).map(l => l.trim());
  const after = (label: string) => {
    const i = lines.findIndex(l => l.toLowerCase().startsWith(label.toLowerCase()));
    if (i < 0) return undefined;
    const rest = lines[i].slice(label.length).trim();
    return rest || lines.slice(i + 1).find(l => l.length > 0);
  };
  const artistUrl = after("Licensor's Username:");
  // ".../users/nastelbom-48128234/" → "nastelbom"
  const artist = artistUrl?.match(/\/users\/([^/]+?)(?:-\d+)?\/?$/)?.[1];
  return {
    title: after('Audio File Title:'),
    artist,
    artistUrl: artistUrl?.startsWith('http') ? artistUrl : undefined,
    sourceUrl: after('Audio File URL:'),
    licenseName: /PIXABAY LICENSE CERTIFICATE/i.test(text) ? 'Pixabay Content License' : undefined,
  };
}

mp3Input.onchange = async () => {
  const file = mp3Input.files?.[0];
  const info = query(form, '.admin__mp3-info');
  mp3Seconds = null;
  info.textContent = '';
  if (!file) return;
  try {
    mp3Seconds = await readLength(file);
    info.textContent = `${formatLength(mp3Seconds)} · ${formatBytes(file.size)}`;
    // A Pixabay download is named "artist-title-id.mp3": a start on the title when there's no certificate yet.
    if (!field('title').value) field('title').value = file.name.replace(/\.mp3$/i, '').replace(/-\d+$/, '').replace(/[-_]+/g, ' ');
  } catch (err) {
    info.textContent = errorMessage(err);
  }
};

licenseInput.onchange = async () => {
  const file = licenseInput.files?.[0];
  const info = query(form, '.admin__license-info');
  info.textContent = '';
  if (!file) return;
  const found = readCertificate(await file.text());
  // The certificate is the reliable source: it replaces a title guessed from the file name.
  const guessedTitle = editing === null && mp3Input.files?.[0] && field('title').value === mp3Input.files[0].name.replace(/\.mp3$/i, '').replace(/-\d+$/, '').replace(/[-_]+/g, ' ');
  let filled = 0;
  for (const [name, value] of Object.entries(found)) {
    if (!value) continue;
    const input = field(name);
    if (!input.value || (name === 'title' && guessedTitle) || (name === 'licenseName' && input.value === input.defaultValue)) { input.value = value; filled++; }
  }
  info.textContent = filled ? `Filled in ${filled} field${filled === 1 ? '' : 's'} from the certificate. Check them.` : file.name;
};

// ───────── The form ─────────

function openForm(track: AdminTrack | null): void {
  editing = track;
  form.reset();
  mp3Seconds = null;
  query(form, '.admin__mp3-info').textContent = track ? `Now: ${formatLength(track.durationSeconds)} · ${formatBytes(track.sizeBytes)}. Choose a file only to replace it.` : '';
  query(form, '.admin__license-info').textContent = track ? `Now: ${track.licenseFileName}. Choose a file only to replace it.` : '';
  query(form, '.admin__form-title').textContent = track ? `Change "${track.title}"` : 'Add a track';
  if (track) {
    field('title').value = track.title;
    field('artist').value = track.artist;
    field('artistUrl').value = track.artistUrl ?? '';
    field('sourceUrl').value = track.sourceUrl ?? '';
    field('tags').value = track.tags.join(', ');
    field('licenseName').value = track.licenseName;
  }
  formError.hidden = true;
  form.hidden = false;
  form.scrollIntoView({ behavior: 'smooth', block: 'start' });
  (track ? field('title') : mp3Input).focus();
}

query<HTMLButtonElement>(page, '.admin__add').onclick = () => openForm(null);
query<HTMLButtonElement>(form, '.admin__cancel').onclick = () => { form.hidden = true; };

form.onsubmit = async e => {
  e.preventDefault();
  formError.hidden = true;
  const mp3 = mp3Input.files?.[0];
  const license = licenseInput.files?.[0];
  const fail = (message: string) => { formError.textContent = message; formError.hidden = false; };
  if (!editing && !mp3) return fail('Choose the MP3.');
  if (!editing && !license) return fail('Choose the license certificate. Every track needs one.');
  if (mp3 && mp3Seconds === null) return fail("The MP3's length couldn't be read. Choose the file again, or use another one.");
  for (const name of ['title', 'artist', 'licenseName']) if (!field(name).value.trim()) return fail('Fill in the title, artist and license.');

  const save = query<HTMLButtonElement>(form, '.admin__save');
  save.disabled = true;
  progress.hidden = !mp3;
  progress.value = 0;
  try {
    const value = (name: string) => field(name).value.trim();
    const saved = await MusicAdminApi.save(editing?.id ?? null, {
      title: value('title'), artist: value('artist'), artistUrl: value('artistUrl'), sourceUrl: value('sourceUrl'),
      tags: value('tags'), licenseName: value('licenseName'),
      durationSeconds: mp3Seconds === null ? undefined : Math.round(mp3Seconds * 100) / 100,
    }, { mp3, license }, f => { progress.value = f; });
    form.hidden = true;
    await load();
    highlight(saved.id);
  } catch (err) {
    fail(errorMessage(err));
  } finally {
    save.disabled = false;
    progress.hidden = true;
  }
};

// ───────── The list ─────────

function highlight(id: string): void {
  const row = page.querySelector<HTMLElement>(`tr[data-id="${CSS.escape(id)}"]`);
  row?.classList.add('media-table__row--selected');
  row?.scrollIntoView({ behavior: 'smooth', block: 'center' });
  setTimeout(() => row?.classList.remove('media-table__row--selected'), 2500);
}

function togglePreview(t: AdminTrack): void {
  if (previewing === t.id) { preview.pause(); previewing = null; }
  else { preview.src = MusicApi.address(t.url); void preview.play().catch(() => undefined); previewing = t.id; }
  render();
}
preview.onended = () => { previewing = null; render(); };

async function remove(t: AdminTrack): Promise<void> {
  const used = t.usedBy === 0 ? 'No trips use it.' : `${t.usedBy === 1 ? '1 trip uses' : `${t.usedBy} trips use`} it, and will play without it.`;
  if (!confirm(`Delete "${t.title}" from the music library? ${used} This can't be undone.`)) return;
  try { await MusicAdminApi.remove(t.id); await load(); }
  catch (err) { showError(err); }
}

function render(): void {
  const body = query(page, '.admin__table tbody');
  body.replaceChildren(...tracks.map(t => {
    const tr = document.createElement('tr');
    tr.dataset.id = t.id;
    const cell = (content: string | Node, className = '') => {
      const td = document.createElement('td');
      td.append(content);
      if (className) td.className = className;
      return td;
    };
    const btn = (text: string, className: string, label: string, onclick: () => void) => {
      const b = document.createElement('button');
      b.type = 'button';
      b.className = `button button--quiet ${className}`;
      b.textContent = text;
      b.setAttribute('aria-label', label);
      b.onclick = onclick;
      return b;
    };
    const title = document.createElement('span');
    title.textContent = t.title;
    if (t.sourceUrl) {
      const source = Object.assign(document.createElement('a'), { href: t.sourceUrl, textContent: ' ↗', target: '_blank', rel: 'noopener' });
      source.setAttribute('aria-label', `Where ${t.title} was found`);
      title.append(source);
    }
    const licenseLink = Object.assign(document.createElement('a'), { href: MusicApi.address(t.licenseUrl), textContent: t.licenseName, target: '_blank', rel: 'noopener' });
    const tags = Object.assign(document.createElement('span'), { className: 'admin__tag-list', textContent: t.tags.join(', ') });
    const actions = document.createElement('div');
    actions.className = 'admin__row-actions';
    actions.append(btn('Edit', '', `Change ${t.title}`, () => openForm(t)), btn('Delete', 'admin__delete', `Delete ${t.title}`, () => void remove(t)));
    tr.append(
      cell(btn(previewing === t.id ? '❚❚' : '▶', 'music__preview', `${previewing === t.id ? 'Stop' : 'Play'} ${t.title}`, () => togglePreview(t))),
      cell(title, 'music__col-title'),
      cell(t.artist, 'admin__col-artist'),
      cell(tags, 'admin__col-tags'),
      cell(formatLength(t.durationSeconds), 'admin__col-num'),
      cell(String(t.usedBy), 'admin__col-num'),
      cell(licenseLink, 'admin__col-license'),
      cell(actions));
    return tr;
  }));
  query(page, '.admin__table').hidden = tracks.length === 0;
  query(page, '.admin__none').hidden = tracks.length > 0;

  const used = [...new Set(tracks.flatMap(t => t.tags))].sort();
  query(page, '.admin__tags-used').textContent = used.length ? used.join(', ') : 'none yet';
  query(page, '#admin-tags').replaceChildren(...used.map(tag => Object.assign(document.createElement('option'), { value: tag })));
}

async function load(): Promise<void> {
  try {
    tracks = await MusicAdminApi.list();
    render();
  } catch (err) {
    showError(err);
  }
}

query<HTMLButtonElement>(page, '.admin__export').onclick = async () => {
  try {
    const blob = await MusicAdminApi.exportFile();
    const a = Object.assign(document.createElement('a'), { href: URL.createObjectURL(blob), download: `music-library-${new Date().toISOString().slice(0, 10)}.json` });
    a.click();
    setTimeout(() => URL.revokeObjectURL(a.href), 10_000);
  } catch (err) {
    showError(err);
  }
};

// ───────── Start ─────────

const admin = await MusicAdminApi.isAdmin();
query(page, '.admin__loading').hidden = true;
if (!admin) query(page, '.admin__denied').hidden = false;
else {
  query(page, '.admin__body').hidden = false;
  await load();
}
