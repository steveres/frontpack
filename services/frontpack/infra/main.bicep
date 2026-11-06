// The FrontPack platform, in its own resource group (frontpack-rg): what every service shares, plus FrontPack's
// own API and the website.
//   Shared:     Log Analytics + App Insights, the container registry, the Container Apps environment.
//   FrontPack:  the accounts (Users table), frontpack-api (accounts, sign-in, Graph), frontpack-web (the
//               website for every service, with the custom domain).
// Each service deploys its own resource group (e.g. services/slideshow/infra) into this environment and
// registry. Deployed in two passes, like the services: containerImage empty first (creates the registry),
// then with the image. See api/README.md.
targetScope = 'resourceGroup'

@description('Short name used to derive resource names.')
@minLength(3)
@maxLength(12)
param appName string = 'frontpack'

param location string = resourceGroup().location

@description('Static Web Apps is offered in fewer regions than the rest; pick the closest of them.')
@allowed(['eastus2', 'centralus', 'westus2', 'westeurope', 'eastasia'])
param webLocation string = 'eastus2'

@description('Full image reference, e.g. <registry>.azurecr.io/frontpack-api:1. Leave empty to deploy infrastructure only.')
param containerImage string = ''

@description('Entra External ID token authority: https://<tenant-subdomain>.ciamlogin.com/<tenant-id>/v2.0')
param authAuthority string

@description('Client id of the API app registration in the External ID tenant (the token audience).')
param apiClientId string

@description('The delegated scope every API requires in its tokens.')
param apiScope string = 'Slideshow.Access'

@description('External ID (customer) tenant id, for Microsoft Graph calls.')
param externalTenantId string

@description('Client id of the Graph app registration in the External ID tenant (federated with the managed identity). Empty disables Graph.')
param graphClientId string = ''

@secure()
@description('Only if managed-identity federation is not possible: a client secret for the Graph app. Leave empty to use federation.')
param graphClientSecret string = ''

@description('Extra origins allowed to call the API from a browser (the Static Web App is always allowed), e.g. http://localhost:3000')
param allowedOrigins array = []

@description('API instances kept running when idle. 0: scales to zero (slow first request after idle); 1: always warm, about US$10/month.')
@minValue(0)
@maxValue(3)
param apiMinReplicas int = 0

@description('Custom domain for the website, e.g. www.example.com. Its CNAME must already point to the Static Web App. Empty: none.')
param customDomain string = ''

@description('The services whose data belongs to the account: their container app name (reached on its internal port 8081 inside the environment).')
param services array = [
  { name: 'slideshow', app: 'slideshow-api' }
]

var suffix = uniqueString(resourceGroup().id)
var names = {
  logs: '${appName}-logs'
  insights: '${appName}-insights'
  identity: '${appName}-api-id'
  registry: toLower(replace('${appName}${suffix}', '-', ''))
  storage: take(toLower(replace('${appName}${suffix}', '-', '')), 24)
  environment: '${appName}-env'
  app: '${appName}-api'
  web: '${appName}-web'
}

// Built-in role definition ids
var roles = {
  acrPull: '7f951dda-4ed3-4680-a7ca-43fe172d538d'
  tableDataContributor: '0a9a7e1f-b9d0-4cc4-a60d-0319b160aaa3'
}

// ───────────── Monitoring (shared) ─────────────

resource logs 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: names.logs
  location: location
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
  }
}

resource insights 'Microsoft.Insights/components@2020-02-02' = {
  name: names.insights
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logs.id
  }
}

// ───────────── Container registry (shared: one repository per service) ─────────────

resource registry 'Microsoft.ContainerRegistry/registries@2023-07-01' = {
  name: names.registry
  location: location
  sku: { name: 'Basic' }
  properties: {
    adminUserEnabled: false
  }
}

// ───────────── Container Apps environment (shared) ─────────────

resource environment 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: names.environment
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logs.properties.customerId
        sharedKey: logs.listKeys().primarySharedKey
      }
    }
  }
}

// ───────────── FrontPack's API: identity, the accounts ─────────────
// User-assigned so its roles exist before the container app first pulls its image.

resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: names.identity
  location: location
}

resource acrPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(registry.id, identity.id, roles.acrPull)
  scope: registry
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.acrPull)
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  #disable-next-line BCP334 // appName (3+) + uniqueString (13) is always long enough
  name: names.storage
  location: location
  kind: 'StorageV2'
  sku: { name: 'Standard_LRS' }
  properties: {
    accessTier: 'Hot'
    allowBlobPublicAccess: false
    allowSharedKeyAccess: false // Entra auth only
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
  }
}

resource tableService 'Microsoft.Storage/storageAccounts/tableServices@2023-05-01' = {
  parent: storage
  name: 'default'
}

// The accounts. FrontPack writes them; each service's identity is given read access to this table by the
// service's own template.
resource usersTable 'Microsoft.Storage/storageAccounts/tableServices/tables@2023-05-01' = {
  parent: tableService
  name: 'Users'
}

resource tableContributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storage.id, identity.id, roles.tableDataContributor)
  scope: storage
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.tableDataContributor)
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

// ───────────── Website (every service's pages) ─────────────

resource web 'Microsoft.Web/staticSites@2023-12-01' = {
  name: names.web
  location: webLocation
  sku: { name: 'Free', tier: 'Free' }
  properties: {} // content is uploaded by the deploy script, not built from a repository
}

// Azure checks the CNAME and issues and renews the certificate itself.
resource webDomain 'Microsoft.Web/staticSites/customDomains@2023-12-01' = if (!empty(customDomain)) {
  parent: web
  name: empty(customDomain) ? 'none' : customDomain
  properties: { validationMethod: 'cname-delegation' }
}

var webOrigin = 'https://${web.properties.defaultHostname}'
var customOrigin = empty(customDomain) ? '' : 'https://${customDomain}'
// The website is always allowed (slot 0), then its custom domain; any extra origins follow.
var extraOrigins = concat(empty(customOrigin) ? [] : [customOrigin], allowedOrigins)
var extraCorsEnv = [for (origin, i) in extraOrigins: { name: 'Cors__AllowedOrigins__${i + 1}', value: origin }]
var corsEnv = concat([{ name: 'Cors__AllowedOrigins__0', value: webOrigin }], extraCorsEnv)

// Each service's side of the account (usage, deletion), on its internal port.
var serviceNamesEnv = [for (s, i) in services: { name: 'Services__${i}__Name', value: s.name }]
var serviceUrlsEnv = [for (s, i) in services: { name: 'Services__${i}__InternalUrl', value: 'http://${s.app}:8081' }]
var servicesEnv = concat(serviceNamesEnv, serviceUrlsEnv)

var appEnv = concat([
  { name: 'AZURE_CLIENT_ID', value: identity.properties.clientId } // the managed identity the API signs in to storage as
  { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', secretRef: 'appinsights' }
  { name: 'Users__Provider', value: 'AzureTable' }
  { name: 'Users__ServiceUri', value: storage.properties.primaryEndpoints.table }
  { name: 'Auth__Authority', value: authAuthority }
  { name: 'Auth__Audiences__0', value: apiClientId }
  { name: 'Auth__Audiences__1', value: 'api://${apiClientId}' }
  { name: 'Auth__RequiredScope', value: apiScope }
  { name: 'Graph__TenantId', value: empty(graphClientId) ? '' : externalTenantId }
  { name: 'Graph__ClientId', value: graphClientId }
  { name: 'Graph__ManagedIdentityClientId', value: identity.properties.clientId }
  { name: 'Graph__UseManagedIdentityFederation', value: empty(graphClientSecret) ? 'true' : 'false' }
], empty(graphClientSecret) ? [] : [{ name: 'Graph__ClientSecret', secretRef: 'graph-client-secret' }], corsEnv, servicesEnv)

var appSecrets = concat(
  [{ name: 'appinsights', value: insights.properties.ConnectionString }],
  empty(graphClientSecret) ? [] : [{ name: 'graph-client-secret', value: graphClientSecret }])

resource app 'Microsoft.App/containerApps@2025-01-01' = if (!empty(containerImage)) {
  name: names.app
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: { '${identity.id}': {} }
  }
  dependsOn: [acrPull, tableContributor, usersTable]
  properties: {
    managedEnvironmentId: environment.id
    configuration: {
      ingress: {
        external: true
        targetPort: 8080
        transport: 'auto'
        allowInsecure: false
      }
      registries: [{ server: registry.properties.loginServer, identity: identity.id }]
      secrets: appSecrets
    }
    template: {
      containers: [
        {
          name: 'api'
          image: containerImage
          resources: { cpu: json('0.25'), memory: '0.5Gi' }
          env: appEnv
          probes: [
            { type: 'Liveness', httpGet: { path: '/healthz', port: 8080 }, periodSeconds: 30 }
            { type: 'Readiness', httpGet: { path: '/healthz', port: 8080 }, periodSeconds: 5 }
            // Checked every second, so a request waiting on a cold start is let through as soon as the
            // app answers (the app needs a few seconds; up to a minute is allowed).
            { type: 'Startup', httpGet: { path: '/healthz', port: 8080 }, periodSeconds: 1, failureThreshold: 60 }
          ]
        }
      ]
      scale: {
        minReplicas: apiMinReplicas
        maxReplicas: 3
        rules: [
          { name: 'http', http: { metadata: { concurrentRequests: '50' } } }
        ]
      }
    }
  }
}

output environmentName string = environment.name
output registryLoginServer string = registry.properties.loginServer
output registryName string = registry.name
output storageName string = storage.name
output insightsName string = insights.name
output identityClientId string = identity.properties.clientId
output identityPrincipalId string = identity.properties.principalId
output apiUrl string = empty(containerImage) ? '' : 'https://${app!.properties.configuration.ingress.fqdn}'
output webName string = web.name
output webUrl string = webOrigin
output customUrl string = customOrigin
