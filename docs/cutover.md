# Cut-over: from the single API to the platform and services (done 2026-11-01)

A record of how it was done. The single-API template, `deploy.ps1` and the combined host it mentions are gone.

Moves www.thefrontpack.com from the single API in `slideshow-rg` to:
- the FrontPack platform in `frontpack-rg`
- Slideshow running in that platform

Albums and files stay where they are, in `slideshow-rg`. Only the accounts (`Users` table) are copied.

| | Before | After |
|---|---|---|
| Website | `slideshow-web` (slideshow-rg) | `frontpack-web` (frontpack-rg), gentle-sand-0bbdb070f.1.azurestaticapps.net |
| Accounts | `Users` in slideshownb5gbzoydsoxc | `Users` in frontpackvf7ufwkbthdvm |
| FrontPack API | inside `slideshow-api` | `frontpack-api` (frontpack-env) |
| Slideshow API | `slideshow-api` in `slideshow-env` | `slideshow-api` in `frontpack-env` (same name, new address) |
| Deploy | `infra/deploy.ps1` | `infra/deploy-services.ps1` |

## Before the window (no downtime)

1. **Roles** (you, with Owner rights):
   - The GitHub deploy identity needs Contributor and Role Based Access Control Administrator on `frontpack-rg`.
   - You need Storage Table Data Contributor on `frontpack-rg`'s storage account, for the accounts copy.
2. **New website address** (you):
   - Entra app registration "Slideshow Website": add the SPA redirect `https://gentle-sand-0bbdb070f.1.azurestaticapps.net/login.html`.
   - Google Maps key: allow `https://gentle-sand-0bbdb070f.1.azurestaticapps.net/*`.
3. **DNS** (you, optional, the day before): lower the TTL of the `www` CNAME at GoDaddy to 600 seconds, so the switch spreads faster.
4. **Dry run** (Claude): `node infra/copy-users.mjs slideshownb5gbzoydsoxc frontpackvf7ufwkbthdvm --dry-run` reads both tables.

## The window (about 15–30 minutes of downtime, mostly DNS)

| # | Who | Step | Check |
|---|---|---|---|
| 1 | Claude | Copy the accounts: `node infra/copy-users.mjs slideshownb5gbzoydsoxc frontpackvf7ufwkbthdvm` | "0 of the source's missing" |
| 2 | Claude | Delete the old API: `az containerapp delete -g slideshow-rg -n slideshow-api --yes`. **The site stops working here.** | |
| 3 | Claude | Deploy everything except the domain: `.\infra\deploy-services.ps1 -SkipCustomDomain -SkipProviders` | Both health checks "ok" |
| 4 | You | On https://gentle-sand-0bbdb070f.1.azurestaticapps.net: sign in, open Albums, play an album, open the account page (usage comes from Slideshow over the internal port) | All work |
| 5 | Claude | Free the domain: `az staticwebapp hostname delete -n slideshow-web -g slideshow-rg --hostname www.thefrontpack.com --yes` | |
| 6 | You | GoDaddy: point the `www` CNAME to `gentle-sand-0bbdb070f.1.azurestaticapps.net` | `nslookup www.thefrontpack.com` shows the new name |
| 7 | Claude | Merge the Phase 3 PR. The Deploy workflow runs `deploy-services.ps1`, which binds the domain (Azure checks the CNAME and issues the certificate). | Workflow green |
| 8 | You | Repeat step 4 on https://www.thefrontpack.com, plus an old share link | All work |

If step 4 fails, the old setup can't simply be switched back, because its API is gone. Two ways out:
- Fix forward. The new stack's logs are in `frontpack-logs`.
- Restore the old setup. Run `infra/deploy.ps1 -ResourceGroup slideshow-rg` from `main` before the Phase 3 PR is merged. It recreates `slideshow-api` in `slideshow-env` from `infra/main.bicep`. The data was never moved, so nothing is lost.

## After (done 2026-11-01, in the cleanup PR)

- Delete what only the single API used, in `slideshow-rg`:
  - `slideshow-web`, `slideshow-env`, `slideshow-logs`, `slideshow-insights`
  - the old registry
  - the `Users` table in Slideshow's storage
- Remove `infra/main.bicep`, `infra/deploy.ps1` and `hosts/Combined.Host`. Local development then runs the two hosts.
- Remove your temporary Storage Table Data roles, and the GitHub variable `AZURE_RESOURCE_GROUP` (no longer used).
- Remove the old website address from the Entra redirect URIs and the Maps key.
