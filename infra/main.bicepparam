using 'main.bicep'

// Entra External ID tenant "Slideshow" (slideshowmap.onmicrosoft.com). None of these are secrets.
param authAuthority = 'https://slideshowmap.ciamlogin.com/ddba6b11-5443-454a-916d-3a9237c12525/v2.0'
param apiClientId = '107ba552-513b-4b86-ba0f-e8e1ca1c7afd'   // "Slideshow API" app registration
param externalTenantId = 'ddba6b11-5443-454a-916d-3a9237c12525'
param graphClientId = ''          // set once the Graph app is set up; empty = skip Entra-side delete/revoke

// The website (Static Web App) is always allowed; add more origins here if needed.
param allowedOrigins = []
param webLocation = 'eastus2'      // Static Web Apps region (not offered in eastus)

// Start-up speed: 0 = free, but the first request after ~5 idle minutes waits about 20 s (measured
// October 2026, nearly all of it Azure preparing an instance); 1 = always instant, about US$10/month.
param apiMinReplicas = 1
