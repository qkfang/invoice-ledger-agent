az group create --name 'rg-invledger' --location 'australiaeast'

az deployment group create --name 'invledger-dev' --resource-group 'rg-invledger' --template-file './main.bicep' --parameters './main.bicepparam'
