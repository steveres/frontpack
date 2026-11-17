<#
  Builds and deploys FrontPack as a platform plus services (replaces deploy.ps1 after the cut-over).
    1. Registers the Azure resource providers used (needed once on a new subscription).
    2. The platform (frontpack-rg): registry, Container Apps environment, monitoring, accounts, website; then
       FrontPack's API image (hosts\FrontPack.Host) and the frontpack-api container app.
    3. Slideshow (slideshow-rg): storage and identity; then its image (hosts\Slideshow.Host) and the
       slideshow-api container app in the platform's environment, with the share links' domain
       (shareDomain in its main.bicepparam) once that domain's DNS records are in place.
    4. Packages the website with each API's address and uploads it to the platform's Static Web App.
  Usage:   .\infra\deploy-services.ps1                     everything
           .\infra\deploy-services.ps1 -SkipSlideshow -SlideshowApiUrl https://<current slideshow-api>
                                         before the cut-over: the platform only, next to the running single API
           .\infra\deploy-services.ps1 -SkipProviders      GitHub Actions: its identity has rights on the
                                                           resource groups only
           .\infra\deploy-services.ps1 -SkipCustomDomain   the website without its custom domain (e.g. while
                                                           the domain's CNAME doesn't point to it yet)
  Needs:   Azure CLI (az login done), .NET 9 SDK, Node.js; services\*\infra\main.bicepparam and
           web\app.config.azure.json. Written for Windows PowerShell 5.1.
#>
param(
  [string]$PlatformResourceGroup = 'frontpack-rg',
  [string]$SlideshowResourceGroup = 'slideshow-rg',
  [string]$Location = 'eastus',
  [switch]$SkipSlideshow,
  [string]$SlideshowApiUrl = '',
  [switch]$SkipCustomDomain,
  [switch]$SkipProviders
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$web = Join-Path $root 'web'
if ($SkipSlideshow -and -not $SlideshowApiUrl) { throw '-SkipSlideshow needs -SlideshowApiUrl: the website still has to reach a Slideshow API.' }

# Runs an external program and fails only on its exit code. Windows PowerShell 5.1 turns anything a
# program writes to stderr (often just warnings or progress) into an error when output is redirected,
# which would stop this script; such lines are shown instead, and stdout is returned.
function Invoke-Native([string]$Exe, [string[]]$Arguments) {
  $saved = $ErrorActionPreference
  $ErrorActionPreference = 'Continue'
  try {
    $out = & $Exe @Arguments 2>&1 | ForEach-Object {
      if ($_ -is [System.Management.Automation.ErrorRecord]) { Write-Host "  $_" -ForegroundColor DarkGray } else { $_ }
    }
  } finally { $ErrorActionPreference = $saved }
  if ($LASTEXITCODE -ne 0) { throw "$Exe $($Arguments -join ' ') failed (exit code $LASTEXITCODE)" }
  return ($out -join "`n") # one string: multi-line JSON must reach ConvertFrom-Json whole in PowerShell 5.1
}
function Invoke-Az { return Invoke-Native 'az' $args }

# Deploys a service's template (services\<name>\infra) and returns its outputs.
function Deploy-Template([string]$ResourceGroup, [string]$Service, [string]$DeploymentName, [string]$Image = '', [string[]]$Parameters = @()) {
  $dir = Join-Path $root "services\$Service\infra"
  $extra = if ($Image) { @('--parameters', "containerImage=$Image") } else { @() }
  foreach ($p in $Parameters) { $extra += @('--parameters', $p) }
  if ($SkipCustomDomain -and $Service -eq 'frontpack') { $extra += @('--parameters', 'customDomain=') }
  $arguments = @('deployment', 'group', 'create', '--resource-group', $ResourceGroup, '--name', $DeploymentName,
    '--template-file', (Join-Path $dir 'main.bicep'), '--parameters', (Join-Path $dir 'main.bicepparam')) + $extra +
    @('--query', 'properties.outputs', '--output', 'json')
  return Invoke-Native 'az' $arguments | ConvertFrom-Json
}

# The share links' domain (e.g. s.thefrontpack.com) has an Azure-managed certificate in the platform's environment
# once it's been set up: its id, so the template keeps the domain on slideshow-api; '' before then.
function Find-ShareCertificate([string]$Domain, [string]$Environment) {
  if (-not $Domain) { return '' }
  $certificates = Invoke-Az containerapp env certificate list --name $Environment --resource-group $PlatformResourceGroup --managed-certificates-only --output json | ConvertFrom-Json
  $found = @($certificates | Where-Object { $_.properties.subjectName -eq $Domain -and $_.properties.provisioningState -eq 'Succeeded' })
  if ($found.Count -gt 0) { return $found[0].id } else { return '' }
}

# Puts the share links' domain on slideshow-api with a free managed certificate, the first time its DNS records
# are found (Azure checks them; the certificate takes a few minutes). Returns the share links' address, or ''
# (share links then use the API's own address) with the records to add.
function Connect-ShareDomain($Outputs, [string]$CertificateId) {
  $domain = $Outputs.shareDomain.value
  if (-not $domain) { return '' }
  if ($CertificateId) { return "https://$domain" }
  $fqdn = $Outputs.apiFqdn.value
  $verification = $Outputs.domainVerificationId.value
  $cname = Resolve-DnsName -Name $domain -Type CNAME -DnsOnly -ErrorAction SilentlyContinue | Where-Object { $_.NameHost -eq $fqdn }
  $txt = Resolve-DnsName -Name "asuid.$domain" -Type TXT -DnsOnly -ErrorAction SilentlyContinue | Where-Object { $_.Strings -contains $verification }
  if (-not ($cname -and $txt)) {
    Write-Host "  Share links use the API's own address until these DNS records exist for $domain (then deploy again):" -ForegroundColor Yellow
    Write-Host "    CNAME  $domain  ->  $fqdn" -ForegroundColor Yellow
    Write-Host "    TXT    asuid.$domain  =  $verification" -ForegroundColor Yellow
    return ''
  }
  Write-Host "  Share links: certificate for $domain (a few minutes)..."
  $app = $Outputs.appName.value
  $environmentId = Invoke-Az containerapp show --name $app --resource-group $SlideshowResourceGroup --query properties.managedEnvironmentId --output tsv
  try {
    Invoke-Az containerapp hostname bind --hostname $domain --name $app --resource-group $SlideshowResourceGroup `
      --environment $environmentId --validation-method CNAME --output none | Out-Null
    return "https://$domain"
  } catch {
    Write-Host "  Couldn't set up $domain yet ($_); share links use the API's own address. Deploy again to retry." -ForegroundColor Yellow
    return ''
  }
}

# With -SkipSlideshow: the share links' address if a domain is already bound to the running Slideshow API, so a
# website-only deploy keeps it; '' when none is (or there's no Slideshow resource group yet, before the cut-over).
function Find-BoundShareUrl {
  try {
    $domains = @(Invoke-Az containerapp list --resource-group $SlideshowResourceGroup `
      --query '[].properties.configuration.ingress.customDomains[].name' --output json | ConvertFrom-Json)
  } catch { return '' }
  if ($domains.Count -gt 0 -and $domains[0]) { return "https://$($domains[0])" } else { return '' }
}

# Builds a host's image with the .NET SDK (no Docker needed), pushes it, and returns its reference.
function Publish-Image([string]$HostProject, [string]$Repository, [string]$Registry, [string]$LoginServer) {
  $tag = (Get-Date).ToUniversalTime().ToString('yyyyMMdd-HHmmss')
  $sha = (& git -C $root rev-parse --short HEAD 2>$null)
  if ($LASTEXITCODE -eq 0 -and $sha) { $tag = "$tag-$sha" }
  # The SDK's container tooling reads registry credentials from these variables.
  $env:DOTNET_CONTAINER_REGISTRY_UNAME = '00000000-0000-0000-0000-000000000000'
  $env:DOTNET_CONTAINER_REGISTRY_PWORD = Invoke-Az acr login --name $Registry --expose-token --query accessToken --output tsv
  try {
    Invoke-Native 'dotnet' @('publish', (Join-Path $root "hosts\$HostProject"), '-c', 'Release', '-r', 'linux-x64', '-t:PublishContainer',
      "-p:ContainerRegistry=$LoginServer", "-p:ContainerImageTag=$tag") | Write-Host
  } finally {
    Remove-Item Env:DOTNET_CONTAINER_REGISTRY_PWORD -ErrorAction SilentlyContinue
  }
  return "$LoginServer/${Repository}:$tag"
}

if ($SkipProviders) {
  Write-Host '1/4 Skipping the resource providers (-SkipProviders): registered by an earlier deploy.'
} else {
  Write-Host '1/4 Resource providers...'
  foreach ($ns in 'Microsoft.App', 'Microsoft.ContainerRegistry', 'Microsoft.Storage', 'Microsoft.OperationalInsights',
                  'Microsoft.Insights', 'Microsoft.ManagedIdentity', 'Microsoft.Web') {
    $state = Invoke-Az provider show --namespace $ns --query registrationState --output tsv
    if ($state -ne 'Registered') {
      Write-Host "  registering $ns (can take a few minutes)"
      Invoke-Az provider register --namespace $ns --wait --output none | Out-Null
    }
  }
}

Write-Host "2/4 Platform ($PlatformResourceGroup) and FrontPack's API..."
Invoke-Az group create --name $PlatformResourceGroup --location $Location --output none | Out-Null
$platform = Deploy-Template $PlatformResourceGroup 'frontpack' 'frontpack-infra'
$registry = $platform.registryName.value
$loginServer = $platform.registryLoginServer.value
$image = Publish-Image 'FrontPack.Host' 'frontpack-api' $registry $loginServer
$platform = Deploy-Template $PlatformResourceGroup 'frontpack' 'frontpack-api' $image
$frontpackApiUrl = $platform.apiUrl.value

if ($SkipSlideshow) {
  Write-Host "3/4 Skipping Slideshow (-SkipSlideshow): the website uses $SlideshowApiUrl."
  $slideshowApiUrl = $SlideshowApiUrl
  $shareUrl = Find-BoundShareUrl
} else {
  Write-Host "3/4 Slideshow ($SlideshowResourceGroup)..."
  Invoke-Az group create --name $SlideshowResourceGroup --location $Location --output none | Out-Null
  $slideshowInfra = Deploy-Template $SlideshowResourceGroup 'slideshow' 'slideshow-infra'
  $shareCertificate = Find-ShareCertificate $slideshowInfra.shareDomain.value $platform.environmentName.value
  $image = Publish-Image 'Slideshow.Host' 'slideshow-api' $registry $loginServer
  $slideshow = Deploy-Template $SlideshowResourceGroup 'slideshow' 'slideshow-api' $image @("shareCertificateId=$shareCertificate")
  $slideshowApiUrl = $slideshow.apiUrl.value
  $shareUrl = Connect-ShareDomain $slideshow $shareCertificate
}

$webName = $platform.webName.value
$webUrl = $platform.webUrl.value
$customUrl = $platform.customUrl.value
Write-Host "4/4 Website -> $webUrl ..."
$staging = Join-Path ([IO.Path]::GetTempPath()) 'frontpack-web-package'
$siteConfig = Get-Content (Join-Path $web 'app.config.azure.json') -Raw | ConvertFrom-Json
$siteConfig | Add-Member -NotePropertyName services -NotePropertyValue ([ordered]@{ frontpack = $frontpackApiUrl; slideshow = $slideshowApiUrl }) -Force
if ($shareUrl) { $siteConfig | Add-Member -NotePropertyName shareUrl -NotePropertyValue $shareUrl -Force }
$env:APP_CONFIG = $siteConfig | ConvertTo-Json -Depth 5 -Compress
Push-Location $web
try {
  if (-not (Test-Path node_modules)) { Invoke-Native 'npm' @('ci') | Write-Host }
  Invoke-Native 'node' @('build.mjs', '--out', $staging) | Write-Host
} finally {
  Pop-Location
  Remove-Item Env:APP_CONFIG -ErrorAction SilentlyContinue
}
$deployToken = Invoke-Az staticwebapp secrets list --name $webName --resource-group $PlatformResourceGroup --query properties.apiKey --output tsv
Invoke-Native 'npx' @('--yes', '@azure/static-web-apps-cli@2', 'deploy', $staging, '--deployment-token', $deployToken, '--env', 'production') | Write-Host

Write-Host ''
Write-Host "Website:          $webUrl"
Write-Host "FrontPack API:    $frontpackApiUrl"
Write-Host "Slideshow API:    $slideshowApiUrl"
Write-Host "Share links:      $(if ($shareUrl) { $shareUrl } else { $slideshowApiUrl })/<code>"
Write-Host "Sign-in redirect: $webUrl/login.html  (must be listed on the 'FrontPack' website app registration)"
Write-Host "Maps key:         $webUrl/*  (must be allowed on the Google Maps key)"
if ($customUrl) { Write-Host "Custom domain:    $customUrl" }
foreach ($api in $frontpackApiUrl, $slideshowApiUrl) {
  try {
    $health = Invoke-RestMethod "$api/healthz" -TimeoutSec 120
    Write-Host "Health $api : $($health.status)"
  } catch {
    Write-Host "Health $api : not answering yet (a new container can take a minute to start); retry in a minute." -ForegroundColor Yellow
  }
}
