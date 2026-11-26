"""Close the working tectonic topology; extensions are provisional, not measured kinematics."""
from pathlib import Path
import json, numpy as np
from matplotlib.path import Path as MPath
root=Path(__file__).resolve().parent
p=root/'tectonics_draft.geojson';t=json.loads(p.read_text());fs=[f for f in t['features'] if f['properties']['category'] not in ('plate_extension','plate_label','plate_junction')]
base=json.loads((root/'continents_draft.geojson').read_text());main=[MPath(f['geometry']['coordinates'][0]) for f in base['features'] if f['properties']['category']=='continent']
def feat(cat,coords,typ,**props):return {'type':'Feature','properties':{'category':cat,'status':'proposed',**props},'geometry':{'type':typ,'coordinates':coords}}
lines={f['properties'].get('label'):f['geometry']['coordinates'] for f in fs if f['properties']['category'] in ('convergence','transform','ocean_rift')}
a,b,c,d,e=(lines[k] for k in 'ABCDE')
N=[64.5,89.4];S=[65,-85];J1=[178.5,35];J2=e[-1]
# Western, central and eastern meridional boundary chains meet at two polar-ocean junctions.
# A is a chord splitting the western face; F is a separate incipient rift inside P2.
extensions=[
('X01',[e[0],[1,88.5],N],'Северное соединение сдвигового пояса E'),
('X02',[e[-1],[-6,30],[-11,22],b[0]],'Океаническое соединение E и B'),
('X03',[b[-1],[34,-63],[45,-75],S],'Южное океаническое продолжение B'),
('X04',[c[-1],[126,88.4],N],'Северное окончание рифта C'),
('X05',[c[0],[116,-82],[91,-85],S],'Южное соединение рифта C'),
('X06',[N,[137,89],[179,88],[179.1,69],[179.0,48],J1,[176,17],[166,5],d[-1]],'Восточная океаническая граница к поясу D'),
('X07',[d[0],[159,-73],[142,-83],[125,-88],[91,-88],S],'Южное океаническое соединение D'),
('X08',[a[0],[-179.8,35],[-180,35]],'Западное соединение A на меридиане 180°'),
('X09',[[180,35],J1],'Западное соединение A (восточная сторона меридиана)'),
('X10',[a[-1],[-50,43],[-31,40],[-15,39.4],J2],'Восточное соединение A с границей E/B')]
for id,coords,name in extensions:
 fs.append(feat('plate_extension',coords,'LineString',id=id,name=name,note='Предполагаемая граница; механизм и относительная скорость не установлены.'))
for id,coords,name in [('P1',[-102,65],'Северо-западная плита'),('P2',[-108,-26],'Юго-западная плита'),('P3',[61,-23],'Центрально-восточная плита'),('P4',[147,-33],'Восточная плита')]:
 fs.append(feat('plate_label',coords,'Point',id=id,name=name,note='Рабочая макроплита; состав микроплит и режимы не утверждены.'))
for id,coords in [('J-N',N),('J-S',S),('J-W',J1),('J-E',J2)]:
 fs.append(feat('plate_junction',coords,'Point',id=id,name='Рабочий тройной узел',note='Топологический узел схемы; точное положение и устойчивость уточняются.'))
# Check inferred links don't cut the interiors of the major continents.
for id,coords,name in extensions:
 pts=np.concatenate([np.linspace(l,r,150) for l,r in zip(coords,coords[1:])]);bad=np.zeros(len(pts),bool)
 for m in main:bad|=m.contains_points(pts)
 if bad.any():print('CROSSES LAND',id,pts[bad][:4])
t['features']=fs;t['properties']['plate_topology']='Four provisional macroplate faces bounded by A/B/C/D/E and connecting dashed segments; F is an internal rift. Topology only, not kinematics.'
p.write_text(json.dumps(t,ensure_ascii=False,separators=(',',':'))+'\n')
print('Added',len(extensions),'provisional connecting segments; 4 macroplate labels and 4 junctions.')
