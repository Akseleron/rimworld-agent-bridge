#!/usr/bin/env python3
"""MCP stdio server for RimWorld via Agent Bridge (no dependencies). Register:
  claude mcp add rimworld -- python3 /path/to/tools/rwb_mcp.py
  codex mcp add rimworld -- python3 /path/to/tools/rwb_mcp.py
"""
import json, os, sys, traceback
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import rwb

LIVE_HELP = ("Live control of the running RimWorld (Agent Bridge mod). Pass cmd plus its args. Call cmd='help' for the full list. "
             "Observe: snapshot capabilities state map pawns pawn things cell area alerts letters feed windows gizmos menu inspect resources factions quests world "
             "debug-list saves defs def mods perf patches research. Act: pause speed tick select camera draft move attack job-end gizmo target "
             "menu-choose dialog window letter designate build zone bills work schedule research-set spawn destroy teleport kill damage heal hediff "
             "need skill forbid equip drop quit incident weather condition godmode goodwill research-finish build-instant quest-accept debug save load quicktest main-menu screenshot. "
             "Play (fair, like a human): trade trade-set trade-accept caravans caravan-form caravan-move world-objects world-tile world-menu world-menu-choose world-target policies assign area-edit storage prisoner colony newgame newgame-open page page-set page-next fair-play. Flow: wait batch. Power: obj (reflect live objects), exec. Other mods may add commands (see capabilities.extensions). "
             "Things are addressed by ThingID (e.g. Human123), cells by x,z. Start with cmd='snapshot'. Any command accepts "
             "fields:[...] to shrink output and timeout (seconds) for waiting commands. Gizmos: prefer the language-independent 'key'.")
TOOLS = [
    {'name': 'rimworld', 'description': LIVE_HELP,
     'inputSchema': {'type': 'object', 'properties': {'cmd': {'type': 'string'}, 'args': {'type': 'object', 'description': 'command arguments, e.g. {"ids":["Human12"],"x":40,"z":55}'}}, 'required': ['cmd']}},
    {'name': 'rimworld_eval', 'description': 'Compile and run C# inside the running game on the main thread. Body of `static object Run(JsonElement args)`; '
     'usings Verse, RimWorld, Verse.AI, RimWorld.Planet, UnityEngine, HarmonyLib, System.Linq included. A bare expression is returned. Requires DevMode.',
     'inputSchema': {'type': 'object', 'properties': {'code': {'type': 'string'}, 'depth': {'type': 'integer', 'default': 2}, 'args': {'type': 'object'}}, 'required': ['code']}},
    {'name': 'rimworld_source', 'description': 'Decompile a game or mod type (full name, e.g. Verse.Pawn, RimWorld.JobDriver_Wait). Optional grep regex returns matching lines with context.',
     'inputSchema': {'type': 'object', 'properties': {'type': {'type': 'string'}, 'grep': {'type': 'string'}, 'assembly': {'type': 'string'}}, 'required': ['type']}},
    {'name': 'rimworld_defs', 'description': 'Search XML Defs (game Data, Mods, Workshop) by regex on defName/Name/label; returns the XML.',
     'inputSchema': {'type': 'object', 'properties': {'query': {'type': 'string'}, 'type': {'type': 'string', 'description': 'e.g. ThingDef'}, 'limit': {'type': 'integer', 'default': 20}, 'full': {'type': 'boolean', 'default': True}}, 'required': ['query']}},
    {'name': 'rimworld_log', 'description': 'Read Player.log (works without the bridge, e.g. after a crash).',
     'inputSchema': {'type': 'object', 'properties': {'lines': {'type': 'integer', 'default': 200}, 'grep': {'type': 'string'}, 'errors': {'type': 'boolean'}, 'previous': {'type': 'boolean'}}}},
]


CLIENT = 'mcp'


def run_tool(name, a):
    if name == 'rimworld':
        payload = dict(a.get('args') or {}); payload['cmd'] = a['cmd']
        return rwb.call(payload, agent=CLIENT)
    if name == 'rimworld_eval': return rwb.eval_code(a['code'], a.get('depth', 2), a.get('args'))
    if name == 'rimworld_source': return rwb.source(a['type'], a.get('assembly'), a.get('grep'))
    if name == 'rimworld_defs': return rwb.defs(a['query'], a.get('type'), a.get('limit', 20), a.get('full', True))
    if name == 'rimworld_log': return rwb.log(a.get('lines', 200), a.get('grep'), a.get('errors', False), a.get('previous', False))
    raise KeyError(name)


def reply(id_, result=None, error=None):
    msg = {'jsonrpc': '2.0', 'id': id_}
    if error: msg['error'] = error
    else: msg['result'] = result
    sys.stdout.write(json.dumps(msg, ensure_ascii=False) + '\n'); sys.stdout.flush()


class InvalidParams(ValueError): pass


def tool_params(p):
    tool = next((t for t in TOOLS if t['name'] == p.get('name')), None)
    if tool is None: raise InvalidParams('unknown or missing tool name')
    a = p.get('arguments', {})
    if not isinstance(a, dict): raise InvalidParams('arguments must be an object')
    schema = tool['inputSchema']
    for key in schema.get('required', []):
        if key not in a: raise InvalidParams(f'missing argument: {key}')
    types = {'string': str, 'object': dict, 'integer': int, 'boolean': bool}
    for key, value in a.items():
        kind = schema['properties'].get(key, {}).get('type')
        if kind and (not isinstance(value, types[kind]) or (kind == 'integer' and isinstance(value, bool))):
            raise InvalidParams(f'{key} must be {kind}')
    return tool['name'], a


for line in sys.stdin:
    if not line.strip(): continue
    id_ = None
    try:
        req = json.loads(line)
        if (not isinstance(req, dict) or req.get('jsonrpc') != '2.0'
                or not isinstance(req.get('method'), str)
                or ('id' in req and (isinstance(req['id'], (dict, list, bool))))):
            reply(None, error={'code': -32600, 'message': 'invalid request'}); continue
        method = req['method']; id_ = req.get('id')
        if 'id' not in req: continue  # notifications
        p = req.get('params', {} if method not in ('initialize', 'prompts/get', 'tools/call') else None)
        if not isinstance(p, dict): raise InvalidParams('params must be an object')
        if method == 'initialize':
            info = p.get('clientInfo', {})
            if not isinstance(info, dict): raise InvalidParams('clientInfo must be an object')
            CLIENT = ('mcp:' + str(info.get('name', 'client')))[:48]
            reply(id_, {'protocolVersion': p.get('protocolVersion', '2025-06-18'), 'capabilities': {'tools': {}, 'prompts': {}},
                        'serverInfo': {'name': 'rimworld-agent-bridge', 'version': '1.1.3'}})
        elif method == 'tools/list': reply(id_, {'tools': TOOLS})
        elif method == 'prompts/list':
            reply(id_, {'prompts': [{'name': 'play_rimworld', 'description': 'Play the running colony fairly (no cheats) as the player',
                                     'arguments': [{'name': 'goal', 'description': 'what to aim for', 'required': False}]}]})
        elif method == 'prompts/get':
            if p.get('name') != 'play_rimworld': raise InvalidParams('unknown or missing prompt name')
            a = p.get('arguments', {})
            if not isinstance(a, dict) or not isinstance(a.get('goal', ''), str):
                raise InvalidParams('prompt arguments must contain a string goal')
            goal = a.get('goal') or 'Survive, grow, and work towards escaping the planet.'
            text = open(os.path.join(os.path.dirname(os.path.abspath(__file__)), 'rwb_play_prompt.md'), encoding='utf-8').read()
            reply(id_, {'messages': [{'role': 'user', 'content': {'type': 'text', 'text': text + '\n\nFirst call the rimworld tool with cmd=fair-play. Goal: ' + goal}}]})
        elif method == 'tools/call':
            name, a = tool_params(p)
            try:
                out = run_tool(name, a); err = not out.get('ok', True)
            except OSError as e: out, err = {'ok': False, 'error': f'bridge not reachable (game running with Agent Bridge?): {e}'}, True
            except Exception: out, err = {'ok': False, 'error': traceback.format_exc(limit=3)}, True
            reply(id_, {'content': [{'type': 'text', 'text': json.dumps(out, ensure_ascii=False, separators=(',', ':'))}], 'isError': err})
        elif method == 'ping': reply(id_, {})
        else: reply(id_, error={'code': -32601, 'message': 'method not found'})
    except json.JSONDecodeError:
        reply(None, error={'code': -32700, 'message': 'parse error'})
    except InvalidParams as e:
        reply(id_, error={'code': -32602, 'message': str(e)})
    except Exception:
        reply(id_, error={'code': -32603, 'message': 'internal error'})
