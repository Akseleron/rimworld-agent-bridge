#!/usr/bin/env python3
"""Live smoke test of Agent Bridge against a loaded colony (spawns/kills things: use a disposable test save or quicktest).
Usage: python tools/rwb_smoke.py   -> one line per command: OK/ERR, latency, args, truncated result."""
import os
import sys, json, time
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__))); import rwb
def c(**k):
    t=time.time(); r=rwb.call(k); dt=(time.time()-t)*1000
    s=json.dumps(r, ensure_ascii=False)
    print(f"{'OK ' if r.get('ok') else 'ERR'} {dt:6.0f}ms {k.get('cmd'):12} {json.dumps({x:y for x,y in k.items() if x!='cmd'},ensure_ascii=False)[:70]:70} {s[:260]}")
    return r.get('result')
c(cmd='snapshot',fields=['state']); c(cmd='map')
col=c(cmd='pawns') or []
ids=[p['id'] for p in col]; a=ids[0] if ids else None
pos=col[0]['pos'] if col else [50,50]
x,z=pos
c(cmd='pawn',id=a)
c(cmd='things',near=pos,radius=4,limit=5)
c(cmd='cell',x=x,z=z)
c(cmd='area',x=x,z=z,radius=6)
c(cmd='alerts'); c(cmd='letters'); c(cmd='windows'); c(cmd='resources')
c(cmd='gizmos',id=a)
c(cmd='menu',pawns=[a],x=x+3,z=z)
c(cmd='debug-list',filter='spawn',limit=5)
c(cmd='saves',limit=2); c(cmd='defs',filter='revolver')
c(cmd='select',ids=[a]); c(cmd='camera',x=x,z=z)
c(cmd='draft',ids=[a]); c(cmd='move',ids=[a],x=x+5,z=z)
c(cmd='tick',n=120)
c(cmd='pawn',id=a)
c(cmd='gizmo',id=a,key='key:Command_ColonistDraft') 
c(cmd='draft',ids=[a],on=True)
sp=c(cmd='spawn',kind='Mech_Scyther',x=x+8,z=z,faction='Mechanoid')
c(cmd='spawn',**{'def':'Steel'},count=200,x=x+2,z=z+2)
c(cmd='spawn',**{'def':'Gun_Revolver'},x=x+1,z=z+1,quality='Legendary')
c(cmd='spawn',**{'def':'Muffalo'},x=x+6,z=z+3,faction='player')  # reports #4/#5: a creature def is generated as a pawn
c(cmd='spawn',**{'def':'Human'},x=x+6,z=z+4)
if sp: c(cmd='attack',ids=[a],target=sp[0])
c(cmd='tick',n=60)
c(cmd='feed',after=0,limit=5)
if sp: c(cmd='damage',id=sp[0],amount=30,**{'def':'Bullet'}); c(cmd='inspect',id=sp[0]); c(cmd='kill',ids=[sp[0]])
c(cmd='wait',until='death',after=0,timeout=2)
c(cmd='heal',ids=[a]); c(cmd='hediff',id=a,**{'def':'Flu'},severity=0.3); c(cmd='hediff',id=a,**{'def':'Flu'},remove=True)
c(cmd='need',id=a,need='Food',value=0.5); c(cmd='skill',id=a,skill='Shooting',level=12)
c(cmd='work',id=a,work='Cooking',priority=1)
c(cmd='schedule',id=a,hour=3,assignment='Joy')
c(cmd='research',list=True); c(cmd='research-set',**{'def':'Electricity'}); c(cmd='research')
b=c(cmd='build',**{'def':'Wall'},x=x-4,z=z,stuff='WoodLog'); c(cmd='build-instant',**{'def':'ElectricStove'},x=x-6,z=z+3)
c(cmd='zone',type='stockpile',x=x-3,z=z-6,x2=x+3,z2=z-4)
c(cmd='designate',designator='PlantsCut',x=x-10,z=z-10,x2=x+10,z2=z+10)
c(cmd='weather',**{'def':'Rain'}); c(cmd='condition',**{'def':'Eclipse'},ticks=2000)
c(cmd='incident'); c(cmd='incident',**{'def':'RaidEnemy'},points=300)
c(cmd='factions'); c(cmd='quests'); c(cmd='world')
c(cmd='obj',target='map',path='mapPawns.AllPawnsCount')
c(cmd='obj',target='thing:'+a,path='skills',op='members')
c(cmd='obj',target='static:Verse.Prefs',path='AutosaveIntervalDays')
# issue #2/#3: agent mistakes must come back as clean errors, not crashes
r=rwb.call({'cmd':'obj','target':'map','path':'mapPawns.AllPawns[999999]'}); print('ERR-EXPECTED' if not r['ok'] and 'out of range' in r['error'] else 'BAD', 'obj index', r)
r=rwb.call({'cmd':'obj','target':'map','path':'Biome','op':'set','value':'x'}); print('ERR-EXPECTED' if not r['ok'] and ('read-only' in r['error'] or 'no ' in r['error']) else 'BAD', 'obj set get-only', r)
c(cmd='obj',target='static:Verse.Prefs',path='DevMode')
c(cmd='def',type='ThingDef',name='Gun_Revolver',depth=0)
c(cmd='patches',owner='akseleron.agentbridge'); c(cmd='capabilities'); c(cmd='mods',filter='harmony'); c(cmd='help',access='Power')
c(cmd='patches',method='Verse.Pawn:Kill')
c(cmd='batch',cmds=[{'cmd':'pause'},{'cmd':'state'}])
c(cmd='debug',path='Actions\\Spawn Pawn...\\Thrumbo',x=x+4,z=z+4)
c(cmd='window'); c(cmd='dialog')
c(cmd='letter',action='dismiss')
c(cmd='godmode',on=False)
c(cmd='screenshot',name='rwb-smoke-'+str(int(time.time())))
c(cmd='perf')
c(cmd='pause')
