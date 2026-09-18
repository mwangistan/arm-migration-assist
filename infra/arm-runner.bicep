// ARM64 validation runner: single Ubuntu 24.04 ARM64 VM + NSG + public IP.
// Deploys to the existing arm-migration-assist resource group.
targetScope = 'resourceGroup'

@description('Deployment region. Must be East US 2 (Dpsv6 quota + main deployment).')
param location string = resourceGroup().location

@description('Short suffix applied to resource names.')
@minLength(2)
@maxLength(12)
param nameSuffix string = 'armmigassist'

@description('VM SKU. Standard_D4ps_v6 = Cobalt 100 Arm64, 4 vCPU / 16 GiB.')
param vmSku string = 'Standard_D4ps_v6'

@description('Local admin username for SSH.')
param adminUsername string = 'armrunner'

@description('Public SSH key material for admin login.')
@secure()
param adminSshPublicKey string

@description('Bearer token required by /api/v1/arm64/runs. Rotate as needed.')
@secure()
param runnerBearerToken string

@description('Your dev/workstation source IP allowed on 22 (SSH). Use e.g. "203.0.113.4/32".')
param sshAllowedSourceCidr string

@description('Ubuntu 24.04 ARM64 image reference.')
param imagePublisher string = 'Canonical'
param imageOffer string = 'ubuntu-24_04-lts'
param imageSku string = 'server-arm64'
param imageVersion string = 'latest'

@description('Tags applied to every resource (subscription policy requires CreatedBy).')
param resourceTags object = {
  CreatedBy: 'bnyandieka@microsoft.com'
  Project: 'arm-migration-assist'
}

var runnerName    = 'vm-${nameSuffix}-arm64-runner'
var nicName       = 'nic-${nameSuffix}-arm64-runner'
var pipName       = 'pip-${nameSuffix}-arm64-runner'
var nsgName       = 'nsg-${nameSuffix}-arm64-runner'
var vnetName      = 'vnet-${nameSuffix}-arm64-runner'
var subnetName    = 'default'
var osDiskName    = 'osdisk-${nameSuffix}-arm64-runner'
var dnsLabel      = toLower('${nameSuffix}-arm64-runner')

// Small dedicated VNet keeps the runner network-isolated from the composed host env.
resource vnet 'Microsoft.Network/virtualNetworks@2023-11-01' = {
  name: vnetName
  location: location
  tags: resourceTags
  properties: {
    addressSpace: { addressPrefixes: [ '10.60.0.0/24' ] }
    subnets: [
      {
        name: subnetName
        properties: {
          addressPrefix: '10.60.0.0/26'
          networkSecurityGroup: { id: nsg.id }
        }
      }
    ]
  }
}

resource nsg 'Microsoft.Network/networkSecurityGroups@2023-11-01' = {
  name: nsgName
  location: location
  tags: resourceTags
  properties: {
    securityRules: [
      {
        name: 'AllowSshFromDev'
        properties: {
          priority: 100
          direction: 'Inbound'
          access: 'Allow'
          protocol: 'Tcp'
          sourceAddressPrefix: sshAllowedSourceCidr
          sourcePortRange: '*'
          destinationAddressPrefix: '*'
          destinationPortRange: '22'
        }
      }
      {
        name: 'AllowRunnerHttp'
        properties: {
          priority: 200
          direction: 'Inbound'
          access: 'Allow'
          protocol: 'Tcp'
          // Runner is bearer-token authenticated; leaving the port publicly reachable is
          // acceptable for the hackathon demo. Rotate the token if leaked.
          sourceAddressPrefix: 'Internet'
          sourcePortRange: '*'
          destinationAddressPrefix: '*'
          destinationPortRange: '8080'
        }
      }
    ]
  }
}

resource pip 'Microsoft.Network/publicIPAddresses@2023-11-01' = {
  name: pipName
  location: location
  tags: resourceTags
  sku: { name: 'Standard' }
  properties: {
    publicIPAllocationMethod: 'Static'
    dnsSettings: { domainNameLabel: dnsLabel }
  }
}

resource nic 'Microsoft.Network/networkInterfaces@2023-11-01' = {
  name: nicName
  location: location
  tags: resourceTags
  properties: {
    ipConfigurations: [
      {
        name: 'ipconfig1'
        properties: {
          privateIPAllocationMethod: 'Dynamic'
          subnet: { id: '${vnet.id}/subnets/${subnetName}' }
          publicIPAddress: { id: pip.id }
        }
      }
    ]
  }
}

// Cloud-init installs toolchains and creates the systemd unit; the actual runner binary
// is uploaded post-boot via scp (kept small so cloud-init stays under the 64 KB limit).
var cloudInit = '''#cloud-config
package_update: true
package_upgrade: false
packages:
  - git
  - curl
  - jq
  - unzip
  - build-essential
  - cmake
  - pkg-config
  - python3
  - python3-venv
  - python3-pip
  - libicu-dev
  - ca-certificates

write_files:
  - path: /etc/systemd/system/arm-validation-runner.service
    permissions: '0644'
    owner: root:root
    content: |
      [Unit]
      Description=ARM Migration Assist validation runner
      After=network-online.target
      Wants=network-online.target

      [Service]
      Type=simple
      User=armrunner
      Group=armrunner
      WorkingDirectory=/opt/arm-validation-runner
      Environment="ARM64_RUNNER_LISTEN_URL=http://0.0.0.0:8080"
      Environment="ARM64_RUNNER_WORK_ROOT=/var/lib/arm-validation-runner/work"
      Environment="ARM64_RUNNER_VM_SKU=__VM_SKU__"
      Environment="ARM64_RUNNER_REGION=__REGION__"
      EnvironmentFile=-/etc/arm-validation-runner/secrets.env
      ExecStart=/opt/arm-validation-runner/Arm64ValidationRunner
      Restart=on-failure
      RestartSec=5
      NoNewPrivileges=true
      ProtectSystem=full
      ReadWritePaths=/var/lib/arm-validation-runner /tmp

      [Install]
      WantedBy=multi-user.target

  - path: /etc/arm-validation-runner/secrets.env
    permissions: '0600'
    owner: armrunner:armrunner
    content: |
      ARM64_RUNNER_BEARER_TOKEN=__BEARER_TOKEN__

  - path: /opt/arm-validation-runner/install-dotnet.sh
    permissions: '0755'
    owner: root:root
    content: |
      #!/usr/bin/env bash
      set -euxo pipefail
      curl -sSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
      bash /tmp/dotnet-install.sh --channel 8.0 --install-dir /usr/share/dotnet
      ln -sf /usr/share/dotnet/dotnet /usr/local/bin/dotnet

  - path: /opt/arm-validation-runner/install-node.sh
    permissions: '0755'
    owner: root:root
    content: |
      #!/usr/bin/env bash
      set -euxo pipefail
      curl -fsSL https://deb.nodesource.com/setup_20.x -o /tmp/nodesource_setup.sh
      bash /tmp/nodesource_setup.sh
      apt-get install -y nodejs

runcmd:
  - id -u armrunner >/dev/null 2>&1 || useradd -m -s /bin/bash armrunner
  - install -d -o armrunner -g armrunner -m 0755 /opt/arm-validation-runner
  - install -d -o armrunner -g armrunner -m 0755 /var/lib/arm-validation-runner
  - install -d -o armrunner -g armrunner -m 0755 /var/lib/arm-validation-runner/work
  - install -d -o root -g root -m 0755 /etc/arm-validation-runner
  - /opt/arm-validation-runner/install-dotnet.sh
  - /opt/arm-validation-runner/install-node.sh
  - systemctl daemon-reload
  - systemctl enable arm-validation-runner.service
  - echo "cloud-init done. Waiting for binary upload before starting service."
'''

var populatedCloudInit = replace(replace(replace(cloudInit, '__BEARER_TOKEN__', runnerBearerToken), '__VM_SKU__', vmSku), '__REGION__', location)

resource vm 'Microsoft.Compute/virtualMachines@2024-03-01' = {
  name: runnerName
  location: location
  tags: resourceTags
  identity: { type: 'SystemAssigned' }
  properties: {
    hardwareProfile: { vmSize: vmSku }
    storageProfile: {
      imageReference: {
        publisher: imagePublisher
        offer: imageOffer
        sku: imageSku
        version: imageVersion
      }
      osDisk: {
        name: osDiskName
        createOption: 'FromImage'
        managedDisk: { storageAccountType: 'Premium_LRS' }
        diskSizeGB: 64
      }
    }
    osProfile: {
      computerName: 'armrunner'
      adminUsername: adminUsername
      linuxConfiguration: {
        disablePasswordAuthentication: true
        ssh: {
          publicKeys: [
            {
              path: '/home/${adminUsername}/.ssh/authorized_keys'
              keyData: adminSshPublicKey
            }
          ]
        }
      }
      customData: base64(populatedCloudInit)
    }
    networkProfile: {
      networkInterfaces: [ { id: nic.id } ]
    }
    diagnosticsProfile: { bootDiagnostics: { enabled: true } }
  }
}

output vmFqdn string = pip.properties.dnsSettings.fqdn
output vmPublicIp string = pip.properties.ipAddress
output runnerUrl string = 'http://${pip.properties.dnsSettings.fqdn}:8080'
output vmPrincipalId string = vm.identity.principalId
