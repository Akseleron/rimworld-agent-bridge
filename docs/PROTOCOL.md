# Protocol (rwb-1)

- Transport: HTTP/1.1 on `127.0.0.1:<port>` (default 8765, next free up to +9). Keep-alive supported.
- Discovery: descriptor JSON `{protocol, version, port, token, pid, gameRoot, managed, runtime, os, assemblies}` at
  `~/.local/state/rimworld-agent-bridge/descriptor.json` or `%LOCALAPPDATA%\AgentBridge\descriptor.json`
  (override with `RWB_DESCRIPTOR`). It is rewritten on every start and deleted when the server stops.
- Auth: header `X-Token: <token>` (or `Authorization: Bearer <token>`). `GET /health` needs no token.
- Identity: optional header `X-Agent: <name>` shown in mod settings (e.g. `claude-code`, `codex`, `mcp:cursor`).
- Request: `POST /` with a JSON object `{"cmd": "<name>", ...args}`.
- Response: `{"ok":true,"result":…}` or `{"ok":false,"error":"…"}`. HTTP 401 bad token, 504 main thread stalled.

## Conventions

- Things: `ThingID` strings (`Human123`, `Steel4567`). Cells: `x`,`z` args; positions in results are `[x,z]`.
- Defs by `defName`. Gizmos by `key` (`key:Command_ColonistDraft`, `verb:Gun_Revolver`, `ability:<def>`), not labels.
- Results omit `null` and `false` fields; booleans appear only when true.
- `fields:["a","b"]` keeps only those keys of the top-level object or of each element of a top-level list.
- `timeout` (seconds, default 60, max 900) is the deadline of waiting commands (`tick`, `wait`, `load`, `save`, …).
- Commands run on the game's main thread, one frame per request (several cheap ones share a frame).
  During loading only `state help feed wait perf mods patches windows capabilities` run; others wait.

## Access levels

| Level | Examples | Gate |
|---|---|---|
| Read | snapshot, pawns, feed, wait, obj get | always |
| Act | draft, move, gizmo, designate, save, load | off in read-only mode |
| Dev | spawn, incident, damage, debug, quicktest | "allow dev" + game Dev mode (if required) |
| Power | exec/eval, obj set/call | "allow power" + game Dev mode (if required) |

## Events

`feed after=<seq>` returns log lines, messages, letters, incidents and deaths with increasing `seq`.
`wait until=letter pattern=Raid after=<seq>` blocks until a matching event (use the `last` seq you saw so nothing is
missed). Without Harmony only log lines and letters are captured (polled).

## Eval

`rwb.py eval '<C#>'` compiles a snippet against the running game's assemblies (from the descriptor) with `dotnet`
and sends the DLL path to `exec`. A bare expression is returned; statements need `return`. `args` is a
`Dictionary<string, object>`. Returning `AgentBridge.Pending` makes the command wait across frames.
