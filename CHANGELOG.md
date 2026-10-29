# Changelog

**English** · [Русский](CHANGELOG.ru.md)

## 1.1.3
Fixes from a full code review.
- Safety: `obj` reads no longer call methods without power access (a read of `Verse.Root.Shutdown` could quit the
  game). Turning the server off now closes open connections and drops queued commands; "Disconnect all agents" keeps
  a disabled server off.
- Fair play: orders, `drop`, policies and context menus work only on your own pawns (enemies could be disarmed);
  `build` respects research and other unlocks; bills accept only recipes of that workbench that are already
  researched; `pawn` hides health conditions the player cannot see. `research-set` now checks prerequisites in every mode.
- Python client: a command is never sent twice after a dropped connection or a timeout; the client waits as long as
  the command does; paths with `&` no longer break `eval`. The MCP server answers bad requests with an error instead
  of exiting.
- Game commands sent from the main menu answer "no game loaded" instead of a NullReferenceException crash report.
- `newgame` and `page-next` are no longer cut off after 75 seconds. A second `tick` while one is running is refused
  instead of stopping both. `batch` rejects waiting commands before running them.
- Telemetry: data from a failed delivery is kept for the next one. Receiver (telemetry/worker.js): a temporary
  GitHub error no longer loses an automatic issue, and duplicates from simultaneous reports are much less likely.

## 1.1.2
- Reports #2 and #3 (Windows, Chinese): an agent probing internals with `obj` set a get-only property and indexed past
  a list's end. `obj` now writes get-only auto-properties through their backing field, explains computed read-only
  properties and out-of-range indexes, and never throws on ambiguous names.
- Errors caused by agent input in `obj`, `exec`/`eval` and other mods' commands are returned to the agent and are no
  longer filed as Agent Bridge crash reports.
- Hardening: a malformed or negative `Content-Length` closes the connection instead of terminating the game; JSON
  nesting is capped at 128; at most 32 concurrent connections; any connection error is contained.
- Fair play: developer windows cannot be operated with `window`.
- Faster thing lookup: one ThingID index per frame instead of a scan of every thing per request.

## 1.1.1
- "Open tools folder" and "Report a bug": proper file:// URI (Windows paths, spaces), short issue link (long URLs were
  rejected), the path / full report always copied to the clipboard, and an on-screen message saying where it points.
- A `tick` whose client timed out no longer leaves a stop target that pauses the game later.
- A mod whose assembly fails to scan no longer stops Agent Bridge's startup.
- Invalid JSON returns 400; idle keep-alive connections close after 15 minutes; agent names are sanitised.
- Fair play also hides "Dev:" options inside already open menus; quitting never waits more than 2 s for statistics.
- The opened path/link is shown exactly (it was capitalised by the translation formatter).
- Shorter "Dev mode needed…" label in all languages (it wrapped over the next checkbox).

## 1.1.0 — fair play: let an AI play the whole game
- **Fair-play mode** (setting / `fair-play` command, only the player can switch it off): no cheats, reflection or
  Dev-only options, nothing under fog of war, speed ≤ 3.
- **New colony through the real setup pages**: `newgame`, or step by step `newgame-open`, `page`, `page-set`,
  `page-next` (scenario, storyteller, world, ideoligion, landing site, colonists).
- **Trading** through the game's trade window: `trade`, `trade-set`, `trade-accept`, `trade-cancel`.
- **Caravans and the world map**: `caravan-form`, `caravans`, `caravan-move`, `world-objects`, `world-tile`,
  `world-menu`, `world-menu-choose`, `world-target`, caravan gizmos (`id=caravan:<ID>`).
- **Colony management**: `policies`, `assign` (apparel/food/drug policy, medical care, allowed area), `area-edit`,
  `storage`, `prisoner`, `colony`, `research-set`.
- `menu`/`menu-choose` accept `target=<id>` (moving traders and animals); `tick` reports `blocked-by-window` right away.
- **Agent player** `Tools/rwb_agent.py`: Claude (official SDK) or any OpenAI-compatible/local model plays fairly;
  MCP prompt `play_rimworld`. Tests: `rwb_playtest.py` (24 checks), `tests/test_agent_loop.py`.

## 1.0.0 — first Steam Workshop release
- Workshop cover and screenshots; anonymous statistics go to the project's Cloudflare receiver
  (status shown in settings: delivered / postponed / offline) and new error signatures become GitHub issues.
- New `telemetry` command; settings window shows the version once (dialog already has the title).
- Agent indicator moved below the top bar and enlarged; `attack`/`move` report downed or dead pawns.
- `rwb eval` imports LudeonTK; clean release folder script (`Tools/make_release.py`).

## 0.4.0 — first public repository
- Standalone mod (was an internal tool of the Zero Sun project); MIT license.
- Own HTTP server on TcpListener (keep-alive, no URL ACL on Windows) and dependency-free JSON.
- Harmony optional: core works through a MonoBehaviour; Harmony adds cell targeting, exact ticks, event hooks.
- Command registry with access levels (Read/Act/Dev/Power) enforced by mod settings; extension API for other mods.
- Mod settings: server toggle, port, read-only, permissions, agents list, recent commands, MCP command, overlay.
- Anonymous statistics/error reports (opt-out, first-run prompt, no endpoint yet) and GitHub bug report button.
- Translations for all 29 game languages.
- Token-saving output: omitted null/false fields, rounded floats, `fields` projection, `snapshot`.

## 0.3.0 (internal)
- Live-tested on RimWorld 1.7 (Waylith/Linux): descriptor under HOME, RimSaves-aware save/load, exact tick stop,
  force-pause detection, language-independent gizmo keys, manual work priorities.
