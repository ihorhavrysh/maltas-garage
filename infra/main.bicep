// The live demo's Azure resources as code: an App Service on the Free (F1) Linux plan and an
// Azure SQL database on the free offer. It describes what exists today (created by hand with
// az); it is not deployed by CI yet. Preview a change without applying it:
//
//   az deployment group what-if -g maltas-garage-demo-rg -f infra/main.bicep \
//     -p sqlAdminPassword=... stripeSecretKey=... stripePublishableKey=... stripeWebhookSecret=... demoSellerStripeAccountId=...
//
// Both tiers are free. The database pauses when its monthly allowance is used up
// (freeLimitExhaustionBehavior AutoPause) instead of switching to paid billing.

@description('Azure region; the free SQL offer is tied to the region it was first used in.')
param location string = resourceGroup().location

@description('Prefix for every resource name.')
param namePrefix string = 'maltas-garage-demo'

@description('Public address of the site, used in e-mails and the sitemap.')
param baseUrl string = 'https://${namePrefix}.azurewebsites.net'

param supportEmail string = 'support@example.com'
param sqlAdminLogin string = 'mgdemoadmin'

@secure()
param sqlAdminPassword string

@secure()
param stripeSecretKey string

@secure()
param stripePublishableKey string

@secure()
param stripeWebhookSecret string

@description('Stripe test-mode connected account shared by the demo sellers.')
@secure()
param demoSellerStripeAccountId string

resource plan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: '${namePrefix}-plan'
  location: location
  kind: 'linux'
  sku: {
    name: 'F1'
    tier: 'Free'
  }
  properties: {
    reserved: true // Linux
  }
}

resource sqlServer 'Microsoft.Sql/servers@2023-08-01-preview' = {
  name: '${namePrefix}-sql'
  location: location
  properties: {
    administratorLogin: sqlAdminLogin
    administratorLoginPassword: sqlAdminPassword
    minimalTlsVersion: '1.2'
    version: '12.0'
  }
}

// Lets App Service reach the server. 0.0.0.0 means "Azure services", from any tenant; narrowing
// it to the app's outbound addresses is listed in docs/open-items.md
resource allowAzure 'Microsoft.Sql/servers/firewallRules@2023-08-01-preview' = {
  parent: sqlServer
  name: 'AllowAzureServices'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

resource database 'Microsoft.Sql/servers/databases@2023-08-01-preview' = {
  parent: sqlServer
  name: '${namePrefix}-db'
  location: location
  sku: {
    name: 'GP_S_Gen5'
    tier: 'GeneralPurpose'
    family: 'Gen5'
    capacity: 1
  }
  properties: {
    useFreeLimit: true
    freeLimitExhaustionBehavior: 'AutoPause' // never bill: pause until the next month
    autoPauseDelay: 60
    minCapacity: json('0.5')
    maxSizeBytes: 34359738368
    requestedBackupStorageRedundancy: 'Local'
  }
}

resource app 'Microsoft.Web/sites@2023-12-01' = {
  name: namePrefix
  location: location
  kind: 'app,linux'
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      alwaysOn: false // not available on F1
      ftpsState: 'FtpsOnly'
      minTlsVersion: '1.2'
      http20Enabled: true
      appSettings: [
        { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
        { name: 'WEBSITE_RUN_FROM_PACKAGE', value: '1' }
        { name: 'SCM_DO_BUILD_DURING_DEPLOYMENT', value: 'false' }
        { name: 'WEBSITE_HTTPLOGGING_RETENTION_DAYS', value: '3' }
        { name: 'App__BaseUrl', value: baseUrl }
        { name: 'App__SupportEmail', value: supportEmail }
        { name: 'Demo__Enabled', value: 'true' }
        { name: 'Demo__SellerStripeAccountId', value: demoSellerStripeAccountId }
        { name: 'Email__Provider', value: 'Log' }
        // Uploads on the persistent /home volume: wwwroot is read-only when run from package
        { name: 'Storage__Provider', value: 'Local' }
        { name: 'Storage__LocalRootPath', value: '/home/data/uploads' }
        { name: 'Stripe__SecretKey', value: stripeSecretKey }
        { name: 'Stripe__PublishableKey', value: stripePublishableKey }
        { name: 'Stripe__WebhookSecret', value: stripeWebhookSecret }
      ]
      connectionStrings: [
        {
          name: 'DefaultConnection'
          type: 'SQLAzure'
          connectionString: 'Server=tcp:${sqlServer.properties.fullyQualifiedDomainName},1433;Database=${database.name};User ID=${sqlAdminLogin};Password=${sqlAdminPassword};Encrypt=True;TrustServerCertificate=False;Connection Timeout=60;MultipleActiveResultSets=true'
        }
      ]
    }
  }
}

output siteUrl string = 'https://${app.properties.defaultHostName}'
