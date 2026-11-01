// Deployed into the platform's resource group by main.bicep: lets the Slideshow API's identity pull its image
// from the shared registry and read the FrontPack accounts (the Users table only).
targetScope = 'resourceGroup'

param registryName string
param storageName string
param principalId string

// Built-in role definition ids
var roles = {
  acrPull: '7f951dda-4ed3-4680-a7ca-43fe172d538d'
  tableDataReader: '76199698-9eea-4c19-bc75-cec21354c6b6'
}

resource registry 'Microsoft.ContainerRegistry/registries@2023-07-01' existing = {
  name: registryName
}

resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' existing = {
  name: storageName
}

resource tableService 'Microsoft.Storage/storageAccounts/tableServices@2023-05-01' existing = {
  parent: storage
  name: 'default'
}

resource usersTable 'Microsoft.Storage/storageAccounts/tableServices/tables@2023-05-01' existing = {
  parent: tableService
  name: 'Users'
}

resource acrPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(registry.id, principalId, roles.acrPull)
  scope: registry
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.acrPull)
    principalId: principalId
    principalType: 'ServicePrincipal'
  }
}

resource usersReader 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(usersTable.id, principalId, roles.tableDataReader)
  scope: usersTable
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.tableDataReader)
    principalId: principalId
    principalType: 'ServicePrincipal'
  }
}
