// The Slideshow service, in its own resource group (slideshow-rg): Table Storage (album details), Blob Storage
// (files), a Storage queue (photos waiting for smaller versions), and slideshow-api, which runs in the FrontPack
// platform's Container Apps environment and pulls from its registry (services/frontpack/infra, deployed first).
// Deployed in two passes: containerImage empty first (storage, identity, roles), then with the image.
//
// The storage account, identity and role assignments get the same names the single-API template
// (infra/main.bicep) gave them in this resource group, so deploying this keeps the existing albums and files.
targetScope = 'resourceGroup'

@description('Short name used to derive resource names.')
@minLength(3)
@maxLength(12)
param appName string = 'slideshow'

param location string = resourceGroup().location

@description('The FrontPack platform: its resource group and the appName its template was deployed with.')
param platformResourceGroup string = 'frontpack-rg'
param platformAppName string = 'frontpack'

@description('Full image reference, e.g. <registry>.azurecr.io/slideshow-api:1. Leave empty to deploy infrastructure only.')
param containerImage string = ''

@description('Entra External ID token authority: https://<tenant-subdomain>.ciamlogin.com/<tenant-id>/v2.0')
param authAuthority string

@description('Client id of the API app registration in the External ID tenant (the token audience).')
param apiClientId string

@description('The delegated scope every API requires in its tokens.')
param apiScope string = 'FrontPack.Access'

@description('The website\'s custom domain (on the platform\'s Static Web App), allowed to call the API. Empty: none.')
param customDomain string = ''

@description('Extra origins allowed to call the API from a browser (the website is always allowed), e.g. http://localhost:3000')
param allowedOrigins array = []

@description('API instances kept running when idle. 0: scales to zero (slow first request after idle); 1: always warm, about US$10/month.')
@minValue(0)
@maxValue(3)
param apiMinReplicas int = 0

var suffix = uniqueString(resourceGroup().id)
var names = {
  identity: '${appName}-api-id'
  storage: take(toLower(replace('${appName}${suffix}', '-', '')), 24)
  app: '${appName}-api'
}

// The platform's names, worked out the way its template does (from its resource group's id).
var platformSuffix = uniqueString('${subscription().id}/resourceGroups/${platformResourceGroup}')
var platform = {
  environment: '${platformAppName}-env'
  insights: '${platformAppName}-insights'
  registry: toLower(replace('${platformAppName}${platformSuffix}', '-', ''))
  storage: take(toLower(replace('${platformAppName}${platformSuffix}', '-', '')), 24)
  web: '${platformAppName}-web'
}

// Built-in role definition ids
var roles = {
  blobDataContributor: 'ba92f5b4-2d11-453d-a403-e96b0029c9fe'
  tableDataContributor: '0a9a7e1f-b9d0-4cc4-a60d-0319b160aaa3'
  queueDataContributor: '974c5e8b-45b9-4653-ba55-5f855dd0fb88'
}

var resizeQueueName = 'resize'
var internalPort = 8081 // FrontPack's API reaches the account's usage and deletion here; no public ingress

// ───────────── The platform ─────────────

resource environment 'Microsoft.App/managedEnvironments@2024-03-01' existing = {
  name: platform.environment
  scope: resourceGroup(platformResourceGroup)
}

resource insights 'Microsoft.Insights/components@2020-02-02' existing = {
  name: platform.insights
  scope: resourceGroup(platformResourceGroup)
}

resource registry 'Microsoft.ContainerRegistry/registries@2023-07-01' existing = {
  #disable-next-line BCP334 // platformAppName (3+) + uniqueString (13) is always long enough
  name: platform.registry
  scope: resourceGroup(platformResourceGroup)
}

resource platformStorage 'Microsoft.Storage/storageAccounts@2023-05-01' existing = {
  #disable-next-line BCP334 // platformAppName (3+) + uniqueString (13) is always long enough
  name: platform.storage
  scope: resourceGroup(platformResourceGroup)
}

resource web 'Microsoft.Web/staticSites@2023-12-01' existing = {
  name: platform.web
  scope: resourceGroup(platformResourceGroup)
}

// ───────────── Identity ─────────────
// User-assigned so its roles exist before the container app first pulls its image.

resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: names.identity
  location: location
}

// In the platform's resource group: pull from its registry, read the accounts.
module platformAccess 'platform-access.bicep' = {
  name: '${appName}-platform-access'
  scope: resourceGroup(platformResourceGroup)
  params: {
    registryName: platform.registry
    storageName: platform.storage
    principalId: identity.properties.principalId
  }
}

// ───────────── Storage ─────────────

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

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: storage
  name: 'default'
  properties: {
    deleteRetentionPolicy: { enabled: true, days: 7 }       // recover from accidental deletes
    containerDeleteRetentionPolicy: { enabled: true, days: 7 }
  }
}

resource mediaContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: 'media'
  properties: { publicAccess: 'None' }
}

// Album details. Created here so the API doesn't have to check for them every time it starts.
resource tableService 'Microsoft.Storage/storageAccounts/tableServices@2023-05-01' = {
  parent: storage
  name: 'default'
}

resource tables 'Microsoft.Storage/storageAccounts/tableServices/tables@2023-05-01' = [for name in ['Albums', 'Media', 'Shares']: {
  parent: tableService
  name: name
}]

// Photos waiting for their smaller versions (720/1280/1920px), made by the API in the background.
resource queueService 'Microsoft.Storage/storageAccounts/queueServices@2023-05-01' = {
  parent: storage
  name: 'default'
}

resource resizeQueue 'Microsoft.Storage/storageAccounts/queueServices/queues@2023-05-01' = {
  parent: queueService
  name: resizeQueueName
}

resource blobContributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storage.id, identity.id, roles.blobDataContributor)
  scope: storage
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.blobDataContributor)
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
  }
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

resource queueContributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storage.id, identity.id, roles.queueDataContributor)
  scope: storage
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.queueDataContributor)
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

// ───────────── The API ─────────────

var webOrigin = 'https://${web.properties.defaultHostname}'
var customOrigin = empty(customDomain) ? '' : 'https://${customDomain}'
// The website is always allowed (slot 0), then its custom domain; any extra origins follow.
var extraOrigins = concat(empty(customOrigin) ? [] : [customOrigin], allowedOrigins)
var extraCorsEnv = [for (origin, i) in extraOrigins: { name: 'Cors__AllowedOrigins__${i + 1}', value: origin }]
var corsEnv = concat([{ name: 'Cors__AllowedOrigins__0', value: webOrigin }], extraCorsEnv)

var appEnv = concat([
  { name: 'AZURE_CLIENT_ID', value: identity.properties.clientId } // the managed identity the API signs in to storage as
  { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', secretRef: 'appinsights' }
  { name: 'ASPNETCORE_HTTP_PORTS', value: '8080;${internalPort}' } // public ingress + the internal port
  { name: 'Internal__Port', value: string(internalPort) }
  { name: 'Users__Provider', value: 'AzureTable' } // the FrontPack accounts, read-only
  { name: 'Users__ServiceUri', value: platformStorage.properties.primaryEndpoints.table }
  { name: 'Tables__Provider', value: 'AzureTable' }
  { name: 'Tables__ServiceUri', value: storage.properties.primaryEndpoints.table }
  { name: 'Storage__Provider', value: 'AzureBlob' }
  { name: 'Storage__BlobServiceUri', value: storage.properties.primaryEndpoints.blob }
  { name: 'Storage__ContainerName', value: mediaContainer.name }
  { name: 'Resize__Provider', value: 'AzureQueue' }
  { name: 'Resize__QueueServiceUri', value: storage.properties.primaryEndpoints.queue }
  { name: 'Resize__QueueName', value: resizeQueue.name }
  { name: 'Auth__Authority', value: authAuthority }
  { name: 'Auth__Audiences__0', value: apiClientId }
  { name: 'Auth__Audiences__1', value: 'api://${apiClientId}' }
  { name: 'Auth__RequiredScope', value: apiScope }
], corsEnv)

resource app 'Microsoft.App/containerApps@2025-01-01' = if (!empty(containerImage)) {
  name: names.app
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: { '${identity.id}': {} }
  }
  dependsOn: [platformAccess, blobContributor, tableContributor, queueContributor, tables]
  properties: {
    managedEnvironmentId: environment.id
    configuration: {
      ingress: {
        external: true
        targetPort: 8080
        transport: 'auto'
        allowInsecure: false
        additionalPortMappings: [
          { external: false, targetPort: internalPort, exposedPort: internalPort }
        ]
      }
      registries: [{ server: registry.properties.loginServer, identity: identity.id }]
      secrets: [{ name: 'appinsights', value: insights.properties.ConnectionString }]
    }
    template: {
      containers: [
        {
          name: 'api'
          image: containerImage
          // Enough so far (checked by hand, November 2026). The background resizing decodes one photo at a time
          // in memory; if big uploads ever restart the container (out of memory), go back to 0.5 / 1Gi.
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
          // Photos waiting in the queue start the API too, so their smaller versions get made even
          // when nobody is using the site (the API scales to zero otherwise).
          {
            name: 'resize-queue'
            azureQueue: { accountName: storage.name, queueName: resizeQueue.name, queueLength: 20, identity: identity.id }
          }
        ]
      }
    }
  }
}

output identityClientId string = identity.properties.clientId
output identityPrincipalId string = identity.properties.principalId
output storageName string = storage.name
output apiUrl string = empty(containerImage) ? '' : 'https://${app!.properties.configuration.ingress.fqdn}'
