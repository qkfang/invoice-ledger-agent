@description('Fabric capacity name')
param name string

@description('Azure region')
param location string

@description('Resource tags')
param tags object = {}

@description('Fabric capacity SKU (e.g. F2, F4, F8, F16, F32, F64)')
param skuName string = 'F2'

@description('Capacity administrators (UPNs / object IDs)')
param adminMembers array = []

resource fabricCapacity 'Microsoft.Fabric/capacities@2023-11-01' = {
  name: name
  location: location
  tags: tags
  sku: {
    name: skuName
    tier: 'Fabric'
  }
  properties: {
    administration: {
      members: adminMembers
    }
  }
}

output id string = fabricCapacity.id
output name string = fabricCapacity.name
