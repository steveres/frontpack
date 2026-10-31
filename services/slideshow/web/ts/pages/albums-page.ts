// albums.html: the Slideshow app's start page. Everyone's public albums; signed in, your own albums
// first (and creating a new one). Both lists come newest first from the API. Last, playing a folder.

import { AlbumsApi, playLink, SharingApi, type AlbumInfo, type PublicAlbum } from '../api/albums-api.js';
import { getAuth, loginUrl } from '../auth/auth.js';
import { mountNavigation } from '../components/navigation.js';
import { thumbnail } from '../components/thumbnail.js';
import { query } from '../utils/dom.js';
import { albumSummary, errorMessage, formatDay } from '../utils/format.js';

void mountNavigation();
const signedIn = (await getAuth()).account() !== null;

const page = query(document, '.albums');
const mineSection = query(page, '.albums__section--mine');
const othersSection = query(page, '.albums__section--others');
const errorEl = query(page, '.albums__error');
const showError = (err: unknown) => { errorEl.textContent = errorMessage(err); errorEl.hidden = false; };
if (new URLSearchParams(location.search).has('deleted')) query(page, '.albums__deleted').hidden = false; // from the Account page

type Status = 'Private' | 'Shared' | 'Public';
const STATUS: Record<Status, { badge: string; title: string }> = {
  Private: { badge: 'badge--muted', title: 'Only you can see it' },
  Shared: { badge: 'badge--ok', title: 'Anyone with the link can watch' },
  Public: { badge: 'badge--ok', title: 'Listed here for everyone to watch' },
};

interface Card {
  name: string; description: string | null; summary: string; createdAt: string; status: Status;
  cover: HTMLElement;
  /** Where the card goes. */
  href: string;
  /** Plays the slideshow; null when there's nothing to show. */
  play: string | null;
  badges?: HTMLElement[];
}

function badge(className: string, text: string, title = ''): HTMLElement {
  const b = document.createElement('span');
  b.className = `badge ${className}`;
  b.textContent = text;
  b.title = title;
  return b;
}

/** The whole card is a link (the name's, stretched over it); Play sits above it. */
function card(c: Card): HTMLLIElement {
  const li = document.createElement('li');
  li.className = 'card album-card';
  li.append(c.cover);

  const name = document.createElement('h3');
  name.className = 'album-card__name';
  const link = document.createElement('a');
  link.className = 'album-card__link';
  link.href = c.href;
  link.textContent = c.name;
  name.append(link);
  li.append(name);

  if (c.description) {
    const description = document.createElement('p');
    description.className = 'album-card__description';
    description.textContent = c.description;
    li.append(description);
  }

  const summary = document.createElement('p');
  summary.className = 'album-card__summary';
  summary.textContent = c.summary;
  li.append(summary);

  const meta = document.createElement('p');
  meta.className = 'album-card__meta';
  meta.textContent = formatDay(c.createdAt);
  const status = STATUS[c.status];
  for (const b of [badge(status.badge, c.status, status.title), ...c.badges ?? []]) meta.append(' ', b);
  li.append(meta);

  const play = document.createElement('a');
  play.className = 'button button--primary album-card__play';
  play.textContent = '▶ Play';
  play.setAttribute('aria-label', `Play ${c.name}`);
  if (c.play) play.href = c.play;
  else {
    play.setAttribute('aria-disabled', 'true');
    play.title = 'Nothing to show yet: no photos or videos with both a capture date and a location.';
  }
  li.append(play);
  return li;
}

const mineCard = (a: AlbumInfo) => card({
  name: a.name, description: a.description, summary: albumSummary(a), createdAt: a.createdAt,
  status: a.isPublic ? 'Public' : a.isShared ? 'Shared' : 'Private',
  cover: thumbnail(a.coverThumbnailUrl, 'image', 'thumb--cover'),
  href: `/album.html?id=${encodeURIComponent(a.id)}`,
  play: a.imageCount + a.videoCount > a.excludedCount ? playLink({ albumId: a.id }) : null,
  badges: a.excludedCount > 0 ? [badge('badge--warning', `${a.excludedCount} not in slideshow`)] : [],
});

const otherCard = (a: PublicAlbum) => {
  const play = a.imageCount + a.videoCount > 0 ? playLink({ shareToken: a.shareToken }) : null;
  return card({
    name: a.name, description: a.description, summary: albumSummary(a), createdAt: a.createdAt, status: 'Public',
    cover: thumbnail(a.coverThumbnailUrl, 'image', 'thumb--cover', true),
    href: play ?? playLink({ shareToken: a.shareToken }), // there's no album page for someone else's album
    play,
  });
};

async function fill<T>(section: HTMLElement, albums: Promise<T[]>, render: (a: T) => HTMLLIElement): Promise<void> {
  try {
    const list = await albums;
    query(section, '.albums__list').replaceChildren(...list.map(render));
    query(section, '.albums__empty').hidden = list.length > 0;
    section.hidden = false;
  } catch (err) {
    showError(err);
  }
}

async function load(): Promise<void> {
  const mine = signedIn ? AlbumsApi.list() : Promise.resolve([]);
  // Your own public albums are under My albums only.
  const others = SharingApi.publicAlbums().then(async all => {
    const own = new Set((await mine.catch(() => [])).map(a => a.id));
    return all.filter(a => !own.has(a.id));
  });
  await Promise.all([signedIn && fill(mineSection, mine, mineCard), fill(othersSection, others, otherCard)]);
  query(page, '.albums__loading').hidden = true;
}

const form = query<HTMLFormElement>(page, '.albums__new');
if (signedIn) {
  form.hidden = false;
  form.onsubmit = async e => {
    e.preventDefault();
    errorEl.hidden = true;
    const button = query<HTMLButtonElement>(form, 'button');
    button.disabled = true;
    try {
      const name = query<HTMLInputElement>(form, '[name=name]').value.trim();
      const description = query<HTMLInputElement>(form, '[name=description]').value.trim();
      const album = await AlbumsApi.create(name, description || undefined);
      location.assign(`/album.html?id=${encodeURIComponent(album.id)}`); // straight to adding files
    } catch (err) {
      showError(err);
      button.disabled = false;
    }
  };
} else {
  query(othersSection, '.albums__heading').textContent = 'Public albums';
  const signIn = query(page, '.albums__sign-in');
  query<HTMLAnchorElement>(signIn, 'a').href = loginUrl();
  signIn.hidden = false;
}

await load();
