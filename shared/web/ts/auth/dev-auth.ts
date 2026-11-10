// Local development sign-in: the API (Development only) issues a token for any email, signed with the
// `dotnet user-jwts` key it trusts. The same email is always the same user. Not used in production.

import { APP_CONFIG } from '../config.js';
import { loginUrl, markSignedIn, NotSignedInError, type Account, type AuthProvider } from './auth.js';

const STORAGE_KEY = 'frontpack.devAuth';
const OLD_STORAGE_KEY = 'slideshow.devAuth'; // before the site was FrontPack: moved over once, so nobody is signed out
const EXPIRY_MARGIN_MS = 60_000;

interface StoredSession { accessToken: string; expiresAt: string; name: string; email: string; }

function load(): StoredSession | null {
  try {
    const old = localStorage.getItem(OLD_STORAGE_KEY);
    if (old !== null) {
      if (localStorage.getItem(STORAGE_KEY) === null) localStorage.setItem(STORAGE_KEY, old);
      localStorage.removeItem(OLD_STORAGE_KEY);
    }
    const s = JSON.parse(localStorage.getItem(STORAGE_KEY) ?? 'null') as StoredSession | null;
    return s && Date.parse(s.expiresAt) - EXPIRY_MARGIN_MS > Date.now() ? s : null;
  } catch { return null; }
}

export interface DevAuthProvider extends AuthProvider {
  readonly mode: 'dev';
  /** Signs in as `email` (used by the login page's form); `musicAdmin` adds the Music.Admin app role. */
  signInAs(email: string, name: string, musicAdmin?: boolean): Promise<void>;
}

export function createDevAuth(): DevAuthProvider {
  return {
    mode: 'dev',

    account(): Account | null {
      const s = load();
      return s ? { name: s.name, email: s.email } : null;
    },

    async signIn(returnTo) {
      localStorage.removeItem(STORAGE_KEY); // whatever we had is no longer accepted
      location.assign(loginUrl(returnTo));
    },

    async signInAs(email, name, musicAdmin = false) {
      const res = await fetch(`${APP_CONFIG.services.frontpack}/dev/token`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ email, name, musicAdmin }),
      });
      if (res.status === 404) throw new Error('Development sign-in is not available. Is the API running in Development, and has `dotnet user-jwts create` been run once?');
      if (!res.ok) {
        const problem = await res.json().catch(() => null) as { detail?: string } | null;
        throw new Error(problem?.detail ?? `Sign-in failed (${res.status}).`);
      }
      const { accessToken, expiresAt } = await res.json() as { accessToken: string; expiresAt: string };
      const session: StoredSession = { accessToken, expiresAt, name: name || email.split('@')[0], email: email.toLowerCase() };
      localStorage.setItem(STORAGE_KEY, JSON.stringify(session));
      markSignedIn();
    },

    async signOut() {
      localStorage.removeItem(STORAGE_KEY);
    },

    async getAccessToken() {
      const s = load();
      if (!s) throw new NotSignedInError();
      return s.accessToken;
    },
  };
}
