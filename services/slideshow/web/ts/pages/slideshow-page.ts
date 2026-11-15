// Entry point for slideshow.html.
//   slideshow.html?album=<id>    plays one of your albums (compiling it first if it has changed)
//   slideshow.html?share=<code>  plays a shared album: anyone with the link, no sign-in
//   slideshow.html?admin=<id>&owner=<user id>  plays anyone's trip, for site admins (from the Overview page)
//   slideshow.html?folder        plays a folder from this computer (hidden for now: nothing links to it)
//   slideshow.html               nothing to play: goes to the Trip Pix page

import { AlbumsApi, SharingApi, SiteAdminApi } from '../api/albums-api.js';
import { requireSignIn } from '../auth/session.js';
import { mountFolderPicker } from '../components/slideshow/folder-picker.js';
import { mountSlideshow } from '../components/slideshow/slideshow.js';
import { contentsFromAdminTrip, contentsFromManifest, contentsFromSharedAlbum } from '../media/api-source.js';
import { query } from '../utils/dom.js';
import { errorMessage } from '../utils/format.js';

const root = query(document, '.slideshow');
const start = query(root, '.start-screen');
const back = query<HTMLAnchorElement>(root, '.slideshow__back');
const backLabel = query(back, '.slideshow__back-label');
const params = new URLSearchParams(location.search);
const albumId = params.get('album'), shareCode = params.get('share');
const adminTrip = params.get('admin'), adminOwner = params.get('owner');

if (shareCode) await playShared(shareCode);
else if (adminTrip && adminOwner) await playAsAdmin(adminOwner, adminTrip);
else if (albumId) await playAlbum(albumId);
else if (params.has('folder')) await playFolder();
else location.replace('/trips.html');

/** Anyone with the link: no sign-in, no way into the owner's account; back goes to the Trip Pix page. */
async function playShared(code: string): Promise<void> {
  const message = query(start, '.start-screen__message');
  query(start, '.start-screen__actions').hidden = true;
  query(root, '.settings__change').hidden = true;
  back.href = '/trips.html';
  back.setAttribute('aria-label', 'Go to the Trip Pix page');
  message.textContent = 'Loading slideshow…';
  try {
    const shared = await SharingApi.shared(code);
    document.title = `${shared.name} - Slideshow`;
    query(start, '.start-screen__title').textContent = shared.name;
    backLabel.textContent = 'Trip Pix';
    if (!shared.slides.length) { message.textContent = 'This slideshow has no photos to show yet.'; return; }
    const slideshow = mountSlideshow(root, { mapAllowed: shared.showMap });
    start.hidden = true;
    slideshow.play(contentsFromSharedAlbum(shared));
  } catch (err) {
    message.textContent = errorMessage(err);
  }
}

/** Someone's trip, for a site admin: as its viewers would see it (with the map), and the way back is to the Overview. */
async function playAsAdmin(ownerId: string, id: string): Promise<void> {
  await requireSignIn();
  const message = query(start, '.start-screen__message');
  query(start, '.start-screen__actions').hidden = true;
  query(root, '.settings__change').hidden = true;
  back.href = '/overview.html';
  backLabel.textContent = 'Admin view · Overview';
  back.setAttribute('aria-label', 'Admin view. Back to the Overview page');
  message.textContent = 'Loading trip…';
  try {
    const trip = await SiteAdminApi.trip(ownerId, id);
    document.title = `Admin view: ${trip.name} - Slideshow`;
    const owner = trip.ownerName ?? trip.ownerEmail ?? 'unknown user';
    query(start, '.start-screen__title').textContent = `${trip.name} (${owner})`;
    if (!trip.slides.length) {
      message.textContent = trip.excludedCount
        ? `Nothing to play: none of its ${trip.excludedCount} photos and videos have both a capture date and a location.`
        : 'This trip has no photos or videos.';
      return;
    }
    const slideshow = mountSlideshow(root, { mapAllowed: true });
    start.hidden = true;
    slideshow.play(contentsFromAdminTrip(trip));
  } catch (err) {
    message.textContent = errorMessage(err);
  }
}

async function playAlbum(id: string): Promise<void> {
  await requireSignIn();
  const albumPage = `/trip.html?id=${encodeURIComponent(id)}`;
  back.href = albumPage;
  backLabel.textContent = 'Trip';
  back.setAttribute('aria-label', 'Back to the trip');
  const message = query(start, '.start-screen__message');
  query(start, '.start-screen__actions').hidden = true; // folder buttons
  query(root, '.settings__change').textContent = 'Back to trip';
  message.textContent = 'Loading trip…';
  const slideshow = mountSlideshow(root, { onChangeSource: () => location.assign(albumPage) });

  const showMessage = (text: string) => {
    const link = document.createElement('a');
    link.href = albumPage;
    link.textContent = 'Back to the trip';
    message.replaceChildren(text, ' ', link);
  };
  try {
    const manifest = await AlbumsApi.playable(id);
    document.title = `${manifest.name} - Slideshow`;
    backLabel.textContent = manifest.name;
    back.setAttribute('aria-label', `Back to the trip ${manifest.name}`);
    query(start, '.start-screen__title').textContent = manifest.name;
    if (!manifest.slides.length) {
      showMessage('Nothing to show: none of the photos or videos in this trip have both a capture date and a location.');
      return;
    }
    start.hidden = true;
    slideshow.play(contentsFromManifest(manifest));
  } catch (err) {
    showMessage(errorMessage(err));
  }
}

async function playFolder(): Promise<void> {
  back.href = '/trips.html';
  back.setAttribute('aria-label', 'Back to the Trip Pix page');
  let picker: { show(): void } | undefined;
  const slideshow = mountSlideshow(root, { onChangeSource: () => picker?.show() });
  picker = await mountFolderPicker(start, contents => slideshow.play(contents));
}
