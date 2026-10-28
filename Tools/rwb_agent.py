#!/usr/bin/env python3
"""Let an LLM play RimWorld through Agent Bridge, in fair-play mode (no cheats).

Local / OpenAI-compatible models (Ollama, LM Studio, llama.cpp server, vLLM…), standard library only:
  python rwb_agent.py --base-url http://localhost:11434/v1 --model qwen3:32b
Claude (official Anthropic SDK, `pip install anthropic`):
  python rwb_agent.py --provider claude
Claude Code / Codex / Cursor do not need this script: add the MCP server (see README) and ask them to play.

Options: --goal "...", --steps 200 (tool calls), --newgame (start a colony from the main menu first),
--log play.jsonl (full transcript). Fair play is switched on in the mod for the session and restored afterwards.
"""
import argparse, json, os, sys, time, urllib.request
from pathlib import Path

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import rwb

PROMPT = (Path(__file__).with_name('rwb_play_prompt.md')).read_text(encoding='utf-8')
TOOL_DESC = ('Send one command to the running RimWorld game. cmd is the command name, args its arguments. '
             'Start with snapshot; help lists commands (fair play hides cheats).')
TOOL_SCHEMA = {'type': 'object', 'properties': {'cmd': {'type': 'string'}, 'args': {'type': 'object'}}, 'required': ['cmd'],
               'additionalProperties': False}
MAX_RESULT = 6000  # characters of tool output given back to the model per call


def run_tool(cmd, args, agent):
    payload = dict(args or {}); payload['cmd'] = cmd
    try: result = rwb.call(payload, agent=agent)
    except OSError as e: result = {'ok': False, 'error': f'game not reachable: {e}'}
    text = json.dumps(result, ensure_ascii=False, separators=(',', ':'))
    return text if len(text) <= MAX_RESULT else text[:MAX_RESULT] + '…(truncated; use fields/limit/filter)'


def set_fair_play():
    """Fair play can be switched on by a client but only off by the player in the mod settings."""
    return rwb.call({'cmd': 'fair-play', 'on': True}).get('ok', False)


class Logger:
    def __init__(self, path): self.f = open(path, 'a', encoding='utf-8') if path else None
    def __call__(self, kind, **data):
        line = {'t': round(time.time(), 3), 'kind': kind, **data}
        if self.f: self.f.write(json.dumps(line, ensure_ascii=False) + '\n'); self.f.flush()
        if kind in ('say', 'tool'): print(f"[{kind}] " + (data.get('text') or f"{data.get('cmd')} {json.dumps(data.get('args') or {}, ensure_ascii=False)[:160]} -> {data.get('result', '')[:200]}"), flush=True)


def openai_loop(a, log):
    """Chat Completions with function calling; works with any OpenAI-compatible server."""
    url = a.base_url.rstrip('/') + '/chat/completions'
    tools = [{'type': 'function', 'function': {'name': 'rimworld', 'description': TOOL_DESC, 'parameters': TOOL_SCHEMA}}]
    messages = [{'role': 'system', 'content': PROMPT}, {'role': 'user', 'content': a.goal}]
    steps = 0
    while steps < a.steps:
        body = json.dumps({'model': a.model, 'messages': messages, 'tools': tools, 'tool_choice': 'auto'}).encode()
        req = urllib.request.Request(url, body, {'Content-Type': 'application/json', 'Authorization': 'Bearer ' + (a.api_key or 'none')})
        with urllib.request.urlopen(req, timeout=600) as r: msg = json.loads(r.read())['choices'][0]['message']
        messages.append({k: v for k, v in msg.items() if k in ('role', 'content', 'tool_calls')})
        if msg.get('content'): log('say', text=msg['content'])
        calls = msg.get('tool_calls') or []
        if not calls:
            messages.append({'role': 'user', 'content': 'Continue playing: take a snapshot, decide, act, advance time.'})
            continue
        for c in calls:
            try: inp = json.loads(c['function'].get('arguments') or '{}')
            except ValueError: inp = {}
            out = run_tool(inp.get('cmd', 'snapshot'), inp.get('args'), 'agent:' + a.model)
            log('tool', cmd=inp.get('cmd'), args=inp.get('args'), result=out)
            messages.append({'role': 'tool', 'tool_call_id': c['id'], 'content': out})
            steps += 1
        # Local models have small contexts: keep the system prompt, the goal and the most recent exchanges.
        if len(messages) > 80:
            tail = messages[-60:]
            while tail and tail[0]['role'] == 'tool': tail = tail[1:]
            messages = messages[:2] + tail


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument('--provider', choices=['openai', 'claude'], default='openai')
    p.add_argument('--base-url', default=os.environ.get('RWB_LLM_URL', 'http://localhost:11434/v1'))
    p.add_argument('--model', default=None)
    p.add_argument('--api-key', default=os.environ.get('OPENAI_API_KEY'))
    p.add_argument('--effort', default='high', help='Claude effort: low|medium|high|xhigh|max')
    p.add_argument('--goal', default='Play this colony: survive, grow, and work towards escaping the planet.')
    p.add_argument('--steps', type=int, default=200)
    p.add_argument('--newgame', action='store_true', help='start a new colony first (game must be in the main menu)')
    p.add_argument('--log', default='rwb_play.jsonl')
    a = p.parse_args()
    log = Logger(a.log)
    previous = rwb.call({'cmd': 'capabilities'}).get('result', {}).get('settings', {}).get('fairPlay', False)
    if a.newgame:
        log('setup', result=rwb.call({'cmd': 'newgame', 'timeout': 900}))
    if not set_fair_play(): print('warning: could not switch fair play on (enable it in the mod settings)')
    try:
        if a.provider == 'claude':
            import rwb_agent_claude
            rwb_agent_claude.loop(a, log, PROMPT, TOOL_DESC, TOOL_SCHEMA, run_tool)
        else:
            if not a.model: sys.exit('--model is required for OpenAI-compatible servers (e.g. qwen3:32b)')
            openai_loop(a, log)
    except KeyboardInterrupt: print('\nstopped')
    finally:
        rwb.call({'cmd': 'pause'})
        if not previous:
            # Fair play blocks reflection, so it cannot switch itself off; tell the player instead.
            print('Fair play stays on: untick it in Options → Mod settings → Agent Bridge if you want dev tools back.')


if __name__ == '__main__':
    main()
