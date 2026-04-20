#!/bin/bash
# One-shot Azure deployment script
# Usage: ./infra/deploy.sh <resource-group> [location]
# Example: ./infra/deploy.sh idiotproof-rg eastus

set -e

RG=${1:-idiotproof-rg}
LOCATION=${2:-eastus}

echo "Deploying IdiotProof to Azure resource group: $RG ($LOCATION)"

# Create resource group if it doesn't exist
az group create --name "$RG" --location "$LOCATION" --output none

# Deploy infrastructure
az deployment group create \
  --resource-group "$RG" \
  --template-file infra/main.bicep \
  --parameters location="$LOCATION" \
  --query "properties.outputs.webAppUrl.value" \
  --output tsv

echo "Done. Configure secrets via Azure Portal > Key Vault or:"
echo "  az keyvault secret set --vault-name idiotproof-kv --name AlpacaApiKeyId --value YOUR_KEY"
echo "  az keyvault secret set --vault-name idiotproof-kv --name AlpacaApiSecretKey --value YOUR_SECRET"
echo "  az keyvault secret set --vault-name idiotproof-kv --name PolygonApiKey --value YOUR_KEY"
echo "  az keyvault secret set --vault-name idiotproof-kv --name ClaudeApiKey --value YOUR_KEY"
