// =============================================================================
// Feature 1 - Repository Assessment Engine (BACKEND ONLY)
// Deploys the backend API as an Azure Container App into the EXISTING
// rg-arm-migration-assist environment. Pairs with infra/frontend.bicep.
//
// Design notes / key decisions:
//  - The Container Apps environment (cae-arm-migration-assist) and the container
//    registry (acrarmmigassist, admin user DISABLED) already exist and are
//    referenced as existing resources - this template never recreates them.
//  - ACR authentication uses a USER-assigned managed identity with the AcrPull
//    role. A user-assigned identity is used (rather than system-assigned, as the
//    planner app uses) specifically to break the classic chicken-and-egg problem:
//    the identity's principalId is known before the app pulls its first image, so
//    the role assignment is guaranteed to exist before the initial image pull.
//  - Ingress targets port 8080 to match the aspnet:10.0 default and the existing
//    planner app convention.
//  - Every resource carries the CreatedBy/Project tags required by subscription
//    policy (require-createdby-tag-assignment).
// =============================================================================

targetScope = 'resourceGroup'

@description('Azure region. Must match the existing environment (eastus2).')
param location string = resourceGroup().location

@description('Name of the backend Container App to create.')
param backendAppName string = 'ca-arm-migration-assessment-api'

@description('Existing Container Apps managed environment.')
param managedEnvironmentName string = 'cae-arm-migration-assist'

@description('Existing Azure Container Registry (admin user disabled).')
param acrName string = 'acrarmmigassist'

@description('Container image tag to deploy (built and pushed by az acr build).')
param imageTag string = 'latest'

@description('Absolute HTTPS base URL of the Feature 2 migration planner API (required by the backend at startup).')
param migrationPlannerBaseUrl string

@description('Allowed CORS origin for the frontend (the Static Web App URL). Empty until the SWA hostname is known.')
param frontendOrigin string = ''

@description('Tag: creator identity (policy-required).')
param createdBy string

@description('Tag: project name (matches existing resources).')
param project string = 'arm-migration-assist'

var tags = {
  CreatedBy: createdBy
  Project: project
}

var acrLoginServer = '${acrName}.azurecr.io'
var backendImage = '${acrLoginServer}/arm-migration-assessment-api:${imageTag}'

// AcrPull role definition (built-in).
var acrPullRoleId = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7f951dda-4ed3-4680-a7ca-43fe172d538d')

// ---- existing infrastructure -------------------------------------------------
resource managedEnvironment 'Microsoft.App/managedEnvironments@2024-03-01' existing = {
  name: managedEnvironmentName
}

resource acr 'Microsoft.ContainerRegistry/registries@2023-07-01' existing = {
  name: acrName
}

// ---- identity + ACR pull permission -----------------------------------------
resource pullIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'id-${backendAppName}'
  location: location
  tags: tags
}

resource acrPullAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(acr.id, pullIdentity.id, 'AcrPull')
  scope: acr
  properties: {
    principalId: pullIdentity.properties.principalId
    roleDefinitionId: acrPullRoleId
    principalType: 'ServicePrincipal'
  }
}

// ---- backend Container App ---------------------------------------------------
// Environment variables use the ASP.NET Core '__' convention for nested config
// keys (MigrationPlanner:BaseUrl -> MigrationPlanner__BaseUrl,
// FrontendOrigins[0] -> FrontendOrigins__0).
var baseEnv = [
  {
    name: 'ASPNETCORE_ENVIRONMENT'
    value: 'Production'
  }
  {
    name: 'MigrationPlanner__BaseUrl'
    value: migrationPlannerBaseUrl
  }
  {
    name: 'ARM_MIGRATION_WORKSPACE_ROOT'
    value: '/tmp/arm-ma'
  }
]
var corsEnv = empty(frontendOrigin) ? [] : [
  {
    name: 'FrontendOrigins__0'
    value: frontendOrigin
  }
]

resource backend 'Microsoft.App/containerApps@2024-03-01' = {
  name: backendAppName
  location: location
  tags: tags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${pullIdentity.id}': {}
    }
  }
  properties: {
    managedEnvironmentId: managedEnvironment.id
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: true
        targetPort: 8080
        transport: 'Auto'
        allowInsecure: false
        traffic: [
          {
            weight: 100
            latestRevision: true
          }
        ]
      }
      registries: [
        {
          server: acrLoginServer
          identity: pullIdentity.id
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'assessment-api'
          image: backendImage
          resources: {
            cpu: json('1.0')
            memory: '2Gi'
          }
          env: concat(baseEnv, corsEnv)
        }
      ]
      scale: {
        minReplicas: 0
        maxReplicas: 2
        rules: [
          {
            name: 'http-scale'
            http: {
              metadata: {
                concurrentRequests: '10'
              }
            }
          }
        ]
      }
    }
  }
  dependsOn: [
    acrPullAssignment
  ]
}

output backendFqdn string = backend.properties.configuration.ingress.fqdn
output backendUrl string = 'https://${backend.properties.configuration.ingress.fqdn}'
