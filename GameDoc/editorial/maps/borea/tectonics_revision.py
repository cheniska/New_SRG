"""Revision of the working Borea tectonic layer; coordinates remain editorial."""
from pathlib import Path
import json
import numpy as np
from matplotlib.path import Path as MPLPath
root=Path(__file__).resolve().parent
base=json.loads((root/'continents_draft.geojson').read_text())
t=json.loads((root/'tectonics_draft.geojson').read_text())
features=t['features']
# Re-running never duplicates the added features.
features=[f for f in features if f['properties']['category'] not in ('high_peak','volcanic_arc','collision_core')]
continents={f['properties']['number']:MPLPath(f['geometry']['coordinates'][0]) for f in base['features'] if f['properties']['category']=='continent'}
def point(cat,coords,**p):return {'type':'Feature','properties':{'category':cat,**p},'geometry':{'type':'Point','coordinates':coords}}
def line(cat,coords,**p):return {'type':'Feature','properties':{'category':cat,**p},'geometry':{'type':'LineString','coordinates':coords}}
# The earlier broad offshore lines are replaced by schematic fronts close to their respective margins.
# Smooth envelopes follow the broad coast without intersecting the continents.
def coast(n,x,mode):
    p=continents[n]
    if mode=='south':
        q=np.arange(0,89,.075);s=q[p.contains_points(np.c_[np.full_like(q,x),q])]
        return min(s) if len(s) else float('nan')
    q=np.arange(-180,180,.075);s=q[p.contains_points(np.c_[q,np.full_like(q,x)])]
    return (min(s) if mode=='west' else max(s)) if len(s) else float('nan')
def offshore(n,xs,mode,delta):
    vals=[]
    for x in xs:
        a=np.array([coast(n,y,mode) for y in [x-2,x-1,x,x+1,x+2]])
        a=a[np.isfinite(a)]
        if not len(a):continue
        v=(min(a)-delta if mode in ('south','west') else max(a)+delta)
        vals.append([round(float(x),2),round(float(v),2)] if mode=='south' else [round(float(v),2),round(float(x),2)])
    return vals
for f in features:
    p=f['properties']
    if p.get('label')=='A':
        f['geometry']['coordinates']=offshore(1,np.arange(-173,-57,4),'south',2.4)
        p['regime']='Косая конвергенция, аккреция микроконтинентальных блоков; локальная континентальная коллизия'
    elif p.get('label')=='B':
        f['geometry']['coordinates']=offshore(7,np.arange(14,-53,-3.5),'west',2.1)
        f['geometry']['coordinates'][0]=[-18,14]
        p['regime']='Океаническая субдукция с локальным столкновением аккретированных континентальных фрагментов'
    elif p.get('label')=='D':
        f['geometry']['coordinates']=offshore(8,np.arange(-63,-6,3.5),'east',1.8)
        p['regime']='Конвергенция и локальная коллизия края континента с плавучим террейном'
# Coast-facing volcanic arc; high collision belt is distinct from the arc.
arc=[[-10.2,8],[-15,2],[-16,-3],[-13,-8],[-9,-13],[-8,-20],[-6,-24],[1,-30],[4,-35],[16,-39],[25,-42],[29,-45],[32,-49]]
features.append(line('volcanic_arc',arc,continent=7,name='Прибрежная вулканическая дуга материка 7',label='B-arc'))
# Rare summits in local collision cores; exact values and locations are proposals.
peaks=[
('A1',1,[-132.2,34.6],9650,'A'),('A2',1,[-114.0,34.3],9250,'A'),
('B1',7,[-5.3,-8.7],9850,'B'),('B2',7,[2.5,-19.8],9420,'B'),
('D1',8,[162.3,-36.8],9180,'D'),('D2',8,[163.6,-25.1],9320,'D')]
for id,n,coords,h,zone in peaks:
    features.append(point('high_peak',coords,id=id,continent=n,name='Пик '+id,height_m=h,zone=zone,status='proposed',note='Абсолютная высота над уровнем моря; узкая вершина на локально утолщённой коре'))
# Reclassify existing fields and add missing heat districts. A field symbol is an approximate district, not a source vent or a city.
fields=[
('G01',1,[-144,43],'Северо-западное предгорное поле','industrial','A','Глубокие трещины и магматические интрузии широкого орогена'),
('G02',1,[-111,40],'Южное предгорное поле материка 1','industrial','A','Трещиноватые предгорья и нагретые глубинные воды'),
('G03',1,[-85,46],'Восточное предгорное поле материка 1','industrial','A','Вторичные разломы и гидротермальная циркуляция'),
('G04',6,[-128.2,2.0],'Северное рифтовое поле','oasis','F','Боковой разлом на рифтовом плече; питание грунтовыми водами'),
('G05',6,[-124.3,-6.0],'Центральное рифтовое поле','oasis','F','Пересечение трещин рифтового плеча и водоносного горизонта'),
('G06',6,[-121.0,-14.0],'Южное экваториальное рифтовое поле','oasis','F','Нагретая вода в защищённой низкой долине к востоку от рифта'),
('G07',6,[-116.5,-23.0],'Южный рифтовый тепловой район','industrial','F','Глубинная циркуляция по рифтовым разломам'),
('G08',7,[-12.1,4.0],'Северный прибрежный геотермальный район','oasis','B','Гидротермальная система на фланге магматической дуги'),
('G09',7,[-11.0,-3.0],'Западный прибрежный геотермальный район','oasis','B','Трещиноватая кора дуги; подпитка осадками и водоносными пластами'),
('G10',7,[-9.0,-11.0],'Западные тёплые предгорья','oasis','B','Фланговый разлом предгорий, глубинное тепло и сток с гор'),
('G11',7,[4.2,-14.5],'Южный экваториальный геотермальный район','oasis','B','Горячие воды в межгорном бассейне на внутренней стороне орогена; вторичные трещины нагретой коры'),
('G12',7,[11,-20],'Юго-западное промышленное поле','industrial','B','Разлом на внутренней стороне орогена'),
('G13',7,[25,-35],'Южное поле материка 7','industrial','B','Гидротермальные воды в трещиноватых предгорьях'),
('G14',8,[151.8,-11.6],'Северное поле материка 8','oasis','D','Фланг узкого вулкано-тектонического района; локальная укрытая долина'),
('G15',8,[153,-25],'Поле материка 8','industrial','D','Западные предгорные трещины и глубинные воды'),
('G16',8,[154,-43],'Южное поле материка 8','industrial','D','Разломы внутренних предгорий')
]
features=[f for f in features if f['properties']['category']!='geothermal']
for id,n,c,name,kind,zone,source in fields:
    features.append(point('geothermal',c,id=id,continent=n,name=name,kind=kind,zone=zone,heat_source=source,status='proposed',note='Зона ресурса; положение отдельного выхода горячей воды и древнего поселения уточняется'))
# Validate all ground points and lines against agreed continents. Offshore lines need not be complete plate networks.
for f in features:
 p=f['properties'];g=f['geometry'];n=p.get('continent')
 if not n:continue
 coords=g['coordinates']
 pts=np.array([coords] if g['type']=='Point' else coords[0] if g['type']=='Polygon' else coords)
 if not continents[n].contains_points(pts).all():
  print('OUTSIDE',p['category'],p.get('id',p.get('name')),pts[~continents[n].contains_points(pts)])
# Check whether sampled offshore segments cross the associated continental polygon.
for f in features:
 p=f['properties'];n={'A':1,'B':7,'D':8}.get(p.get('label'))
 if n:
  c=np.array(f['geometry']['coordinates']);pts=np.concatenate([np.linspace(a,b,20) for a,b in zip(c,c[1:])])
  if continents[n].contains_points(pts).any():print('FRONT CROSSES CONTINENT',p['label'],pts[continents[n].contains_points(pts)][:5])
t['features']=features
t['properties'].update(status='working_revision_2026-11-26',scope='Main tectonic fronts; proposed collision summits, coastal volcanic arc and thermal districts. Not a closed global plate model.')
(root/'tectonics_draft.geojson').write_text(json.dumps(t,ensure_ascii=False,separators=(',',':'))+'\n')
print(len(features),'features;',len(peaks),'summits;',len(fields),'fields')
