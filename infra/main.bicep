// Slideshow on Azure: website on Static Web Apps; API on Container Apps with Table Storage (album
// details), Blob Storage (files), a Storage queue (photos waiting for smaller versions) and App Insights. deploy.ps1 runs this twice: first with containerImage
// empty (creates the registry etc.), then, after pushing the image, with containerImage set. See api/README.md.
targetScope = 'resourceGroup'

@description('Short name used to derive resource names.')
@minLength(3)
@maxLength(12)
param appName string = 'slideshow'

param location string = resourceGroup().location

@description('Static Web Apps is offered in fewer regions than the rest; pick the closest of them.')
@allowed(['eastus2', 'centralus', 'westus2', 'westeurope', 'eastasia'])
param webLocation string = 'eastus2'

@description('Full image reference, e.g. <registry>.azurecr.io/slideshow-api:1. Leave empty to deploy infrastructure only.')
param containerImage string = ''

@description('Entra External ID token authority: https://<tenant-subdomain>.ciamlogin.com/<tenant-id>/v2.0')
param authAuthority string

@description('Client id of the API app registration in the External ID tenant (the token audience).')
param apiClientId string

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
  blobDataContributor: 'ba92f5b4-2d11-453d-a403-e96b0029c9fe'
  tableDataContributor: '0a9a7e1f-b9d0-4cc4-a60d-0319b160aaa3'
  queueDataContributor: '974c5e8b-45b9-4653-ba55-5f855dd0fb88'
}

var resizeQueueName = 'resize'

// ───────────── Monitoring ─────────────

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

// ───────────── Identity ─────────────
// User-assigned so its roles exist before the container app first pulls its image.

resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: names.identity
  location: location
}

// ───────────── Container registry ─────────────

resource registry 'Microsoft.ContainerRegistry/registries@2023-07-01' = {
  name: names.registry
  location: location
  sku: { name: 'Basic' }
  properties: {
    adminUserEnabled: false
  }
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

// ───────────── Blob storage ─────────────

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

resource tables 'Microsoft.Storage/storageAccounts/tableServices/tables@2023-05-01' = [for name in ['Users', 'Albums', 'Media', 'Shares']: {
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

// ───────────── Container Apps ─────────────

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

// ───────────── Website ─────────────

resource web 'Microsoft.Web/staticSites@2023-12-01' = {
  name: names.web
  location: webLocation
  sku: { name: 'Free', tier: 'Free' }
  properties: {} // content is uploaded by deploy.ps1, not built from a repository
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

var appEnv = concat([
  { name: 'AZURE_CLIENT_ID', value: identity.properties.clientId } // the managed identity the API signs in to storage as
  { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', secretRef: 'appinsights' }
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
  { name: 'Graph__TenantId', value: empty(graphClientId) ? '' : externalTenantId }
  { name: 'Graph__ClientId', value: graphClientId }
  { name: 'Graph__ManagedIdentityClientId', value: identity.properties.clientId }
  { name: 'Graph__UseManagedIdentityFederation', value: empty(graphClientSecret) ? 'true' : 'false' }
], empty(graphClientSecret) ? [] : [{ name: 'Graph__ClientSecret', secretRef: 'graph-client-secret' }], corsEnv)

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
  dependsOn: [acrPull, blobContributor, tableContributor, queueContributor, tables]
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
          resources: { cpu: json('0.5'), memory: '1Gi' }
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

output registryLoginServer string = registry.properties.loginServer
output registryName string = registry.name
output identityClientId string = identity.properties.clientId
output identityPrincipalId string = identity.properties.principalId
output apiUrl string = empty(containerImage) ? '' : 'https://${app!.properties.configuration.ingress.fqdn}'
output webName string = web.name
output webUrl string = webOrigin
output customUrl string = customOrigin
