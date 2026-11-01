using 'main.bicep'

// The Slideshow service, in slideshow-rg, running in the FrontPack platform (frontpack-rg).
param platformResourceGroup = 'frontpack-rg'
param authAuthority = 'https://slideshowmap.ciamlogin.com/ddba6b11-5443-454a-916d-3a9237c12525/v2.0'
param apiClientId = '107ba552-513b-4b86-ba0f-e8e1ca1c7afd'   // "Slideshow API" app registration

// The website's custom domain (on frontpack-web), allowed to call the API.
param customDomain = 'www.thefrontpack.com'
param allowedOrigins = []

// 0 = free, but the first request after ~5 idle minutes waits about 20 s; 1 = always instant, about US$10/month.
param apiMinReplicas = 1
