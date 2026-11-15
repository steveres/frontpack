// overview.html: the site admins' overview (the API's Site.Admin app role). Totals, then every user and every
// trip with their counts, storage and share-link views, in tables that sort and filter. Any trip's slideshow
// opens from its row, for checking reported photos. The API checks the role on every call; this page only
// hides itself.

import { adminPlayLink, SiteAdminApi, type Overview, type OverviewTrip, type OverviewUser } from '../api/albums-api.js';
import { requireSignIn } from '../auth/session.js';
import { mountNavigation } from '../components/navigation.js';
import { query } from '../utils/dom.js';
import { errorMessage, formatBytes } from '../utils/format.js';

await requireSignIn();
void mountNavigation();

const page = query(document, '.admin');
const errorEl = query(page, '.admin__error');
const showError = (err: unknown) => { errorEl.textContent = errorMessage(err); errorEl.hidden = false; };
const usersFilter = query<HTMLInputElement>(page, '.overview__users-filter');
const tripsFilter = query<HTMLInputElement>(page, '.overview__trips-filter');
const tripsShow = query<HTMLSelectElement>(page, '.overview__trips-show');

// ───────── Little builders ─────────

function el<K extends keyof HTMLElementTagNameMap>(tag: K, className?: string, text?: string): HTMLElementTagNameMap[K] {
  const e = document.createElement(tag);
  if (className) e.className = className;
  if (text !== undefined) e.textContent = text;
  return e;
}

const count = (n: number) => n.toLocaleString();
const day = (iso: string | null) => iso ? new Date(iso).toLocaleDateString(undefined, { year: 'numeric', month: 'short', day: 'numeric' }) : '—';
const muted = (text: string) => el('span', 'overview__muted', text);

function badge(kind: 'ok' | 'warning' | 'muted', text: string, title?: string): HTMLElement {
  const b = el('span', `badge badge--${kind}`, text);
  if (title) b.title = title;
  return b;
}

// ───────── Sortable, filterable tables ─────────

type Value = string | number | null;

interface Column<T> {
  label: string;
  cell: (row: T) => Node | string;
  /** What the column sorts by; none: not sortable. */
  sort?: (row: T) => Value;
  /** Numbers: right-aligned, biggest first on the first click. */
  num?: boolean;
}

/** Nulls always last; text by the viewer's locale. */
function compare(a: Value, b: Value): number {
  if (a === null || b === null) return a === b ? 0 : a === null ? 1 : -1;
  return typeof a === 'number' && typeof b === 'number' ? a - b : String(a).localeCompare(String(b), undefined, { sensitivity: 'base' });
}

function sortableTable<T>(table: HTMLTableElement, columns: Column<T>[], sortBy: number, descending: boolean) {
  let rows: T[] = [];
  let keep: (row: T) => boolean = () => true;
  const headers: HTMLTableCellElement[] = [];
  const headRow = table.createTHead().insertRow();
  columns.forEach((c, i) => {
    const th = el('th');
    th.scope = 'col';
    if (c.num) th.className = 'admin__col-num';
    headers.push(th);
    headRow.append(th);
    if (!c.sort) { th.append(c.label ? c.label : el('span', 'visually-hidden', 'Actions')); return; }
    const button = el('button', 'overview__sort', c.label);
    button.type = 'button';
    button.onclick = () => {
      if (sortBy === i) descending = !descending;
      else { sortBy = i; descending = !!c.num; }
      render();
    };
    th.append(button);
  });
  const body = table.createTBody();

  function render(): number {
    headers.forEach((th, i) => {
      if (i === sortBy) th.setAttribute('aria-sort', descending ? 'descending' : 'ascending');
      else th.removeAttribute('aria-sort');
    });
    const by = columns[sortBy].sort!;
    const shown = rows.filter(keep).sort((a, b) => {
      const va = by(a), vb = by(b);
      if (va === null || vb === null) return compare(va, vb); // nulls last either way
      return descending ? compare(vb, va) : compare(va, vb);
    });
    body.replaceChildren(...shown.map(row => {
      const tr = el('tr');
      for (const c of columns) {
        const td = tr.insertCell();
        if (c.num) td.className = 'admin__col-num';
        td.append(c.cell(row));
      }
      return tr;
    }));
    return shown.length;
  }

  return {
    set(newRows: T[]) { rows = newRows; return render(); },
    filter(test: (row: T) => boolean) { keep = test; return render(); },
  };
}

// ───────── The page ─────────

let overview: Overview | null = null;

function storageCell(bytes: number): Node {
  const quota = overview?.quotaBytesPerUser ?? 0;
  const span = el('span', undefined, formatBytes(bytes));
  if (quota > 0) {
    const pct = Math.round((bytes / quota) * 100);
    span.append(' ', pct >= 80 ? badge('warning', `${pct}%`, 'of the quota') : muted(`${pct}%`));
  }
  return span;
}

/** Shows that user's trips in the trips table. */
function showTripsOf(u: OverviewUser): void {
  tripsFilter.value = u.email ?? u.id;
  tripsShow.value = 'all';
  applyTripsFilter();
  query(page, '.overview__trips').scrollIntoView({ behavior: 'smooth', block: 'start' });
}

const users = sortableTable<OverviewUser>(query(page, '.overview__users'), [
  { label: 'Name', sort: u => u.name, cell: u => u.name ?? muted('(no account)') },
  { label: 'Email', sort: u => u.email, cell: u => u.email ?? muted(u.id) },
  { label: 'Joined', sort: u => u.createdAt, cell: u => day(u.createdAt) },
  { label: 'Last seen', sort: u => u.lastSeenAt, cell: u => day(u.lastSeenAt) },
  {
    label: 'Trips', num: true, sort: u => u.tripCount, cell: u => {
      if (!u.tripCount) return '0';
      const b = el('button', 'overview__link', count(u.tripCount));
      b.type = 'button';
      b.title = 'Show their trips';
      b.onclick = () => showTripsOf(u);
      return b;
    },
  },
  { label: 'Photos', num: true, sort: u => u.photoCount, cell: u => count(u.photoCount) },
  { label: 'Videos', num: true, sort: u => u.videoCount, cell: u => count(u.videoCount) },
  { label: 'Storage', num: true, sort: u => u.bytes, cell: u => storageCell(u.bytes) },
], 3, true); // most recently seen first

function status(t: OverviewTrip): Node {
  if (t.isPublic) return badge('warning', 'Public', 'Listed for everyone on Trip Pix');
  if (t.isShared) return badge('ok', 'Shared', 'Anyone with the link can watch');
  return badge('muted', 'Private');
}

function tripCell(t: OverviewTrip): Node {
  const cell = el('span', 'overview__trip', t.name);
  if (t.description) cell.append(el('span', 'overview__description', t.description));
  return cell;
}

const trips = sortableTable<OverviewTrip>(query(page, '.overview__trips'), [
  { label: 'Trip', sort: t => t.name, cell: tripCell },
  { label: 'Owner', sort: t => t.ownerName ?? t.ownerEmail, cell: t => {
      const cell = el('span', 'overview__trip', t.ownerName ?? '(no account)');
      cell.append(el('span', 'overview__description', t.ownerEmail ?? t.ownerId));
      return cell;
    } },
  { label: 'Sharing', sort: t => t.isPublic ? 2 : t.isShared ? 1 : 0, cell: status },
  { label: 'Photos', num: true, sort: t => t.photoCount, cell: t => count(t.photoCount) },
  { label: 'Videos', num: true, sort: t => t.videoCount, cell: t => count(t.videoCount) },
  { label: 'Left out', num: true, sort: t => t.excludedCount, cell: t => t.excludedCount ? count(t.excludedCount) : muted('0') },
  { label: 'Storage', num: true, sort: t => t.bytes, cell: t => formatBytes(t.bytes) },
  { label: 'Views', num: true, sort: t => t.views, cell: t => count(t.views) },
  { label: 'Last viewed', sort: t => t.lastViewedAt, cell: t => day(t.lastViewedAt) },
  { label: 'Updated', sort: t => t.updatedAt, cell: t => day(t.updatedAt) },
  { label: '', cell: t => {
      const a = el('a', 'button button--quiet overview__view', 'View');
      a.href = adminPlayLink(t);
      a.title = `Play "${t.name}" (logged)`;
      return a;
    } },
], 9, true); // most recently updated first

const has = (text: string | null, q: string) => !!text && text.toLowerCase().includes(q);

function applyUsersFilter(): void {
  const q = usersFilter.value.trim().toLowerCase();
  const shown = users.filter(u => !q || has(u.name, q) || has(u.email, q) || u.id === q);
  query(page, '.overview__users-count').textContent = `${count(shown)} of ${count(overview?.users.length ?? 0)}`;
}

function applyTripsFilter(): void {
  const q = tripsFilter.value.trim().toLowerCase();
  const show = tripsShow.value;
  const shown = trips.filter(t =>
    (show === 'all' || (show === 'public' ? t.isPublic : t.isShared))
    && (!q || has(t.name, q) || has(t.description, q) || has(t.ownerName, q) || has(t.ownerEmail, q) || t.ownerId === q));
  query(page, '.overview__trips-count').textContent = `${count(shown)} of ${count(overview?.trips.length ?? 0)}`;
}

usersFilter.oninput = applyUsersFilter;
tripsFilter.oninput = applyTripsFilter;
tripsShow.onchange = applyTripsFilter;

function tile(label: string, value: string, detail?: string, warn = false): HTMLElement {
  const li = el('li', `overview__tile${warn ? ' overview__tile--warning' : ''}`);
  li.append(el('span', 'overview__tile-value', value), el('span', 'overview__tile-label', label));
  if (detail) li.append(el('span', 'overview__tile-detail', detail));
  return li;
}

function render(o: Overview): void {
  overview = o;
  const t = o.totals;
  query(page, '.overview__tiles').replaceChildren(
    tile('Users', count(t.users), `${count(t.newUsers30Days)} new in 30 days`),
    tile('Active', count(t.activeUsers30Days), 'used Trip Pix in 30 days'),
    tile('Trips', count(t.trips), `${count(t.sharedTrips)} shared · ${count(t.publicTrips)} public`),
    tile('Photos', count(t.photos), `${count(t.videos)} videos`),
    tile('Storage', formatBytes(t.bytes), 'as uploaded'),
    tile('Views', count(t.views), 'share-link openings'),
    tile('Resize failures', count(t.resizeFailures), 'photos playing full size', t.resizeFailures > 0),
  );
  query(page, '.overview__time').textContent = `As of ${new Date(o.generatedAt).toLocaleString(undefined, { dateStyle: 'medium', timeStyle: 'short' })}.`;
  users.set(o.users);
  trips.set(o.trips);
  applyUsersFilter();
  applyTripsFilter();
}

async function load(): Promise<void> {
  errorEl.hidden = true;
  const refresh = query<HTMLButtonElement>(page, '.overview__refresh');
  refresh.disabled = true;
  try { render(await SiteAdminApi.overview()); }
  catch (err) { showError(err); }
  finally { refresh.disabled = false; }
}

query<HTMLButtonElement>(page, '.overview__refresh').onclick = () => void load();

const admin = await SiteAdminApi.isAdmin();
query(page, '.admin__loading').hidden = true;
if (!admin) {
  query(page, '.admin__denied').hidden = false;
} else {
  query(page, '.admin__body').hidden = false;
  await load();
}
