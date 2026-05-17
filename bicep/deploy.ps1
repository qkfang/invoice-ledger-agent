az group create --name 'rg-invledger' --location 'swedencentral'

az deployment group create --name 'invledger-dev' --resource-group 'rg-invledger' --template-file './main.bicep' --parameters './main.bicepparam'

$spAppId = az ad sp list --display-name 'sp-demo-01' --query '[0].appId' -o tsv
$subscriptionId = az account show --query 'id' -o tsv
az role assignment create --assignee $spAppId --role 'Contributor' --scope "/subscriptions/$subscriptionId/resourceGroups/rg-invledger"
az role assignment create --assignee $spAppId --role 'User Access Administrator' --scope "/subscriptions/$subscriptionId/resourceGroups/rg-invledger"
