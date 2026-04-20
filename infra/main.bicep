@description('Location for all resources')
param location string = resourceGroup().location

@description('Base name for all resources')
param appName string = 'idiotproof'

@description('App Service SKU (F1=Free, B1=Basic, P1v3=Production)')
param sku string = 'B1'

@description('Alpaca API Key ID (stored in Key Vault)')
@secure()
param alpacaApiKeyId string = ''

@description('Alpaca API Secret (stored in Key Vault)')
@secure()
param alpacaApiSecret string = ''

@description('Polygon.io API Key (stored in Key Vault)')
@secure()
param polygonApiKey string = ''

@description('Claude API Key (stored in Key Vault)')
@secure()
param claudeApiKey string = ''

var webAppName = '${appName}-web'
var planName = '${appName}-plan'
var kvName = '${appName}-kv'
var logWorkspaceName = '${appName}-logs'

// App Service Plan
resource appServicePlan 'Microsoft.Web/serverfarms@2023-01-01' = {
  name: planName
  location: location
  sku: {
    name: sku
    tier: sku == 'F1' ? 'Free' : sku == 'B1' ? 'Basic' : 'PremiumV3'
  }
  properties: {
    reserved: false // Windows
  }
}

// Key Vault
resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: kvName
  location: location
  properties: {
    sku: { family: 'A', name: 'standard' }
    tenantId: subscription().tenantId
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 7
  }
}

// Store secrets in Key Vault
resource secretAlpacaKey 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = if (!empty(alpacaApiKeyId)) {
  parent: keyVault
  name: 'AlpacaApiKeyId'
  properties: { value: alpacaApiKeyId }
}

resource secretAlpacaSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = if (!empty(alpacaApiSecret)) {
  parent: keyVault
  name: 'AlpacaApiSecretKey'
  properties: { value: alpacaApiSecret }
}

resource secretPolygon 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = if (!empty(polygonApiKey)) {
  parent: keyVault
  name: 'PolygonApiKey'
  properties: { value: polygonApiKey }
}

resource secretClaude 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = if (!empty(claudeApiKey)) {
  parent: keyVault
  name: 'ClaudeApiKey'
  properties: { value: claudeApiKey }
}

// Log Analytics Workspace
resource logWorkspace 'Microsoft.OperationalInsights/workspaces@2022-10-01' = {
  name: logWorkspaceName
  location: location
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
  }
}

// Web App
resource webApp 'Microsoft.Web/sites@2023-01-01' = {
  name: webAppName
  location: location
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: appServicePlan.id
    httpsOnly: true
    siteConfig: {
      netFrameworkVersion: 'v10.0'
      minTlsVersion: '1.2'
      ftpsState: 'Disabled'
      alwaysOn: sku != 'F1'
      appSettings: [
        {
          name: 'ASPNETCORE_ENVIRONMENT'
          value: 'Production'
        }
        {
          name: 'WEBSITES_ENABLE_APP_SERVICE_STORAGE'
          value: 'true'
        }
        {
          name: 'AlpacaApiKeyId'
          value: '@Microsoft.KeyVault(VaultName=${kvName};SecretName=AlpacaApiKeyId)'
        }
        {
          name: 'AlpacaApiSecretKey'
          value: '@Microsoft.KeyVault(VaultName=${kvName};SecretName=AlpacaApiSecretKey)'
        }
        {
          name: 'PolygonApiKey'
          value: '@Microsoft.KeyVault(VaultName=${kvName};SecretName=PolygonApiKey)'
        }
        {
          name: 'ClaudeApiKey'
          value: '@Microsoft.KeyVault(VaultName=${kvName};SecretName=ClaudeApiKey)'
        }
      ]
    }
  }
}

// Grant web app access to Key Vault
resource kvRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVault.id, webApp.id, 'Key Vault Secrets User')
  scope: keyVault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4633458b-17de-408a-b874-0445c86b69e6')
    principalId: webApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

// Diagnostic settings
resource diagnosticSettings 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  name: 'diag-${webAppName}'
  scope: webApp
  properties: {
    workspaceId: logWorkspace.id
    logs: [
      { category: 'AppServiceHTTPLogs'; enabled: true }
      { category: 'AppServiceConsoleLogs'; enabled: true }
      { category: 'AppServiceAppLogs'; enabled: true }
    ]
    metrics: [
      { category: 'AllMetrics'; enabled: true }
    ]
  }
}

output webAppUrl string = 'https://${webApp.properties.defaultHostName}'
output webAppName string = webApp.name
output keyVaultName string = keyVault.name
