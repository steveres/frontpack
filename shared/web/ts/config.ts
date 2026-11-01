// Per-environment settings, read at startup. (Each service keeps its own tunable constants.)

import type { AppConfig } from './app-config-types.js';

// /app-config.json is written by build.mjs (locally) or infra/deploy.ps1 (Azure), so the same compiled
// scripts run everywhere. Modules that import this file wait for it (top-level await).
const loaded = await (await fetch('/app-config.json', { cache: 'no-cache' })).json() as AppConfig & { googleMapsApiKey?: string };

export const APP_CONFIG: AppConfig = loaded;
export const GOOGLE_MAPS_API_KEY = loaded.googleMapsApiKey ?? '';
