"""Working, reproducible Borea map routing program.
Input: equirectangular JS relief export (model_grid.json, model_height/hazard/land.bin), current GeoJSON. Output: vector proposals.
No flows in m3/s, no exact river widths or road alignments inferred from this coarse grid."""
from pathlib import Path
from collections import defaultdict
import json, heapq, math
import numpy as np
from scipy import ndimage
from matplotlib.path import Path as MPath
root=Path(__file__).resolve().parent
base=json.loads((root/'continents_draft.geojson').read_text())
nodes=json.loads((root/'settlements_draft.geojson').read_text())['features']
grid=json.loads((root/'model_grid.json').read_text());W0,H0=grid['W'],grid['H'];W,H=W0//2,H0//2
z0=np.fromfile(root/'model_height.bin',dtype=np.float32).reshape(H0,W0)
risk0=np.fromfile(root/'model_hazard.bin',dtype=np.float32).reshape(H0,W0)
mask0=np.fromfile(root/'model_land.bin',dtype=np.uint8).reshape(H0,W0)
def avg(a):return a.reshape(H,2,W,2).mean(axis=(1,3))
z=avg(z0);risk=avg(risk0);lon=-180+(np.arange(W)+.5)*360/W;lat=90-(np.arange(H)+.5)*180/H
xx,yy=np.meshgrid(lon,lat);pts=np.c_[xx.ravel(),yy.ravel()]
cont=np.zeros((H,W),np.int16);paths={}
for f in base['features']:
 p=f['properties']
 if p['category']!='continent':continue
 n=p['number'];path=MPath(f['geometry']['coordinates'][0]);paths[n]=path
 cont[path.contains_points(pts).reshape(H,W)]=n
land=cont>0;z=np.where(land,np.maximum(20,z),0);N=W*H
metric=np.maximum(.12,np.cos(np.radians(lat)))
def idx(x,y):return y*W+x
def xy(i):y,x=divmod(int(i),W);return [round(float(lon[x]),3),round(float(lat[y]),3)]
def nbs(i,n,only_land=True):
 y,x=divmod(i,W)
 for dy,dx in ((0,-1),(0,1),(-1,0),(1,0),(-1,-1),(1,-1),(-1,1),(1,1)):
  jx=(x+dx)%W;jy=y+dy
  if not(0<=jy<H):continue
  if only_land and cont[jy,jx]!=n:continue
  if dx and dy and only_land and (cont[y,jx]!=n or cont[jy,x]!=n):continue
  yield idx(jx,jy),math.hypot(dx*(metric[y]+metric[jy])*.5,dy)
# Priority flood routing. Parent links point toward a coast and strictly decrease filled height.
flat=z.ravel();haz=risk.ravel();cf=cont.ravel();filled=np.full(N,np.inf);parent=np.full(N,-2,np.int32)
coast=[];heap=[]
for y,x in zip(*np.where(land)):
 i=idx(x,y);n=cont[y,x]
 if any(cf[j]!=n for j,_ in nbs(i,n,False)):
  filled[i]=flat[i];parent[i]=-1;heapq.heappush(heap,(float(flat[i]),i));coast.append(i)
while heap:
 h,i=heapq.heappop(heap);n=cf[i]
 for j,dist in nbs(i,n):
  if parent[j]!=-2:continue
  filled[j]=max(float(flat[j]),h+.03);parent[j]=i;heapq.heappush(heap,(filled[j],j))
assert np.all(parent[land.ravel()]>=-1)
coastdist=ndimage.distance_transform_edt(land)
# Relative precipitation/runoff potential: humid low-latitude coastal belts, snow accumulation in mountains,
# continental/polar interiors drier. No absolute precipitation or transport discharge inferred.
moist=(.14+.56*np.exp(-(np.abs(yy)/37)**1.5)+.22*np.exp(-coastdist/28)+.25*np.clip(z/2600,0,1))*np.cos(np.radians(yy))
acc=np.where(land,moist,0).ravel();rank=np.argsort(filled[land.ravel()]);inds=np.flatnonzero(land.ravel())[rank[::-1]]
bestchild=np.full(N,-1,np.int32)
for i in inds:
 p=parent[i]
 if p<0:continue
 acc[p]+=acc[i]
 if bestchild[p]<0 or acc[i]>acc[bestchild[p]]:bestchild[p]=i
def chain(out):
 a=[int(out)]
 while bestchild[a[-1]]>=0:a.append(int(bestchild[a[-1]]))
 return a[::-1]
def simplify(a):
 if len(a)<3:return a
 q=[a[0]]
 for i in range(1,len(a)-1):
  ay,ax=divmod(a[i-1],W);by,bx=divmod(a[i],W);cy,cx=divmod(a[i+1],W)
  if (by-ay,bx-ax)!=(cy-by,cx-bx):q.append(a[i])
 q.append(a[-1]);return q
def valid_segment(a,b,n):
 x=np.linspace(a[0],b[0],max(3,int(math.hypot(b[0]-a[0],b[1]-a[1])/.11)+2));y=np.linspace(a[1],b[1],len(x))
 return paths[n].contains_points(np.c_[x,y],radius=.0001).all()
def valid_line(coords,n):return all(valid_segment(a,b,n) for a,b in zip(coords,coords[1:]))
def line(cat,coords,**p):return {'type':'Feature','properties':{'category':cat,'status':'proposed',**p},'geometry':{'type':'LineString','coordinates':coords}}
def point(cat,c,**p):return {'type':'Feature','properties':{'category':cat,'status':'proposed',**p},'geometry':{'type':'Point','coordinates':c}}
hydro=[];rivers=[];used=set()
limits={1:6,2:2,3:2,4:2,5:2,6:8,7:9,8:4}
for n in range(1,9):
 selected=[]
 for out in sorted((i for i in coast if cf[i]==n),key=lambda i:acc[i],reverse=True):
  if len(selected)>=limits[n]:break
  a=chain(out)
  if len(a)<8:continue
  ox,oy=xy(out)
  if any(math.hypot((ox-x)*math.cos(math.radians(oy)),oy-y)<4.0 for x,y in selected):continue
  coords=[xy(i) for i in simplify(a)]
  if not valid_line(coords,n):continue
  id=f'R{len(rivers)+1:02}';selected.append((ox,oy));rivers.append((a,id,n))
  hydro.append(line('river',coords,id=id,continent=int(n),kind='main',source_height_m=int(flat[a[0]]),outlet_height_m=int(flat[a[-1]]),flow_index=round(float(acc[out]),2),note='Weighted drainage path on synthetic elevation. Outlet is nearest coastal grid land cell, not exact coastline.'))
  used.update(a)
# A few larger tributaries, traced to junction on a main channel, excluding duplicated main segments.
for a,rid,n in rivers:
 cand=[]
 for i in a[3:]:
  for j,dist in nbs(i,n):
   if parent[j]==i and j not in used and acc[j]>.20*acc[a[-1]]:cand.append((acc[j],j,i))
 for score,j,junction in sorted(cand,reverse=True)[:3]:
  branch=chain(j)+[junction]
  if len(branch)<7 or any(i in used for i in branch[:-1]):continue
  coords=[xy(i) for i in simplify(branch)]
  if not valid_line(coords,n):continue
  id=f'T{sum(f["properties"]["category"]=="tributary" for f in hydro)+1:02}'
  hydro.append(line('tributary',coords,id=id,continent=int(n),joins=rid,kind='tributary',flow_index=round(float(score),2),note='Conditional tributary, same drainage model.'))
  used.update(branch)
# Depressions large enough to warrant investigating lakes. These are proposed water bodies, not actual
# extents: local retention, inflow and evaporative budget remain unresolved.
dep=np.zeros(N);dep[land.ravel()]=filled[land.ravel()]-flat[land.ravel()]
dep=dep.reshape(H,W);lakegrid=np.zeros((H,W),bool);lakelist=[]
for n in range(1,9):
 labeled,count=ndimage.label((cont==n)&(dep>65)&(z<3500),np.ones((3,3),np.uint8))
 cands=[]
 for label in range(1,count+1):
  ys,xs=np.where(labeled==label)
  if len(xs)<3:continue
  # Mark a center that actually lies in the depression.
  d=dep[ys,xs];i=int(np.argmax(d));x=int(xs[i]);y=int(ys[i])
  w=float(np.sum(metric[ys]));score=w*math.sqrt(float(np.mean(d)))*(.4+.6*math.exp(-(abs(lat[y])/45)**2))
  cands.append((score,label,x,y,len(xs),w,float(np.max(d))))
 for score,label,x,y,count,w,deep in sorted(cands,reverse=True)[:(3 if n in (1,6,7) else 1)]:
  id=f'L{len(lakelist)+1:02}';rad=min(.62,max(.17,.13*math.sqrt(w)))
  # Avoid depicting all coarse depression cells as water: point symbol plus illustrative footprint.
  c=xy(idx(x,y));lakelist.append((c,rad));lakegrid|=(labeled==label)
  hydro.append(point('lake_candidate',c,id=id,continent=n,radius_deg=round(rad,2),height_m=int(z[y,x]),depression_depth_m=int(deep),note='Potential lake/impounded basin, symbolic extent only; water budget and actual shoreline not modeled.'))
# Route representative city connections (MST per continent and extra links in populous networks).
node_by={}
for f in nodes:
 p=f['properties'];n=p['continent'];c=f['geometry']['coordinates'];y=int((90-c[1])*H/180);x=int((c[0]+180)*W/360)
 found=[]
 for dy in range(-3,4):
  for dx in range(-3,4):
   yy0=y+dy;xx0=(x+dx)%W
   if 0<=yy0<H and cont[yy0,xx0]==n:
    i=idx(xx0,yy0)
    if valid_segment(c,xy(i),n):found.append((math.hypot(dx,dy),i))
 assert found,(p['id'],c)
 node_by[p['id']]=(f,min(found)[1])
def a_star(start,goal,n):
 q=[(0.,start)];cost={start:0.};back={};gy,gx=divmod(goal,W)
 def heuristic(i):
  y,x=divmod(i,W);dx=abs(x-gx);return math.hypot(min(dx,W-dx)*min(metric[y],metric[gy]),y-gy)
 while q:
  _,i=heapq.heappop(q)
  if i==goal:
   a=[i]
   while a[-1]!=start:a.append(back[a[-1]])
   return a[::-1]
  c=cost[i]
  for j,d in nbs(i,n):
   if flat[j]>4100 and j!=goal:continue
   # Reach stable lowlands, pass around steep relief, avoid high hazard and lake depressions.
   step=d*(1+1.15*max(0,(flat[i]+flat[j])*.5-750)/1800+1.6*abs(flat[i]-flat[j])/750+4.2*max(haz[i],haz[j])**2+1.7*lakegrid.ravel()[j])
   v=c+step
   if v<cost.get(j,float('inf')):
    cost[j]=v;back[j]=i;heapq.heappush(q,(v+heuristic(j),j))
 return None
def dist_pts(a,b):
 x,y=a;u,v=b;return math.hypot((x-u)*math.cos(math.radians((y+v)/2)),y-v)
roads=[]
for n in range(1,9):
 ids=[id for id,(f,i) in node_by.items() if f['properties']['continent']==n]
 if len(ids)<2:continue
 edges=sorted((dist_pts(node_by[a][0]['geometry']['coordinates'],node_by[b][0]['geometry']['coordinates']),a,b) for j,a in enumerate(ids) for b in ids[j+1:])
 roots={i:i for i in ids}
 def rootof(i):
  while roots[i]!=i:i=roots[i]
  return i
 made=set();extras=2 if n in (6,7) else 1 if n==1 else 0
 def add(a,b):
  f,start=node_by[a];g,goal=node_by[b];path=a_star(start,goal,n)
  if path is None:return False
  coords=[f['geometry']['coordinates']]+[xy(i) for i in simplify(path)]+[g['geometry']['coordinates']]
  coords=[c for i,c in enumerate(coords) if i==0 or c!=coords[i-1]]
  if not valid_line(coords,n):return False
  kind='trunk' if f['properties']['kind']!='specialized' and g['properties']['kind']!='specialized' else 'regional'
  roads.append(line('transport_corridor',coords,id=f'D{len(roads)+1:02}',continent=n,from_node=a,to_node=b,kind=kind,note='Working on-land corridor; main trunk may combine paved road and electrified rail. No offshore ice roads or bridges inferred.'))
  made.add(frozenset((a,b)));return True
 for d,a,b in edges:
  if rootof(a)!=rootof(b) and add(a,b):roots[rootof(a)]=rootof(b)
 assert len({rootof(i) for i in ids})==1,(n,ids)
 for d,a,b in edges:
  if not extras:break
  if frozenset((a,b)) not in made and add(a,b):extras-=1
# Verify all routes on mainland and each D8 chain descending in filled DEM.
assert all(filled[parent[i]]<filled[i] for i in inds if parent[i]>=0)
assert all(valid_line(f['geometry']['coordinates'],f['properties']['continent']) for f in hydro+roads if f['geometry']['type']=='LineString')
out={'type':'FeatureCollection','name':'Borea_hydrology_transport_3290','properties':{'status':'proposed','grid':[W,H],'source':'Same synthetic DEM as atlas; 2x downsample. Priority flood drainage, coarse relative precipitation, candidate basin markers, terrain/risk-aware terrestrial routes.','notes':['River courses and outlet points schematic, not exact valleys or measured flows.','Lake symbols are candidate basins, not actual extents.','Land corridors connect candidate settlement nodes; no political boundaries, detailed road widths or seasonal maritime routes.']},'features':hydro+roads}
(root/'hydrology_transport_draft.geojson').write_text(json.dumps(out,ensure_ascii=False,separators=(',',':'))+'\n')
from collections import Counter
print(Counter(f['properties']['category'] for f in out['features']));print('route paths',sum(len(f['geometry']['coordinates']) for f in roads))
