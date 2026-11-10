// /api/v1/albums and their images (photos + videos), the music library they choose their music from
// (/api/v1/music), and its admin side (/api/v1/admin/music).

import { apiFor, ApiError } from './api-client.js';
import { APP_CONFIG } from '../config.js';

const api = apiFor('slideshow');

export interface AlbumInfo {
  id: string; name: string; description: string | null; createdAt: string; updatedAt: string;
  imageCount: number; videoCount: number; musicCount: number;
  /** Photos/videos the slideshow leaves out (no capture date or no location). */
  excludedCount: number;
  totalBytes: number;
  compiledAt: string | null;
  /** Never compiled, or changed since. */
  isStale: boolean;
  /** Preview of the album's first photo (in slideshow order), or null. */
  coverThumbnailUrl: string | null;
  /** Anyone with the link can watch. */
  isShared: boolean;
  /** Listed on the Albums page for everyone (always shared too). */
  isPublic: boolean;
}

/** Link sharing for an album. `token` goes in the share link; null when not shared. `isPublic`: listed for everyone. */
export interface ShareInfo { enabled: boolean; token: string | null; showMap: boolean; isPublic: boolean; }

/** Someone's public album, as anyone sees it. Counts are of what its slideshow shows; `shareToken` plays it. */
export interface PublicAlbum {
  id: string; name: string; description: string | null; createdAt: string; updatedAt: string;
  imageCount: number; videoCount: number; musicCount: number;
  shareToken: string;
  /** Preview of the slideshow's first photo (no sign-in needed), or null. */
  coverThumbnailUrl: string | null;
}

/** What a share link gives: the slideshow only. */
export interface SharedAlbum { name: string; showMap: boolean; slides: MediaInfo[]; music: LibraryTrack[]; }

/** A track of the music library. `url` plays it and `licenseUrl` is its license certificate (text); no sign-in needed. */
export interface LibraryTrack {
  id: string; title: string; artist: string; artistUrl: string | null; sourceUrl: string | null;
  tags: string[]; durationSeconds: number; licenseName: string; url: string; licenseUrl: string;
}

/** A library track as music admins see it. `usedBy`: how many trips chose it. */
export interface AdminTrack extends LibraryTrack {
  licenseFileName: string; sizeBytes: number; addedAt: string; updatedAt: string; usedBy: number;
}

/** The most tracks a trip can have. */
export const TRACKS_PER_TRIP = 20;

/** The address to send people: plays the album without signing in. */
export const shareLink = (token: string) => `${location.origin}/slideshow.html?share=${encodeURIComponent(token)}`;

/** Where Play goes: your own album, or a shared one by its code. */
export const playLink = (album: { albumId: string } | { shareToken: string }) => 'albumId' in album
  ? `/slideshow.html?album=${encodeURIComponent(album.albumId)}`
  : `/slideshow.html?share=${encodeURIComponent(album.shareToken)}`;

export type MissingData = 'date' | 'location';

export interface MediaInfo {
  id: string; fileName: string; kind: 'image' | 'video'; contentType: string; sizeBytes: number; uploadedAt: string;
  /** Local wall-clock capture time, "2026-07-04T09:31:05", or null. */
  taken: string | null;
  takenSource: 'exif' | 'video' | null;
  location: { lat: number; lon: number } | null;
  /** Shown in the slideshow (music: always). */
  playable: boolean;
  /** Why not, when not playable. */
  missing: MissingData[];
  /** Photos: the smaller versions made so far (longest edge in px), fetched with `?size=` on `url`. */
  sizes: number[];
  /** Path of the file's bytes on the API. */
  url: string;
  /** Path of a small JPEG preview (photos only), or null. */
  thumbnailUrl: string | null;
  /** Videos: running time in seconds, or null when unknown. */
  durationSeconds: number | null;
}

export interface AlbumManifest {
  albumId: string; name: string; compiledAt: string; isStale: boolean;
  /** Playable photos and videos in play order. */
  slides: MediaInfo[];
  /** The trip's music, in play order. */
  music: LibraryTrack[];
  excludedCount: number;
}

export type Collection = 'images';

const albums = '/api/v1/albums';
const at = (id: string) => `${albums}/${encodeURIComponent(id)}`;

export const AlbumsApi = {
  list: () => api.get<AlbumInfo[]>(albums),
  info: (id: string) => api.get<AlbumInfo>(`${at(id)}/info`),
  async create(name: string, description?: string): Promise<AlbumInfo> {
    return (await api.post<AlbumInfo>(albums, { name, description }))!;
  },
  remove: (id: string) => api.delete(at(id)),
  /** Rename, or change the description (an empty one removes it). Omitted fields stay as they are. */
  update: (id: string, changes: { name?: string; description?: string }) => api.patch<AlbumInfo>(at(id), changes),
  async compile(id: string): Promise<AlbumManifest> {
    return (await api.post<AlbumManifest>(`${at(id)}/compile`))!;
  },
  /** The compiled album, compiling first if it never was or has changed since. */
  async playable(id: string): Promise<AlbumManifest> {
    try {
      const manifest = await api.get<AlbumManifest>(at(id));
      return manifest.isStale ? await AlbumsApi.compile(id) : manifest;
    } catch (err) {
      if (err instanceof ApiError && err.code === 'album_not_compiled') return AlbumsApi.compile(id);
      throw err;
    }
  },
};

export const SharingApi = {
  get: (albumId: string) => api.get<ShareInfo>(`${at(albumId)}/share`),
  /** Making it public shares it too; turning sharing off makes it private again. */
  update: (albumId: string, changes: { enabled?: boolean; showMap?: boolean; isPublic?: boolean }) => api.put<ShareInfo>(`${at(albumId)}/share`, changes),
  async newLink(albumId: string): Promise<ShareInfo> {
    return (await api.post<ShareInfo>(`${at(albumId)}/share/reset`))!;
  },
  /** For viewers: no sign-in. */
  shared: (token: string) => api.publicGet<SharedAlbum>(`/api/v1/shared/${encodeURIComponent(token)}`),
  /** Everyone's public albums, newest first. No sign-in. */
  publicAlbums: () => api.publicGet<PublicAlbum[]>('/api/v1/public-albums'),
  download: (url: string) => api.publicBlob(url),
};

export const MediaApi = {
  list: (albumId: string, collection: Collection) => api.get<MediaInfo[]>(`${at(albumId)}/${collection}`),

  /** Uploads one file. The API reads its capture date and location from the file itself. */
  upload(albumId: string, collection: Collection, file: File, onProgress?: (fraction: number) => void): Promise<MediaInfo> {
    const form = new FormData();
    form.append('file', file, file.name);
    // Videos record UTC; the uploader's offset turns that into local time like a photo's EXIF date.
    form.append('utcOffsetMinutes', String(-new Date().getTimezoneOffset()));
    return api.upload<MediaInfo>(`${at(albumId)}/${collection}`, form, onProgress);
  },

  remove: (albumId: string, collection: Collection, id: string) => api.delete(`${at(albumId)}/${collection}/${encodeURIComponent(id)}`),

  /** Deletes several files of one collection at once; resolves with how many were deleted. */
  async removeMany(albumId: string, collection: Collection, ids: string[]): Promise<number> {
    return (await api.post<{ deleted: number }>(`${at(albumId)}/${collection}/delete`, { ids }))!.deleted;
  },

  /** The file's bytes, by the `url` the API gave for it. */
  download: (url: string) => api.blob(url),
};

/** The music library (no sign-in), and a trip's choice from it. */
export const MusicApi = {
  library: () => api.publicGet<LibraryTrack[]>('/api/v1/music'),
  /** A track's MP3, by its `url` (no sign-in: shared slideshows play it too). */
  download: (url: string) => api.publicBlob(url),
  /** The full address of a library file, for an <audio> element or a link. */
  address: (url: string) => apiBase() + url,
  forTrip: (albumId: string) => api.get<LibraryTrack[]>(`${at(albumId)}/music`),
  /** The trip's music, in play order; resolves with it as saved. */
  setForTrip: (albumId: string, trackIds: string[]) => api.put<LibraryTrack[]>(`${at(albumId)}/music`, { trackIds }),
};

/** The details of a library track, as the admin form sends them (every field optional when changing a track). */
export interface TrackFields {
  title?: string; artist?: string; artistUrl?: string; sourceUrl?: string; tags?: string; licenseName?: string;
  durationSeconds?: number;
}

/** Music admins only (the API's Music.Admin app role). */
export const MusicAdminApi = {
  /** Whether the signed-in user is a music admin. */
  async isAdmin(): Promise<boolean> {
    try { await api.get('/api/v1/admin/music/access', { noRedirect: true }); return true; }
    catch { return false; }
  },
  list: () => api.get<AdminTrack[]>('/api/v1/admin/music'),
  /** Adds a track (`id` null) or changes one; a new MP3 or license file replaces the old one. */
  save(id: string | null, fields: TrackFields, files: { mp3?: File; license?: File }, onProgress?: (fraction: number) => void): Promise<AdminTrack> {
    const form = new FormData();
    for (const [key, value] of Object.entries(fields)) if (value !== undefined) form.append(key, String(value));
    if (files.mp3) form.append('file', files.mp3, files.mp3.name);
    if (files.license) form.append('license', files.license, files.license.name);
    return api.upload<AdminTrack>(id ? `/api/v1/admin/music/${encodeURIComponent(id)}` : '/api/v1/admin/music', form, onProgress);
  },
  remove: (id: string) => api.delete(`/api/v1/admin/music/${encodeURIComponent(id)}`),
  /** The catalog as a JSON file (a backup; without the MP3s). */
  exportFile: () => api.blob('/api/v1/admin/music/export'),
};

function apiBase(): string { return APP_CONFIG.services.slideshow; }
