from pathlib import Path
import json
from matplotlib.path import Path as MPLPath
root=Path(__file__).resolve().parent
base=json.loads((root/'continents_draft.geojson').read_text());tect=json.loads((root/'tectonics_draft.geojson').read_text())
land={f['properties']['number']:MPLPath(f['geometry']['coordinates'][0]) for f in base['features'] if f['properties']['category']=='continent'}
fields={f['properties']['id']:f for f in tect['features'] if f['properties']['category']=='geothermal'}
# Representative agglomeration nodes, not a census, not a catalogue of every city.
nodes=[
('U01',6,[-127.85,2.23],'major','G04','Экваториальный тепловой и промышленный узел'),
('U02',6,[-123.87,-5.74],'major','G05','Теплично-промышленный и транспортный узел'),
('U03',6,[-120.59,-13.71],'major','G06','Геотермальная агломерация рифтового плеча'),
('U04',7,[-11.13,3.63],'major','G08','Прибрежно-геотермальный агломерационный узел'),
('U05',7,[-10.66,-2.93],'major','G09','Тепловой промышленно-торговый узел'),
('U06',7,[-8.55,-10.9],'major','G10','Долинный агломерационный узел'),
('U07',7,[4.9,-14.2],'major','G11','Экваториальный межгорный агломерационный узел'),
('U08',6,[-90,-7],'regional',None,'Земледелие, переработка, логистика восточной низменности'),
('U09',6,[-96,-12],'regional',None,'Земледельческо-промышленный узел'),
('U10',6,[-81,-10],'regional',None,'Прибрежное снабжение и продовольственное хозяйство'),
('U11',7,[36,5],'regional',None,'Северные низменности, производство продовольствия'),
('U12',7,[54,2],'regional',None,'Узел земледелия и транспортных связей'),
('U13',7,[62,-3],'regional',None,'Переработка и центр обслуживания'),
('U14',1,[-143.4,44.0],'regional','G01','Тепловой промышленный узел высоких широт'),
('U15',1,[-110.2,41.2],'regional','G02','Предгорный теплоэнергетический узел'),
('U16',1,[-84.4,47.0],'regional','G03','Горнопромышленный узел на устойчивой площадке'),
('U17',1,[-120,26.5],'specialized',None,'Холодный прибрежный и ледовый транспортный узел'),
('U18',1,[-95,28.0],'specialized',None,'Портово-промысловый узел южного берега'),
('U19',2,[-31,58],'specialized',None,'Северное снабжение и локальная промышленность'),
('U20',3,[28,58],'specialized',None,'Северное снабжение и ледовый транспорт'),
('U21',4,[93,59],'specialized',None,'Межматериковый логистический узел'),
('U22',5,[154,60],'specialized',None,'Северная промышленно-транспортная периферия'),
('U23',6,[-115.6,-22.1],'regional','G07','Южный тепловой промышленный узел'),
('U24',7,[12,-19],'regional','G12','Внутренние предгорья, добыча и переработка'),
('U25',7,[26,-34],'specialized','G13','Южный тепловой и добывающий узел'),
('U26',8,[152.5,-10.9],'regional','G14','Северное ограниченное тепловое ядро материка 8'),
('U27',8,[151.8,-24.4],'regional','G15','Тепловой и промышленный узел в западных предгорьях'),
('U28',8,[152.8,-42.4],'specialized','G16','Южный добывающий и тепловой узел')
]
# Validate representative points with true continental polygons.
features=[]
for id,n,xy,kind,field,role in nodes:
 if not land[n].contains_point(xy):print('OUTSIDE',id,n,xy)
 if field and field not in fields:raise ValueError(field)
 prop={'category':'settlement_node','id':id,'continent':n,'kind':kind,'thermal_field':field,'role':role,'status':'proposed','note':'Узел агломерации/хозяйственной зоны, не отдельный город; локальная безопасная площадка подлежит уточнению'}
 features.append({'type':'Feature','properties':prop,'geometry':{'type':'Point','coordinates':xy}})
out={'type':'FeatureCollection','name':'Borea_settlement_nodes_3290','properties':{'status':'proposed','year':3290,'scope':'Representative settlement nodes, no names or exact population. Relative density is rendered from the same nodes and geography.'},'features':features}
(root/'settlements_draft.geojson').write_text(json.dumps(out,ensure_ascii=False,separators=(',',':'))+'\n')
print(len(features),'nodes')
