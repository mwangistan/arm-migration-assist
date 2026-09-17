// =============================================================================
// Feature 1 - Repository Assessment Engine (FRONTEND ONLY)
// Deploys the frontend host as an Azure Static Web App (Free tier) into the
// EXISTING rg-arm-migration-assist resource group. Pairs with infra/backend.bicep.
//
// Content is published separately (SWA CLI with the deployment token); no GitHub
// repository is linked here. The SPA is built with VITE_API_BASE_URL pointing at
// the backend Container App, so the two tiers are wired at build time.
// =============================================================================

targetScope = 'resourceGroup'

@description('Static Web Apps is not available in every region; pick a supported one.')
@allowed(['eastus2', 'centralus', 'westus2', 'eastasia', 'westeurope'])
param staticWebAppLocation string = 'eastus2'

@description('Name of the frontend Static Web App to create.')
param staticWebAppName string = 'swa-arm-migration-assessment'

@description('Tag: creator identity (policy-required).')
param createdBy string

@description('Tag: project name (matches existing resources).')
param project string = 'arm-migration-assist'

var tags = {
  CreatedBy: createdBy
  Project: project
}

resource staticWebApp 'Microsoft.Web/staticSites@2024-04-01' = {
  name: staticWebAppName
  location: staticWebAppLocation
  tags: tags
  sku: {
    name: 'Free'
    tier: 'Free'
  }
  properties: {
    allowConfigFileUpdates: true
  }
}

output staticWebAppName string = staticWebApp.name
output staticWebAppHostname string = staticWebApp.properties.defaultHostname
output staticWebAppUrl string = 'https://${staticWebApp.properties.defaultHostname}'
