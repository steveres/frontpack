// Which version of a photo to download from the API: the smallest that looks sharp on this screen,
// one or two sizes smaller on a slow connection.

import { MAX_PIXEL_RATIO, PHOTO_SIZES } from '../config.js';
import { slowness } from '../utils/network.js';

/** Longest edge in px to ask for (`?size=`), or null for the original. Asked again for every photo, so it follows the connection. */
export function photoSize(): number | null {
  const needed = Math.max(screen.width, screen.height) * Math.min(window.devicePixelRatio || 1, MAX_PIXEL_RATIO);
  const ladder: (number | null)[] = [...PHOTO_SIZES, null]; // null: bigger than the largest, so the original
  const fits = ladder.findIndex(size => size === null || size >= needed);
  return ladder[Math.max(0, fits - slowness())];
}
