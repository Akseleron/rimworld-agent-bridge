# Changelog

**English** · [Русский](CHANGELOG.ru.md)

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
