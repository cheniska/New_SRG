"""Working regional geography for Borea (3290).
Requires the atlas DEM export: node export_model_grid.cjs [wiki-site/borea-natural.js].
Outputs reproducible relative classes, qualitative prospect zones, and diagnostic tables.
Not a climate simulation, ore inventory, crop yield model or calibrated hydrogeological survey.
"""
from pathlib import Path
import json, math, heapq
from collections import Counter
import numpy as np
from scipy import ndimage
from scipy.spatial import cKDTree
from matplotlib.path import Path as MPath
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
from matplotlib.colors import ListedColormap
from matplotlib.patches import Patch
from matplotlib.lines import Line2D

ROOT=Path(__file__).resolve().parent
def read(name): return json.loads((ROOT/name).read_text())
base=read("continents_draft.geojson")
tect=read("tectonics_draft.geojson")["features"]
hydro=read("hydrology_transport_draft.geojson")["features"]
nodes=read("settlements_draft.geojson")["features"]
cl=read("climate_draft.json")
grid=read("model_grid.json"); W0,H0=grid["W"],grid["H"]; W,H=W0//2,H0//2
def model(name,dtype):
    raw=np.fromfile(ROOT/("model_"+name+".bin"),dtype=dtype).reshape(H0,W0)
    return raw.reshape(H,2,W,2).mean(axis=(1,3))
elevation=model("height",np.float32); elevation_land=np.maximum(elevation,0)
risk=model("hazard",np.float32); glacier=model("glacier",np.uint8)>.5
land=model("land",np.uint8)>=.5; north=model("north",np.uint8)>=.5
lon=-180+(np.arange(W)+.5)*360/W; lat=90-(np.arange(H)+.5)*180/H
X,Y=np.meshgrid(lon,lat); absolute=np.abs(Y); coslat=np.cos(np.radians(Y))
area=coslat/coslat.sum()*base["properties"]["surface_area_million_km2"]*1e6
step=2*np.pi*1.3*6371/W # km per longitude step at equator
P=np.c_[X.ravel(),Y.ravel()]
def polygon_mask(coords): return MPath(coords[0]).contains_points(P).reshape(H,W)
cont=np.zeros((H,W),np.int16)
for f in base["features"]:
    if f["properties"]["category"]=="continent":
        cont[polygon_mask(f["geometry"]["coordinates"])]=f["properties"]["number"]
cont[~land]=0
# Any remaining represented islands are kept independent from the mainland drainage.
islands,n_island=ndimage.label(land&(cont==0),np.ones((3,3),int))
cont[islands>0]=8+islands[islands>0]
glacier &= land
def shift_dist(seeds):
    # Periodic longitudinal extension; areas near meridian seam are not discontinuous.
    a=np.tile(seeds,(1,3))
    d=ndimage.distance_transform_edt(~a)[:,W:2*W]
    return d*step # approximate grid distance, used only for broad screening
def xyz(lo,la):
    lo,la=np.radians(lo),np.radians(la)
    return np.stack([np.cos(la)*np.cos(lo),np.cos(la)*np.sin(lo),np.sin(la)],axis=-1)
grid_xyz=xyz(X.ravel(),Y.ravel())
def geo_distance(features):
    # Densely sampled spherical line points; KD tree avoids expensive full-grid sweeps.
    samples=[]
    for f in features:
        coords=np.asarray(f["geometry"]["coordinates"],float)
        if coords.ndim==1: coords=coords[None,:]
        samples.append(coords[0])
        for a,b in zip(coords,coords[1:]):
            delta=b-a; delta[0]=(delta[0]+180)%360-180
            for t in np.linspace(0,1,max(2,int(np.linalg.norm(delta)/.45)+1))[1:]:
                samples.append(a+t*delta)
    if not samples:return np.full((H,W),1e6)
    q=np.asarray(samples)
    chord=cKDTree(xyz(q[:,0],q[:,1])).query(grid_xyz,workers=2)[0].reshape(H,W)
    return 2*1.3*6371*np.arcsin(np.clip(chord*.5,0,1))
def features(*kinds): return [f for f in tect if f["properties"]["category"] in kinds]
def union_polys(fs):
    m=np.zeros((H,W),bool)
    for f in fs:m|=polygon_mask(f["geometry"]["coordinates"])
    return m

# Moisture source is mean seasonal openness, not the annual union considered ice-free year-round.
score=np.full((H,W),99.)
for b in cl["potential_seasonal_opening_basins"]:
    a=((X-b["center"][0]+180)%360-180)/b["axes"][0]
    u=(Y-b["center"][1])/b["axes"][1]; theta=np.arctan2(u,a)
    ir=1+.085*np.sin(3*theta+b["phase"])+.045*np.sin(5*theta-b["phase"])
    score=np.minimum(score,(a*a+u*u)/ir**2)
open_fraction=np.zeros((H,W),float)
for p in cl["seasonal_preview_parameters"].values():
    limit=np.clip(p["bias"]+p["hem"]*np.clip(Y/20,-1,1),.18,1.)
    open_fraction+=(score<=limit)/4
open_fraction[land|north]=0
# Relative vapor capacity declines strongly with latitude. Ice allows weak sublimation.
capacity=.13+.87*np.exp(-(absolute/47)**1.65)
source=capacity*(.075+.925*open_fraction)
# Reverse-sign trade winds at low latitudes and midlatitude easterlies; smooth transitions.
# Two directional passes blend into a background of meridional mixing, not a predicted wind chart.
tropical=1/(1+np.exp((absolute-28)/4))
polar=1/(1+np.exp((65-absolute)/5))
weight_west=np.clip(.10+.80*tropical+.48*polar,0,1)
height_sm=ndimage.gaussian_filter(elevation_land,1.35,mode=("nearest","wrap"))
precip=[]
for sign in (1,-1):
    humidity=np.zeros(H)
    buf=np.zeros((H,W))
    for turn in range(3):
        for i in range(W):
            x=i if sign==1 else W-1-i
            prev=(x-sign)%W
            uplift=np.maximum(0,height_sm[:,x]-height_sm[:,prev])
            # Upward forced condensation; water supply is depleted across mountain barriers.
            fraction=np.clip(.022+.090*np.clip(uplift/240,0,3)+.014*np.exp(-((np.abs(lat)-43)/16)**2),.015,.32)
            p=humidity*fraction
            buf[:,x]=np.where(land[:,x],p,0)
            humidity=np.where(land[:,x],np.maximum(0,humidity-p)+.00045*capacity[:,x],
                              humidity+.12*(source[:,x]-humidity))
    precip.append(buf)
p_raw=precip[0]*weight_west+precip[1]*(1-weight_west)
# Neighboring air masses allow some precipitation off the narrow zonal trajectories.
smooth=ndimage.gaussian_filter(p_raw,2.2,mode=("nearest","wrap"))
p_raw=np.where(land,.79*p_raw+.21*smooth+.0006*capacity,0)
p_ref=float(np.quantile(p_raw[land],.99))
rain=np.clip(np.log1p(9*p_raw/p_ref)/np.log(10),0,1)

# Thaw window: an editorial local model, compatible in intent with IV.5, not a new climate canon.
# Compare only relative land suitability; do not force a global average on land alone.
mean=4.0-.37*absolute-.28*np.maximum(absolute-40,0)-.62*np.maximum(absolute-60,0)-.0055*elevation_land
amp=np.minimum(20,8.5+.27*absolute)
# Equatorial seasonal cycle remains small; local oasis sites are NOT expanded into macro crop belts.
summer=mean+amp
thaw=np.arccos(np.clip((1.5-mean)/amp,-1,1))/np.pi
thaw[glacier]=0
# Relative annual runoff after atmospheric losses/storage in ice.
# Glacier balance is unknown: assign a lower contribution, not all snowfall as immediately collectable water.
runoff=np.where(land,p_raw*(.33+.32*thaw),0)
runoff[glacier]*=.28
# Priority flood: same topological method as the existing river network; no existing paths are replaced.
N=W*H; flat=elevation_land.ravel(); cf=cont.ravel(); lm=land.ravel()
filled=np.full(N,np.inf); parent=np.full(N,-2,np.int32); heap=[]; order=[]
def nb(i,own=False):
    y,x=divmod(i,W); n=cf[i]
    for dy,dx in ((0,-1),(0,1),(-1,0),(1,0),(-1,-1),(1,-1),(-1,1),(1,1)):
        jy=y+dy;jx=(x+dx)%W
        if not 0<=jy<H:continue
        j=jy*W+jx
        if own and (cf[j]!=n or not lm[j] or (dx and dy and (cf[y*W+jx]!=n or cf[jy*W+x]!=n))):continue
        yield j
for i in np.flatnonzero(lm):
    if any(cf[j]!=cf[i] for j in nb(i)):
        parent[i]=-1;filled[i]=flat[i];heapq.heappush(heap,(float(flat[i]),int(i)))
while heap:
    h,i=heapq.heappop(heap);order.append(i)
    for j in nb(i,True):
        if parent[j]!=-2:continue
        parent[j]=i;filled[j]=max(float(flat[j]),h+.03);heapq.heappush(heap,(filled[j],j))
assert np.all(parent[lm]>=-1)
acc=(runoff*area).ravel().copy(); catch=np.where(land,area,0).ravel().copy()
for i in reversed(order):
    j=parent[i]
    if j>=0: acc[j]+=acc[i];catch[j]+=catch[i]
outlets=np.flatnonzero(parent==-1)
assert abs(acc[outlets].sum()-float(np.sum(runoff*area)))<1e-6*float(np.sum(runoff*area))
assert abs(catch[outlets].sum()-area[land].sum())<1e-7*area[land].sum()
flow=acc.reshape(H,W)
riverfeatures=[f for f in hydro if f["properties"]["category"] in ("river","tributary")]
riverdist=geo_distance(riverfeatures)
# Valley access uses distance to the already mapped river corridors and accumulated runoff from
# surrounding land. It is not the volume that a settlement can sustainably abstract.
flow_ref=float(np.quantile(flow[land],.985));flow_index=np.clip(np.log1p(flow/(flow_ref*.04))/np.log1p(25),0,1)
flow_access=ndimage.maximum_filter(flow_index,size=5,mode=("nearest","wrap"))
water=np.clip(.50*rain*(.4+.6*thaw)+.33*flow_access+.17*np.exp(-riverdist/180)*flow_access,0,1)
# Permanent land ice stores water, but does not make immediate liquid water widely available.
water[glacier]*=.35

# Open farming screening. A slope is estimated on the coarse grid (not arable soil).
# Temperate rain or meltwater, thaw duration and terrain must coexist. No presumed crops or yields.
zg=ndimage.gaussian_filter(elevation_land,.8,mode=("nearest","wrap"))
# Local relief over 5x5 cells is a better coarse barrier to continuous mechanized fields.
local_relief=ndimage.maximum_filter(zg,5,mode=("nearest","wrap"))-ndimage.minimum_filter(zg,5,mode=("nearest","wrap"))
productive=(land&~glacier)&(summer>9)&(thaw>.24)&(elevation_land<650)&(local_relief<750)&(water>.36)&(risk<.45)
limited=(land&~glacier)&(summer>5)&(thaw>.17)&(elevation_land<1250)&(local_relief<1200)&(water>.23)&(risk<.63)
forage=(land&~glacier)&(summer>-1)&(thaw>.025)&(elevation_land<2200)&(local_relief<2400)
ag=np.zeros((H,W),np.uint8);ag[land]=4
ag[forage]=3;ag[limited]=2;ag[productive]=1
ag[land&(glacier|(elevation_land>2800)|(local_relief>2700))]=5
assert not np.any((ag==1)&glacier)
assert not np.any((ag==1)&(risk>=.45))
# Formation types, not a map of proven deposits. Stable massifs and arc/rift belts already in tectonic map.
mass=union_polys(features("stable_massif"))
arc=geo_distance(features("volcanic_arc"))
young=geo_distance(features("mountain_belt"))
old=geo_distance(features("old_mountain_belt"))
rift=geo_distance(features("continental_rift"))
ridge=geo_distance(features("ocean_rift"))
trenches=geo_distance(features("convergence"))
res=np.zeros((H,W),np.uint8);res[land]=1
res[land&mass]=2
res[land&((young<210)|(old<210))]=5
res[land&(rift<230)]=4
res[land&(arc<220)]=3
# Continental sediment margins only; pressure/temperature is necessary, organic methane and
# sediment thickness are not known. Avoid putting clathrates on active deep trench or ocean ridge.
# Restrict to modeled cold 350–1500 m submerged margins, adjacent to land rather than isolated ocean.
dist_land=shift_dist((cont>0)&(cont<=8))
dist_mass=shift_dist(mass)
# Broad, relatively quiet mainland sedimentary margins adjoining stable basement. This is
# a screening mask for candidate sedimentary provinces, not a belt of actual hydrates.
res[(~land)&(elevation<=-350)&(elevation>=-1500)&(dist_land<650)&(dist_mass<1450)&(trenches>280)&(ridge>180)]=6
res[(~land)&(ridge<110)&(elevation<-1200)]=7
assert np.all(res[land]>0) and not np.any((res==6)&land)

def classes(index,bounds):
    a=(np.digitize(index,bounds)+1).astype(np.uint8);a[~land]=0;return a
pr=classes(rain,[.10,.24,.43,.68])
wa=classes(water,[.12,.24,.40,.60]);wa[glacier]=6

def index_at(c):
    lo,la=c;return min(H-1,max(0,int((90-la)*H/180))),int((lo+180)*W/360)%W
def pct(m,domain=land):
    return round(100*float(area[m&domain].sum()/area[domain].sum()),2) if domain.any() else 0
def rle(a):
    a=a.ravel();k=np.r_[0,np.flatnonzero(a[1:]!=a[:-1])+1,len(a)]
    return [[int(a[k[i]]),int(k[i+1]-k[i])] for i in range(len(k)-1)]
# Each G gets screening based on neighboring supply and uplands, not guaranteed fracture connectivity.
fields=[]
for f in features("geothermal"):
    p=f["properties"]; y,x=index_at(f["geometry"]["coordinates"])
    lo,la=f["geometry"]["coordinates"]; d=2*1.3*6371*np.arcsin(np.sqrt(np.clip(np.sin(np.radians(Y-la)/2)**2+coslat*np.cos(np.radians(la))*np.sin(np.radians((X-lo+180)%360-180)/2)**2,0,1)))
    nearby=(d<400)&land
    ups=nearby&(elevation_land>elevation_land[y,x]+200)
    w=np.exp(-(d/220)**2)*area
    supply=float(np.average(rain[nearby],weights=w[nearby]))
    upland=float(np.average(rain[ups],weights=w[ups])) if ups.any() else 0
    # A low rating is not grounds to delete a field; local deep flow paths and meltwater are unresolved.
    screen=.55*supply+.45*upland
    cls="относительно благоприятная" if screen>=.43 else "умеренная" if screen>=.22 else "ограниченная; нужен отдельный расчёт подпитки"
    fields.append({"id":p["id"],"class":cls,"screen_index":round(screen*100),"nearby_ice_share_pct":pct(glacier,nearby),
                   "note":"Окно 400 км: условный потенциал осадков и вышележащих склонов; не установленная площадь водосбора или дебит."})
# Report catchment screening at the established outlet points without renaming or rerouting rivers.
river_summary=[]
for f in hydro:
    if f["properties"]["category"]!="river":continue
    p=f["properties"]; y,x=index_at(f["geometry"]["coordinates"][-1])
    # Select a nearby same-continent modeled coastal outlet, because the new coarse land mask
    # may differ by a cell. Label unmatched cases explicitly rather than invent a catchment.
    cand=[]
    for dy in range(-2,3):
        for dx in range(-2,3):
            yy=y+dy;xx=(x+dx)%W
            if 0<=yy<H and cont[yy,xx]==p["continent"] and parent[yy*W+xx]==-1:
                cand.append((dx*dx+dy*dy,-acc[yy*W+xx],yy*W+xx))
    if not cand:
        river_summary.append({"id":p["id"],"continent":p["continent"],"matched":False});continue
    _,_,i=min(cand)
    river_summary.append({"id":p["id"],"continent":p["continent"],"matched":True,"model_catchment_km2":int(round(catch[i],-3)),
                          "relative_yield_index":round(100*acc[i]/max(acc[outlets]),1)})
summary={}
for n in range(1,9):
    m=(cont==n);assert m.any()
    summary[str(n)]={"land_area_million_km2":round(float(area[m].sum()/1e6),2),
                    "precip_index_mean":round(float(np.average(rain[m],weights=area[m])*100),1),
                    "water_index_mean":round(float(np.average(water[m],weights=area[m])*100),1),
                    "agriculture_pct_by_class":[pct(ag==i,m) for i in range(1,6)],
                    "open_candidates_million_km2":round(float(area[m&((ag==1)|(ag==2))].sum()/1e6),2)}
site_summary=[]
for f in nodes:
    p=f["properties"];y,x=index_at(f["geometry"]["coordinates"])
    near=(np.abs(Y-lat[y])<2.2)&(np.abs((X-lon[x]+180)%360-180)*np.maximum(coslat,.10)<2.2)&land
    assert near.any(),p["id"]
    site_summary.append({"id":p["id"],"precip_index":round(100*float(rain[y,x])),
                     "water_index":round(100*float(water[y,x])),"near_open_candidate_pct":pct((ag==1)|(ag==2),near),
                     "resource_class":int(res[y,x])})
PALETTES={
 "precipitation":["#35546a","#e1d1ac","#c3c9ab","#93bcb2","#438eaa","#185175"],
 "waterResources":["#35546a","#dcc69e","#b9c9a6","#7fb9b9","#3a91bc","#174b78","#c8dce9"],
 "mineralResources":["#35546a","#c5c2b7","#b88b6c","#cb5b4b","#8259ad","#d7a94b","#709dad","#32bdad"],
 "agriculture":["#35546a","#4d9263","#acc45f","#d3bd7e","#a8adb4","#e2e8ee"]
}
LABELS={
 "precipitation":["Океан","Очень мало","Мало","Умеренно","Много","Наибольшие значения"],
 "waterResources":["Океан","Очень ограниченный","Ограниченный","Средний","Повышенный","Наибольший","Ледник: запас, не доступная вода"],
 "mineralResources":["Океан / нет схемы","Суша: не дифференцировано","Устойчивые массивы: Fe–V (условно)","Дуги: Cu","Рифт: Ni–Cu, Fe–V, Cr","Горные пояса: поднятые руды (условно)","Холодные осадки: метановые гидраты","Океанический рифт: Cu-сульфиды (гипотеза)"],
 "agriculture":["Океан","Лучший кандидат открытых полей","Ограниченное сезонное земледелие","Пастбища / промысловая тундра","Преимущественно защищённое хозяйство","Ледник / высокий крутой рельеф"]
}
grids={"precipitation":pr,"waterResources":wa,"mineralResources":res,"agriculture":ag}
total=[pct(ag==i) for i in range(1,6)]
result={"status":"proposed","year":3290,"projection":"equirectangular","grid":[W,H],
        "description":"Relative scenario; no measured precipitation, actual freshwater withdrawals, ore reserves or cropland. Class 0 denotes ocean / no regional classification; mineral prospects are geological environments.",
        "layers":{k:{"runs":rle(a),"colors":PALETTES[k],"labels":LABELS[k]} for k,a in grids.items()},
        "summary":{"land_area_million_km2":round(float(area[land].sum()/1e6),2),"agriculture_pct_by_class":total,
                   "open_best_million_km2":round(float(area[ag==1].sum()/1e6),2),
                   "open_conditional_million_km2":round(float(area[ag==2].sum()/1e6),2),
                   "precip_index_reference":"Logarithmic display index, 99th percentile of editorial modeled land precipitation = 100, capped; relative only",
                   "runoff_mass_balance_error":float(abs(acc[outlets].sum()-np.sum(runoff*area))),
                   "continents":summary,"thermal_fields":fields,"settlement_nodes":site_summary,"river_outlets":river_summary}}
for k,v in result["layers"].items():assert sum(n for _,n in v["runs"])==W*H and max(c for c,_ in v["runs"])<len(v["colors"])
(ROOT/"regional_draft.json").write_text(json.dumps(result,ensure_ascii=False,separators=(",",":"))+"\n")
(ROOT/"regional_summary.json").write_text(json.dumps(result["summary"],ensure_ascii=False,indent=2)+"\n")
print(json.dumps(result["summary"]|{"settlement_nodes":"...","river_outlets":"..."},ensure_ascii=False,indent=2))
# Static companion maps, each same grid, equal-area display via transformed latitude.
titles={"precipitation":"Осадки · относительный годовой потенциал","waterResources":"Поверхностная водообеспеченность · относительный индекс",
        "mineralResources":"Геологические районы · перспективы ресурсов","agriculture":"Сельское хозяйство · природная пригодность"}
for k,a in grids.items():
    fig,ax=plt.subplots(figsize=(16,8.1),facecolor="#111f2b")
    fig.subplots_adjust(left=.055,right=.975,top=.86,bottom=.23)
    ax.set_facecolor("#35546a")
    edges_x=np.linspace(-180,180,W+1);edges_y=90*np.sin(np.radians(np.linspace(90,-90,H+1)))
    ax.pcolormesh(edges_x,edges_y,a,cmap=ListedColormap(PALETTES[k]),vmin=-.5,vmax=len(PALETTES[k])-.5,shading="flat",rasterized=True)
    for f in base["features"]:
        if f["properties"]["category"] not in ("continent","island"):continue
        c=np.array(f["geometry"]["coordinates"][0]);ax.plot(c[:,0],90*np.sin(np.radians(c[:,1])),color="#263b49",lw=.55)
    if k in ("waterResources","agriculture"):
        for f in riverfeatures:
            c=np.array(f["geometry"]["coordinates"]);ax.plot(c[:,0],90*np.sin(np.radians(c[:,1])),color="#1c6089",lw=.5 if f["properties"]["category"]=="tributary" else .8,alpha=.8)
    if k in ("waterResources","agriculture"):
        for f in features("geothermal"):
            lo,la=f["geometry"]["coordinates"];ax.scatter(lo,90*np.sin(np.radians(la)),s=30,marker="D",color="#ffba56",edgecolor="#30272c",lw=.65,zorder=5)
    if k=="agriculture":
        for f in nodes:
            lo,la=f["geometry"]["coordinates"];ax.scatter(lo,90*np.sin(np.radians(la)),s=17,color="#8a3452",edgecolor="#ffffff",lw=.4,zorder=5)
    if k=="mineralResources":
        for f in features("convergence","continental_rift","ocean_rift","transform"):
            c=np.array(f["geometry"]["coordinates"]);ax.plot(c[:,0],90*np.sin(np.radians(c[:,1])),color="#f4e3c2",lw=.95,ls="--",alpha=.85)
    if k=="precipitation":
        # Direction of prevailing zonal flow assumption, not real monthly winds.
        for la,sign in [(-46,-1),(-12,1),(12,1),(46,-1)]:
            for lo in [-175,-54,106]:
                ax.annotate("",xy=(lo+sign*9,90*np.sin(np.radians(la))),xytext=(lo,90*np.sin(np.radians(la))),arrowprops=dict(arrowstyle="->",color="#fff0d4",lw=1.15),zorder=7)
    for i,(lo,la) in enumerate([(-122,58),(-30,68),(31,69),(94,69),(154,68),(-127,-34),(52,-22),(146,-37)],1):
        ax.text(lo,90*np.sin(np.radians(la)),str(i),fontsize=9,fontweight="bold",ha="center",va="center",
                bbox=dict(boxstyle="circle,pad=.24",fc="#f9f3dc",ec="#3d5662",lw=.7),zorder=10,color="#253644")
    ax.set_xlim(-180,180);ax.set_ylim(-90,90)
    ax.set_xticks(np.arange(-180,181,60));ax.set_yticks(90*np.sin(np.radians([-90,-60,-30,0,30,60,90])),["90° ю.ш.","60° ю.ш.","30° ю.ш.","0°","30° с.ш.","60° с.ш.","90° с.ш."])
    ax.tick_params(colors="#bfd1db",labelsize=8,length=0)
    ax.grid(color="#e8eeee",alpha=.18,ls=":",lw=.6)
    for s in ax.spines.values():s.set_color("#597281")
    fig.text(.055,.95,"БОРЕЯ · 3290",color="#f0f2ec",fontsize=24,fontweight="bold",ha="left")
    fig.text(.055,.897,titles[k],color="#b9d3e1",fontsize=14,ha="left")
    handles=[Patch(facecolor=c,label=l,edgecolor="#10202b") for c,l in zip(PALETTES[k],LABELS[k])]
    if k in ("waterResources","agriculture"):handles += [Line2D([0],[0],color="#12659d",lw=1.6,label="Рабочие реки"),Line2D([0],[0],marker="D",color="none",markerfacecolor="#ffba56",markeredgecolor="#30272c",label="Тепловые поля")]
    if k=="agriculture":handles += [Line2D([0],[0],marker="o",color="none",markerfacecolor="#8a3452",markeredgecolor="white",label="Узлы расселения")]
    ax.legend(handles=handles,ncol=3 if k=="mineralResources" else 4,loc="upper left",bbox_to_anchor=(0,-.10),frameon=False,labelcolor="#d2e1e7",fontsize=9,columnspacing=1.3,handlelength=1.2)
    note={"precipitation":"Логарифмический индекс 0–100 относительно модели; преобладают снег и таяние. Стрелки — гипотеза о зональном переносе.",
          "waterResources":"Реки из прежнего слоя. Оценка стока, доступности и сезонного таяния; не дебиты, не запасы водоносных горизонтов.",
          "mineralResources":"Типы благоприятных геологических обстановок. Не месторождения, объёмы, содержание руд или современная добыча.",
          "agriculture":"Поля — только кандидаты по низкому рельефу, оттепели, воде и риску; площади не равны фактической пашне."}[k]
    fig.text(.055,.035,note,color="#a7becb",fontsize=10)
    fig.savefig(ROOT/("regional_"+k+".png"),dpi=120,facecolor=fig.get_facecolor())
    plt.close(fig)
