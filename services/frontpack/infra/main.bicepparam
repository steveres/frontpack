using 'main.bicep'

// The FrontPack platform, in frontpack-rg. Entra External ID tenant "FrontPack" (frontpack.onmicrosoft.com);
// none of these are secrets.
param authAuthority = 'https://frontpack.ciamlogin.com/5d39e5a1-382c-46d1-9e63-37212ba7870e/v2.0'
param apiClientId = '8bb70d3f-c621-497f-a1a9-0be9c0d6aac4'   // "FrontPack API" app registration
param externalTenantId = '5d39e5a1-382c-46d1-9e63-37212ba7870e'
param graphClientId = '4e12935d-79d2-47f3-9c11-4a502333330d'   // "FrontPack Graph" (company tenant, multi-tenant): deletes Entra users and revokes their sessions

// The website (Static Web App) and its custom domain are always allowed; add more origins here if needed.
param allowedOrigins = []
param webLocation = 'eastus2'      // Static Web Apps region (not offered in eastus)

// The www CNAME (DNS at GoDaddy) points to this Static Web App. Also a sign-in redirect on the website's app
// registration, and an allowed address on the Google Maps key.
param customDomain = 'www.thefrontpack.com'

// 0 = free, but the first request after ~5 idle minutes waits about 20 s; 1 = always instant, about US$10/month.
param apiMinReplicas = 1
