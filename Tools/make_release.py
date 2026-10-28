#!/usr/bin/env python3
"""Assemble a clean mod folder for Steam Workshop / GitHub release (no .git, Source, bin, caches).

  python Tools/make_release.py [dest]   # default: ../AgentBridge-release
"""
import shutil, sys
from pathlib import Path
root = Path(__file__).resolve().parents[1]
dest = Path(sys.argv[1]) if len(sys.argv) > 1 else root.parent/'AgentBridge-release'
dll = root/'1.7/Assemblies/AgentBridge.dll'
if not dll.is_file(): sys.exit('build first: cd Source && dotnet build -c Release')
if dest.exists():
    keep = dest/'About/PublishedFileId.txt'
    saved = keep.read_text() if keep.is_file() else None
    shutil.rmtree(dest)
else: saved = None
for item in ['About', '1.7', 'Languages', 'LICENSE', 'README.md', 'README.ru.md', 'PRIVACY.md', 'PRIVACY.ru.md', 'CHANGELOG.md', 'CHANGELOG.ru.md', 'docs/PLAYING.md', 'docs/PLAYING.ru.md']:
    src = root/item
    if src.is_dir(): shutil.copytree(src, dest/item)
    elif src.is_file(): (dest/item).parent.mkdir(parents=True, exist_ok=True); shutil.copy2(src, dest/item)
(dest/'About/Preview.svg').unlink(missing_ok=True)  # source art stays in the repo
(dest/'Tools').mkdir()
for f in ['rwb.py', 'rwb_mcp.py', 'rwb_smoke.py', 'rwb_agent.py', 'rwb_agent_claude.py', 'rwb_play_prompt.md', 'rwb_playtest.py']: shutil.copy2(root/'Tools'/f, dest/'Tools'/f)
# Steam keeps the Workshop id here after the first upload; preserve it across rebuilds.
pid = root/'About/PublishedFileId.txt'
if saved and not pid.is_file(): (dest/'About/PublishedFileId.txt').write_text(saved)
size = sum(f.stat().st_size for f in dest.rglob('*') if f.is_file())
print(f'release folder: {dest} ({size // 1024} KB)')
if not (dest/'About/Preview.png').is_file(): print('WARNING: About/Preview.png missing (Workshop preview image)')
