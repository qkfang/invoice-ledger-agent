@description('Base name prefix for all resources')
param baseName string = 'invledger'

@description('Azure region for all resources')
param location string = 'australiaeast'

@description('Principal object IDs to grant access to deployed resources')
param principals array = []

var commonTags = {
  SecurityControl: 'Ignore'
}
var foundryName = '${baseName}-foundry'
var docIntelligenceName = '${baseName}-di'
var storageAccountName = replace('${baseName}sa', '-', '')
var logAnalyticsName = '${baseName}-law'
var appInsightsName = '${baseName}-ai'
var appServicePlanName = '${baseName}-asp'
var webAppName = '${baseName}-web'

// ── Storage Account ──────────────────────────────────────────────────────────
resource storageAccount 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: storageAccountName
  location: location
  tags: commonTags
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  properties: {
    accessTier: 'Hot'
    supportsHttpsTrafficOnly: true
    minimumTlsVersion: 'TLS1_2'
    allowBlobPublicAccess: true // poc only
    publicNetworkAccess: 'Enabled' // poc only
    networkAcls: {
      bypass: 'AzureServices'
      defaultAction: 'Allow'
    }
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: storageAccount
  name: 'default'
}

resource noticesContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: 'notices'
  properties: {
    publicAccess: 'Blob'
  }
}

// ── Log Analytics Workspace ──────────────────────────────────────────────────
resource logAnalyticsWorkspace 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: logAnalyticsName
  location: location
  tags: commonTags
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
  }
}

// ── Application Insights ─────────────────────────────────────────────────────
resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: appInsightsName
  location: location
  tags: commonTags
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalyticsWorkspace.id
  }
}

// ── App Service Plan ─────────────────────────────────────────────────────────
resource appServicePlan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: appServicePlanName
  location: location
  tags: commonTags
  sku: {
    name: 'S1'
    tier: 'Standard'
  }
  kind: 'linux'
  properties: {
    reserved: true
  }
}

// ── AI Foundry ───────────────────────────────────────────────────────────────
module azureFoundry 'foundry.bicep' = {
  name: 'foundryDeployment'
  params: {
    name: foundryName
    location: location
    tags: commonTags
  }
}

resource foundryAccount 'Microsoft.CognitiveServices/accounts@2025-10-01-preview' existing = {
  name: foundryName
  dependsOn: [azureFoundry]
}

resource foundryDiagnostics 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  name: 'send-to-law'
  scope: foundryAccount
  properties: {
    workspaceId: logAnalyticsWorkspace.id
    logs: [
      { categoryGroup: 'allLogs', enabled: true }
      { categoryGroup: 'audit', enabled: true }
    ]
    metrics: [
      { category: 'AllMetrics', enabled: true }
    ]
  }
}

// ── Document Intelligence ────────────────────────────────────────────────────
module docIntelligence 'docintelligence.bicep' = {
  name: 'docIntelligenceDeployment'
  params: {
    name: docIntelligenceName
    location: location
    tags: commonTags
  }
}

resource docIntelligenceAccount 'Microsoft.CognitiveServices/accounts@2024-10-01' existing = {
  name: docIntelligenceName
  dependsOn: [docIntelligence]
}

// ── Web App ──────────────────────────────────────────────────────────────────
module webApp 'webapp.bicep' = {
  name: 'webAppDeployment'
  params: {
    name: webAppName
    location: location
    tags: commonTags
    appServicePlanId: appServicePlan.id
    appSettings: {
      APPLICATIONINSIGHTS_CONNECTION_STRING: appInsights.properties.ConnectionString
      ApplicationInsightsAgent_EXTENSION_VERSION: '~3'
      AZURE_AI_PROJECT_ENDPOINT: azureFoundry.outputs.projectEndpoint
      AZURE_AI_MODEL_DEPLOYMENT_NAME: azureFoundry.outputs.deploymentName
      AZURE_DOC_INTELLIGENCE_ENDPOINT: docIntelligence.outputs.endpoint
      AZURE_STORAGE_ACCOUNT_NAME: storageAccountName
      AZURE_TENANT_ID: tenant().tenantId
    }
    appCommandLine: 'dotnet invledger.dll'
  }
}

resource webAppResource 'Microsoft.Web/sites@2024-04-01' existing = {
  name: webAppName
  dependsOn: [webApp]
}

resource webAppDiagnostics 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  name: 'send-to-law'
  scope: webAppResource
  properties: {
    workspaceId: logAnalyticsWorkspace.id
    logs: [
      { category: 'AppServiceHTTPLogs', enabled: true }
      { category: 'AppServiceConsoleLogs', enabled: true }
      { category: 'AppServiceAppLogs', enabled: true }
    ]
    metrics: [
      { category: 'AllMetrics', enabled: true }
    ]
  }
}

// ── Role IDs ─────────────────────────────────────────────────────────────────
var cognitiveServicesOpenAIUserRoleId = '5e0bd9bd-7b93-4f28-af87-19fc36ad61bd'
var cognitiveServicesUserRoleId = 'a97b65f3-24c7-4388-baec-2e87135dc908'
var azureAIUserRoleId = '53ca6127-db72-4b80-b1b0-d745d6d5456d'
var azureAIDeveloperRoleId = '64702f94-c441-49e6-a78b-ef80e0188fee'
var storageBlobDataContributorRoleId = 'ba92f5b4-2d11-453d-a403-e96b0029c9fe'

// ── Role assignments: Web App → Foundry ──────────────────────────────────────
resource webAppOpenAIUserRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(foundryAccount.id, resourceId('Microsoft.Web/sites', webAppName), cognitiveServicesOpenAIUserRoleId)
  scope: foundryAccount
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', cognitiveServicesOpenAIUserRoleId)
    principalId: webApp.outputs.principalId
    principalType: 'ServicePrincipal'
  }
}

resource webAppAIUserRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(foundryAccount.id, resourceId('Microsoft.Web/sites', webAppName), azureAIUserRoleId)
  scope: foundryAccount
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', azureAIUserRoleId)
    principalId: webApp.outputs.principalId
    principalType: 'ServicePrincipal'
  }
}

resource webAppAIDeveloperRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(foundryAccount.id, resourceId('Microsoft.Web/sites', webAppName), azureAIDeveloperRoleId)
  scope: foundryAccount
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', azureAIDeveloperRoleId)
    principalId: webApp.outputs.principalId
    principalType: 'ServicePrincipal'
  }
}

resource webAppCogServicesUserRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(foundryAccount.id, resourceId('Microsoft.Web/sites', webAppName), cognitiveServicesUserRoleId)
  scope: foundryAccount
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', cognitiveServicesUserRoleId)
    principalId: webApp.outputs.principalId
    principalType: 'ServicePrincipal'
  }
}

// ── Role assignment: Web App → Document Intelligence ─────────────────────────
resource webAppDocIntelligenceRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(docIntelligenceAccount.id, resourceId('Microsoft.Web/sites', webAppName), cognitiveServicesUserRoleId)
  scope: docIntelligenceAccount
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', cognitiveServicesUserRoleId)
    principalId: webApp.outputs.principalId
    principalType: 'ServicePrincipal'
  }
}

// ── Role assignment: Web App → Storage (read notices) ────────────────────────
resource webAppStorageBlobRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storageAccount.id, resourceId('Microsoft.Web/sites', webAppName), storageBlobDataContributorRoleId)
  scope: storageAccount
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', storageBlobDataContributorRoleId)
    principalId: webApp.outputs.principalId
    principalType: 'ServicePrincipal'
  }
}

// ── Role assignments: additional principals ──────────────────────────────────
resource userOpenAIUserRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for principal in principals: {
  name: guid(foundryAccount.id, principal.id, cognitiveServicesOpenAIUserRoleId)
  scope: foundryAccount
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', cognitiveServicesOpenAIUserRoleId)
    principalId: principal.id
    principalType: principal.principalType
  }
}]

resource userAIUserRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for principal in principals: {
  name: guid(foundryAccount.id, principal.id, azureAIUserRoleId)
  scope: foundryAccount
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', azureAIUserRoleId)
    principalId: principal.id
    principalType: principal.principalType
  }
}]

resource userAIDeveloperRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for principal in principals: {
  name: guid(foundryAccount.id, principal.id, azureAIDeveloperRoleId)
  scope: foundryAccount
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', azureAIDeveloperRoleId)
    principalId: principal.id
    principalType: principal.principalType
  }
}]

resource userCogServicesUserRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for principal in principals: {
  name: guid(foundryAccount.id, principal.id, cognitiveServicesUserRoleId)
  scope: foundryAccount
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', cognitiveServicesUserRoleId)
    principalId: principal.id
    principalType: principal.principalType
  }
}]

resource userDocIntelligenceRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for principal in principals: {
  name: guid(docIntelligenceAccount.id, principal.id, cognitiveServicesUserRoleId)
  scope: docIntelligenceAccount
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', cognitiveServicesUserRoleId)
    principalId: principal.id
    principalType: principal.principalType
  }
}]

resource userStorageBlobRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for principal in principals: {
  name: guid(storageAccount.id, principal.id, storageBlobDataContributorRoleId)
  scope: storageAccount
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', storageBlobDataContributorRoleId)
    principalId: principal.id
    principalType: principal.principalType
  }
}]
