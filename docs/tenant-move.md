# Moving sign-in to the "thefrontpack" tenant

The sign-in pages move from `slideshowmap.ciamlogin.com` to **`thefrontpack.ciamlogin.com`**. A tenant's subdomain can't
be renamed, so this means a new Entra External ID tenant. Signing in there gives each person a **new account ID**, so
their albums, files and share links are re-filed under it afterwards (`infra/move-accounts.mjs`). Google and Apple
sign-in come after, in the new tenant.

## 1. Set up the new tenant (you, about 30 minutes; nothing changes on the site yet)

In the [Entra admin center](https://entra.microsoft.com), signed in to your Azure (workforce) tenant:

1. **Create the tenant:** Entra ID → Overview → **Manage tenants** → Create → **External**.
   - **Tenant name** `FrontPack`
   - **Domain name** `thefrontpack`, which gives `thefrontpack.onmicrosoft.com`; sign-in is at
     `thefrontpack.ciamlogin.com`
   - **Location** United States
   - **Subscription** Azure subscription 1
   - **Resource group** new: `frontpack-identity`

   Then switch to the new tenant (Settings icon → Directories + subscriptions). Note its **tenant ID**.
2. **The API:** App registrations → New → **FrontPack API**, single tenant.
   - **Expose an API:** Application ID URI `api://<api-client-id>` (the default). Add a scope **`FrontPack.Access`**,
     consent by *admins and users*.
   - **Manifest:** set the access token version to 2 (`"requestedAccessTokenVersion": 2`).
   - **Token configuration:** Add optional claim → **Access** → `email`. The API records each person's email, and
     the move matches old and new accounts by it.
   - Note the **Application (client) ID**.
3. **The website:** App registrations → New → **FrontPack**. This name shows as "Sign in to access FrontPack".
   - **Platform:** *Single-page application*, with these redirect URIs:
     - `https://www.thefrontpack.com/login.html`
     - `https://gentle-sand-0bbdb070f.1.azurestaticapps.net/login.html`
     - `http://localhost:3000/login.html`
   - **API permissions:** Add → My APIs → FrontPack API → `FrontPack.Access`. Then **Grant admin consent**.
   - Note the **Application (client) ID**.
4. **User flow:** External Identities → User flows → New → *Sign up and sign in*.
   - Same as the old tenant: **Email with password** (or email one-time passcode), and collect **Display Name**.
   - Then Applications → add **FrontPack**.
5. **Branding:** upload the kit in `services/frontpack/entra-branding` (see its README).
6. **Send me the three IDs:** the tenant ID, the FrontPack API's client ID, and the FrontPack website's client ID.

## 2. The switch (me, then you merge; sign-in is briefly reset)

1. **Claude** opens a PR with the new values:
   - in both `services/*/infra/main.bicepparam`: `authAuthority = https://thefrontpack.ciamlogin.com/<tenant-id>/v2.0`,
     `apiClientId`, `externalTenantId`, and `apiScope = 'FrontPack.Access'`
   - in `web/app.config.azure.json`: `clientId`, `authority = https://thefrontpack.ciamlogin.com/<tenant-id>/`,
     and `apiScope = api://<api-client-id>/FrontPack.Access`
2. **You** merge it. The deploy switches the website and both APIs to the new tenant together.
   - Everyone is signed out.
   - Signing in now goes to `thefrontpack.ciamlogin.com`, where each person **signs up again with the same email**.
   - Their albums look empty until step 3.

## 3. Move the albums (both of us)

1. **Both people sign in once** on www.thefrontpack.com, through the new tenant. That creates their new accounts.
2. **You** give your user the data roles the script needs, then tell me:
   - **Storage Table Data Contributor** on `frontpackvf7ufwkbthdvm` and on `slideshownb5gbzoydsoxc`
   - **Storage Blob Data Contributor** on `slideshownb5gbzoydsoxc`
3. **Claude** runs `node infra/move-accounts.mjs frontpackvf7ufwkbthdvm slideshownb5gbzoydsoxc`. On its own it only
   reports who moves and how much. Then it runs with **`--copy`**:
   - files first, then the albums and file rows, under the new IDs
   - share links are pointed at the new owner, so they keep working
   - the old data is left as it is
4. **You** check the site: your albums, photos, playback, the account page's usage, and a share link.
5. **Claude** runs with **`--delete-old`**. This removes the old copies and old accounts, but only items whose copy
   is confirmed.
6. **You** remove the three data roles again.

If an old account has no email (the old tenant may not have put it in the token), the script says so. Pair those
by hand with `--map <old-id>=<new-id>`. The old ID is the person's Object ID in the old tenant (Users); the new ID
is the one in the new tenant.

## 4. Afterwards

- **Google and Apple sign-in** in the new tenant (separate steps).
- **The old tenant** (`slideshowmap`, resource group `slideshow-identity`): delete it once nothing uses it. That
  means removing its app registrations and users first; Entra then lets you delete it.
