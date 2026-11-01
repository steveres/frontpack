// Builds the website from its parts (see parts.mjs). The TypeScript compiles 1:1 into native ES modules
// (no bundler); per-environment settings are not compiled in but read at startup from /app-config.json.
//   npm run build     build once (into .build/)
//   npm run watch     rebuild whenever a .ts file is saved
//   npm run dev       watch + serve on http://localhost:3000
//   npm start         build once + serve
//   node build.mjs --out <dir>   production package in <dir>: the parts as one site (used by infra/deploy.ps1)
//
// app-config.json is made from:
//   app.config.json         committed defaults (local development)
//   app.config.local.json   optional, git-ignored: your local overrides (not used with --out)
//   APP_CONFIG              optional environment variable (JSON): overrides both (e.g. production values)
//   apikey.txt / GOOGLE_MAPS_API_KEY   the Google Maps key (git-ignored file or environment variable)
//   each service's service.json        the menu links ("nav")
import { spawn } from 'node:child_process';
import { cpSync, copyFileSync, existsSync, mkdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { BUILD, checkNoClashes, GENERATED, JS_OUT, parts, readJson, WEB } from './parts.mjs';

process.chdir(WEB);
const VENDOR = [['node_modules/@azure/msal-browser/lib/msal-browser.min.js', 'vendor/msal-browser.min.js']];

/** Writes <siteDir>/app-config.json. */
function writeAppConfig(siteDir, all, { local }) {
  const config = {
    ...readJson('app.config.json'),
    ...(local ? readJson('app.config.local.json') : {}),
    ...(process.env.APP_CONFIG ? JSON.parse(process.env.APP_CONFIG) : {}),
  };
  const key = (process.env.GOOGLE_MAPS_API_KEY ?? (existsSync('apikey.txt') ? readFileSync('apikey.txt', 'utf8') : '')).trim();
  if (key && !/^[\w-]+$/.test(key)) throw new Error('apikey.txt should contain only the API key (letters, digits, - and _).');
  if (!key) console.warn('No Google Maps key (apikey.txt or GOOGLE_MAPS_API_KEY): the map will show a "no key" message.');
  if (!config.auth?.mode) throw new Error('The config needs "auth.mode".');
  const missing = all.filter(p => p.name !== 'shared' && !config.services?.[p.name]).map(p => p.name);
  if (missing.length) throw new Error(`The config's "services" needs the API address of: ${missing.join(', ')}.`);

  const nav = all.flatMap(p => p.service.nav ?? []).sort((a, b) => (a.order ?? 50) - (b.order ?? 50)).map(({ order, ...link }) => link);
  mkdirSync(siteDir, { recursive: true });
  writeFileSync(join(siteDir, 'app-config.json'), JSON.stringify({ ...config, nav, googleMapsApiKey: key }, null, 2) + '\n');
  console.log(`Config: ${Object.entries(config.services).map(([name, url]) => `${name} API ${url}`).join(', ')}; sign-in "${config.auth.mode}".`);
}

/** The shell's staticwebapp.config.json with each service's routes (redirects) in front of its own. */
function writeStaticWebAppConfig(siteDir, all) {
  const config = readJson(join(all[0].site, 'staticwebapp.config.json'));
  config.routes = [...all.flatMap(p => p.service.routes ?? []), ...(config.routes ?? [])];
  writeFileSync(join(siteDir, 'staticwebapp.config.json'), JSON.stringify(config, null, 2) + '\n');
}

function copyVendor(siteDir) {
  mkdirSync(join(siteDir, 'vendor'), { recursive: true });
  for (const [from, to] of VENDOR) copyFileSync(from, join(siteDir, to));
}

/** Runs tsc; resolves with its exit code. In watch mode it keeps running. */
function tsc(args) {
  return new Promise(resolve => {
    const child = spawn('npx', ['tsc', '-p', '.', ...args], { stdio: 'inherit', shell: process.platform === 'win32' });
    child.on('exit', code => resolve(code ?? 1));
  });
}

const argv = process.argv.slice(2);
const watch = argv.includes('--watch');
const serve = argv.includes('--serve');
const outIndex = argv.indexOf('--out');
const all = parts();
checkNoClashes(all);

if (outIndex >= 0) {
  // Production package: the parts' files as one site, with its own config and compiled scripts.
  const out = argv[outIndex + 1];
  if (!out) throw new Error('--out needs a directory.');
  const compiled = join(BUILD, 'package-js');
  rmSync(out, { recursive: true, force: true });
  rmSync(compiled, { recursive: true, force: true });
  for (const part of all) if (existsSync(part.site)) cpSync(part.site, out, { recursive: true });
  writeAppConfig(out, all, { local: false });
  writeStaticWebAppConfig(out, all);
  copyVendor(out);
  const code = await tsc(['--outDir', compiled, '--sourceMap', 'false']);
  if (code !== 0) { console.error('Build failed.'); process.exit(code); }
  for (const part of all) {
    const js = part.js.replace(JS_OUT, compiled);
    if (existsSync(js)) cpSync(js, join(out, 'js'), { recursive: true });
  }
  console.log(`Packaged the site in ${out}.`);
} else {
  // Claim the port before touching any files, so a second `npm run dev` stops cleanly
  // instead of wiping the output another one is serving.
  if (serve) {
    try { await (await import('./server.js')).start(); }
    catch (err) { console.error(err.message ?? err); process.exit(1); }
  }

  writeAppConfig(GENERATED, all, { local: true });
  copyVendor(GENERATED);
  rmSync(JS_OUT, { recursive: true, force: true }); // no stale modules from renamed/deleted .ts files

  if (watch) {
    await tsc(['--watch', '--preserveWatchOutput']);
  } else {
    const code = await tsc([]);
    if (code !== 0) { console.error('Build failed.'); process.exitCode = code; }
    else console.log(`Built ${JS_OUT}.`);
  }
}
