"""Render the accepted data, never regenerate coordinate polygons."""
from pathlib import Path
import json,numpy as np
from PIL import Image
import matplotlib.pyplot as plt
from matplotlib.patches import Polygon,Patch
from matplotlib import patheffects
root=Path(__file__).resolve().parent
d=json.loads((root/'continents_draft.geojson').read_text());features=d['features']
def project(p):
 a=np.array(p);return np.c_[a[:,0],np.sin(np.radians(a[:,1]))*90]
plt.rcParams['font.family']='DejaVu Sans'
fig=plt.figure(figsize=(15,9.2),facecolor='#101c2a',dpi=150)
fig.text(.057,.95,'БОРЕЯ',color='#f5f6f4',fontsize=30,weight='bold',va='top')
fig.text(.057,.891,'Согласованные очертания материков  •  3290 год',color='#a6c3d2',fontsize=14,va='top')
fig.text(.94,.944,'8 материков\n≈ 40% суши  /  ≈ 60% океана',ha='right',va='top',color='#e3e9e8',fontsize=13,linespacing=1.5)
ax=fig.add_axes([.055,.225,.89,.615]);ax.set_facecolor('#36566c');ax.set_xlim(-180,180);ax.set_ylim(-90,90);ax.set_aspect('auto')
for f in features:
 if f['properties']['category']=='schematic_northern_perennial_ice_extent':ax.add_patch(Polygon(project(f['geometry']['coordinates'][0]),fc='#b6d9e2',ec='#e0f7ff',linestyle=(0,(5,3)),lw=.9,zorder=1))
for lon in [-120,-60,0,60,120]:ax.axvline(lon,color='#c7d8df',lw=.65,ls=':',alpha=.58,zorder=2)
for lat in [-60,-30,0,30,60]:ax.axhline(np.sin(np.radians(lat))*90,color='#d9e5ea',lw=1.1 if lat==0 else .65,ls='--' if lat==0 else ':',alpha=.74,zorder=2)
for f in features:
 category=f['properties']['category']
 if category not in ('continent','island'):continue
 ax.add_patch(Polygon(project(f['geometry']['coordinates'][0]),closed=True,fc='#dad6b9' if category=='continent' else '#c4c3aa',ec='#162d3d',lw=1.35 if category=='continent' else .65,zorder=3,joinstyle='round'))
label_lonlat=[(-122,47),(-28,66),(29,63),(94,64),(153,64),(-117,-24),(42,-21),(152,-33)]
for i,(lon,lat) in enumerate(label_lonlat,1):ax.text(lon,np.sin(np.radians(lat))*90,str(i),ha='center',va='center',fontsize=15,weight='bold',color='#142938',zorder=5,bbox=dict(boxstyle='circle,pad=.33',fc='#f5eee0',ec='#294351',lw=1.2))
fig.text(.5,.85,'СЕВЕРНЫЙ ЛЕДОВЫЙ МАССИВ',ha='center',va='bottom',fontsize=11,weight='bold',color='#c0dfe7')
ax.text(-42,7,'Экваториальное\nморе',ha='center',va='center',fontsize=10,color='#eff8fb',zorder=5,path_effects=[patheffects.withStroke(linewidth=2.5,foreground='#36566c')])
ax.set_xticks([-180,-120,-60,0,60,120,180]);ax.set_xticklabels(['180°','120° з.д.','60° з.д.','0°','60° в.д.','120° в.д.','180°'],fontsize=9,color='#b4cbd6')
latitudes=[-90,-60,-30,0,30,60,90];ax.set_yticks([np.sin(np.radians(x))*90 for x in latitudes]);ax.set_yticklabels(['90° ю.ш.','60° ю.ш.','30° ю.ш.','0°','30° с.ш.','60° с.ш.','90° с.ш.'],fontsize=9,color='#b4cbd6');ax.tick_params(length=0,pad=6)
for sp in ax.spines.values():sp.set_color('#7795a6');sp.set_linewidth(.85)
handles=[Patch(fc='#dad6b9',ec='#162d3d',label='Материковая суша'),Patch(fc='#b6d9e2',ec='#e0f7ff',label='Северный многолетний морской лёд'),Patch(fc='#36566c',ec='#7795a6',label='Океан (прочие льды не нанесены)')]
fig.legend(handles=handles,frameon=False,ncol=3,loc='lower left',bbox_to_anchor=(.052,.154),labelcolor='#e7eceb',fontsize=12,columnspacing=2.3,handlelength=1.45)
fig.text(.057,.12,'1 — крупный северный     2–5 — малые северные',fontsize=12.5,color='#f0eff0',weight='bold')
fig.text(.057,.085,'6–7 — крупные материки экваториальных и южных широт     8 — малый южный',fontsize=12.5,color='#f0eff0')
fig.text(.057,.037,'Новые очертания 2–5 согласованы; координаты региональных объектов остаются рабочими. Равновеликая проекция.',color='#a6c3d2',fontsize=9.5)
p=root/'continents_draft.png';fig.savefig(p,dpi=150,facecolor=fig.get_facecolor());plt.close(fig);Image.open(p).convert('RGB').quantize(colors=192,dither=Image.Dither.NONE).save(p,optimize=True)
