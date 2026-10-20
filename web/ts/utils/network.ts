// How fast the connection is: what downloads actually achieve, and until there's one to go by, the
// browser's rough class of connection where it gives one (Chrome, Android; iPhones give none).

import { SLOW_MBPS, VERY_SLOW_MBPS } from '../config.js';

/** The Network Information API: not in every browser, so not in TypeScript's DOM types. */
interface Connection { effectiveType?: string; saveData?: boolean; }
const connection = (): Connection | undefined => (navigator as Navigator & { connection?: Connection }).connection;

const MIN_SAMPLE_BYTES = 30_000; // smaller downloads measure the latency more than the speed
let measuredMbps: number | null = null;

/** Notes a finished download (body only, so a slow first response from the API doesn't count). */
export function recordDownload(bytes: number, ms: number): void {
  if (bytes < MIN_SAMPLE_BYTES || ms <= 0) return;
  const mbps = (bytes * 8) / (ms * 1000);
  measuredMbps = measuredMbps === null ? mbps : 0.6 * measuredMbps + 0.4 * mbps; // follows changes, smooths blips
}

/** 0 on a good connection, 1 on a slow one, 2 on a very slow one or with data saving on. */
export function slowness(): 0 | 1 | 2 {
  const c = connection();
  if (c?.saveData) return 2;
  if (measuredMbps !== null) return measuredMbps < VERY_SLOW_MBPS ? 2 : measuredMbps < SLOW_MBPS ? 1 : 0;
  // Not `downlink`: before it has measured anything Chrome reports a made-up 1.55 Mbit/s.
  return c?.effectiveType === 'slow-2g' || c?.effectiveType === '2g' ? 2 : c?.effectiveType === '3g' ? 1 : 0;
}
