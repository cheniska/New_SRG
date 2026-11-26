"""Schematic Borea climate layer (not a climate simulation).
Run beside continents_draft.geojson and tectonics_draft.geojson.
Requires numpy, matplotlib and Pillow.
"""
from pathlib import Path
import json
import numpy as np
import matplotlib.pyplot as plt
from matplotlib.path import Path as MplPath
from matplotlib.patches import Polygon, Patch
from matplotlib.lines import Line2D

root=Path(__file__).resolve().parent
continents=json.loads((root/'continents_draft.geojson').read_text())
tectonics=json.loads((root/'tectonics_draft.geojson').read_text())
land_features=[f for f in continents['features'] if f['properties']['category'] in ('continent','island')]
north=next(f for f in continents['features'] if f['properties']['category']=='schematic_northern_perennial_ice_extent')
def project(coords):
    p=np.asarray(coords,float)
    return np.c_[p[:,0],90*np.sin(np.radians(p[:,1]))]
def xy(lon,lat): return lon,90*np.sin(np.radians(lat))
# Equal-area cells; area ratios computed from masks, not from latitude-grid counts.
nx,ny=1440,720
lon=np.linspace(-180+180/nx,180-180/nx,nx)
y=np.linspace(-90+90/ny,90-90/ny,ny)
xx,yy=np.meshgrid(lon,y)
lat=np.degrees(np.arcsin(yy/90))
points=np.c_[xx.ravel(),yy.ravel()]
land=np.zeros((ny,nx),bool)
for f in land_features:
    land |= MplPath(project(f['geometry']['coordinates'][0])).contains_points(points).reshape(ny,nx)
north_mask=MplPath(project(north['geometry']['coordinates'][0])).contains_points(points).reshape(ny,nx)
# Basin-shaped envelopes of potential local seasonal thaw. The northward extension
# in the western sea corresponds to its low-latitude shores, not permanent open sea.
basins=[
    {'id':'I','name':'Экваториальное море между 6 и 7','center':[-44,0], 'axes':[43,18], 'phase':0.8},
    {'id':'II','name':'Море между 1 и 6','center':[-119,13], 'axes':[49,11], 'phase':2.0},
    {'id':'III','name':'Восточный океанический бассейн','center':[122,0], 'axes':[41,18], 'phase':3.3},
    {'id':'IV','name':'Западный океанический сектор','center':[178,-3], 'axes':[26,14], 'phase':4.6},
]
scores=[]
for b in basins:
    dx=(xx-b['center'][0]+180)%360-180
    a=dx/b['axes'][0]
    u=(lat-b['center'][1])/b['axes'][1]
    theta=np.arctan2(u,a)
    irregular=1+0.085*np.sin(3*theta+b['phase'])+0.045*np.sin(5*theta-b['phase'])
    scores.append((a*a+u*u)/(irregular*irregular))
score=np.min(scores,axis=0)
opening=(score<=1)&~land&~north_mask
ocean=~land
rate=100*opening.sum()/ocean.sum()
# Land classes, deliberately schematic. Coasts and relief perturb climate locally.
clim=np.zeros((ny,nx),int)
abs_lat=np.abs(lat)
# The first zone is not warm everywhere; it denotes the most favourable latitudes.
clim[abs_lat>18]=1
clim[abs_lat>38]=2
clim[abs_lat>65]=3
# Mountain layer marks local cold/precipitation effects, not the full snowline.
# Colors: land climate 0-3, perennial sea ice 4, seasonal opening envelope 5.
arr=np.where(land,clim,np.where(opening,5,4))
# A visibly distinct cold polar terrestrial region; not a claim that it is all glacier.
palette=['#a7b17a','#d2bf94','#b9c5cd','#e8e4e0','#9fc5d6','#32627a']
from matplotlib.colors import ListedColormap,BoundaryNorm
cmap=ListedColormap(palette)
norm=BoundaryNorm(np.arange(-.5,6,.999999),6)
plt.rcParams['font.family']='DejaVu Sans'
fig=plt.figure(figsize=(16.4,10.2),dpi=150,facecolor='#101c2a')
fig.text(.055,.949,'БОРЕЯ  /  КЛИМАТ И МОРЯ',fontsize=29,weight='bold',va='top',color='#f4f6f4')
fig.text(.055,.897,'Рабочая природная карта • 3290 год • северный ледовый регион: 1–5',fontsize=13.3,va='top',color='#aec6d5')
ax=fig.add_axes([.055,.285,.89,.56])
ax.imshow(arr,cmap=cmap,norm=norm,extent=(-180,180,-90,90),origin='lower',interpolation='nearest',aspect='auto')
for f in land_features:
    ax.add_patch(Polygon(project(f['geometry']['coordinates'][0]),facecolor='none',edgecolor='#425a66',linewidth=1.05 if f['properties']['category']=='continent' else .45,zorder=3))
# Wind directions are an illustrative reversed simple planetary circulation.
for lon0 in (-153,-54,40,140):
    for lat0 in (-12,12):
        ax.annotate('',xy=xy(lon0+15,lat0-3*np.sign(lat0)),xytext=xy(lon0-10,lat0+4*np.sign(lat0)),arrowprops=dict(arrowstyle='-|>',color='#ecf4f6',lw=1.1,alpha=.82,shrinkA=1,shrinkB=1),zorder=5)
for lon0 in (-143,-40,62,144):
    for lat0 in (-44,44):
        ax.annotate('',xy=xy(lon0-12,lat0+4*np.sign(lat0)),xytext=xy(lon0+12,lat0-3*np.sign(lat0)),arrowprops=dict(arrowstyle='-|>',color='#4b6373',lw=1.1,alpha=.72,shrinkA=1,shrinkB=1),zorder=5)
for lat0 in (-60,-30,0,30,60): ax.axhline(xy(0,lat0)[1],lw=.65,color='#39596b',alpha=.48,ls='--' if lat0==0 else ':',zorder=2)
for lon0 in (-120,-60,0,60,120): ax.axvline(lon0,lw=.55,color='#39596b',alpha=.42,ls=':',zorder=2)
for f in tectonics['features']:
    cat=f['properties']['category']
    if cat in ('mountain_belt','old_mountain_belt'):
        p=project(f['geometry']['coordinates'])
        ax.plot(p[:,0],p[:,1],color='#8f7261',lw=5,solid_capstyle='round',alpha=.88,zorder=4)
        ax.plot(p[:,0],p[:,1],color='#f6efe0',lw=1.25,solid_capstyle='round',zorder=5)
    elif cat=='geothermal':
        ax.scatter(*xy(*f['geometry']['coordinates']),s=59,marker='D',facecolor='#ffbd50',edgecolor='#694c35',lw=.85,zorder=7)
for i,(a,b) in enumerate([(-122,59),(-30,68),(31,69),(94,69),(154,68),(-127,-34),(52,-22),(146,-38)],1):
    ax.text(*xy(a,b),str(i),ha='center',va='center',fontsize=11,weight='bold',color='#253d4b',zorder=8,bbox=dict(boxstyle='circle,pad=.26',fc='#f2f1e8',ec='#526774',lw=.85))
# Marine labels placed inside ocean portions, not on continents.
for title,p in [('I',(-50,-5)),('II',(-119,19)),('III',(121,0)),('IV',(172,0))]:
    ax.text(*xy(*p),title,ha='center',va='center',fontsize=12,weight='bold',color='#f5fbfe',zorder=9,bbox=dict(boxstyle='round,pad=.28',fc='#294f65',ec='#b1d2df',lw=.7))
ax.set_xlim(-180,180);ax.set_ylim(-90,90)
ax.set_xticks([-180,-120,-60,0,60,120,180]);ax.set_xticklabels(['180°','120° з.д.','60° з.д.','0°','60° в.д.','120° в.д.','180°'],fontsize=9,color='#b4cbd6')
ticks=[-90,-60,-30,0,30,60,90];ax.set_yticks([xy(0,l)[1] for l in ticks]);ax.set_yticklabels(['90° ю.ш.','60° ю.ш.','30° ю.ш.','0°','30° с.ш.','60° с.ш.','90° с.ш.'],fontsize=9,color='#b4cbd6');ax.tick_params(length=0,pad=5)
for sp in ax.spines.values():sp.set_color('#7795a6');sp.set_linewidth(.85)
handles=[Patch(facecolor=palette[0],label='Наиболее мягкая зона суши'),Patch(facecolor=palette[1],label='Холодная сезонная суша'),Patch(facecolor=palette[2],label='Тундра, мерзлота'),Patch(facecolor=palette[3],label='Полярная суша'),Patch(facecolor=palette[4],label='Преимущественно многолетний морской лёд'),Patch(facecolor=palette[5],edgecolor='#8eb5c8',label='Возможное сезонное открытие моря'),Line2D([0],[0],color='#8f7261',lw=5,label='Горные системы'),Line2D([0],[0],marker='D',markerfacecolor='#ffbd50',markeredgecolor='#694c35',ls='',markersize=8,label='Геотермальные поля'),Line2D([0],[0],color='#b4cbd6',marker='>',lw=1.4,label='Условное направление ветра')]
fig.legend(handles=handles,ncol=3,frameon=False,loc='lower left',bbox_to_anchor=(.05,.145),labelcolor='#e6ecee',fontsize=10,columnspacing=1.5,handlelength=1.8,labelspacing=1.0)
fig.text(.055,.118,'I — экваториальное море 6–7     II — море 1–6     III — восточный бассейн     IV — западный сектор',fontsize=11,color='#dbe7ed')
fig.text(.055,.080,f'Около {rate:.0f}% океана попадает в область возможного сезонного открытия; вода открывается не везде одновременно.',fontsize=10.3,color='#e7cfa7')
fig.text(.055,.044,'Контуры условны. Осадки, высотные границы ледников, динамика льда и течения требуют дальнейшего уточнения.',fontsize=9.4,color='#a4bccb')
out=root/'climate_draft.png'
fig.savefig(out,dpi=150,facecolor=fig.get_facecolor())
from PIL import Image
Image.open(out).convert('RGB').quantize(colors=192,dither=Image.Dither.NONE).save(out,optimize=True)
print('Land fraction:',land.mean(),'Ocean fraction:',ocean.mean(),'Seasonal opening envelope fraction of ocean:',rate,'Area million km2:',opening.mean()*862)
for b,s in zip(basins,scores):
    print(b['id'],100*((s<=1)&ocean&~north_mask).sum()/ocean.sum())
params={'status':'proposed','year':3290,'projection':'cylindrical equal-area; y=90*sin(latitude)','basis':['continents_draft.geojson','tectonics_draft.geojson'],'land_zones_abs_lat_degrees':[18,38,65],'potential_seasonal_opening_basins':basins,'seasonal_preview_parameters':{'spring':{'bias':.48,'hem':-.12},'summer':{'bias':.72,'hem':.25},'autumn':{'bias':.77,'hem':.11},'winter':{'bias':.61,'hem':-.27}},'potential_opening_envelope_ocean_percent':round(rate,1),'potential_opening_envelope_million_km2':round(opening.mean()*862,1),'notes':['Not a simulation; no instantaneous ice-free area or fixed annual-min/max implied.','Exclude land and the approved northern perennial ice region.','Wind arrows are an illustrative west-to-east trade-wind analog and east-to-west midlatitude analog for retrograde rotation; actual circulation remains open.','Polar land class does not imply complete coverage by continental glaciers. Geothermal markers do not depict areas of warm climate.']}
(root/'climate_draft.json').write_text(json.dumps(params,ensure_ascii=False,indent=2)+'\n')
