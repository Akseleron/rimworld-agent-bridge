# Let an AI play RimWorld — fairly

**English** · [Русский](PLAYING.ru.md)

Agent Bridge is a tool for agents and modders, and also a toy: switch on **fair play** and any AI can play your
colony with exactly the abilities a human player has — no cheats, no peeking. Watch it, take over at any time.

## Fair play

Options → Mod settings → Agent Bridge → **Fair play (only what a player can do)**, or let the agent send
`fair-play` (an agent can switch it on, only you can switch it off). While it is on:

- refused: spawning, damage/heal, incidents, weather, god mode, debug actions, C# eval, reflection
  (`obj`, `patches`, `debug-list`), Dev-only menu options and gizmos ("Dev: …"), ultrafast speed;
- hidden: everything under fog of war, incident internals (only letters/messages are visible);
- allowed: everything a player does — orders, right-click menus, gizmos and abilities, building, zones and areas,
  storage, work, schedules, policies, research, prisoners, trading, caravans and the world map, dialogs and letters,
  save/load, and starting a new colony through the normal setup pages.

The time only moves when the agent runs `tick`, so it can think as long as it likes without the colony suffering.

## Choose your player

| Player | How |
|---|---|
| Claude Code / Codex / Cursor / any MCP client | add the MCP server (Mod settings → Copy MCP setup command), then use the `play_rimworld` prompt or say “play my RimWorld colony fairly” |
| Claude via API | `pip install anthropic`, set `ANTHROPIC_API_KEY`, `python Tools/rwb_agent.py --provider claude` |
| Local model (Ollama, LM Studio, llama.cpp, vLLM) | `python Tools/rwb_agent.py --base-url http://localhost:11434/v1 --model qwen3:32b` |
| Any OpenAI-compatible service | `--base-url <url>/v1 --model <name> --api-key <key>` |

`rwb_agent.py` options: `--newgame` (start a colony from the main menu first), `--goal "…"`, `--steps 200`,
`--effort high` (Claude), `--log play.jsonl` (full transcript of every decision and command). It switches fair play
on, plays, and pauses the game when it stops (Ctrl+C at any time).

Tips: models with good tool use and a large context play best; small local models work but forget plans — give them
a narrow goal ("survive the first 10 days", "build a kitchen and a freezer"). The system prompt the agents use is
`Tools/rwb_play_prompt.md`; edit it to change the play style.

## Test that a player-agent can play

- `python Tools/rwb_playtest.py --days 1` — from the main menu: new colony through the setup pages, fair play on,
  cheats refused, then work, schedules, policies, areas, zones, storage, research, building, designations, a day of
  play and a caravan (24 checks).
- `python Tools/tests/test_agent_loop.py` — the agent loop with a scripted model against the running game.
- `python Tools/rwb_smoke.py` — all ~90 tool commands (dev mode, fair play off) on a throwaway colony.

## Commands for playing

| Area | Commands |
|---|---|
| Overview | `snapshot`, `colony`, `alerts`, `letters`, `windows`, `area`, `pawns`, `pawn`, `things`, `cell` |
| Time | `tick n=…` (stops on dialogs: `stop=blocked-by-window`), `pause`, `speed`, `wait until=letter…` |
| Orders | `draft`, `move`, `attack`, `equip`, `drop`, `forbid`, `job-end`, `gizmos`/`gizmo`+`target`, `menu`/`menu-choose` (`target=<id>` for moving things) |
| Colony | `build`, `designate`, `zone`, `area-edit`, `storage`, `bills`, `work`, `schedule`, `assign`, `policies`, `research`/`research-set`, `prisoner` |
| Dialogs | `dialog`, `window`, `letter` |
| Trade | open with `menu-choose` (“Trade with …”) or the comms console, then `trade`, `trade-set`, `trade-accept`, `trade-cancel` |
| World | `caravan-form` (needs `tile`), `caravans`, `caravan-move`, `world-objects`, `world-tile`, `world-menu`/`world-menu-choose`, `world-target`, caravan gizmos via `gizmos id=caravan:<ID>` |
| New game | `newgame` (all pages) or `newgame-open`, `page`, `page-set`, `page-next` |
| Saves | `save` (never overwrites), `load`, `saves` |
