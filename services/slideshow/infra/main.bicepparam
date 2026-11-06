using 'main.bicep'

// The Slideshow service, in slideshow-rg, running in the FrontPack platform (frontpack-rg).
param platformResourceGroup = 'frontpack-rg'
param authAuthority = 'https://frontpack.ciamlogin.com/5d39e5a1-382c-46d1-9e63-37212ba7870e/v2.0'
param apiClientId = '8bb70d3f-c621-497f-a1a9-0be9c0d6aac4'   // "FrontPack API" app registration

// The website's custom domain (on frontpack-web), allowed to call the API.
param customDomain = 'www.thefrontpack.com'
param allowedOrigins = []

// 0 = free, but the first request after ~5 idle minutes waits about 20 s; 1 = always instant, about US$10/month.
param apiMinReplicas = 1
