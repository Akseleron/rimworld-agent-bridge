#!/usr/bin/env python3
"""Plumbing test for rwb_agent.py without a real model: a scripted OpenAI-compatible server issues tool calls
(snapshot, colony, a cheat that fair play must refuse, tick) against the running game, then the loop ends.
Run with the game in a colony: python Tools/tests/test_agent_loop.py"""
import json, os, subprocess, sys, threading
from http.server import BaseHTTPRequestHandler, HTTPServer

SCRIPT = [('snapshot', {'fields': ['state']}), ('colony', {}), ('spawn', {'def': 'Steel', 'x': 1, 'z': 1}), ('tick', {'n': 120})]
seen = []


class Fake(BaseHTTPRequestHandler):
    def log_message(self, *a): pass
    def do_POST(self):
        body = json.loads(self.rfile.read(int(self.headers['Content-Length'])))
        seen.append(body['messages'][-1])
        i = sum(1 for m in body['messages'] if m['role'] == 'tool')
        if i < len(SCRIPT):
            cmd, args = SCRIPT[i]
            msg = {'role': 'assistant', 'content': f'step {i}', 'tool_calls': [{'id': f'c{i}', 'type': 'function', 'function': {'name': 'rimworld', 'arguments': json.dumps({'cmd': cmd, 'args': args})}}]}
        else:
            msg = {'role': 'assistant', 'content': 'done'}
        data = json.dumps({'choices': [{'message': msg}]}).encode()
        self.send_response(200); self.send_header('Content-Type', 'application/json'); self.send_header('Content-Length', str(len(data))); self.end_headers(); self.wfile.write(data)


srv = HTTPServer(('127.0.0.1', 0), Fake)
threading.Thread(target=srv.serve_forever, daemon=True).start()
here = os.path.dirname(os.path.abspath(__file__))
log = os.path.join(here, 'agent_loop.jsonl')
if os.path.exists(log): os.remove(log)
r = subprocess.run([sys.executable, os.path.join(here, '..', 'rwb_agent.py'), '--base-url', f'http://127.0.0.1:{srv.server_port}/v1',
                    '--model', 'scripted', '--steps', str(len(SCRIPT)), '--log', log], capture_output=True, text=True, timeout=600)
tools = [json.loads(l) for l in open(log) if '"kind": "tool"' in l]
ok = [len(tools) == len(SCRIPT),
      json.loads(tools[0]['result'])['ok'],
      'fair-play' in json.loads(tools[2]['result']).get('error', ''),
      json.loads(tools[3]['result'])['ok']]
print('tool calls:', [t['cmd'] for t in tools]); print('checks:', ok)
print('PASS' if all(ok) else 'FAIL\n' + r.stdout[-1500:] + r.stderr[-1500:])
sys.exit(0 if all(ok) else 1)
