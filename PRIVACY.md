# Privacy

**English** · [Русский](PRIVACY.ru.md)

Agent Bridge serves only `127.0.0.1` (your own computer) and requires a random token. Agents on your machine can
control the game; nothing on the network can.

## Anonymous statistics and error reports

Both are **on by default**, asked about once on first launch, and can be turned off at any time in
*Options → Mod settings → Agent Bridge*. "Show what would be sent" displays the exact payload.

**Collected**

- random install id (generated locally, not linked to Steam or you)
- Agent Bridge version, RimWorld version, runtime (coreclr/mono), OS family (Unix/Win32NT), game language
- session length in minutes, whether Harmony is present
- per-command success/error counts (only names of registered commands)
- agent *kinds* that connected (claude, codex, mcp, curl, other) — never the raw client string
- for Agent Bridge failures: exception type, command name, a hash signature and up to 12 stack frames with your home
  directory, user name and absolute paths removed

**Never collected**: names of pawns/colonies/saves, file paths, your user name, mod lists, game content, IP-based
location (the receiving server does not store IPs).

**Where it goes**: `https://agent-bridge-telemetry.ivanzhe03.workers.dev` (Cloudflare Worker, source in
`telemetry/worker.js`). It stores daily aggregate counts and files each new error signature once as a GitHub issue in
this repository. Reports are sent at most every 30 minutes and on game exit.

"Report a bug" sends nothing by itself: it opens a prefilled GitHub issue in your browser and copies the full report to
your clipboard, so you can review it before posting.
