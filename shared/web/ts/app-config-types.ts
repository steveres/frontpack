// Shape of the per-environment settings that build.mjs writes to /app-config.json.

/** A service whose API the website calls: "frontpack" (accounts, sign-in) or one of the services. */
export type ServiceName = 'frontpack' | 'slideshow';

export interface AppConfig {
  /** Where each service's API runs, without a trailing slash, e.g. { "frontpack": "http://localhost:5160" } */
  services: Record<ServiceName, string>;
  auth: DevAuthConfig | EntraAuthConfig;
  /** The site's menu, gathered by build.mjs from each service's service.json, in order. */
  nav: NavLink[];
}

export interface NavLink {
  label: string;
  href: string;
  /** Other pages in the same section, which also mark this link as current. */
  also?: string[];
}

/** Local development: sign in with any email; the API (in Development) issues the token. */
export interface DevAuthConfig { mode: 'dev'; }

/** Microsoft Entra External ID. */
export interface EntraAuthConfig {
  mode: 'entra';
  /** The SPA app registration's client id. */
  clientId: string;
  /** Including the tenant, e.g. https://<tenant-subdomain>.ciamlogin.com/<tenant-id>/ */
  authority: string;
  /** The API's delegated scope, e.g. api://<api-client-id>/FrontPack.Access */
  apiScope: string;
}
