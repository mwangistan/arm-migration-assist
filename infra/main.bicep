// Infrastructure for ARM Migration Assist - Migration Planner API.
// Scope: resource group. Idempotent; safe to re-run.
targetScope = 'resourceGroup'

@description('Deployment region for all resources.')
param location string = resourceGroup().location

@description('Short suffix applied to resource names to keep them stable across re-runs.')
@minLength(2)
@maxLength(12)
param nameSuffix string = 'armmigassist'

@description('Container image reference for the API. Defaults to the quickstart image so infra can be provisioned before the first real build is pushed.')
param containerImage string = 'mcr.microsoft.com/k8se/quickstart:latest'

@description('Comma-separated list of browser origins allowed to call the API.')
param allowedOrigins string = 'http://localhost:5173,http://localhost:3000'

@description('Deploy the Azure AI Foundry (AI Services) account and phi-4 model deployment.')
param deployFoundry bool = true

@description('Optional URL of the AutomatedMigration (F3) API. When set, F2 will POST plans to this endpoint and embed the returned jobId in its response.')
param automationApiUrl string = ''

@description('Log Analytics workspace retention in days.')
param logRetentionDays int = 30

var acrName         = 'acr${nameSuffix}'
var lawName         = 'law-${nameSuffix}'
var caeName         = 'cae-${nameSuffix}'
var appName         = 'ca-${nameSuffix}-planner-api'
var foundryName     = 'foundry-${nameSuffix}'
var uamiName        = 'id-${nameSuffix}-planner-api'
var phiDeploymentNm = 'phi-4'

// User-assigned MI is created first so RBAC can be granted before the Container App exists,
// avoiding a chicken-and-egg cycle with AcrPull.
resource uami 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: uamiName
  location: location
}

resource law 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: lawName
  location: location
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: logRetentionDays
    features: { enableLogAccessUsingOnlyResourcePermissions: true }
  }
}

resource acr 'Microsoft.ContainerRegistry/registries@2023-11-01-preview' = {
  name: acrName
  location: location
  sku: { name: 'Basic' }
  properties: {
    adminUserEnabled: false
    publicNetworkAccess: 'Enabled'
  }
}

resource foundry 'Microsoft.CognitiveServices/accounts@2024-10-01' = if (deployFoundry) {
  name: foundryName
  location: location
  kind: 'AIServices'
  sku: { name: 'S0' }
  identity: { type: 'SystemAssigned' }
  properties: {
    customSubDomainName: foundryName
    publicNetworkAccess: 'Enabled'
    disableLocalAuth: false
  }
}

resource phi 'Microsoft.CognitiveServices/accounts/deployments@2024-10-01' = if (deployFoundry) {
  parent: foundry
  name: phiDeploymentNm
  sku: {
    name: 'GlobalStandard'
    capacity: 1
  }
  properties: {
    model: {
      format: 'Microsoft'
      name: 'Phi-4'
      version: '7'
    }
    raiPolicyName: 'Microsoft.DefaultV2'
  }
}

resource cae 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: caeName
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: law.properties.customerId
        sharedKey: law.listKeys().primarySharedKey
      }
    }
    zoneRedundant: false
  }
}

var acrPullRoleId               = '7f951dda-4ed3-4680-a7ca-43fe172d538d'
var cognitiveServicesUserRoleId = 'a97b65f3-24c7-4388-baec-2e87135dc908'

resource acrPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: acr
  name: guid(acr.id, uami.id, 'AcrPull')
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', acrPullRoleId)
    principalId: uami.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

resource cognitiveServicesUser 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (deployFoundry) {
  scope: foundry!
  name: guid(foundry!.id, uami.id, 'CognitiveServicesUser')
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', cognitiveServicesUserRoleId)
    principalId: uami.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

var foundryInferenceEndpoint = deployFoundry ? '${foundry!.properties.endpoint}models' : ''

resource app 'Microsoft.App/containerApps@2024-03-01' = {
  name: appName
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${uami.id}': {}
    }
  }
  properties: {
    managedEnvironmentId: cae.id
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: true
        targetPort: 8080
        transport: 'auto'
        allowInsecure: false
      }
      registries: [
        {
          server: acr.properties.loginServer
          identity: uami.id
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'api'
          image: containerImage
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          env: [
            { name: 'MIGRATIONPLANNER_MODEL_PROVIDER',   value: deployFoundry ? 'Phi' : 'Fake' }
            { name: 'MIGRATIONPLANNER_PHI_ENDPOINT',     value: foundryInferenceEndpoint }
            { name: 'MIGRATIONPLANNER_PHI_DEPLOYMENT',   value: phiDeploymentNm }
            { name: 'MIGRATIONPLANNER_ALLOWED_ORIGINS',  value: allowedOrigins }
            { name: 'MIGRATIONPLANNER_AUTOMATION_API_URL', value: automationApiUrl }
            { name: 'AZURE_CLIENT_ID',                   value: uami.properties.clientId }
          ]
        }
      ]
      scale: {
        minReplicas: 0
        maxReplicas: 3
      }
    }
  }
  dependsOn: [
    acrPull
  ]
}

output acrLoginServer   string = acr.properties.loginServer
output acrName          string = acr.name
output containerAppName string = app.name
output containerAppFqdn string = app.properties.configuration.ingress.fqdn
output foundryEndpoint  string = deployFoundry ? foundry!.properties.endpoint : ''
output uamiClientId     string = uami.properties.clientId
output resourceGroup    string = resourceGroup().name
