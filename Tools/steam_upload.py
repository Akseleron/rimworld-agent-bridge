#!/usr/bin/env python3
"""Update the Steam Workshop item from the release folder through the running game (SteamUGC needs the game's Steam
session). Run make_release.py first. The game must allow power commands: fair play off and either Dev mode on or
"require Dev mode" off in the Agent Bridge settings (restore them afterwards).

  python Tools/steam_upload.py "short change note"
"""
import json, os, re, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import rwb

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
RELEASE = os.path.join(os.path.dirname(ROOT), 'AgentBridge-release')
DESCRIPTION = os.path.join(ROOT, 'docs', 'steam', 'description.bbcode')

SCRIPT = r'''
using System; using System.IO; using System.Collections.Generic; using Steamworks; using AgentBridge;
public static class Script
{
    static string status = "submitting"; static CallResult<SubmitItemUpdateResult_t> sr;
    public static object Run(Dictionary<string, object> args)
    {
        string root = %(root)s;
        var id = new PublishedFileId_t(ulong.Parse(File.ReadAllText(Path.Combine(root, "About", "PublishedFileId.txt")).Trim()));
        var h = SteamUGC.StartItemUpdate(SteamUtils.GetAppID(), id);
        bool ok = SteamUGC.SetItemDescription(h, File.ReadAllText(%(description)s))
            & SteamUGC.SetItemPreview(h, Path.Combine(root, "About", "Preview.png"))
            & SteamUGC.SetItemTags(h, new List<string> { "Mod", "1.7" })
            & SteamUGC.SetItemContent(h, root);
        if (!ok) return "error: SetItem returned false";
        sr = CallResult<SubmitItemUpdateResult_t>.Create((s, io) => status = io || s.m_eResult != EResult.k_EResultOK ? "error " + s.m_eResult : "done");
        sr.Set(SteamUGC.SubmitItemUpdate(h, %(note)s));
        return new Pending(() => status == "submitting" ? null : (object)new { status }, 600, () => "timeout");
    }
}
'''


def main():
    if len(sys.argv) != 2: sys.exit(__doc__)
    version = re.search(r'<modVersion>([^<]+)', open(os.path.join(RELEASE, 'About', 'About.xml'), encoding='utf-8').read()).group(1)
    code = SCRIPT % {'root': json.dumps(RELEASE), 'description': json.dumps(DESCRIPTION), 'note': json.dumps(version + ' — ' + sys.argv[1])}
    dll, err = rwb.compile_snippet(code)
    if err: sys.exit(err)
    result = rwb.call({'cmd': 'exec', 'dll': str(dll), 'timeout': 600})
    print(json.dumps(result, ensure_ascii=False))
    sys.exit(0 if result.get('ok') and result['result'].get('status') == 'done' else 1)


if __name__ == '__main__':
    main()
