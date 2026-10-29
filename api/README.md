# Slideshow API

ASP.NET Core 9 API that stores each user's albums (photos, videos, music) and serves them to the
slideshow SPA. Design and endpoint reference: [docs/api-design.md](../docs/api-design.md).

```
api/
  src/Slideshow.Api/            the API
    Endpoints/                  account, albums, images + music
    Identity/                   token validation, logout cut-off, Microsoft Graph
    Media/                      file-type sniffing, EXIF/video date + GPS, natural sort, previews,
                                smaller versions of photos + the background worker that makes them
    Storage/                    Azure Blob / local folder; resize queue (Azure Storage queue / memory)
    Data/                       entities + data store (Table Storage; in memory / JSON file locally)
  tests/Slideshow.Api.Tests/    integration + unit tests
infra/                          Bicep template, parameters, deploy script
```

## Run the tests

```powershell
cd api
dotnet test
```
No Azure, database server or emulator needed: the tests run the real app with its data in memory and
files in a temp folder, with tokens signed by a test key.

To run the same tests against the Table Storage and queue code, start Azurite, then:
```powershell
npx -p azurite azurite --inMemoryPersistence --skipApiVersionCheck   # in another terminal
$env:SLIDESHOW_TEST_TABLES = 'azurite'; dotnet test; Remove-Item Env:SLIDESHOW_TEST_TABLES
```

## Run locally

Two launch profiles, both on http://localhost:5160:

| Profile | Album details | Media storage | Needs |
|---|---|---|---|
| `local` (default) | memory, saved to `App_Data/data.json` | folder `App_Data/media` | nothing |
| `local-azure` | Azurite (Table Storage emulator) | Azurite (Blob Storage emulator) | Node.js |

Photos waiting for their smaller versions are queued in memory with `local` (lost when the API stops:
photos still waiting then stay full size) and in Azurite's queue service with `local-azure`.

```powershell
cd api\src\Slideshow.Api
dotnet run                               # or: dotnet run --launch-profile local-azure
```

For `local-azure`, start Azurite first in another terminal (`--skipApiVersionCheck` because the Azure
SDK is usually newer than the emulator):
```powershell
npx -p azurite azurite --skipApiVersionCheck --location $env:TEMP\azurite
```

### Browse and try the API

Open http://localhost:5160 (Development only). It redirects to an API explorer (`/scalar`) listing
every endpoint, where you can send requests and upload files.

Every `/api/v1` call needs a token. Locally, make one with the .NET SDK's `user-jwts` tool (run in
`api\src\Slideshow.Api`):
```powershell
dotnet user-jwts create --scope Slideshow.Access --claim oid=11111111-2222-3333-4444-555555555555 --claim name="Local Dev" --output token
```
Paste it into the explorer's authentication box (Bearer). Use a different `oid` to act as another user.
Then: `POST /api/v1/account` (register) → `POST /api/v1/albums` → upload to `.../images` and `.../music`
→ `POST .../compile` → `GET /api/v1/albums/{id}`.

The website's development sign-in uses the same key: in Development only, `POST /dev/token`
`{ "email": "...", "name": "..." }` returns a token for that email (the same email is always the same
user). It is not mapped in any other environment.

These tokens only work in Development: they are signed with a key kept in your user secrets, and
configured in `appsettings.Development.json`. In Azure only Entra External ID tokens are accepted.

## Set up Entra External ID (once)

Done in the [Microsoft Entra admin center](https://entra.microsoft.com).

1. **Create an external tenant**: Entra ID → Overview → Manage tenants → Create → *External*.
   Note its **tenant id** and **subdomain** (`<subdomain>.onmicrosoft.com`).
   The token authority is `https://<subdomain>.ciamlogin.com/<tenant-id>/v2.0`.
2. Switch to the external tenant. **Register the API**: App registrations → New → "Slideshow API",
   single tenant.
   - Expose an API → set the Application ID URI (`api://<api-client-id>`) → Add a scope
     `Slideshow.Access` (who can consent: admins and users).
   - Manifest → set `"requestedAccessTokenVersion": 2`.
   - Note the **Application (client) id** → `apiClientId`.
3. **Register the SPA**: App registrations → New → "Slideshow SPA", platform *Single-page application*,
   redirect URI where the SPA will run (e.g. `http://localhost:5173`). API permissions → Add →
   My APIs → Slideshow API → `Slideshow.Access` → Grant admin consent.
4. **User flow**: External Identities → User flows → New → "Sign up and sign in" (email + password,
   or email one-time passcode; collect Display Name). Then Applications → add *Slideshow SPA*.
5. **Graph access** (lets the API delete the Entra user on account deletion and revoke sessions on
   logout). Optional: without it those two steps are skipped and logged; data is still deleted and
   old tokens are still refused.
   - Preferred, no secrets: in your **Azure** tenant, register a *multi-tenant* app "Slideshow Graph".
     Under Certificates & secrets → Federated credentials → Add → *Managed identity* → pick
     `slideshow-api-id` (created by the first deployment). Add Microsoft Graph **application**
     permissions `User.ReadWrite.All`, then grant admin consent **in the external tenant** by visiting
     `https://login.microsoftonline.com/<external-tenant-id>/adminconsent?client_id=<graph-app-client-id>`
     as an external-tenant admin. Set `graphClientId`.
   - Fallback: register the Graph app in the external tenant, add the same permission with admin
     consent, create a client secret, and pass it as `graphClientSecret` at deployment (stored as a
     Container Apps secret).

## Deploy to Azure

Needs the [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli), an Azure subscription,
the .NET 9 SDK and Node.js. Docker is **not** needed; the .NET SDK builds and pushes the image.

1. Entra values: API side in [infra/main.bicepparam](../infra/main.bicepparam), website side in
   [web/app.config.azure.json](../web/app.config.azure.json) (already filled in for the Slideshow tenant).
2. Deploy:
   ```powershell
   az login
   .\infra\deploy.ps1 -ResourceGroup slideshow-rg -Location eastus
   ```
   It registers the resource providers (first time), creates or updates the resources, builds and
   pushes the API image, deploys it, then packages the website with production settings (its
   `app-config.json` points at the deployed API and uses Entra sign-in) and uploads it to Static Web
   Apps. It prints both addresses. Run it again to ship a new version; `-SkipApi` ships only the website.
3. First time only: add `<website address>/login.html` as a redirect URI (platform: Single-page
   application) on the **Slideshow Website** app registration in Entra.

What gets created: Static Web App (Free plan; `eastus2`, since Static Web Apps isn't offered in every
region), Container Apps environment + app (scale 0–3), Azure Container Registry, Storage account
(private `media` container for files and compiled manifests, tables `Users`, `Albums`, `Media` and
`Shares` for album details, queue `resize` for photos waiting for their smaller versions; shared keys
off, 7-day soft delete), Log Analytics + Application Insights, and a user-assigned managed identity with
exactly the roles it needs (AcrPull, Storage Blob Data Contributor, Storage Table Data Contributor,
Storage Queue Data Contributor). The API scales up for web requests and for photos waiting in the queue. The website's address is always allowed by the API's
CORS settings.

Notes
- To browse the album details yourself, open the storage account's *Storage browser* in the Azure
  portal (or Azure Storage Explorer); your account needs the *Storage Table Data Reader* role on it.
- The first request after an idle period is slow: the app scales from zero. Set `apiMinReplicas = 1`
  in `main.bicepparam` to keep one instance warm (costs more).
- Restrict the Google Maps key to the website's address in the Google Cloud console.

### Deploy automatically from GitHub

[.github/workflows/deploy.yml](../.github/workflows/deploy.yml) runs the same `deploy.ps1` after CI
passes on `main` (every merged pull request), or from the Actions tab (**Deploy → Run workflow**). It
signs in to Azure with OpenID Connect, so no password is stored in GitHub. Set it up once, after a first
manual deploy (which registers the resource providers and creates the resource group):

1. Run these in PowerShell (they print the values GitHub needs in step 2), signed in to the Azure
   tenant that holds the subscription (`az login`; not the Entra External ID tenant used for sign-in)
   and to GitHub (`gh auth login`). You need rights to create app registrations, and Owner on the
   resource group:
   ```powershell
   $rg     = 'slideshow-rg'
   $repo   = 'steveres/frontpack'
   $sub    = az account show --query id --output tsv
   $tenant = az account show --query tenantId --output tsv
   # How GitHub names this repository to Azure: newer repositories include the account and repository
   # ids ("repo:steveres@3514811/frontpack@1405865751"), older ones just the names ("repo:steveres/frontpack").
   $prefix = gh api "repos/$repo/actions/oidc/customization/sub" --jq .sub_claim_prefix
   if (-not $prefix) { $prefix = "repo:$repo" }

   # The identity GitHub deploys as
   $appId = az ad app create --display-name 'FrontPack GitHub deploy' --query appId --output tsv
   az ad sp create --id $appId --output none

   # Trust GitHub, but only for this repository's "production" environment
   @{ name = 'github-production'; issuer = 'https://token.actions.githubusercontent.com'
      subject = "${prefix}:environment:production"; audiences = @('api://AzureADTokenExchange') } |
     ConvertTo-Json | Set-Content -Encoding ascii federated-credential.json
   az ad app federated-credential create --id $appId --parameters federated-credential.json --output none
   Remove-Item federated-credential.json

   # Rights on the resource group only: change resources, and give the API's identity its roles (main.bicep does)
   $scope = az group show --name $rg --query id --output tsv
   az role assignment create --assignee $appId --role 'Contributor' --scope $scope --output none
   az role assignment create --assignee $appId --role 'Role Based Access Control Administrator' --scope $scope --output none

   # What to enter in GitHub (step 2)
   "AZURE_CLIENT_ID=$appId"; "AZURE_TENANT_ID=$tenant"; "AZURE_SUBSCRIPTION_ID=$sub"
   "AZURE_RESOURCE_GROUP=$rg"; "AZURE_LOCATION=$(az group show --name $rg --query location --output tsv)"
   ```
2. In GitHub: **Settings → Environments → New environment** named `production`. On it, add the five
   lines printed above as **environment variables**: the part before `=` is the name (e.g.
   `AZURE_CLIENT_ID`), the part after is the value (e.g. a GUID). Add the Google Maps key as the
   **environment secret** `GOOGLE_MAPS_API_KEY`. To approve each deploy before it starts, tick
   **Required reviewers** and add yourself. Or, in the same PowerShell window:
   ```powershell
   gh variable set AZURE_CLIENT_ID       --repo $repo --env production --body $appId
   gh variable set AZURE_TENANT_ID       --repo $repo --env production --body $tenant
   gh variable set AZURE_SUBSCRIPTION_ID --repo $repo --env production --body $sub
   gh variable set AZURE_RESOURCE_GROUP  --repo $repo --env production --body $rg
   gh variable set AZURE_LOCATION        --repo $repo --env production --body (az group show --name $rg --query location --output tsv)
   gh secret set GOOGLE_MAPS_API_KEY     --repo $repo --env production   # asks for the key
   ```

Each run deploys everything (infrastructure, API, website) from the commit CI tested, one at a time.
It skips the resource provider check (`-SkipProviders`): that needs rights on the whole subscription.

If the **Azure login** step fails:
- *"Ensure 'client-id' and 'tenant-id' are supplied"*: the variables are missing or misnamed; check
  them under **Settings → Environments → production**.
- *"AADSTS700213: No matching federated identity record"*: the trust's subject doesn't match what
  GitHub sent (the error shows it). Compare with
  `az ad app federated-credential list --id <AZURE_CLIENT_ID>` and update it with
  `az ad app federated-credential update`.

## Configuration reference

| Setting | Meaning |
|---|---|
| `Auth:Authority`, `Auth:Audiences`, `Auth:RequiredScope` | Entra External ID token validation |
| `Tables:Provider` (`Local`/`AzureTable`), `Tables:LocalPath` or `Tables:ServiceUri` / `Tables:ConnectionString` | album details |
| `Storage:Provider` (`FileSystem`/`AzureBlob`), `Storage:BlobServiceUri` or `Storage:ConnectionString` | media bytes |
| `Graph:TenantId`, `Graph:ClientId`, `Graph:ClientSecret` | Entra user deletion / session revocation |
| `Resize:Provider` (`Memory`/`AzureQueue`), `Resize:QueueServiceUri` or `Resize:ConnectionString`, `Resize:QueueName`, `Resize:MaxAttempts` | queue of photos waiting for their smaller versions |
| `Limits:*` | per-kind file size, per-user quota, files per album, albums per user, requests per minute |
| `Cors:AllowedOrigins` | SPA origins |

Environment variables use `__` for `:` (e.g. `Tables__Provider`).

## Data changes

Tables have no schema: a new field is just a new property in `Data/TableDataStore.cs` (and in the
entity). Rows written before it existed simply lack it, so give it a sensible default when reading.
