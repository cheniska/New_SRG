"""Proposed historical geography of Borea, NOT literal ancestral borders.
Requires the common model grid exported by export_model_grid.cjs and regional_draft.json.
The present-day terrain is an analog for ancient habitat, not a reconstruction of climate at -7000.
Outputs reference objects, a coarse habitat raster and four static companion maps.
"""
from pathlib import Path
import json
import math
import numpy as np
from scipy.spatial import cKDTree
from matplotlib.path import Path as MPath
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
from matplotlib.colors import ListedColormap
from matplotlib.patches import Patch
from matplotlib.lines import Line2D

ROOT=Path(__file__).resolve().parent
def read(name):return json.loads((ROOT/name).read_text())
geo=read("continents_draft.geojson"); tect=read("tectonics_draft.geojson")["features"]
node_features=read("settlements_draft.geojson")["features"]
nodes={f["properties"]["id"]:f for f in node_features}
fields={f["properties"]["id"]:f for f in tect if f["properties"]["category"]=="geothermal"}
regional=read("regional_draft.json"); W,H=regional["grid"]
summary=read("regional_summary.json")
grid=read("model_grid.json"); W0,H0=grid["W"],grid["H"]
assert (W0,H0)==(W*2,H*2)
def model(name,dtype):
    a=np.fromfile(ROOT/("model_"+name+".bin"),dtype=dtype).reshape(H0,W0)
    return a.reshape(H,2,W,2).mean(axis=(1,3))
def decode(k):
    r=regional["layers"][k]["runs"];return np.repeat([p[0] for p in r],[p[1] for p in r]).reshape(H,W)
land=model("land",np.uint8)>=.5; glacier=model("glacier",np.uint8)>.5
height=model("height",np.float32); hazard=model("hazard",np.float32)
ag=decode("agriculture"); water=decode("waterResources")
lon=-180+(np.arange(W)+.5)*360/W; lat=90-(np.arange(H)+.5)*180/H
X,Y=np.meshgrid(lon,lat); lat_abs=np.abs(Y)
def xyz(lo,la):
    lo,la=np.radians(lo),np.radians(la)
    return np.stack([np.cos(la)*np.cos(lo),np.cos(la)*np.sin(lo),np.sin(la)],axis=-1)
X3=xyz(X.ravel(),Y.ravel())
def distance(coords):
    coords=np.array(coords,float)
    chord=cKDTree(xyz(coords[:,0],coords[:,1])).query(X3,workers=2)[0].reshape(H,W)
    return 2*1.3*6371*np.arcsin(np.clip(chord*.5,0,1))
oases=[f["geometry"]["coordinates"] for f in fields.values() if f["properties"]["kind"]=="oasis"]
industrial=[f["geometry"]["coordinates"] for f in fields.values() if f["properties"]["kind"]=="industrial"]
dist_oasis=distance(oases);dist_industrial=distance(industrial)
# The ice-edge proxy is only an analog of a cold-adapted environment, not a historical ice front.
ice_dist=distance(np.c_[X[glacier&land],Y[glacier&land]])
# Three pre-cat historical environmental associations. Distances deliberately delimit broad search
# areas; hot springs themselves occupy much smaller areas. Overlap is retained as 3 bit flags.
valley=land&(hazard<.65)&(height<2100)&~glacier&((dist_oasis<800)|(dist_industrial<420))
upland=land&(hazard<.7)&~glacier&(height<3000)&(((lat_abs>=43)&(ice_dist<650))|((lat_abs>=26)&(ice_dist<270))|((height>1200)&(ice_dist<230)))
plain=land&~glacier&(height<1000)&(lat_abs<23)&np.isin(ag,[1,2])&(water>=2)&(hazard<.55)
bits=valley.astype(np.uint8)+2*upland.astype(np.uint8)+4*plain.astype(np.uint8)
ancestry=np.where(land,np.where(bits,bits,8),0).astype(np.uint8)
assert not np.any((ancestry>0)&~land)
palette=["#385b70","#c48a4b","#729fca","#a291ae","#94af66","#e1bd62","#8bbdb0","#ba9f84","#c9c6b9"]
labels=["Океан","Долинные · природная область","Нагорные · природная область","Долинные / нагорные · пересечение","Равнинные · природная область","Долинные / равнинные · пересечение","Нагорные / равнинные · пересечение","Три области · пересечение","Суша без выделенного ядра"]
def rle(a):
    a=a.ravel();p=np.r_[0,np.flatnonzero(a[1:]!=a[:-1])+1,len(a)]
    return [[int(a[p[i]]),int(p[i+1]-p[i])] for i in range(len(p)-1)]
def index_at(c):
    lo,la=c;return min(H-1,max(0,int((90-la)*H/180))),int((lo+180)*W/360)%W
def feat(id,category,kind,coords,name,description,views,**extra):
    return {"type":"Feature","properties":{"id":id,"category":category,"kind":kind,"name":name,"description":description,
            "views":views,"status":"proposed",**extra},"geometry":{"type":"Point","coordinates":coords}}
B=[-104,-13]
# B01 is the center of the approved broad location, NOT an entrance or the footprint of the underground network.
# Site is inside the stable eastern massif, outside active zones.
mf=next(f for f in tect if f["properties"].get("name")=="Восточный устойчивый массив материка 6")
assert MPath(mf["geometry"]["coordinates"][0]).contains_point(B)
iy,ix=index_at(B)
assert land[iy,ix] and hazard[iy,ix]<.15 and water[iy,ix]>=2
items=[feat("B01","historical_site","bunker",B,"Исторический район единого бункерного анклава",
    "Западный склон восточного устойчивого массива материка 6. Один исторический очаг после катастрофы около −7000; район согласован, точное положение и глубинная подпитка сети требуют разработки. Метка показывает район, не площадь убежищ.",
    ["ancestry","refugia","historyRegions","modernHeritage"],continent=6,
    view_notes={"ancestry":"Бункерная линия сформировалась после −7000; не относится к докатастрофическим природным ареалам.",
        "refugia":"Относительно небольшой уцелевший анклав одной большой сети; самостоятельное подземное хозяйство до массового расселения 1500–2000.",
        "historyRegions":"Исторический район подземного анклава, не государство и не указание на современную политическую границу.",
        "modernHeritage":"Район происхождения и возможного сохранения родственных связей; потомки расселены по всей Борее."})]
# A few independent surface refuge candidates near the working modern fields.
# Their long-lived past activity is a proposal; current G points are merely geographic references.
ref=[
("RF01","G02",1,"Северное предгорное ядро","Промежуточный холодный очаг у природных выходов тепла; потенциальная преемственность долинных и нагорных популяций."),
("RF02","G04",6,"Северный рифтовый очаг","Сравнительно мягкая долина; вероятная преемственность долинных и части равнинных общин."),
("RF03","G05",6,"Центральный рифтовый очаг","Укрытая долина у глубокого тепла и локальной воды; соединение долинных и периферийных равнинных популяций."),
("RF04","G08",7,"Западный северный очаг","Наиболее благоприятная подпитка из морских осадков и гор; теплые долины и укрытые приморские участки."),
("RF05","G10",7,"Западный предгорный очаг","Тёплые источники на фланге гор; вероятная преемственность долинных и нагорных групп, возможны смешанные общины."),
("RF06","G14",8,"Юго-восточный изолированный очаг","Северный оазис отдельного материка; возможное длительное независимое развитие и сохранение популяций тёплых долин.")
]
for id,g,c,name,desc in ref:
    items.append(feat(id,"historical_refuge","surface",fields[g]["geometry"]["coordinates"],name,
        desc+" Современное поле "+g+" служит географическим ориентиром, его древняя активность и непрерывность не доказаны.",
        ["refugia"],continent=c,field=g))
# Region anchors intentionally do NOT delineate proposed political borders or number of federal regions.
regions=[
("HR01","U15","Южные предгорья материка 1","Тепловые и горнопромысловые узлы на подступах к северному массиву; старые поселения у тепла и возобновление промыслов после отступления ледников."),
("HR02","U19","Материк 2 · западная ледовая периферия","Обособленные сообщества северного ледового региона; позднее ресурсная и снабженческая роль. Древняя непрерывность не установлена."),
("HR03","U20","Материк 3 · сдвиговая окраина и устойчивый массив","Свои маршруты и местные хозяйства, выбор безопасных устойчивых площадок. Политическая история требует отдельной разработки."),
("HR04","U21","Материк 4 · ледовый транспортный сектор","Региональная самобытность формируется из сообщения между материками и обособленных северных поселений; не один общий северный народ."),
("HR05","U22","Материк 5 · восточная ледовая периферия","Удалённые хозяйства и промыслы, собственная историческая традиция при современной интенсивной связи."),
("HR06","U02","Материк 6 · центральный рифтовый пояс","Древние тепловые долины, затем индустриальные центры с развитой переработкой; городские и территориальные объединения конкурировали за ресурсы."),
("HR07","U08","Материк 6 · восточные низменности","Равнинные сельские традиции, поселения на вновь освоенной после ледников суше, поздняя аграрная и промышленная специализация; близость к бункерному району не означает общего происхождения."),
("HR08","U23","Материк 6 · южное рифтовое предгорье","Тепловые, рудные и пастбищно-промысловые районы, более суровый климат и разреженные поселения."),
("HR09","U05","Материк 7 · западная прибрежная дуга","Древние очаги долинных популяций, тепло, вода и минерализация; исторические города с разными местными традициями, ныне крупнейший агломерационный пояс."),
("HR10","U12","Дераданская земля · согласованный исторический район","Согласованный район — северные низменности и северный край устойчивого массива материка 7; U11–U13 — современные ориентиры хозяйственного региона. U12 — возможный историко-административный центр, не утверждённая столица."),
("HR11","U24","Материк 7 · внутренние предгорья","Тыловая связь западных тепловых городов с внутренними землями, смешанные сельскохозяйственные и промышленные районы; местные исторические объединения."),
("HR12","U25","Материк 7 · южные тепловые земли","Более холодные и менее густо населённые территории; промышленность и промысел вокруг отдельных тепловых узлов."),
("HR13","U26","Материк 8 · северный оазис","Самостоятельный очаг на другом материке; более локальная сеть земледелия и тепловых поселений."),
("HR14","U28","Материк 8 · южное предгорье","Добывающие и тепловые округа; прибрежная и внутренняя логистика, более суровое природное окружение.")
]
for id,u,name,desc in regions:
    n=nodes[u]
    items.append(feat(id,"historical_region","deradan" if id=="HR10" else "region",n["geometry"]["coordinates"],name,desc,
              ["historyRegions"],continent=n["properties"]["continent"],node=u))
# An actual existing lowland polygon, explicitly a geographic core not a state boundary.
low=next(f for f in tect if f["properties"].get("name")=="Северные низменности материка 7")
items.append({"type":"Feature","properties":{"id":"HR10-area","category":"historical_extent","kind":"deradan",
    "name":"Северные низменности — согласованное природное ядро Дераданской земли","views":["historyRegions"],"status":"proposed",
    "description":"Контур существующей географической низменности, а не граница исторического государства."},"geometry":low["geometry"]})
# Approval is of the broad locations, not of precise coordinates, borders or historic capital.
for f in items:
    if f["properties"]["id"]=="B01":f["properties"]["status"]="region_approved"
    elif f["properties"]["id"]=="HR10":f["properties"]["status"]="region_approved_center_proposed"
    elif f["properties"]["id"]=="HR10-area":f["properties"]["status"]="region_approved_not_boundary"
# 3290: EVERY city is mixed; markers indicate regional heritage and context, not genetic frequencies.
valley_nodes={"U01","U02","U03","U04","U05","U06","U07","U23","U24","U26"}
plain_nodes={"U08","U09","U10","U11","U12","U13"}
upland_nodes={"U14","U15","U16","U17","U18","U19","U20","U21","U22","U25","U27","U28"}
assert valley_nodes|plain_nodes|upland_nodes==set(nodes)
for id,n in nodes.items():
    p=n["properties"]
    if id in valley_nodes:
        kind="heritage_valley";hint="Вероятно заметно наследие древних тепловых долин. Современные агломерации смешанные."
    elif id in plain_nodes:
        kind="heritage_plain";hint="История местных равнинных и сельских общин может быть заметна в малых поселениях и локальной традиции. Современные узлы смешанные."
    else:
        kind="heritage_upland";hint="В окружающих холодных районах может сохраняться наследие нагорных и приполярных общин; наличие исторической линии не означает большинства в городе."
    if id in {"U08","U09","U10","U02","U03"}:
        hint+=" На материке находится предполагаемый древний бункерный анклав; часть его потомков могла расселиться в соседних хозяйственных районах. Точные доли неизвестны."
    items.append(feat(id,"heritage_node",kind,n["geometry"]["coordinates"],p["role"],hint,["modernHeritage"],
                      continent=p["continent"],node_kind=p["kind"]))
for f in items:
    if f["geometry"]["type"]=="Point":
        i,j=index_at(f["geometry"]["coordinates"])
        assert land[i,j],(f["properties"]["id"],f["geometry"]["coordinates"])
        assert f["properties"]["id"] and f["properties"]["description"]
assert len([f for f in items if f["properties"]["kind"]=="bunker"])==1
assert len([f for f in items if f["properties"]["category"]=="heritage_node"])==28
assert len([f for f in items if f["properties"]["category"]=="historical_refuge"])==6
result={"status":"historical_regions_approved_general_details_proposed","year":3290,"grid":[W,H],"projection":"equirectangular",
        "note":"Broad ancestral areas, B01 and Deradan region approved 2026-11-26; coordinates and further spatial hypotheses remain working. Modern DEM reused only as a geographic analog. No exact historical coast, ice front, state boundaries or demographic percentages inferred.",
        "layers":{"ancestry":{"runs":rle(ancestry),"colors":palette,"labels":labels}},
        "features":items,
        "validation":{"bunker_site":{"center":B,"height_m":round(float(height[iy,ix])),"hazard_index":round(float(hazard[iy,ix]),3),"water_class":int(water[iy,ix]),"stable_massif":True},
            "surface_refuge_candidates":6,"historical_region_anchors":len(regions),"modern_nodes":len(nodes),"preserves_existing_geography":True}}
assert sum(n for _,n in result["layers"]["ancestry"]["runs"])==W*H
(ROOT/"history_draft.json").write_text(json.dumps(result,ensure_ascii=False,separators=(",",":"))+"\n")
print(json.dumps(result["validation"],ensure_ascii=False,indent=2))
# Static maps: four views, same geography and theme as regional atlas.
style={
 "bunker":("#8c429a","D"),"surface":("#d1803e","o"),"region":("#547e84","o"),"deradan":("#b83b4d","*"),
 "heritage_valley":("#c48a4b","o"),"heritage_plain":("#94af66","o"),"heritage_upland":("#729fca","o")
}
views={
 "ancestry":("history_origins.png","Исторические природные ареалы формирования", "Схема природных условий трёх древних линий; B01 относится к отдельной эпохе после −7000. Не границы реальных популяций."),
 "refugia":("history_refugia.png","Кандидаты очагов выживания после катастрофы (≈−7000…−2000)","Современные береговые линии — ориентир; древний ледник не реконструирован. Шесть поверхностных вариантов и единственный B01."),
 "historyRegions":("history_regions.png","Исторические региональные ядра · до объединения / наследие","14 опорных районов для сюжетной разработки, не число государств. Контур — согласованный район Дерадана; красная звезда — кандидат центра."),
 "modernHeritage":("history_3290.png","3290 · современное смешанное население и историческое наследие","Все городские узлы смешанные. Цвет обводки — вероятная местная традиция, а не доля или большинство популяционной линии.")
}
for view,(name,title,note) in views.items():
    fig,ax=plt.subplots(figsize=(16,8.1),facecolor="#111f2b")
    fig.subplots_adjust(left=.055,right=.975,top=.86,bottom=.23);ax.set_facecolor("#385b70")
    def transform(c):
        c=np.asarray(c);return c[:,0],90*np.sin(np.radians(c[:,1]))
    if view=="ancestry":
        ax.pcolormesh(np.linspace(-180,180,W+1),90*np.sin(np.radians(np.linspace(90,-90,H+1))),
                      ancestry,cmap=ListedColormap(palette),vmin=-.5,vmax=len(palette)-.5,shading="flat",rasterized=True)
    for f in geo["features"]:
        cat=f["properties"]["category"]
        if cat=="schematic_northern_perennial_ice_extent" and view!="ancestry":
            x,y=transform(f["geometry"]["coordinates"][0]);ax.fill(x,y,color="#a9cbd5",zorder=1)
        if cat not in ("continent","island"):continue
        x,y=transform(f["geometry"]["coordinates"][0])
        if view!="ancestry":ax.fill(x,y,color="#c8c7b7",zorder=2)
        ax.plot(x,y,color="#304653",lw=.6,zorder=3)
    if view=="historyRegions":
        x,y=transform(low["geometry"]["coordinates"][0]);ax.fill(x,y,color="#af62706b",ec="#b83b4d",lw=1.6,ls="--",zorder=4)
    for f in items:
        p=f["properties"]
        if view not in p["views"] or f["geometry"]["type"]!="Point":continue
        x,la=f["geometry"]["coordinates"];y=90*np.sin(np.radians(la))
        c,m=style[p["kind"]];size=112 if p["kind"] in ("deradan","bunker") else 53 if view!="modernHeritage" else 68 if p.get("node_kind")!="major" else 104
        ax.scatter([x],[y],s=size,marker=m,color="#405368" if p["category"]=="heritage_node" else c,edgecolors=c if p["category"]=="heritage_node" else "#263746",lw=1.55 if p["category"]=="heritage_node" else .85,zorder=8)
        if view!="modernHeritage" or p["kind"]=="bunker":
            # Separate labels for dense equatorial groups.
            xoff=2.1; yoff=3.0
            if p["id"] in ("RF03","HR08"):yoff=-5
            if p["id"] in ("RF05","HR11"):xoff=-10
            ax.text(x+xoff,y+yoff,p["id"],fontsize=8,color="#1d2938",fontweight="bold",bbox=dict(fc="#f2eadb",ec="none",alpha=.87,pad=1),zorder=9)
    # Numbering remains consistent with the existing atlas; labels avoid principal markers.
    for i,(x,la) in enumerate([(-122,58),(-30,72),(31,72),(94,72),(154,72),(-132,-34),(58,-24),(144,-39)],1):
        ax.text(x,90*np.sin(np.radians(la)),str(i),fontsize=9,fontweight="bold",ha="center",va="center",
                bbox=dict(boxstyle="circle,pad=.22",fc="#faf2de",ec="#465765",lw=.65),zorder=6,color="#263746")
    ax.set_xlim(-180,180);ax.set_ylim(-90,90)
    ax.set_xticks(np.arange(-180,181,60));ax.set_yticks(90*np.sin(np.radians([-90,-60,-30,0,30,60,90])),["90° ю.ш.","60° ю.ш.","30° ю.ш.","0°","30° с.ш.","60° с.ш.","90° с.ш."])
    ax.tick_params(colors="#bfd1db",labelsize=8,length=0);ax.grid(color="#e8eeee",alpha=.18,ls=":",lw=.6)
    for s in ax.spines.values():s.set_color("#597281")
    fig.text(.055,.95,"БОРЕЯ · РАБОЧАЯ ИСТОРИЧЕСКАЯ ГЕОГРАФИЯ",color="#f0f2ec",fontsize=21,fontweight="bold",ha="left")
    fig.text(.055,.897,title,color="#b9d3e1",fontsize=14,ha="left")
    def mark(kind,label):
        c,m=style[kind];return Line2D([0],[0],marker=m,color="none",markerfacecolor="#405368" if kind.startswith("heritage_") else c,markeredgecolor=c if kind.startswith("heritage_") else "#243546",markeredgewidth=1.6 if kind.startswith("heritage_") else 1,markersize=9,label=label)
    if view=="ancestry":
        handles=[Patch(facecolor=c,label=l,edgecolor="#172533") for i,(c,l) in enumerate(zip(palette,labels)) if i in np.unique(ancestry)]
        handles.append(mark("bunker","B01 · поздний единый анклав"))
    elif view=="refugia":
        handles=[Patch(facecolor="#a9cbd5",label="Современный северный морской лёд"),mark("surface","RF · кандидат поверхностного очага"),mark("bunker","B01 · единый бункерный анклав")]
    elif view=="historyRegions":
        handles=[mark("region","HR · опорное региональное ядро"),mark("deradan","Дерадан · кандидат центра"),
                 Patch(facecolor="#af62706b",edgecolor="#b83b4d",label="Низменность · природное ядро"),mark("bunker","B01 · исторический анклав")]
    else:
        handles=[mark("heritage_valley","Долинное наследие"),mark("heritage_plain","Равнинное наследие"),mark("heritage_upland","Нагорное наследие"),mark("bunker","Место бункерного происхождения"),Patch(facecolor="#c8c7b7",label="Распределение долей не установлено")]
    ax.legend(handles=handles,ncol=3,loc="upper left",bbox_to_anchor=(0,-.1),frameon=False,labelcolor="#d2e1e7",fontsize=9,columnspacing=1.3,handlelength=1.2)
    fig.text(.055,.035,note,color="#a7becb",fontsize=10)
    fig.savefig(ROOT/name,dpi=120,facecolor=fig.get_facecolor());plt.close(fig)
