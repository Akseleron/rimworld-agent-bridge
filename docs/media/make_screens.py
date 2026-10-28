#!/usr/bin/env python3
"""Builds the Steam Workshop screenshots (1920x1080) from real in-game captures in src/ and real command output.
Requires rsvg-convert. Run: python docs/media/make_screens.py"""
import json, subprocess
from pathlib import Path
from xml.sax.saxutils import escape

here = Path(__file__).resolve().parent
src = here/'src'
BG, PANEL, TXT, DIM, ACC, OK = '#0f141d', '#0b0f15', '#e6edf5', '#7f8ea3', '#ffb070', '#7fe0c2'
FONT = "'Fira Sans','DejaVu Sans',sans-serif"
MONO = "'Fira Mono','DejaVu Sans Mono',monospace"


def term(x, y, w, h, lines, size=21, lh=1.45):
    """Terminal panel; lines are (kind, text): cmd / out / gap."""
    out = [f'<rect x="{x}" y="{y}" width="{w}" height="{h}" rx="18" fill="{PANEL}" stroke="#ffffff" stroke-opacity="0.08"/>',
           ''.join(f'<circle cx="{x + 26 + i * 22}" cy="{y + 24}" r="6" fill="{c}"/>' for i, c in enumerate(['#ff5f57', '#febc2e', '#28c840']))]
    ty = y + 70
    for kind, text in lines:
        if kind == 'gap': ty += size * 0.6; continue
        color, prefix = (TXT, f'<tspan fill="{ACC}">$ </tspan>') if kind == 'cmd' else (TXT, '  ') if kind == 'cont' else (OK if kind == 'ok' else DIM, '')
        out.append(f'<text x="{x + 28}" y="{ty}" font-family="{MONO}" font-size="{size}" fill="{color}" xml:space="preserve">{prefix}{escape(text)}</text>')
        ty += size * lh
    return '\n'.join(out)


def title(t, sub):
    return (f'<text x="80" y="104" font-family="{FONT}" font-size="54" font-weight="800" fill="{TXT}">{escape(t)}</text>'
            f'<text x="80" y="150" font-family="{FONT}" font-size="28" fill="{DIM}">{escape(sub)}</text>'
            f'<text x="1840" y="104" text-anchor="end" font-family="{FONT}" font-size="26" font-weight="700" fill="{ACC}" letter-spacing="4">AGENT BRIDGE</text>')


def image(path, x, y, w, h):
    return (f'<clipPath id="c{x}{y}"><rect x="{x}" y="{y}" width="{w}" height="{h}" rx="18"/></clipPath>'
            f'<image href="{path.as_uri()}" x="{x}" y="{y}" width="{w}" height="{h}" preserveAspectRatio="xMidYMid slice" clip-path="url(#c{x}{y})"/>'
            f'<rect x="{x}" y="{y}" width="{w}" height="{h}" rx="18" fill="none" stroke="#ffffff" stroke-opacity="0.12"/>')


def render(name, body):
    svg = f'<svg xmlns="http://www.w3.org/2000/svg" width="1920" height="1080"><rect width="1920" height="1080" fill="{BG}"/>{body}</svg>'
    (here/f'{name}.svg').write_text(svg)
    subprocess.run(['rsvg-convert', '-w', '1920', '-h', '1080', str(here/f'{name}.svg'), '-o', str(here/f'{name}.png')], check=True)
    (here/f'{name}.svg').unlink()
    print(here/f'{name}.png')


# 1. orders -> real combat (commands exactly as sent for src/combat.png)
render('1-orders', title('Your agent plays the colony', 'Plain JSON commands drive pawns — no screenshots, no mouse coordinates') + term(80, 200, 700, 800, [
    ('cmd', 'rwb pawns filter=hostile'), ('out', '[{"id":"Pony_Griffon26825","pos":[71,36]},'), ('out', ' ... 4 more]'), ('gap', ''),
    ('cmd', 'rwb equip id=Human1554 target=Gun_AssaultRifle…'), ('ok', '{"ok":true,"result":true}'), ('gap', ''),
    ('cmd', 'rwb draft ids=\'["Human1554","Human1569"]\''), ('ok', '{"ok":true,"result":[...drafted]}'), ('gap', ''),
    ('cmd', 'rwb move ids=\'["Human1554"]\' x=58 z=72'), ('ok', '{"ok":true,"result":[{"dest":[58,72]}]}'), ('gap', ''),
    ('cmd', 'rwb tick n=900'), ('ok', '{"ok":true,"result":{"ran":900}}'), ('gap', ''),
    ('cmd', 'rwb attack ids=\'["Human1554","Human1569"]\''), ('cont', '    target=Pony_Griffon26825'), ('ok', '{"ok":true,"result":[{"job":"AttackStatic"},…]}'),
]) + image(src/'combat-crop.png', 820, 200, 1020, 800))

# 2. the map as text (real 'area' output for the cropped view)
area = json.loads((src/'area.json').read_text())['result']
rows = [('cmd', 'rwb area x=60 z=80 radius=14')] + [('out', r) for r in area['rows']] + [('gap', ''), ('ok', 'C colonist  H hostile  W wall  T tree  i item')]
render('2-map', title('Agents read the map as text', 'One character per cell — cheap for LLM tokens, exact for pathing') + term(80, 200, 640, 820, rows, size=19, lh=1.3)
       + image(src/'area-crop.png', 760, 200, 1080, 820))

# 3. player stays in control
render('3-control', title('You stay in control', 'Fair play, read-only mode, permission levels, and every command agents send')
       + image(src/'settings.png', 80, 200, 1080, 810) + term(1200, 200, 640, 700, [
           ('out', 'Works with'), ('ok', '  Claude Code, Codex, Cursor,'), ('ok', '  any MCP client, curl, Python'), ('gap', ''),
           ('out', 'Talks only to 127.0.0.1 + token'), ('gap', ''),
           ('out', '~110 commands:'), ('ok', '  orders, gizmos, menus, dialogs'), ('ok', '  trade, caravans, world map'), ('ok', '  exact ticks, event waits'), ('ok', '  save / load, new colony'), ('gap', ''),
           ('out', 'Fair play: an AI plays with'), ('ok', '  only a player\'s abilities'), ('gap', ''),
           ('out', 'Any language · no required mods'),
       ], size=22))
