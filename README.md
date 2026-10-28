# Agent Bridge for RimWorld

Control RimWorld 1.7 from AI agents and scripts — Claude Code, Codex, any MCP client, or plain HTTP — with JSON
commands instead of screenshots and mouse coordinates.

```
$ python Tools/rwb.py snapshot fields='["colonists"]'
{"ok":true,"result":{"colonists":[{"id":"Human1551","name":"Austin","pos":[45,109],"health":1,"job":"Wait_Wander","mood":1}, ...]}}
$ python Tools/rwb.py draft ids='["Human1554"]'
$ python Tools/rwb.py gizmo id=Human1554 key=verb:Gun_Revolver target=Pony_Griffon26825
$ python Tools/rwb.py tick n=600
{"ok":true,"result":{"tick":2655,"ran":600}}
```

## Features

- **~90 commands**: observation (`snapshot`, `pawns`, `pawn`, `things`, `cell`, text map `area`, `alerts`, `letters`,
  `windows`), orders (`draft`, `move`, `attack`, `gizmo`+`target`, right-click `menu`/`menu-choose`, `designate`,
  `build`, `zone`, `bills`, `work`, `schedule`, `equip`), dialogs and letters, `save`/`load`, precise time control
  (`tick n=…` stops on the exact tick, `wait until=death|letter|log…`), every Dev-mode debug action (`debug path=…`),
  world editing (`spawn`, `incident`, `damage`, `heal`…), and modder tools (`obj` live reflection, `patches`, C# `eval`).
- **Token-efficient**: compact JSON, null/false fields omitted, rich text stripped, `fields:[…]` projection, `snapshot`
  in one call, `batch` for several commands per frame, keep-alive connections. A command costs one frame (~17 ms).
- **Language-independent**: commands use defNames, ThingIDs and stable gizmo keys (`key:Command_ColonistDraft`).
- **No required dependencies.** Harmony (if installed, as in nearly every modlist) adds cell targeting for debug
  tools/menus, exact tick stops and event hooks; without it the bridge degrades gracefully.
- **Extensible by other mods** without a hard dependency (see [docs/EXTENDING.md](docs/EXTENDING.md)).
- **Player in control**: mod settings show connected agents and recent commands, offer read-only mode and permission
  levels, and an on-screen indicator shows when an agent acts. Only 127.0.0.1 is served; a random token is required.
- **Cross-platform**: one .NET 10 assembly for RimWorld 1.7 on Linux, Windows and macOS (Windows/macOS not yet
  verified natively — reports welcome).
- **Translated** into all 29 languages that ship with RimWorld.

## Install

1. Subscribe on Steam Workshop (coming) or copy this folder into `RimWorld/Mods/AgentBridge`.
2. Enable **Agent Bridge** (anywhere after Harmony, if you use it).
3. Start the game. The log shows `[AgentBridge] … listening on 127.0.0.1:8765`.

Connection details are written to a descriptor readable only by you:
`~/.local/state/rimworld-agent-bridge/descriptor.json` (Linux/macOS) or `%LOCALAPPDATA%\AgentBridge\descriptor.json`
(Windows). The tools read it automatically.

## Use from agents

**MCP** (Claude Code, Codex, other clients) — the exact command is also in *Mod settings → Agent Bridge → Copy MCP setup*:

```
claude mcp add rimworld -- python3 "<RimWorld>/Mods/AgentBridge/Tools/rwb_mcp.py"
codex mcp add rimworld -- python3 "<RimWorld>/Mods/AgentBridge/Tools/rwb_mcp.py"
```

Tools: `rimworld` (any command), `rimworld_eval` (C# in the game), `rimworld_source` (decompile game/mod types),
`rimworld_defs` (search XML defs), `rimworld_log` (read Player.log, works after a crash).

**CLI**: `python Tools/rwb.py <cmd> key=value …` (values are JSON when possible). `python Tools/rwb.py help`.

**HTTP**: `POST http://127.0.0.1:8765/` with header `X-Token: <token>` and body `{"cmd":"pawns","filter":"hostile"}`.
Protocol details: [docs/PROTOCOL.md](docs/PROTOCOL.md).

Start with `snapshot`; `help` lists commands with their access level.

## Settings

Options → Mod settings → Agent Bridge: enable/disable the server, port, read-only mode, allow dev commands, allow power
commands (eval/reflection), require game Dev mode for both (default on), on-screen indicator, connected agents, recent
commands, copy MCP command, regenerate token (disconnects everyone), statistics/error reports, bug report.

## Privacy

Anonymous statistics and error reports are on by default and asked about once on first launch; they can be turned off
at any time. See [PRIVACY.md](PRIVACY.md) for exactly what is collected. This build has no collection server configured,
so nothing is sent yet; "Report a bug" opens a prefilled GitHub issue instead.

## Building

```
cd Source && dotnet build -c Release     # Source/bin/AgentBridge.dll, copied to 1.7/Assemblies/
dotnet build -c Release -p:SkipModCopy=true   # while RimWorld runs: never overwrite a loaded assembly
```

Reference assemblies come from NuGet (`Krafs.Rimworld.Ref`, `Lib.Harmony`), so no game install is needed to build.
`python Tools/rwb_smoke.py` runs a live test of ~70 commands against a throwaway colony (`rwb.py quicktest`).

## License

MIT. RimWorld is © Ludeon Studios; this is an unofficial mod.
