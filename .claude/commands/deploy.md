Deploy the IdiotProof Blazor app via **MindAttic.Deploy** (sibling repo at `D:\Projects\MindAttic\MindAttic.Deploy`).

Run this command and report the result:

```
powershell -NoProfile -ExecutionPolicy Bypass -Command "cd D:\Projects\MindAttic\MindAttic.Deploy; npm run deploy -- --app idiotproof"
```

The app entry (`MindAttic.Deploy/projects.json` -> `apps[]` slug `idiotproof`) is currently **disabled** pending Azure infra (App Service + `AZURE_WEBAPP_PUBLISH_PROFILE`), so today this prints the "disabled" note and exits 0. See `.claude/skills/deploy/SKILL.md` for the steps to enable it.

Notes:
- There is no landing page to deploy. MindAttic.Deploy has no catalog mode (`--only idiotproof` is rejected) and there is no `mindattic.com/idiotproof.htm` page. This repo's README on GitHub -- https://github.com/mindattic/IdiotProof -- is the project page; edit `README.md` and push to `main` to update it.
