// Microsoft Entra External ID sign-in with MSAL (redirect flow).
// MSAL is loaded as a classic script from /vendor (copied there by build.mjs); only its types are imported.

import type { AccountInfo, Configuration, IPublicClientApplication } from '@azure/msal-browser';
import { APP_CONFIG } from '../config.js';
import type { EntraAuthConfig } from '../app-config-types.js';
import { loginUrl, markSignedIn, NotSignedInError, type AuthProvider } from './auth.js';

type Msal = typeof import('@azure/msal-browser');

/** Loads /vendor/msal-browser.min.js once; it defines window.msal. */
function loadMsal(): Promise<Msal> {
  const w = window as unknown as { msal?: Msal };
  if (w.msal) return Promise.resolve(w.msal);
  return new Promise((resolve, reject) => {
    const script = document.createElement('script');
    script.src = '/vendor/msal-browser.min.js';
    script.onload = () => (w.msal ? resolve(w.msal) : reject(new Error('MSAL did not load.')));
    script.onerror = () => reject(new Error('Could not load the sign-in library.'));
    document.head.append(script);
  });
}

/**
 * MSAL marks a sign-in "in progress" (in session storage, with the app's client id) until the redirect returns,
 * and only that same app clears the mark. One left by another app, such as the site's app registration before the
 * move to the frontpack tenant, would otherwise block every sign-in with "interaction_in_progress".
 */
function clearOtherAppsInteraction(clientId: string): void {
  const KEY = 'msal.interaction.status';
  for (const storage of [sessionStorage, localStorage]) {
    try {
      const raw = storage.getItem(KEY);
      if (raw && (JSON.parse(raw) as { clientId?: string }).clientId !== clientId) storage.removeItem(KEY);
    } catch { /* unreadable: leave it to MSAL */ }
  }
}

export async function createEntraAuth(): Promise<AuthProvider> {
  const config = APP_CONFIG.auth as EntraAuthConfig;
  const msal = await loadMsal();
  const msalConfig: Configuration = {
    auth: {
      clientId: config.clientId,
      authority: config.authority,
      // External ID hosts (*.ciamlogin.com) aren't in Microsoft's cloud discovery; trust the host directly.
      knownAuthorities: [new URL(config.authority).host],
      redirectUri: `${location.origin}/login.html`,       // must be registered on the SPA app registration
      postLogoutRedirectUri: `${location.origin}/`,
    },
    cache: { cacheLocation: 'localStorage' },               // stay signed in across tabs
  };
  clearOtherAppsInteraction(config.clientId);
  const app: IPublicClientApplication = await msal.createStandardPublicClientApplication(msalConfig);

  // Completes a sign-in redirect if this page load is the return from Entra.
  const result = await app.handleRedirectPromise();
  if (result?.account) { app.setActiveAccount(result.account); markSignedIn(); }
  // Only accounts from this tenant: the browser may still hold one from the tenant the site used before.
  const tenant = new URL(config.authority).pathname.split('/').find(Boolean)?.toLowerCase();
  const ours = (a: AccountInfo | null) => a && (!tenant || a.tenantId?.toLowerCase() === tenant || a.homeAccountId.toLowerCase().endsWith(`.${tenant}`)) ? a : null;
  const current = (): AccountInfo | null => ours(app.getActiveAccount()) ?? app.getAllAccounts().find(a => ours(a)) ?? null;
  const scopes = [config.apiScope];
  let redirecting = false; // a token redirect has started: further requests just wait for it

  return {
    mode: 'entra',

    account() {
      const a = current();
      return a ? { name: a.name ?? a.username, email: a.username } : null;
    },

    async signIn(returnTo) {
      if (location.pathname !== '/login.html') { location.assign(loginUrl(returnTo)); return; }
      // After Entra, MSAL lands on redirectUri and then returns to this exact URL
      // (login.html?returnTo=...), so the login page still knows where to go next.
      // prompt 'login': always show the sign-in page. Otherwise Entra may try a Microsoft account the
      // browser is already signed in to (e.g. the tenant's admin) and fail with AADSTS50020.
      await app.loginRedirect({ scopes, prompt: 'login' });
    },

    async signOut() {
      const account = current();
      await app.logoutRedirect(account ? { account } : undefined);
    },

    async getAccessToken() {
      const account = current();
      if (!account) throw new NotSignedInError();
      try {
        return (await app.acquireTokenSilent({ scopes, account })).accessToken;
      } catch (err) {
        if (err instanceof msal.InteractionRequiredAuthError && !redirecting) {
          redirecting = true;
          await app.acquireTokenRedirect({ scopes, account }); // comes back to this page
        }
        throw err;
      }
    },
  };
}
