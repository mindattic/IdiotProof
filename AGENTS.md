# MindAttic project agent entrypoint

Read the shared protocol at ..\mindattic-agent-standard\AGENTS.md and this project's own
documentation. Common prompt commands are implemented by the shared runner; do not add a second
copy under a provider-specific command folder.

## Driving IdiotProof (the app itself)

IdiotProof is operated through one process, the IdiotProof Hub, by the `idiotproof` CLI or the
`idiotproof` MCP server. Before doing anything with the app (accounts, orders, strategies, market
data, docs), start every session with:

    idiotproof agent bootstrap

It works with the Hub down and tells you whether to run `idiotproof hub start`. Then follow
docs/agent/IDIOTPROOF_PROTOCOL.md: everything goes through the Hub, exit 3 (blocked) is final,
exit 4 means show the person the preview and confirm only on their explicit yes, and every time
is Eastern (ET). Command reference: docs/CLI_COMMANDS.md and docs/MCP_TOOLS.md (regenerate with
`idiotproof agent export`). Project knowledge: `idiotproof docs search <words>`, docs/BIBLE.md
(the laws in section 5 are non-negotiable) and docs/USER_STORIES.md.
