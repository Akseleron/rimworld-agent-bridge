#!/usr/bin/env python3
"""Renders an approximate Steam Workshop page (docs/steam/mockup.png) from the real cover, screenshots and text."""
import base64, subprocess
from pathlib import Path
from xml.sax.saxutils import escape
root = Path(__file__).resolve().parents[2]
out = Path(__file__).resolve().parent
F = "'Fira Sans','DejaVu Sans',sans-serif"
cover, shots = root/'About/Preview.png', [root/f'docs/media/{n}.png' for n in ('1-orders', '2-map', '3-control')]
def img(p, x, y, w, h, extra=''):
    # rsvg only follows file links inside the SVG's folder, so embed the PNGs.
    return f'<image href="data:image/png;base64,{base64.b64encode(p.read_bytes()).decode()}" x="{x}" y="{y}" width="{w}" height="{h}" preserveAspectRatio="xMidYMid meet" {extra}/>'


def t(x, y, s, size=15, color='#c6d4df', weight=400): return f'<text x="{x}" y="{y}" font-family="{F}" font-size="{size}" font-weight="{weight}" fill="{color}">{escape(s)}</text>'
desc = ["Agent Bridge lets AI agents and scripts — Claude Code, Codex, Cursor, any MCP client,",
        "Python or plain HTTP — control RimWorld with simple JSON commands instead of",
        "screenshots and mouse coordinates.", "",
        "What it can do", "• See the colony: one-call snapshot, pawns, items, alerts, a compact text map",
        "• Give orders: draft, move, attack, equip, menus, gizmos with targeting, designations…",
        "• Control time precisely: run exactly N ticks, wait for a raid, a death or a log line",
        "• Developer tools: every Dev-mode debug action, spawning, incidents, C# in the game", "",
        "You stay in control", "• Only 127.0.0.1 + token · agent list and command log · read-only mode · on-screen indicator"]
body = [f'<rect width="1600" height="1180" fill="#1b2838"/>', '<rect width="1600" height="64" fill="#171a21"/>',
        t(40, 41, 'STORE      LIBRARY      COMMUNITY      AKSELERON', 16, '#dcdedf', 600),
        t(40, 110, 'RimWorld  ›  Workshop  ›  Akseleron\'s Workshop', 14, '#8f98a0'),
        t(40, 152, 'Agent Bridge', 32, '#ffffff', 400),
        '<rect x="40" y="176" width="1010" height="568" fill="#000"/>', img(cover, 40, 176, 1010, 568)]
for i, p in enumerate([cover] + shots):
    x = 40 + i * 172
    body += [f'<rect x="{x}" y="756" width="160" height="90" fill="#000" stroke="{"#66c0f4" if i == 0 else "#2a3f5a"}" stroke-width="2"/>', img(p, x, 756, 160, 90)]
body += ['<rect x="1080" y="176" width="480" height="300" fill="#16202d"/>', img(cover, 1100, 196, 440, 248),
         '<rect x="1080" y="490" width="480" height="64" rx="3" fill="#5c7e10"/>', '<rect x="1080" y="490" width="480" height="64" rx="3" fill="url(#g)"/>',
         t(1240, 531, '+  Subscribe', 22, '#d2efa9', 600),
         t(1080, 590, 'File Size         441 KB', 15, '#8f98a0'), t(1080, 616, 'Posted             28 Oct 2026', 15, '#8f98a0'),
         t(1080, 642, 'Tags                 Mod, 1.7', 15, '#8f98a0'),
         t(1080, 690, 'Created by', 13, '#8f98a0'), t(1080, 712, 'Akseleron', 17, '#66c0f4', 600)]
y = 890
for line in desc:
    bold = line in ('What it can do', 'You stay in control')
    body.append(t(40, y, line, 19 if bold else 16, '#ffffff' if bold else '#acb2b8', 700 if bold else 400)); y += 26 if line else 14
svg = ('<svg xmlns="http://www.w3.org/2000/svg" width="1600" height="1180"><defs><linearGradient id="g" x1="0" y1="0" x2="0" y2="1">'
       '<stop offset="0" stop-color="#75b022"/><stop offset="1" stop-color="#588a1b"/></linearGradient></defs>' + ''.join(body) + '</svg>')
(out/'mockup.svg').write_text(svg)
subprocess.run(['rsvg-convert', str(out/'mockup.svg'), '-o', str(out/'mockup.png')], check=True)
(out/'mockup.svg').unlink(); print(out/'mockup.png')
