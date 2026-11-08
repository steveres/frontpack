# FrontPack

The FrontPack website (www.thefrontpack.com) and its Slideshow app, which plays albums of photos and
videos, draws the route between them on a Google map, and plays the album's MP3s as background music.

| Folder | What |
|---|---|
| [shared/](shared/) | What every service shares: the website shell (`web/`: sign-in, nav, API client, base CSS) and the API set-up (`api/FrontPack.ServiceDefaults`: sign-in, CORS, rate limits, health) |
| [services/](services/) | Each service's website pages: `frontpack/web` (Home, About, Login, Account), `slideshow/web` (Albums, Album, Slideshow) |
| [web/](web/) | Builds the website from `shared/web` and `services/*/web` (TypeScript + CSS, no framework, no bundler) |
| [hosts/](hosts/) | The API hosts, one per service: `FrontPack.Host` and `Slideshow.Host` ([api/README.md](api/README.md): run, test, Entra and deploy set-up) |
| [infra/](infra/) | The deploy script (each resource group's Bicep template is in `services/*/infra`) |
| [docs/](docs/) | Design documents ([API design](docs/api-design.md)) |
| [.github/workflows/](.github/workflows/) | CI: API tests, website build and Bicep check on every pull request. Deploy: to Azure once CI passes on `main` ([setup](api/README.md#deploy-automatically-from-github)) |
| `prepare.bat`, `prepare.ps1` | Optional, Windows: prepare a local photo folder (HEIC → JPEG, date order) |

## Website

Requires [Node.js](https://nodejs.org).

```powershell
cd web
npm install          # once
npm run dev          # build, rebuild on save, serve on http://localhost:3000
```

Other scripts: `npm run build` (build once), `npm run watch` (rebuild on save), `npm start` (build once
and serve).

**Google Maps key:** put it in `web/apikey.txt` (git-ignored), or set `GOOGLE_MAPS_API_KEY`. The key
needs the **Maps JavaScript API** enabled. It is visible to anyone who loads the page (that's how
Maps keys work), so in the Google Cloud console restrict it to your site's addresses, e.g.
`http://localhost:3000/*` and the production URL.

**API addresses:** `"services"` in `web/app.config.json` gives each service's API address
(`"frontpack"`, `"slideshow"`). Locally: http://localhost:5170 and http://localhost:5171 (see [api/README.md](api/README.md)).

**Sign-in:** chosen in `web/app.config.json` (committed defaults; put personal overrides in the
git-ignored `app.config.local.json`, or pass `APP_CONFIG` as JSON in CI):

- `"auth": { "mode": "dev" }` (default) — local development without Entra. Run the API in Development
  (`dotnet run` in `hosts/FrontPack.Host` and in `hosts/Slideshow.Host`), open http://localhost:3000/login.html and sign in with any
  email; each email is a separate user. The API issues the token from `POST /dev/token`, which only
  exists in Development and uses the `dotnet user-jwts` signing key (run `dotnet user-jwts create`
  there once if you haven't).
- `"auth": { "mode": "entra", "clientId": "<spa-client-id>", "authority": "https://<tenant>.ciamlogin.com/<tenant-id>/", "apiScope": "api://<api-client-id>/FrontPack.Access" }`
  — Microsoft Entra External ID (see [api/README.md](api/README.md) for the tenant setup). Register
  `<site>/login.html` as the SPA redirect URI.

Pages:

| Page | What |
|---|---|
| `index.html` | FrontPack home: the welcome, over rotating cycling photos |
| `about.html` | The FrontPack story |
| `login.html` | Sign in; the first sign-in registers the account with the API |
| `trips.html` | Slideshow start page (the nav's **Trip Pix**): your trips, then everyone's public trips, with cover photos and Play; create a trip. `albums.html` redirects here |
| `trip.html?id=…` | One trip (an album in the API): upload photos, videos and MP3s (drag and drop); file list with previews, showing which files the slideshow will show; play; delete. `album.html?id=…` forwards here |
| `slideshow.html?album=…` | Play a trip (compiles it first if it changed) |
| `slideshow.html?share=…` | Play a shared trip: anyone with the link, no sign-in (trip page → **Share…**) |
| `slideshow.html?folder` | Play a folder from this computer (no sign-in needed). Hidden for now: nothing links to it, and plain `slideshow.html` goes to `trips.html` |
| `account.html` | Usage, sign out everywhere, delete account |

**What the slideshow shows:** photos and videos that have both a real capture date (EXIF for photos,
the video's own creation date) and a location (GPS), in date order. Anything missing either is kept in
the album but left out of the slideshow, and the album page marks it with the reason.

**Playing a local folder** (hidden for now: the Trip Pix page doesn't offer it): open
http://localhost:3000/slideshow.html?folder in Chrome or Edge and choose the folder. The
folder and settings are remembered. Optionally run `prepare.bat` on the folder first (drag the folder
onto it): it converts HEIC files to JPEG and writes `slideshow.json`, which puts the slideshow in
date-taken order and supplies the dates on the timeline.

### Layout

The site is put together from parts: the shell in `shared/web` and each service's pages in
`services/<name>/web`. Each part has `site/` (HTML pages, CSS, images, served from the site root) and
`ts/` (compiled into `/js`), laid out as if they were one folder, so the URLs are the same whichever part
a file is in. A Slideshow page imports `../utils/dom.js` as before; TypeScript's `rootDirs` finds it in
`shared/web/ts`. The build stops if two parts have a file at the same address.

```
shared/web/                     the shell every page uses
  site/                         components/navigation.html, css/base/, css/components/navigation.css, logo, icons, staticwebapp.config.json
  ts/
    api/                        api-client (apiFor(service): token, errors, 401 → sign in, upload progress), account-api
    auth/                       auth (provider interface), dev-auth, entra-auth (MSAL), session (sign in/out flows)
    components/navigation.ts    the header; menu links from each service's service.json
    utils/                      DOM helpers, formatting, connection speed
    config.ts                   per-environment settings (/app-config.json)
services/frontpack/web/         Home, About, Login, Account (site/ + ts/pages/), service.json
services/slideshow/web/         Albums, Album, Slideshow, service.json (menu link, /slideshows.html redirect)
  ts/
    pages/                      one entry module per HTML page
    components/                 thumbnail (lazy previews), media viewer, share panel; slideshow/ (mount, player, map, timeline, zoom, music, folder picker)
    api/albums-api.ts           the Slideshow API
    media/                      media types, GPS readers, local-folder source, API album source
    utils/settings.ts           the slideshow's saved settings
    slideshow-config.ts         tunable constants
web/
  parts.mjs                     finds the parts
  build.mjs                     tsc into .build/ (git-ignored), app-config.json (with the menu), --out: the parts as one site
  server.js                     local dev server (Express): serves the parts laid over one another
  tsconfig.json                 lists each part's ts/ in rootDirs (add a new service there)
```

Each part has a small `package.json` saying it is ES modules (TypeScript needs that). A new service's
pages go in `services/<name>/web` with a `service.json`; add its `ts/` to `rootDirs` in
`web/tsconfig.json` and its API address to `"services"`.

`mountSlideshow(element)` plays any `SlideshowContents` (slides + music) it is given; it does not know
where they come from: the folder picker (`media/folder-source.ts`) or an album on the API
(`media/api-source.ts`, which downloads each file when needed and prefetches the next). Photos from
the API come in the size that suits the screen and connection (`media/photo-size.ts`: 720, 1280 or
1920px, made by the API after upload, or the original on big screens); zooming in loads the original.
To check which size arrived, turn on **Show details** in the slideshow's settings (off by default): it
shows each slide's resolution as downloaded and its date taken.
