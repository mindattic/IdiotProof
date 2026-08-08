# Azure provisioning checklist — IdiotProof

**Nothing in this file has been run.** It's a step-by-step reference for when you're ready to
actually provision Azure infrastructure and flip the deploy pipeline on. Every `az` command
creates a real, billed resource — run them one at a time and check the result before moving on,
rather than pasting the whole file into a shell.

## Architecture this assumes

- **IdiotProof.Blazor** → Azure App Service (`idiotproof-web`), Linux or Windows plan, single
  instance is fine to start.
- **IdiotProof.Monitor** → stays on your PC as a Windows Service (per your decision — fastest
  path, matches the existing on-box design). It talks to Azure SQL over the internet instead of
  LocalDB, and shares the same Key Vault-backed DataProtection key ring as the Azure-hosted
  Blazor so it can decrypt `UserApiKeys` rows the web app writes.
- **Database** → Azure SQL (replaces LocalDB). Both Blazor and Monitor connect directly — no new
  HTTP API between them; this matches the existing "shared SQL" design (see `docs/BIBLE.md`,
  `docs/AMENDMENTS.md` IP-A9) and is far less work than building an authenticated API surface
  from scratch.
- **Secrets** → Azure Key Vault. `IdiotProof.Blazor/Program.cs` and `IdiotProof.Monitor/Program.cs`
  already read `DataProtection:AzureBlobUri` / `DataProtection:KeyVaultKeyUri` from
  `IConfiguration` and fall back to file-based storage when unset — so this is additive, not a
  behavior change, until these App Settings actually exist.
- **Broker/LLM credentials** → already cloud-ready via `MindAttic.Vault`'s
  `BrokerCredentialResolver`/`LlmCredentialResolver` (registered in `Program.cs` via
  `AddMindAtticVault(configuration)`) and `AppSettings.OverlayFromConfiguration` in
  `IdiotProof.Engine`. Nothing to change in code — just put secrets in Key Vault under the right
  names (below) and they take precedence automatically.

## Existing resources found in your subscription

`az group list` / `az sql server list` / `az webapp list` at the time this was written showed:
- Resource groups: `MyApps` (centralus), `DefaultResourceGroup-CUS`, `DefaultResourceGroup-EUS`.
- `MyApps` already has an App Service plan (`ASP-MyApps-99fe`) hosting `cursory` and
  `prose`, and a SQL server `prose-sql`.

**Decision needed:** reuse `MyApps` (one more DB on `prose-sql`, one more app on the
existing plan — cheaper) vs. a fresh resource group/plan/server for IdiotProof (cleaner
isolation, easier to tear down independently, its own cost line). The commands below assume a
**fresh** RG named `idiotproof-rg` — swap in `MyApps` + `prose-sql` if you'd rather share.

## 1. Resource group

```bash
az group create --name idiotproof-rg --location centralus
```

## 2. Azure SQL — server + database

```bash
az sql server create \
  --name idiotproof-sql \
  --resource-group idiotproof-rg \
  --location centralus \
  --admin-user idiotproofadmin \
  --admin-password '<generate a strong password, store it — not in this repo>'

az sql db create \
  --resource-group idiotproof-rg \
  --server idiotproof-sql \
  --name IdiotProof \
  --service-objective Basic     # cheapest tier; step up later if needed

# Allow Azure services (the App Service) through the firewall:
az sql server firewall-rule create \
  --resource-group idiotproof-rg \
  --server idiotproof-sql \
  --name AllowAzureServices \
  --start-ip-address 0.0.0.0 --end-ip-address 0.0.0.0

# Allow YOUR on-box Monitor to reach it — find your current public IP first
# (e.g. `curl ifconfig.me`), then:
az sql server firewall-rule create \
  --resource-group idiotproof-rg \
  --server idiotproof-sql \
  --name AllowMonitorBox \
  --start-ip-address <your-public-ip> --end-ip-address <your-public-ip>
```

Residential IPs change — if Monitor starts failing to connect after an ISP re-lease, the fix is
re-running the last command with the new IP (or switching to a static IP / VPN later).

Run the accumulated EF Core migrations against this new database once it's reachable
(`dotnet ef database update` from `IdiotProof.Blazor`, pointed at the Azure connection string via
`ConnectionStrings__IdiotProof`) — don't hand-run the SQL.

## 3. Storage account (DataProtection key ring blob)

```bash
az storage account create \
  --name idiotproofdpkeys \
  --resource-group idiotproof-rg \
  --location centralus \
  --sku Standard_LRS

az storage container create \
  --account-name idiotproofdpkeys \
  --name dataprotection-keys \
  --auth-mode login
```

## 4. Key Vault

```bash
az keyvault create \
  --name idiotproof-kv \
  --resource-group idiotproof-rg \
  --location centralus \
  --enable-rbac-authorization true

# The DataProtection key (used to encrypt the key ring itself, not a broker secret):
az keyvault key create --vault-name idiotproof-kv --name dataprotection-key --kty RSA --size 2048
```

Secrets — use the `--` separator so App Service / Key Vault references map back to the
`MindAttic:Vault:...` config sections `VaultConfigurationKeys` expects
(see `MindAttic.Vault/Configuration/VaultConfigurationKeys.cs`):

```bash
az keyvault secret set --vault-name idiotproof-kv \
  --name "MindAttic--Vault--Brokers--alpaca-paper--apiKey" --value "<paper key id>"
az keyvault secret set --vault-name idiotproof-kv \
  --name "MindAttic--Vault--Brokers--alpaca-paper--secret" --value "<paper secret>"
az keyvault secret set --vault-name idiotproof-kv \
  --name "MindAttic--Vault--Brokers--alpaca-live--apiKey" --value "<live key id>"
az keyvault secret set --vault-name idiotproof-kv \
  --name "MindAttic--Vault--Brokers--alpaca-live--secret" --value "<live secret>"
az keyvault secret set --vault-name idiotproof-kv \
  --name "MindAttic--Vault--LLM--claude--apiKey" --value "<claude api key>"
```

These are for the **global/host-level** Sandbox-fallback broker and LLM voting default
(`IdiotProof.Engine.Settings.AppSettings`) — separate from each user's own per-strategy Paper/Live
keys, which live encrypted in the `UserApiKeys` SQL table via the `/api-keys` page.

## 5. App Service

```bash
az appservice plan create \
  --name idiotproof-plan --resource-group idiotproof-rg \
  --location centralus --sku B1 --is-linux false

az webapp create \
  --name idiotproof-web --resource-group idiotproof-rg \
  --plan idiotproof-plan --runtime "dotnet:10"

# System-assigned managed identity — used to auth to Key Vault + Storage without secrets.
az webapp identity assign --name idiotproof-web --resource-group idiotproof-rg
```

App Settings (Azure "Application Settings" become env vars — matches
`ConnectionStrings__IdiotProof`'s `__`-for-`:` convention already used in `Program.cs`):

```bash
az webapp config appsettings set --name idiotproof-web --resource-group idiotproof-rg --settings \
  ConnectionStrings__IdiotProof="Server=tcp:idiotproof-sql.database.windows.net,1433;Database=IdiotProof;User ID=idiotproofadmin;Password=<pwd>;Encrypt=True;TrustServerCertificate=False;" \
  DataProtection__AzureBlobUri="https://idiotproofdpkeys.blob.core.windows.net/dataprotection-keys/keys.xml" \
  DataProtection__KeyVaultKeyUri="https://idiotproof-kv.vault.azure.net/keys/dataprotection-key"
```

Broker/LLM secrets don't need explicit App Settings — either use **Key Vault references**
(`@Microsoft.KeyVault(SecretUri=https://idiotproof-kv.vault.azure.net/secrets/MindAttic--Vault--Brokers--alpaca-live--apiKey)`)
as App Setting values, or let `AddMindAtticVault`'s `ConfigurationCredentialStore` read Key Vault
directly if wired as a configuration provider — confirm which pattern `AddMindAtticVaultFiles`
already expects before choosing (check `MindAttic.Vault/Configuration/MindAtticConfigurationSource.cs`).

## 6. RBAC — managed identity access to Key Vault + Storage

```bash
principalId=$(az webapp identity show --name idiotproof-web --resource-group idiotproof-rg --query principalId -o tsv)

az role assignment create --assignee "$principalId" \
  --role "Key Vault Secrets User" \
  --scope "/subscriptions/<sub-id>/resourceGroups/idiotproof-rg/providers/Microsoft.KeyVault/vaults/idiotproof-kv"

az role assignment create --assignee "$principalId" \
  --role "Key Vault Crypto User" \
  --scope "/subscriptions/<sub-id>/resourceGroups/idiotproof-rg/providers/Microsoft.KeyVault/vaults/idiotproof-kv"

az role assignment create --assignee "$principalId" \
  --role "Storage Blob Data Contributor" \
  --scope "/subscriptions/<sub-id>/resourceGroups/idiotproof-rg/providers/Microsoft.Storage/storageAccounts/idiotproofdpkeys"
```

Monitor (on-box) needs the same two Key Vault roles + the Storage role, granted to **your own
Azure AD user or a dedicated service principal** (not the web app's managed identity — Monitor
isn't running in Azure). `DefaultAzureCredential` on your dev box picks up your `az login`
session automatically; for the Windows Service running as a non-interactive user, use a service
principal with a client secret or certificate instead, injected via
`AZURE_CLIENT_ID`/`AZURE_CLIENT_SECRET`/`AZURE_TENANT_ID` env vars (which `DefaultAzureCredential`
also reads).

## 7. GitHub Actions service principal (`AZURE_CREDENTIALS`)

`.github/workflows/deploy.yml` already expects this secret name (fixed to match — it was
previously out of sync with `MindAttic.Deploy/projects.json`, which referenced
`AZURE_WEBAPP_PUBLISH_PROFILE` instead):

```bash
az ad sp create-for-rbac \
  --name "idiotproof-github-actions" \
  --role contributor \
  --scopes "/subscriptions/<sub-id>/resourceGroups/idiotproof-rg" \
  --sdk-auth
```

Paste the JSON output into a GitHub repo secret named `AZURE_CREDENTIALS` in `mindattic/IdiotProof`
(`gh secret set AZURE_CREDENTIALS --repo mindattic/IdiotProof < sp.json`, or via the GitHub UI —
don't leave the JSON in a file in this repo).

## 8. Turn the deploy pipeline on

Once everything above exists and the App Settings are wired:
1. In `MindAttic.Deploy/projects.json`, set the `idiotproof` app's `"disabled"` to `false`.
2. Push to `main` (or `workflow_dispatch`) to trigger `.github/workflows/deploy.yml`.
3. Watch the Azure App Service log stream (`az webapp log tail --name idiotproof-web
   --resource-group idiotproof-rg`) for the first boot — DataProtection and Vault are both
   fail-closed by design in production, so a missing setting shows up immediately as a startup
   crash rather than a silent fallback to an insecure default.

## Local Monitor changes needed once Azure SQL + Key Vault exist

Update the on-box Monitor's environment (however it's launched today — Windows Service config,
`.env`, etc.) with:
- `ConnectionStrings__IdiotProof` → the same Azure SQL connection string as the App Service.
- `DataProtection__AzureBlobUri` / `DataProtection__KeyVaultKeyUri` → same values as above.
- Azure AD credentials for `DefaultAzureCredential` (your `az login` session, or a service
  principal's `AZURE_CLIENT_ID`/`AZURE_CLIENT_SECRET`/`AZURE_TENANT_ID`) so it can reach Key
  Vault/Blob without prompting interactively.
