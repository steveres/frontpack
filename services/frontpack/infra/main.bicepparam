using 'main.bicep'

// The FrontPack platform, in frontpack-rg. Entra External ID tenant "Slideshow" (slideshowmap.onmicrosoft.com);
// none of these are secrets.
param authAuthority = 'https://slideshowmap.ciamlogin.com/ddba6b11-5443-454a-916d-3a9237c12525/v2.0'
param apiClientId = '107ba552-513b-4b86-ba0f-e8e1ca1c7afd'   // "FrontPack API" app registration (formerly "Slideshow API")
param externalTenantId = 'ddba6b11-5443-454a-916d-3a9237c12525'
param graphClientId = ''          // set once the Graph app is set up; empty = skip Entra-side delete/revoke

// The website (Static Web App) and its custom domain are always allowed; add more origins here if needed.
param allowedOrigins = []
param webLocation = 'eastus2'      // Static Web Apps region (not offered in eastus)

// The www CNAME (DNS at GoDaddy) points to this Static Web App. Also a sign-in redirect on the website's app
// registration, and an allowed address on the Google Maps key.
param customDomain = 'www.thefrontpack.com'

// 0 = free, but the first request after ~5 idle minutes waits about 20 s; 1 = always instant, about US$10/month.
param apiMinReplicas = 1
