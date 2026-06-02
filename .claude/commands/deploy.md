Deploy the IdiotProof landing page (`mindattic.com/idiotproof.htm`) via **MindAttic.Deploy** (sibling repo at `D:\Projects\MindAttic\MindAttic.Deploy`).

Renders this repo's `README.md` through the catalog template (`template/index.template.htm`, Cyberspace theme, MindAttic.UiUx components loaded via jsDelivr) and FTPS-uploads the single-file result. One repo owns the whole FTP pipeline — there is no per-project deploy state in this folder.

Run this command and report the result:

```
powershell -NoProfile -ExecutionPolicy Bypass -Command "cd D:\Projects\MindAttic\MindAttic.Deploy; npm run deploy -- --only idiotproof"
```

It will:

1. Render `D:\Projects\MindAttic\IdiotProof\README.md` through the catalog template.
2. FTPS-upload `out/idiotproof.htm` to `/mindattic.com/idiotproof.htm`.

After running, summarize the result and flag any failures.

Notes:
- Catalog entry: `MindAttic.Deploy/projects.json` -> `projects[]` slug `idiotproof` (theme: Cyberspace).
- Credentials: MindAttic.Vault at `%APPDATA%\MindAttic\Deploy\ftp.json` (transitional fallback: `MindAttic.Deploy/secrets/ftp.json`, gitignored).
- A Blazor app deploy also exists in `apps[]` (`--app idiotproof`) but is **disabled** pending Azure infra (App Service + `AZURE_WEBAPP_PUBLISH_PROFILE`). Until that's provisioned, `/deploy` ships the landing page only.
