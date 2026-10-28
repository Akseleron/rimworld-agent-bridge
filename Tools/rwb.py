#!/usr/bin/env python3
"""Agent Bridge CLI and library: drive a running RimWorld through the in-game JSON bridge.

  rwb.py state
  rwb.py pawns filter=hostile
  rwb.py move ids='["Human12"]' x=40 z=55
  rwb.py tick n=600 timeout=120
  rwb.py snapshot                            # one call: state, map, colonists, hostiles, alerts, new events
  rwb.py pawns fields='["id","pos","job"]'   # any command: keep only these keys
  rwb.py --json '{"cmd":"debug","path":"Spawning\\\\Spawn thing"}'
  rwb.py eval 'return Find.CurrentMap.mapPawns.AllPawnsCount;'
  rwb.py source Verse.Pawn grep=Kill        # offline: decompiled game code
  rwb.py defs Gun_Revolver                  # offline: XML defs in game + mods
  rwb.py log grep=AgentBridge errors=true   # offline: Player.log
Values are parsed as JSON when possible (numbers, lists, true/false), else kept as strings.
Output is JSON on stdout; exit code 1 on {"ok":false}, 2 if the bridge is unreachable.
"""
import hashlib, http.client, json, os, re, shutil, subprocess, sys, urllib.error
import xml.etree.ElementTree as ET
from pathlib import Path

HOME = Path.home()
WINDOWS = os.name == 'nt'
if WINDOWS:
    DEFAULT_GAME = Path(os.environ.get('ProgramFiles(x86)', r'C:\Program Files (x86)'))/'Steam/steamapps/common/RimWorld'
    USERDATA = HOME/'AppData/LocalLow/Ludeon Studios/RimWorld by Ludeon Studios'
    CACHE = Path(os.environ.get('LOCALAPPDATA', HOME))/'AgentBridge/cache'
elif sys.platform == 'darwin':
    DEFAULT_GAME = HOME/'Library/Application Support/Steam/steamapps/common/RimWorld/RimWorldMac.app'
    USERDATA = HOME/'Library/Application Support/RimWorld'
    CACHE = HOME/'Library/Caches/AgentBridge'
else:
    DEFAULT_GAME = HOME/'.local/share/Steam/steamapps/common/RimWorld'
    USERDATA = HOME/'.config/unity3d/Ludeon Studios/RimWorld by Ludeon Studios'
    CACHE = Path(os.environ.get('XDG_CACHE_HOME', HOME/'.cache'))/'rwb'


def agent_name():
    """Identifies this client in the in-game agent list (X-Agent header)."""
    if os.environ.get('RWB_AGENT'): return os.environ['RWB_AGENT']
    if os.environ.get('CLAUDECODE') or os.environ.get('CLAUDE_CODE_ENTRYPOINT'): return 'claude-code'
    if any(k.startswith('CODEX') for k in os.environ): return 'codex'
    return 'rwb-cli'


def descriptor_path():
    # Matches Bridge.DescriptorPath (under the user profile: Steam's Linux container hides /run/user and /tmp).
    if os.environ.get('RWB_DESCRIPTOR'): return Path(os.environ['RWB_DESCRIPTOR'])
    if WINDOWS: return Path(os.environ.get('LOCALAPPDATA', HOME))/'AgentBridge/descriptor.json'
    return HOME/'.local/state/rimworld-agent-bridge/descriptor.json'


def descriptor():
    return json.loads(descriptor_path().read_text())


def game_root():
    if os.environ.get('RWB_GAME_ROOT'): return Path(os.environ['RWB_GAME_ROOT'])
    try: return Path(descriptor()['gameRoot'])
    except (OSError, KeyError, ValueError): return DEFAULT_GAME


_conn = None
_conn_key = None


def call(payload, timeout=None, agent=None):
    """Send one command; returns the decoded {"ok":..} envelope. Raises OSError if unreachable.
    Reuses one keep-alive connection per process (the MCP server sends many commands)."""
    global _conn, _conn_key
    d = descriptor()
    if timeout is None: timeout = float(payload.get('timeout', 60)) + 20
    body = json.dumps(payload, separators=(',', ':')).encode()
    headers = {'X-Token': d['token'], 'X-Agent': agent or agent_name(), 'Content-Type': 'application/json'}
    for attempt in (0, 1):
        if _conn is None or _conn_key != (d['port'], d['token']):
            _conn = http.client.HTTPConnection('127.0.0.1', d['port'], timeout=timeout); _conn_key = (d['port'], d['token'])
        _conn.timeout = timeout
        if _conn.sock: _conn.sock.settimeout(timeout)
        try:
            _conn.request('POST', '/', body, headers)
            return json.loads(_conn.getresponse().read() or b'{"ok":false}')
        except (http.client.HTTPException, ConnectionError, BrokenPipeError):
            _conn.close(); _conn = None  # stale keep-alive after a game restart: retry once on a fresh socket
            if attempt: raise


# ---------- eval: compile a C# snippet against the running game's assemblies, then exec it ----------
USINGS = ['System', 'System.Linq', 'System.Collections.Generic', 'Verse', 'Verse.AI', 'RimWorld',
          'RimWorld.Planet', 'UnityEngine', 'HarmonyLib', 'LudeonTK', 'AgentBridge']


def compile_snippet(code):
    d = descriptor()
    managed = Path(d.get('managed') or d['gameRoot'])
    if 'class Script' not in code:
        body = code if re.search(r'\breturn\b', code) else code.rstrip().rstrip(';') + ';\nreturn null;' if '\n' in code or ';' in code else 'return ' + code + ';'
        code = '\n'.join(f'using {u};' for u in USINGS) + '\npublic static class Script {\n public static object Run(Dictionary<string, object> args) {\n' + body + '\n }\n}\n'
    refs = [managed/'Assembly-CSharp.dll'] + sorted(managed.glob('Unity*.dll')) + sorted(managed.glob('Waylith*.dll')) + [Path(p) for p in d.get('assemblies', [])]
    refs = list(dict.fromkeys(str(p) for p in refs if Path(p).is_file()))
    digest = hashlib.sha256((code + '\n'.join(refs)).encode()).hexdigest()[:16]
    work = CACHE/'eval'/digest
    dll = work/'out'/f'rwb_eval_{digest}.dll'
    if dll.is_file(): return dll, None
    work.mkdir(parents=True, exist_ok=True)
    (work/'Script.cs').write_text(code)
    items = ''.join(f'<Reference Include="{Path(r).stem}" HintPath="{r}" Private="false" />' for r in refs)
    (work/'eval.csproj').write_text(
        '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><LangVersion>latest</LangVersion>'
        f'<AssemblyName>rwb_eval_{digest}</AssemblyName><Nullable>disable</Nullable><ImplicitUsings>disable</ImplicitUsings>'
        '<AllowUnsafeBlocks>true</AllowUnsafeBlocks><NoWarn>CS0168;CS0219;CS8019;CS1998</NoWarn><GenerateDocumentationFile>false</GenerateDocumentationFile>'
        f'</PropertyGroup><ItemGroup>{items}</ItemGroup></Project>')
    (work/'Directory.Build.props').write_text('<Project></Project>')
    r = subprocess.run(['dotnet', 'build', str(work/'eval.csproj'), '-c', 'Release', '-o', str(work/'out'), '--nologo', '-v', 'q'],
                       capture_output=True, text=True)
    if r.returncode or not dll.is_file():
        errors = sorted({re.sub(r'\s*\[.*\]$', '', l.split('Script.cs', 1)[-1]) for l in r.stdout.splitlines() if ' error ' in l})
        shutil.rmtree(work, ignore_errors=True)
        return None, errors or [r.stdout[-2000:]]
    return dll, None


def eval_code(code, depth=2, args=None, timeout=60):
    dll, errors = compile_snippet(code)
    if errors: return {'ok': False, 'error': 'compile failed', 'errors': errors}
    return call({'cmd': 'exec', 'dll': str(dll), 'depth': depth, 'args': args or {}, 'timeout': timeout})


# ---------- offline helpers ----------
def ilspy():
    return shutil.which('ilspycmd') or str(HOME/'.dotnet/tools/ilspycmd')


def source(type_name, assembly=None, grep=None, context=6, limit=400):
    """Decompile one type. Searches Assembly-CSharp, then mod assemblies, unless assembly is given."""
    candidates = [Path(assembly)] if assembly else [game_root()/'Assembly-CSharp.dll']
    if not assembly:
        try: candidates += [Path(p) for p in descriptor().get('assemblies', [])]
        except (OSError, ValueError): pass
    for dll in candidates:
        if not dll.is_file(): continue
        r = subprocess.run([ilspy(), '-t', type_name, str(dll)], capture_output=True, text=True)
        text = '\n'.join(l for l in r.stdout.splitlines() if 'not using the latest version' not in l and not l.startswith('Latest version'))
        if r.returncode == 0 and ('class ' in text or 'struct ' in text or 'interface ' in text or 'enum ' in text):
            lines = text.splitlines()
            if grep:
                rx = re.compile(grep, re.I); keep = set()
                for i, l in enumerate(lines):
                    if rx.search(l): keep.update(range(max(0, i-context), min(len(lines), i+context+1)))
                lines = [f'{i+1}: {lines[i]}' for i in sorted(keep)]
            return {'ok': True, 'assembly': str(dll), 'lines': len(text.splitlines()), 'code': '\n'.join(lines[:limit]),
                    'truncated': len(lines) > limit}
    return {'ok': False, 'error': f'type {type_name} not found (full name with namespace needed)'}


def def_roots():
    g = game_root()
    roots = [g/'Data', g/'Mods']
    ws = g.parent.parent/'workshop/content/294100'
    if ws.is_dir(): roots.append(ws)
    return roots


def defs(query, def_type=None, limit=20, full=True):
    """Find XML defs by defName/label/ParentName regex across Data, Mods and Workshop."""
    rx = re.compile(query, re.I); pre = re.compile(query.lstrip('^').rstrip('$'), re.I)
    out = []
    for root in def_roots():
        for f in root.rglob('*.xml'):
            if 'Defs' not in f.parts and 'Patches' not in f.parts: continue
            try: text = f.read_text(errors='replace')
            except OSError: continue
            if not pre.search(text): continue
            try: tree = ET.fromstring(text)
            except ET.ParseError: continue
            for el in tree:
                if def_type and el.tag != def_type: continue
                keys = [el.findtext('defName') or '', el.get('Name') or '', el.findtext('label') or '']
                if any(k and rx.search(k) for k in keys):
                    item = {'type': el.tag, 'defName': keys[0] or None, 'Name': keys[1] or None, 'parent': el.get('ParentName'),
                            'file': str(f)}
                    if full: item['xml'] = ET.tostring(el, encoding='unicode')[:6000]
                    out.append(item)
                    if len(out) >= limit: return {'ok': True, 'results': out, 'truncated': True}
    return {'ok': True, 'results': out}


def log(lines=200, grep=None, errors=False, previous=False):
    f = USERDATA/'Logs'/('Player-prev.log' if previous else 'Player.log')
    if not f.is_file(): return {'ok': False, 'error': f'no {f}'}
    with open(f, 'rb') as h:
        h.seek(0, 2); size = h.tell(); h.seek(max(0, size - 8_000_000)); data = h.read().decode(errors='replace').splitlines()
    if errors: data = [l for l in data if re.search(r'exception|error|\bat \w', l, re.I)]
    if grep: data = [l for l in data if re.search(grep, l, re.I)]
    return {'ok': True, 'file': str(f), 'lines': data[-lines:]}


# ---------- CLI ----------
def parse(argv):
    if argv and argv[0] == '--json': return json.loads(argv[1])
    payload = {'cmd': argv[0] if argv else 'state'}
    positional = []
    for item in argv[1:]:
        k, eq, v = item.partition('=')
        if not eq or not re.fullmatch(r'[A-Za-z_][\w-]*', k): positional.append(item); continue
        try: payload[k] = json.loads(v)
        except ValueError: payload[k] = v
    if positional: payload['_'] = positional
    return payload


def main(argv):
    p = parse(argv); cmd = p.pop('cmd'); pos = p.pop('_', [])
    if cmd == 'eval': return eval_code(' '.join(pos) or p.get('code', ''), p.get('depth', 2), p.get('args'), p.get('timeout', 60))
    if cmd == 'source': return source(pos[0] if pos else p['type'], p.get('assembly'), p.get('grep'))
    if cmd == 'defs': return defs(pos[0] if pos else p['query'], p.get('type'), p.get('limit', 20), p.get('full', True))
    if cmd == 'log': return log(p.get('lines', 200), p.get('grep'), p.get('errors', False), p.get('previous', False))
    if pos: return {'ok': False, 'error': f'unexpected positional args {pos}; use key=value'}
    p['cmd'] = cmd
    return call(p)


if __name__ == '__main__':
    try: result = main(sys.argv[1:])
    except (FileNotFoundError, ConnectionError, urllib.error.URLError) as e:
        print(json.dumps({'ok': False, 'error': f'bridge not reachable (game running with Agent Bridge?): {e}'})); sys.exit(2)
    # Compact by default (fewer tokens for agents); RWB_PRETTY=1 for humans.
    print(json.dumps(result, ensure_ascii=False, indent=1 if os.environ.get('RWB_PRETTY') else None, separators=None if os.environ.get('RWB_PRETTY') else (',', ':')))
    sys.exit(0 if result.get('ok') else 1)
