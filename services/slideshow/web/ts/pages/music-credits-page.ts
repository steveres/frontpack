// music-credits.html: every track in the music library, with its artist, where it came from and its license.
// No sign-in needed (linked from the site footer).

import { MusicApi, type LibraryTrack } from '../api/albums-api.js';
import { musicBy } from '../components/music-credit.js';
import { mountNavigation } from '../components/navigation.js';
import { query } from '../utils/dom.js';
import { errorMessage } from '../utils/format.js';

void mountNavigation();

const page = query(document, '.credits');

function link(text: string, href: string): HTMLAnchorElement {
  return Object.assign(document.createElement('a'), { textContent: text, href, target: '_blank', rel: 'noopener' });
}

/** "Sunny Road Trip" / "Music by dimmysad from Pixabay · Pixabay Content License" */
function item(t: LibraryTrack): HTMLLIElement {
  const li = document.createElement('li');
  li.className = 'credits__item';
  const title = Object.assign(document.createElement('span'), { className: 'credits__title', textContent: t.title });
  const by = document.createElement('span');
  by.className = 'credits__by';
  by.append(musicBy(t));
  const meta = document.createElement('span');
  meta.className = 'credits__meta';
  meta.append(link(t.licenseName, MusicApi.address(t.licenseUrl)));
  li.append(title, by, meta);
  return li;
}

try {
  const tracks = (await MusicApi.library()).sort((a, b) => a.title.localeCompare(b.title));
  query(page, '.credits__list').replaceChildren(...tracks.map(item));
  query(page, '.credits__none').hidden = tracks.length > 0;
} catch (err) {
  const e = query(page, '.credits__error');
  e.textContent = errorMessage(err);
  e.hidden = false;
} finally {
  query(page, '.credits__loading').hidden = true;
}
