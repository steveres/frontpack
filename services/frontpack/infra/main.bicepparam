using 'main.bicep'

// The FrontPack platform, in frontpack-rg. Entra External ID tenant "Slideshow" (slideshowmap.onmicrosoft.com);
// none of these are secrets.
param authAuthority = 'https://slideshowmap.ciamlogin.com/ddba6b11-5443-454a-916d-3a9237c12525/v2.0'
param apiClientId = '107ba552-513b-4b86-ba0f-e8e1ca1c7afd'   // "Slideshow API" app registration
param externalTenantId = 'ddba6b11-5443-454a-916d-3a9237c12525'
param graphClientId = ''          // set once the Graph app is set up; empty = skip Entra-side delete/revoke

// The website (Static Web App) and its custom domain are always allowed; add more origins here if needed.
param allowedOrigins = []
param webLocation = 'eastus2'      // Static Web Apps region (not offered in eastus)

// Empty until the cut-over: www.thefrontpack.com stays on slideshow-web (infra/main.bicep) until then. At the
// cut-over the www CNAME (DNS at GoDaddy) moves to this Static Web App and this becomes 'www.thefrontpack.com'.
param customDomain = ''

// 0 = free, but the first request after ~5 idle minutes waits about 20 s; 1 = always instant, about US$10/month.
param apiMinReplicas = 1
