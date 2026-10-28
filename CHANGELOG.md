# Changelog

**English** · [Русский](CHANGELOG.ru.md)

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
