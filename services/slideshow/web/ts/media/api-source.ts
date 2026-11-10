// Slideshow contents from the API: a compiled album (signed in) or a shared album (share link, no
// sign-in). Files are downloaded when the player asks for them, and the few most recent are kept so
// prefetching pays off. Photos come in the size that suits the screen and connection (photo-size.ts);
// zooming in fetches the original.

import { MediaApi, MusicApi, SharingApi, type LibraryTrack, type MediaInfo } from '../api/albums-api.js';
import type { MediaItem, SlideshowContents, Track } from './media-types.js';
import { photoSize } from './photo-size.js';

type Download = (url: string) => Promise<Blob>;

/** Downloads on demand (from the url `urlOf` gives) and keeps the `keep` most recently used files in memory. */
function downloader(keep: number, download: Download, urlOf: (m: MediaInfo) => string = m => m.url) {
  const cache = new Map<string, Promise<File>>(); // insertion order = least recently used first
  return (m: MediaInfo) => (): Promise<File> => {
    let file = cache.get(m.id);
    if (file) {
      cache.delete(m.id); // re-inserted below as most recent
    } else {
      // A smaller version is a JPEG whatever the original was: go by what came.
      file = download(urlOf(m)).then(blob => new File([blob], m.fileName, { type: blob.type || m.contentType }));
      file.catch(() => cache.delete(m.id)); // don't keep failures
    }
    cache.set(m.id, file);
    while (cache.size > keep) cache.delete(cache.keys().next().value!);
    return file;
  };
}

/** A photo in the size picked for this screen and connection; videos as they are. */
function slideUrl(m: MediaInfo): string {
  const size = m.kind === 'image' ? photoSize() : null;
  return size === null ? m.url : `${m.url}?size=${size}`;
}

/** A library track, downloaded when the player gets to it (no sign-in: shared slideshows play them too). */
function libraryTrack(t: LibraryTrack): Track {
  return {
    name: `${t.title} by ${t.artist}`,
    credit: { title: t.title, artist: t.artist, artistUrl: t.artistUrl, sourceUrl: t.sourceUrl },
    getFile: async () => new File([await MusicApi.download(t.url)], `${t.title}.mp3`, { type: 'audio/mpeg' }),
  };
}

function contents(slides: MediaInfo[], music: LibraryTrack[], download: Download): SlideshowContents {
  const slide = downloader(4, download, slideUrl); // current, next, and a couple for stepping back
  const original = downloader(1, download);        // the photo zoomed into
  const items: MediaItem[] = slides.map(m => ({
    name: m.fileName,
    kind: m.kind === 'video' ? 'video' : 'image',
    taken: m.taken ?? undefined,
    location: m.location, // null when the album's owner hid the map from viewers
    getFile: slide(m),
    getFullFile: m.kind === 'image' ? original(m) : undefined,
  }));
  return { items, tracks: music.map(libraryTrack) };
}

/** The signed-in owner's compiled album. */
export const contentsFromManifest = (manifest: { slides: MediaInfo[]; music: LibraryTrack[] }): SlideshowContents =>
  contents(manifest.slides, manifest.music, MediaApi.download);

/** A shared album, played from its share link without signing in. */
export const contentsFromSharedAlbum = (shared: { slides: MediaInfo[]; music: LibraryTrack[] }): SlideshowContents =>
  contents(shared.slides, shared.music, SharingApi.download);
