# Releasing

[Русский](RELEASING.ru.md)

1. Bump `modVersion` in About/About.xml, `Version` in Source/AgentBridge.csproj and `Bridge.Version`; update CHANGELOG.
2. `cd Source && dotnet build -c Release`, then `python Tools/make_release.py` (clean folder without .git/Source).
3. Live check with `python Tools/rwb_smoke.py` on a quicktest colony.
4. Tag `vX.Y.Z`, push; attach a zip of the release folder to a GitHub Release.
5. Steam Workshop: put the release folder into `RimWorld/Mods` (only one copy of the packageId may be installed),
   enable Dev mode, Mods menu → select Agent Bridge → *Upload to Steam Workshop*. The first upload creates
   `About/PublishedFileId.txt`; commit it so later uploads update the same item.
See RELEASING.ru.md for the detailed first-time walkthrough, telemetry server and signing key setup.
