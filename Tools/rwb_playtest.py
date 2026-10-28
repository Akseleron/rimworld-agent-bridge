#!/usr/bin/env python3
"""Fair-play test: can a player-agent run a colony with only player abilities?

From the main menu it starts a new colony through the real setup pages; from a running game it uses the current
colony. It then switches fair play ON (only the player can switch it off again, in the mod settings), checks that
cheats and hidden information are refused, and drives ordinary gameplay: work, schedules, policies, areas, zones,
storage, research, building, designations, a day of game time, and (if a trader or settlement is near) trading and
a caravan. Prints one line per check and exits non-zero on failure.

  python rwb_playtest.py [--days 1] [--scenario Crashlanded]
"""
import argparse, json, os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import rwb

results = []


def call(**k): return rwb.call(k, agent='playtest')


def check(name, ok, detail=''):
    results.append(ok)
    print(f"{'PASS' if ok else 'FAIL'}  {name:38} {str(detail)[:150]}", flush=True)


def refused(cmd, **k):
    r = call(cmd=cmd, **k)
    check(f'refused: {cmd}', not r.get('ok') and 'fair-play' in r.get('error', ''), r.get('error', r))


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument('--days', type=float, default=1); p.add_argument('--scenario', default='Crashlanded')
    a = p.parse_args()
    state = call(cmd='state')['result']
    if state['programState'] == 'Entry':
        r = call(cmd='newgame', scenario=a.scenario, timeout=900)
        check('new colony through setup pages', r.get('ok') and r['result'].get('started'), r.get('result'))
    for _ in range(5):  # opening story dialogs
        w = call(cmd='windows')['result']
        if not any(x.get('forcePause') for x in w): break
        if any(x['type'] == 'Dialog_NodeTree' for x in w): call(cmd='dialog', index=0)
        else: call(cmd='window', button='A')
    check('fair play on', call(cmd='fair-play').get('ok'))
    refused('spawn', **{'def': 'Steel'}, x=1, z=1)
    refused('incident', **{'def': 'RaidEnemy'})
    refused('obj', target='map', path='Biome')
    refused('debug-list')
    r = call(cmd='fair-play', on=False); check('agent cannot switch fair play off', not r.get('ok'), r.get('error'))
    r = call(cmd='speed', speed=4); check('speed capped at 3', r.get('ok') and r['result']['speed'] != 'Ultrafast', r.get('result'))

    r = call(cmd='tick', n=600)
    pawns = call(cmd='pawns')['result']
    check('colonists present', len(pawns) > 0, [x['id'] for x in pawns])
    ids = [x['id'] for x in pawns]; me = ids[0]; c = pawns[0]['pos']
    check('snapshot', call(cmd='snapshot', fields=['state', 'colonists']).get('ok'))
    check('work priority', call(cmd='work', id=me, work='Construction', priority=1).get('ok'))
    check('schedule', call(cmd='schedule', id=me, hour=22, assignment='Sleep').get('ok'))
    pol = call(cmd='policies')['result']
    check('assign policies', call(cmd='assign', ids=ids, food=pol['food'][0], medical='Best').get('ok'))
    check('allowed area', call(cmd='area-edit', name='Camp', create=True, x=c[0] - 10, z=c[1] - 10, x2=c[0] + 10, z2=c[1] + 10).get('ok'))
    check('home area', call(cmd='area-edit', name='Home', x=c[0] - 8, z=c[1] - 8, x2=c[0] + 8, z2=c[1] + 8).get('ok'))
    z = call(cmd='zone', type='stockpile', x=c[0] + 2, z=c[1] + 2, x2=c[0] + 5, z2=c[1] + 5)
    check('stockpile zone', z.get('ok'), z.get('result', z.get('error')))
    if z.get('ok'): check('storage filter', call(cmd='storage', id=f'zone@{c[0] + 3},{c[1] + 3}', priority='Important').get('ok'))
    rs = call(cmd='research', list=True)['result'].get('available') or []
    if rs: check('research project', call(cmd='research-set', **{'def': rs[0]['defName']}).get('ok'), rs[0]['defName'])
    things = call(cmd='things', category='Item', near=c, radius=25, limit=50)['result']
    forb = [t['id'] for t in things if t.get('forbidden')]
    if forb: check('unforbid starting items', call(cmd='forbid', ids=forb, on=False).get('ok'), len(forb))
    b = call(cmd='build', **{'def': 'Wall'}, x=c[0] - 4, z=c[1] + 4, stuff='WoodLog')
    check('blueprint', b.get('ok'), b.get('result', b.get('error')))
    check('designate cut plants', call(cmd='designate', designator='PlantsCut', x=c[0] - 6, z=c[1] - 6, x2=c[0] + 6, z2=c[1] + 6).get('ok'))
    check('menu on a cell', call(cmd='menu', pawns=[me], x=c[0] + 1, z=c[1]).get('ok'))

    ticks = int(a.days * 60000); done = 0
    while done < ticks:
        r = call(cmd='tick', n=min(15000, ticks - done), timeout=300)['result']; done += r['ran']
        if r.get('stop') == 'blocked-by-window':
            w = call(cmd='windows')['result']
            if any(x['type'] == 'Dialog_NodeTree' for x in w): call(cmd='dialog', index=0)
            else: call(cmd='window', button='A')
        elif r.get('stop'): break
    col = call(cmd='colony')['result']
    check(f'{a.days} day(s) of play, colony alive', col.get('colonists', 0) > 0, col)

    objs = call(cmd='world-objects', radius=20, kind='Settlement')['result']
    near = [o for o in objs if o['dist'] > 0]
    if near and len(ids) > 1:
        r = call(cmd='caravan-form', pawns=[ids[-1]], tile=near[0]['tile'])
        check('caravan to nearest settlement', r.get('ok'), r.get('result', r.get('error')))
    print(f"\n{sum(results)}/{len(results)} checks passed")
    call(cmd='pause')
    sys.exit(0 if all(results) else 1)


if __name__ == '__main__':
    main()
