"""Generate repeatable encounter definitions from a live server movement survey."""
import json
import random
import uuid
from collections import deque, Counter
from pathlib import Path
from PIL import Image, ImageDraw

root = Path(__file__).resolve().parents[2]
survey = json.loads((root/'Distribution/Data/LabrynthPopulation/survey.json').read_text())
floor = {(x,y) for x,y,z in survey['Points'] if z == 0}
steps = [(1,0),(-1,0),(0,1),(0,-1)]
distance = {}
q = deque()
for x,y in floor:
    if any((x+dx,y+dy) not in floor for dx,dy in steps):
        distance[x,y] = 1
        q.append((x,y))
while q:
    x,y = q.popleft()
    for dx,dy in steps:
        p = x+dx,y+dy
        if p in floor and p not in distance:
            distance[p] = distance[x,y]+1
            q.append(p)

# Room cores exclude narrow passageways; their connected area drives encounter severity.
cores = {p for p in floor if distance[p] >= 4}
rooms = []
while cores:
    start = min(cores)
    cores.remove(start)
    component = {start}
    q = deque([start])
    while q:
        x,y = q.popleft()
        for dx,dy in steps:
            p = x+dx,y+dy
            if p in cores:
                cores.remove(p)
                component.add(p)
                q.append(p)
    if len(component) >= 8:
        rooms.append(component)
rooms.sort(key=lambda r: (-len(r), min(r)))
room_for = {}
for i,room in enumerate(rooms):
    for x,y in room:
        for dx in range(-3,4):
            for dy in range(-3,4):
                p = x+dx,y+dy
                if p in floor:
                    room_for.setdefault(p,i)

rng = random.Random(92726)
candidates = sorted(p for p in floor if distance[p] >= 2 and abs(p[0]-5248)+abs(p[1]-784) >= 21
                    and abs(p[0]-5276)+abs(p[1]-715) >= 5)
rng.shuffle(candidates)
candidates.sort(key=lambda p: -distance[p])
selected = []
for p in candidates:
    if all((p[0]-x)**2+(p[1]-y)**2 >= 8**2 for x,y in selected):
        selected.append(p)
selected.sort(key=lambda p: (p[1],p[0]))
assert 40 <= len(selected) <= 180, len(selected)
palette = {'Gargoyle':'#db7c58','GiantSpider':'#927357','TormentedMinotaur':'#edc159',
           'StoneHarpy':'#99bdd0','GiantSerpent':'#66ab65','Wraith':'#baa8dc','Lich':'#df6dd1'}
types = list(palette)
defs, details = [], []
tier_counts = Counter()
for n,(x,y) in enumerate(selected):
    room = room_for.get((x,y))
    area = len(rooms[room]) if room is not None else 0
    tier = 'great hall' if area >= 180 else 'chamber' if area >= 40 else 'small room' if area else 'passage'
    sequence = tier_counts[tier]
    tier_counts[tier] += 1
    if tier == 'great hall':
        monster = ['TormentedMinotaur','Lich','Gargoyle','TormentedMinotaur','StoneHarpy'][sequence%5]
        count = 2 if monster == 'TormentedMinotaur' else 5
    elif tier == 'chamber':
        monster = ['TormentedMinotaur','Lich','StoneHarpy','Gargoyle','GiantSpider'][sequence%5]
        count = 1 if monster == 'TormentedMinotaur' else 4
    elif tier == 'small room':
        monster = ['Wraith','Gargoyle','StoneHarpy','GiantSpider','GiantSerpent','Lich'][sequence%6]
        count = 3
    else:
        monster = ['GiantSpider','GiantSerpent','Wraith','StoneHarpy','Gargoyle','TormentedMinotaur','Lich','Wraith'][sequence%8]
        count = 1 if monster == 'TormentedMinotaur' else 2
    name = f'Labrynth Dungeon - {n+1:03d} {tier} {monster}'
    defs.append(dict(type='Spawner',name=name,guid=str(uuid.uuid5(uuid.NAMESPACE_URL,f'zodiac/labrynth/{x}/{y}')),
                     location=dict(x=x,y=y,z=0),map='Felucca',count=count,minDelay='00:05:00',maxDelay='00:08:00',
                     team=0,homeRange=1,walkingRange=5 if area>=40 else 3,
                     entries=[dict(name=monster,probability=100,maxCount=count)]))
    details.append(dict(x=x,y=y,room=room,roomCoreArea=area,tier=tier,monster=monster,count=count))

out = root/'Tools/LabrynthPopulation'
(out/'encounters.json').write_text(json.dumps(defs,indent=2)+'\n')
(out/'plan.json').write_text(json.dumps(dict(spawners=len(defs),population=sum(d['count'] for d in defs),
    rooms=[dict(coreArea=len(r),bounds=[min(x for x,y in r),min(y for x,y in r),max(x for x,y in r),max(y for x,y in r)]) for r in rooms],
    encounters=details),indent=2)+'\n')
image = Image.new('RGB',(720,840),'#17191b')
draw = ImageDraw.Draw(image)
for x,y in floor:
    shade = min(105,30+distance[x,y]*5)
    draw.rectangle(((x-5168)*5,(y-632)*5,(x-5168)*5+4,(y-632)*5+4),fill=(shade,shade,shade))
for d in details:
    x,y=(d['x']-5168)*5,(d['y']-632)*5
    draw.ellipse((x-4,y-4,x+4,y+4),fill=palette[d['monster']])
draw.text((8,795),f"{len(defs)} encounter spawners / {sum(d['count'] for d in defs)} creatures",fill='white')
for i,(name,color) in enumerate(palette.items()):
    draw.text((8+(i%4)*178,813+(i//4)*13),name,fill=color)
image.save(out/'encounter-map.png')
print('Encounters:',len(defs),'Population:',sum(d['count'] for d in defs))
print('Room core sizes:',[len(r) for r in rooms])
print('Population by type:',dict(Counter({t:sum(d['count'] for d in details if d['monster']==t) for t in types})))
