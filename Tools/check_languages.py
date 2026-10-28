#!/usr/bin/env python3
"""Checks every Languages/*/Keyed/AgentBridge.xml has exactly the English keys and the same {n} placeholders."""
import re, sys, xml.etree.ElementTree as ET
from pathlib import Path
root = Path(__file__).resolve().parents[1]/'Languages'
def load(p): return {e.tag: e.text or '' for e in ET.parse(p).getroot()}
base = load(root/'English/Keyed/AgentBridge.xml')
bad = 0
for f in sorted(root.glob('*/Keyed/AgentBridge.xml')):
    d = load(f)
    if set(d) != set(base): print(f, 'keys differ:', set(base) ^ set(d)); bad += 1
    for k, v in base.items():
        if sorted(re.findall(r'\{\d\}', v)) != sorted(re.findall(r'\{\d\}', d.get(k, ''))): print(f, k, 'placeholders differ'); bad += 1
print(len(list(root.glob('*/Keyed/AgentBridge.xml'))), 'languages,', bad, 'problems')
sys.exit(1 if bad else 0)
