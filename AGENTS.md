# Agent Bridge — notes for agents (Claude, Codex)

Standalone RimWorld 1.7 mod (not part of Zero Sun). Public repo github.com/Akseleron/rimworld-agent-bridge,
Steam Workshop id 3828798135 (About/PublishedFileId.txt). English mod name "Agent Bridge" everywhere.

- Build: `cd Source && dotnet build -c Release` (copies to 1.7/Assemblies). While RimWorld runs use
  `-p:SkipModCopy=true` — never overwrite a loaded DLL. Game Mods/AgentBridge is a symlink to this repo.
- Launch the game only via RimStack «Играть»; quit with `python Tools/rwb.py quit confirm=true`.
  Do not start the game or click while the user uses the desktop (DP-3).
- Tests: `Tools/rwb_smoke.py` (quicktest colony, Dev mode on, fair play off), `Tools/rwb_playtest.py`
  (fair play, from main menu), `Tools/tests/test_agent_loop.py`, `Tools/check_languages.py` (29 languages).
- User's game settings: Dev mode OFF, fair play ON. If a test needs otherwise, edit Config/Prefs.xml and
  Config/Mod_AgentBridge_AgentBridgeMod.xml only while the game is closed, and restore them afterwards.
- Release: bump About.xml modVersion, Source csproj Version, Bridge.Version, rwb_mcp version, CHANGELOG(.ru);
  `python Tools/make_release.py` → ../AgentBridge-release; Steam update via SteamUGC from an in-game eval
  (description docs/steam/description.bbcode, EN then RU); signed tag `git tag -s vX.Y.Z`, GitHub release with zip.
- Telemetry: auto error reports become GitHub issues (label auto-report). Agent-caused errors in Power/Inspect
  commands must not be reported as crashes.
- Every user-facing string goes through Languages/*/Keyed/AgentBridge.xml (all 29 languages, `\n` for newlines).
