// Plays the album's music back to back in random order, never the same track twice in a row.

import type { Track } from '../../media/media-types.js';
import { sleep } from '../../utils/dom.js';

/** A twentieth of a second of silence (8 kHz, 8-bit mono WAV), for checking whether sound may play. */
const SILENCE = (() => {
  const samples = 400, bytes = new Uint8Array(44 + samples);
  const view = new DataView(bytes.buffer);
  const text = (at: number, s: string) => { for (let i = 0; i < s.length; i++) bytes[at + i] = s.charCodeAt(i); };
  text(0, 'RIFF'); view.setUint32(4, 36 + samples, true); text(8, 'WAVEfmt ');
  view.setUint32(16, 16, true); view.setUint16(20, 1, true); view.setUint16(22, 1, true);   // PCM, mono
  view.setUint32(24, 8000, true); view.setUint32(28, 8000, true); view.setUint16(32, 1, true); view.setUint16(34, 8, true);
  text(36, 'data'); view.setUint32(40, samples, true);
  bytes.fill(128, 44); // 8-bit silence
  return 'data:audio/wav;base64,' + btoa(String.fromCharCode(...bytes));
})();

/**
 * Whether the browser will play sound right now, without a tap first. Desktop browsers usually
 * will once the viewer has clicked on the site; phones won't until the viewer taps the page itself.
 */
export function canPlaySound(): Promise<boolean> {
  const probe = new Audio(SILENCE);
  return Promise.race([
    probe.play().then(() => true, () => false),
    sleep(1000).then(() => false), // no answer: assume not
  ]).finally(() => probe.pause());
}

export class MusicPlayer {
  private audio = new Audio();
  private tracks: Track[] = [];
  private current = -1;
  private url: string | null = null;
  private failures = 0;      // consecutive unplayable tracks
  private session = 0;       // bumping this abandons a track still loading
  private waitingForGesture = false;
  private fadeTimer: number | undefined;
  private faded = false;     // faded out at the end of the slideshow; resume() starts again

  constructor() {
    this.audio.onended = () => { if (this.url) void this.playRandom(); }; // (not the silence startOnTap plays)
    this.audio.onerror = () => { if (this.url) void this.trackFailed(); };
  }

  start(tracks: Track[]): void {
    this.stop();
    this.audio.volume = 1;
    this.tracks = tracks;
    this.failures = 0;
    if (tracks.length) void this.playRandom();
  }

  stop(): void {
    clearInterval(this.fadeTimer);
    this.faded = false;
    this.session++;
    this.audio.pause();
    this.release();
    this.tracks = [];
    this.current = -1;
  }

  setMuted(muted: boolean): void { this.audio.muted = muted; }

  /** Lowers the volume to silence over `ms`, then pauses (the slideshow has ended). */
  fadeOut(ms: number): void {
    clearInterval(this.fadeTimer);
    const from = this.audio.volume, start = performance.now();
    this.fadeTimer = window.setInterval(() => {
      const t = Math.min(1, (performance.now() - start) / ms);
      this.audio.volume = from * (1 - t);
      if (t < 1) return;
      clearInterval(this.fadeTimer);
      this.audio.pause();
      this.faded = true;
    }, 50);
  }

  /**
   * Call from the tap that starts the slideshow: plays the current track from its beginning. iOS only
   * lets an element play sound if play() was called during a tap, so if the track is still loading,
   * a moment of silence plays now and the track can follow when it arrives.
   */
  startOnTap(): void {
    if (!this.tracks.length) return;
    if (this.url) this.audio.currentTime = 0;
    else this.audio.src = SILENCE;
    void this.audio.play().catch(() => undefined);
  }

  /** After a fade-out (or during one): full volume again, with a new track if it had stopped. */
  resume(): void {
    clearInterval(this.fadeTimer);
    this.audio.volume = 1;
    if (this.faded && this.tracks.length) {
      this.faded = false;
      this.current = -1;
      void this.playRandom();
    }
  }

  private async playRandom(): Promise<void> {
    const session = ++this.session;
    const n = this.tracks.length;
    if (!n) return;
    // With more than one track, pick among the others.
    const pick = this.current < 0 ? Math.floor(Math.random() * n)
      : n === 1 ? 0 : (this.current + 1 + Math.floor(Math.random() * (n - 1))) % n;
    this.current = pick;
    try {
      const file = await this.tracks[pick].getFile();
      if (session !== this.session) return;
      this.release();
      this.url = URL.createObjectURL(file);
      this.audio.src = this.url;
      await this.audio.play();
      this.failures = 0;
    } catch (err) {
      if (session !== this.session) return;
      if ((err as DOMException).name === 'NotAllowedError') this.playOnGesture(); // browser wants a click first
      else void this.trackFailed();
    }
  }

  private async trackFailed(): Promise<void> {
    console.warn(`Could not play ${this.tracks[this.current]?.name}`);
    if (++this.failures >= this.tracks.length) return; // nothing playable; give up quietly
    await sleep(250);
    void this.playRandom();
  }

  /** Browsers block sound until the user interacts with the page; start on the first click or key. */
  private playOnGesture(): void {
    if (this.waitingForGesture) return;
    this.waitingForGesture = true;
    const session = this.session;
    const resume = () => {
      document.removeEventListener('pointerdown', resume);
      document.removeEventListener('keydown', resume);
      this.waitingForGesture = false;
      if (session === this.session) void this.audio.play().catch(() => undefined);
    };
    document.addEventListener('pointerdown', resume);
    document.addEventListener('keydown', resume);
  }

  private release(): void {
    if (this.url) {
      const url = this.url;
      this.url = null; // cleared first so the resulting error event isn't treated as a bad track
      this.audio.removeAttribute('src');
      this.audio.load();
      URL.revokeObjectURL(url);
    }
  }
}
