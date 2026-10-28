# Telemetry receiver

**English** · [Русский](README.ru.md)

Options considered for collecting reports from players:

| Option | Verdict |
|---|---|
| Steam Workshop | no API for mods to submit data; comments are manual only |
| GitHub Issues directly from the game | would require shipping a write token in the mod — anyone could extract and abuse it |
| **Small serverless receiver** (this folder) | token stays server-side, rate-limited, de-duplicates errors, free tier is enough |
| Player-initiated report | "Report a bug" button: prefilled GitHub issue + clipboard; works with no server |

## Deploy (Cloudflare Workers, free tier)

```
npm create cloudflare@latest agent-bridge-telemetry   # choose "Hello World" worker, replace src with worker.js
npx wrangler kv namespace create STATS                 # add the binding to wrangler.toml
npx wrangler secret put GITHUB_TOKEN                   # fine-grained token: this repo, Issues read/write
npx wrangler secret put REPO                           # Akseleron/rimworld-agent-bridge
npx wrangler deploy
```

Then set `Telemetry.DefaultEndpoint` to `https://<worker>.workers.dev/v1/report` and release. Until then the mod sends
nothing. Daily aggregates live in KV under `day:<date>:<version>`; each new error signature becomes one issue labelled
`auto-report`, later occurrences only increase its counter.
