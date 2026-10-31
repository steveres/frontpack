// The website's parts, which build.mjs and server.js put together into one site:
//   shared/web             the site shell: sign-in, nav, API client, base CSS
//   services/<name>/web    each service's pages (FrontPack's own Home, About, Account and Login among them)
// Each part has site/ (pages, CSS, images: served from the site root), ts/ (compiled into /js) and, for a
// service, service.json (its menu links and its redirects for staticwebapp.config.json).
// A new service's ts/ must also be listed in tsconfig.json "rootDirs".
import { existsSync, readdirSync, readFileSync } from 'node:fs';
import { join, relative } from 'node:path';
import { fileURLToPath } from 'node:url';

export const WEB = fileURLToPath(new URL('.', import.meta.url));
export const BUILD = join(WEB, '.build');
/** Where tsc writes: .build/js/<part path>/ts/... (see tsconfig.json). */
export const JS_OUT = join(BUILD, 'js');
/** Files made by the build (app-config.json, vendor/), served on top of the parts locally. */
export const GENERATED = join(BUILD, 'site');

const ROOT = join(WEB, '..');

export const readJson = file => (existsSync(file) ? JSON.parse(readFileSync(file, 'utf8')) : {});

export function parts() {
  const servicesDir = join(ROOT, 'services');
  const services = readdirSync(servicesDir, { withFileTypes: true })
    .filter(d => d.isDirectory() && existsSync(join(servicesDir, d.name, 'web')))
    .map(d => ({ name: d.name, dir: join(servicesDir, d.name, 'web') }));
  return [{ name: 'shared', dir: join(ROOT, 'shared', 'web') }, ...services].map(p => ({
    ...p,
    site: join(p.dir, 'site'),
    ts: join(p.dir, 'ts'),
    js: join(JS_OUT, relative(ROOT, p.dir), 'ts'),
    service: readJson(join(p.dir, 'service.json')),
  }));
}

const filesIn = dir => (existsSync(dir) ? readdirSync(dir, { recursive: true, withFileTypes: true }).filter(e => e.isFile())
  .map(e => relative(dir, join(e.parentPath, e.name)).replaceAll('\\', '/')) : []);

/** Throws if two parts would put a file at the same address (pages and assets, or compiled scripts). */
export function checkNoClashes(all) {
  const seen = new Map();
  for (const part of all) {
    const urls = [...filesIn(part.site).map(f => '/' + f), ...filesIn(part.ts).filter(f => f.endsWith('.ts')).map(f => '/js/' + f.replace(/\.ts$/, '.js'))];
    for (const url of urls) {
      if (seen.has(url)) throw new Error(`${url} is in both ${seen.get(url)} and ${part.name}: rename one of them.`);
      seen.set(url, part.name);
    }
  }
}
