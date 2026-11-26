'use strict';
(() => {
const $=id=>document.getElementById(id), data=globalThis.BOREA_DATA;
if(!data){$('error').hidden=false;return}
const stage=$('stage'), canvas=$('globe'), overlay=$('overlay'), ctxO=overlay.getContext('2d');
const layer=$('layer'), season=$('season'), grid=$('grid');
const PI=Math.PI, RAD=PI/180, TAU=2*PI, W=(typeof location!=='undefined'&&new URLSearchParams(location.search).get('resolution')==='high')?3072:1536,H=W/2;
const wrap=x=>((x+180)%360+360)%360-180, clamp=(x,a,b)=>Math.max(a,Math.min(b,x));
const land=data.continents.features.filter(f=>['continent','island'].includes(f.properties.category));
const north=data.continents.features.find(f=>f.properties.category==='schematic_northern_perennial_ice_extent');
const tect=data.tectonics.features, cl=data.climate,nodes=data.settlements?.features??[],fields=tect.filter(f=>f.properties.category==='geothermal'),summits=tect.filter(f=>f.properties.category==='high_peak'),plateLabels=tect.filter(f=>f.properties.category==='plate_label'),waterFeatures=data.hydrology?.features??[],lakeNodes=waterFeatures.filter(f=>f.properties.category==='lake_candidate');
const landLabels=[[-122,58],[-30,68],[31,69],[94,69],[154,68],[-127,-34],[52,-22],[146,-37]];
function surface(){const c=document.createElement('canvas');c.width=W;c.height=H;return c}
function xy(lon,lat){return[(lon+180)*W/360,(90-lat)*H/180]}
function path(ctx,pts,close=false){ctx.beginPath();pts.forEach(([lon,lat],i)=>{const [x,y]=xy(lon,lat);i?ctx.lineTo(x,y):ctx.moveTo(x,y)});if(close)ctx.closePath()}
const mask=surface(), mc=mask.getContext('2d',{willReadFrequently:true});
function makeMask(features){mc.clearRect(0,0,W,H);mc.fillStyle='#fff';for(const f of features){path(mc,f.geometry.coordinates[0],true);mc.fill()}return mc.getImageData(0,0,W,H).data}
const lm=makeMask(land), nm=makeMask([north]), isLand=new Uint8Array(W*H), isNorth=new Uint8Array(W*H), scores=new Float32Array(W*H), weights=new Float32Array(H), lats=new Float32Array(H);
let oceanWeight=0, potentialWeight=0;
for(let j=0;j<H;j++){let lat=90-(j+.5)*180/H;lats[j]=lat;weights[j]=Math.cos(lat*RAD);
 for(let i=0;i<W;i++){let k=j*W+i,lon=-180+(i+.5)*360/W;isLand[k]=lm[4*k+3]>127;isNorth[k]=nm[4*k+3]>127;if(isLand[k]){scores[k]=99;continue}
 oceanWeight+=weights[j]; if(isNorth[k]){scores[k]=99;continue}let best=99;
 for(const b of cl.potential_seasonal_opening_basins){const a=wrap(lon-b.center[0])/b.axes[0],u=(lat-b.center[1])/b.axes[1],theta=Math.atan2(u,a),ir=1+.085*Math.sin(3*theta+b.phase)+.045*Math.sin(5*theta-b.phase);
 best=Math.min(best,(a*a+u*u)/(ir*ir))}
 scores[k]=best;if(best<=1)potentialWeight+=weights[j];
 }}
const texture=surface(),tc=texture.getContext('2d',{willReadFrequently:true});
const C={land:'#d8d0b1',sea:'#365d74',north:'#b4d5df',mild:'#a7b17a',cold:'#d2bf94',tundra:'#b9c5cd',polar:'#e8e4e0',ice:'#9fc5d6',seasonal:'#d3e7ec',open:'#30647e',stable:'#8eb18e',low:'#e8e2a0',mount:'#957256',converge:'#f07150',rift:'#38ccea',riftLand:'#159cbf',transform:'#c99ce8',geo:'#ffbd50',snow:'#d7e0e4',rock:'#8d6668',water:'#254969',floe:'#a5c5d1',cloud:'#f2f5f7',deep:'#163963',slope:'#458eb0',shelf:'#addce1',plain:'#7ab47d',hill:'#d5cc84',upland:'#d4a05c',peak:'#a65c35',veryHigh:'#60311f',arc:'#dc7359',oasis:'#66c77a',majorCity:'#b43745',regionalCity:'#d88935',smallCity:'#7c619d',pop0:'#e8ebe8',pop1:'#cfdfda',pop2:'#a4c4be',pop3:'#eede8b',pop4:'#f4a752',pop5:'#d04f46',pop6:'#ad4158',pop7:'#8b2b5f',pop8:'#421853',plate:'#e9d8a8',glacier:'#8ebede',landSnow:'#e9efed',dryLand:'#b7ad91',seaPerennial:'#b1d3e0',seaSeason:'#dbebed',waterOpen:'#24597e',riverOpen:'#12659d',riverFrozen:'#4d7791',lakeIce:'#cbe5ef',oasisNatural:'#577e59',urban:'#777c82',road:'#53514f',light:'#ffe1a3',nightDark:'#172334',trunk:'#2e586c',regRoute:'#b0784c'};
const history=data.history??{}, historyFeatures=history.features??[], histModes=['ancestry','refugia','historyRegions','modernHeritage'];
const isHistorical=m=>histModes.includes(m)&&historyFeatures.length>0;
const regional={...(data.regional?.layers??{}),...(history.layers??{})}, regionalSummary=data.regional?.summary??{}, regionalDecoded={};
const historyGrid=m=>m==='ancestry'?history.grid:data.regional.grid;
const historyMarkers=m=>historyFeatures.filter(f=>f.geometry.type==='Point'&&f.properties.views?.includes(m));
const historyColors={bunker:'#8c429a',surface:'#d1803e',region:'#547e84',deradan:'#b83b4d',heritage_valley:'#c48a4b',heritage_plain:'#94af66',heritage_upland:'#729fca'};
Object.assign(C,historyColors,{deradanArea:'#af6270',heritageMixed:'#405368'});
const isRegional=m=>Object.hasOwn(regional,m);
C.regionalUnknown='#b7b4ac';
for(const [mode,layer] of Object.entries(regional))for(let i=0;i<layer.colors.length;i++)C[mode+i]=layer.colors[i];
const rgba=Object.fromEntries(Object.entries(C).map(([k,v])=>[k,[parseInt(v.slice(1,3),16),parseInt(v.slice(3,5),16),parseInt(v.slice(5,7),16)]]));
const legends={
 geography:[['land','Суша'],['sea','Океан'],['north','Северный ледовый регион']],
 tectonics:[['converge','Сближение плит'],['rift','Океанический рифт'],['riftLand','Континентальный рифт'],['transform','Сдвиговой пояс'],['mount','Горы'],['stable','Устойчивые массивы'],['low','Низменности'],['arc','Вулканическая дуга'],['geo','Тепловые поля'],['oasis','Кандидаты оазисов'],['veryHigh','Пики >8,85 км'],['plate','Предполагаемая граница плит']],
 climate:[['mild','Наиболее мягкая суша'],['cold','Холодная сезонная суша'],['tundra','Тундра / мерзлота'],['polar','Полярная суша'],['ice','Многолетний морской лёд'],['seasonal','Сезонный лёд'],['open','Открытие моря'],['geo','Геотермальные поля']],
 ice:[['land','Суша'],['ice','Многолетний лёд'],['seasonal','Сезонный лёд'],['open','Открытие моря'],['north','Северный ледовый регион']],
 relief:[['deep','−8,5…−6 км'],['slope','−6…−1,2 км'],['shelf','−1,2…0 км'],['plain','0…250 м'],['hill','250…750 м'],['upland','0,75…2,5 км'],['peak','2,5…5,5 км'],['veryHigh','5,5…10 км']],
 heat:[['oasis','Естественный оазис (кандидат)'],['geo','Промышленное тепловое поле'],['arc','Вулканическая дуга'],['converge','Конвергенция'],['riftLand','Рифт']],
 cities:[['majorCity','Крупнейший агломерационный узел'],['regionalCity','Региональный узел'],['smallCity','Специализированный узел'],['oasis','Тепловой оазис'],['geo','Промышленное поле']],
 population:[['pop0','0'],['pop1','0,01'],['pop2','0,1'],['pop3','0,5'],['pop4','2'],['pop5','8'],['pop6','25'],['pop7','60'],['pop8','100']],
 hydrology:[['dryLand','Суша без сплошного снега'],['landSnow','Сезонный снег'],['glacier','Многолетние наземные ледники'],['seaPerennial','Многолетний морской лёд'],['seaSeason','Сезонный морской лёд'],['waterOpen','Открытое море'],['riverOpen','Реки и открытые озёра'],['riverFrozen','Замерзающие водотоки'],['lakeIce','Лёд озёр'],['oasisNatural','Тепловые оазисы']],
 transport:[['trunk','Магистральный коридор'],['regRoute','Региональная связь'],['majorCity','Главный узел'],['regionalCity','Региональный узел'],['smallCity','Специализированный узел']],
 surface:[['rock','Открытый грунт и тундра'],['snow','Снег и ледники'],['floe','Морской лёд'],['water','Открытая вода'],['oasisNatural','Незамерзающие природные оазисы']],
 space:[['rock','Открытый грунт и тундра'],['snow','Снег и ледники'],['floe','Морской лёд'],['water','Открытая вода'],['oasisNatural','Тепловые оазисы'],['cloud','Облачность']],
 day:[['rock','Грунт и тундра'],['snow','Снег / ледники'],['water','Открытая вода'],['oasisNatural','Естественные оазисы'],['urban','Промышленно-городская застройка'],['road','Наземные магистрали']],
 night:[['nightDark','Тёмная поверхность'],['light','Огни застройки и обслуживаемых участков дорог']]
};
// Region classes are generated from the same common DEM, at their actual resolution.
for(const [mode,l] of Object.entries(regional)){legends[mode]=l.labels.map((label,i)=>[mode+i,label]);
 legends[mode].push(['regionalUnknown','Мелкие участки вне разрешения сетки']);
 if(mode==='ancestry')legends[mode].push(['bunker','B01 · единый анклав после −7000']);
 if(mode==='waterResources'||mode==='agriculture')legends[mode].push(['riverOpen','Рабочие речные системы'],['geo','Тепловые поля']);
 if(mode==='agriculture')legends[mode].push(['majorCity','Главные узлы'],['regionalCity','Региональные узлы'],['smallCity','Специализированные узлы']);
 if(mode==='mineralResources')legends[mode].push(['plate','Основные тектонические пояса']);
}
if(isHistorical('refugia')){
 legends.refugia=[['land','Суша · современный ориентир'],['north','Современный северный морской лёд'],['surface','RF · кандидат поверхностного очага'],['bunker','B01 · единый подземный анклав']];
 legends.historyRegions=[['land','Суша'],['north','Северный морской лёд'],['region','HR · опорный район'],['deradan','Дерадан · кандидат центра'],['deradanArea','Низменность · природное ядро, не граница'],['bunker','B01 · исторический анклав']];
 legends.modernHeritage=[['land','Суша'],['heritage_valley','Долинное региональное наследие'],['heritage_upland','Нагорное региональное наследие'],['heritage_plain','Равнинное региональное наследие'],['heritageMixed','Смешанные современные городские узлы'],['bunker','Исторический очаг бункерной линии']];
}
function regionalGrid(mode){
 if(regionalDecoded[mode])return regionalDecoded[mode];
 const [w,h]=historyGrid(mode),arr=new Uint8Array(w*h);let pos=0;
 for(const [code,n] of regional[mode].runs){arr.fill(code,pos,pos+n);pos+=n}
 if(pos!==arr.length)throw Error('Invalid Borea regional grid: '+mode);
 return regionalDecoded[mode]=arr
}
function regionalPixels(mode,buf){
 const [w,h]=historyGrid(mode),arr=regionalGrid(mode),palette=regional[mode].colors.map((c,i)=>rgba[mode+i]);
 for(let y=0;y<H;y++){const sy=Math.min(h-1,Math.floor((y+.5)*h/H))*w;
  for(let x=0;x<W;x++){const k=y*W+x,q=k*4,code=arr[sy+Math.min(w-1,Math.floor((x+.5)*w/W))];
   // Use the authoritative coastline. Tiny islands below the regional grid are not classified.
   const c=isLand[k]?(code&&!(mode==='mineralResources'&&code>=6)?palette[code]:[183,180,172]):
       mode==='mineralResources'&&code>=6?palette[code]:palette[0];
   buf[q]=c[0];buf[q+1]=c[1];buf[q+2]=c[2];buf[q+3]=255
  }
 }
}
function regionalOverlays(ctx,mode){
 const S=W/1536;ctx.save();ctx.lineJoin='round';ctx.lineCap='round';
 if(mode==='waterResources'||mode==='agriculture'){
  ctx.strokeStyle='#195d89c9';for(const f of waterFeatures){if(!['river','tributary'].includes(f.properties.category))continue;ctx.lineWidth=(f.properties.category==='river'?1.2:.75)*S;path(ctx,f.geometry.coordinates);ctx.stroke()}
 }
 if(mode==='mineralResources'){ctx.strokeStyle='#f7e7c8d6';ctx.lineWidth=1.5*S;ctx.setLineDash([5*S,3*S]);for(const f of tect){if(!['convergence','continental_rift','ocean_rift','transform'].includes(f.properties.category))continue;path(ctx,f.geometry.coordinates);ctx.stroke()}}
 ctx.restore()
}
function drawHistoryMarker(ctx,x,y,p,S=1){
 const r=(p.kind==='bunker'||p.kind==='deradan'?7.0:p.category==='heritage_node'?(p.node_kind==='major'?5.5:4.1):5.2)*S;
 ctx.save();ctx.beginPath();
 if(p.kind==='deradan'){
  for(let i=0;i<10;i++){const a=-Math.PI/2+i*Math.PI/5,d=(i%2===0?1:.45)*r;const px=x+Math.cos(a)*d,py=y+Math.sin(a)*d;i?ctx.lineTo(px,py):ctx.moveTo(px,py)}ctx.closePath()
 }else if(p.kind==='bunker'){ctx.moveTo(x,y-r);ctx.lineTo(x+r,y);ctx.lineTo(x,y+r);ctx.lineTo(x-r,y);ctx.closePath()}
 else ctx.arc(x,y,r,0,TAU);
 const color=historyColors[p.kind];ctx.fillStyle=p.category==='heritage_node'?C.heritageMixed:color;
 ctx.strokeStyle=p.category==='heritage_node'?color:'#fff5da';ctx.lineWidth=(p.category==='heritage_node'?2.15:1.25)*S;ctx.fill();ctx.stroke();ctx.restore();return r
}
function historyOverlay(ctx,m,flat=false){
 ctx.save();const S=W/1536;
 for(const f of historyFeatures){const p=f.properties;if(!p.views?.includes(m))continue;
  if(f.geometry.type==='Polygon'){
   path(ctx,f.geometry.coordinates[0],true);ctx.fillStyle='#af62705a';ctx.fill();ctx.strokeStyle='#c44358';ctx.lineWidth=2*S;ctx.setLineDash([5*S,4*S]);ctx.stroke();ctx.setLineDash([]);
  }else if(flat){
   const [x,y]=xy(...f.geometry.coordinates);drawHistoryMarker(ctx,x,y,p,S);
  }
 }ctx.restore()
}
function historyBase(buf){
 for(let k=0;k<isLand.length;k++){const c=rgba[isLand[k]?'land':isNorth[k]?'north':'sea'],q=k*4;buf[q]=c[0];buf[q+1]=c[1];buf[q+2]=c[2];buf[q+3]=255}
}
const historyStats={
 ancestry:'Три древние природные линии · B01 сформировался после −7000',
 refugia:(history.validation?.surface_refuge_candidates??0)+' кандидатов поверхностных очагов · один подземный',
 historyRegions:(history.validation?.historical_region_anchors??0)+' рабочих региональных ориентиров · район Дерадана согласован',
 modernHeritage:(history.validation?.modern_nodes??0)+' смешанных хозяйственных узлов · доли линий не определены'
};
const historyNotes={
 ancestry:'Природные области формирования, а не точная карта древних популяций. Использован современный рельеф как ориентир. Пересечения обозначают переходные зоны; B01 — отдельный поздний очаг.',
 refugia:'≈−7000…−2000. Точки — возможные пережившие катастрофу районы возле тепла. Береговые линии и северный морской лёд показаны современными для ориентира, древнее оледенение не реконструировано.',
 historyRegions:'Рабочие историко-хозяйственные ядра; не государства и не административные границы. Контур на материке 7 — существующая низменность как согласованный природный район Дерадана, не граница диктатуры. Центр пока предварительный. Нажмите на метку.',
 modernHeritage:'Все узлы смешанные. Цветное кольцо обозначает вероятное историческое наследие окружающей местности, а не принадлежность населения к линии, её долю или большинство. B01 — место происхождения, не распределение потомков.'
};
const regionalStats={
 precipitation:'Годовой потенциал осадков · логарифмический индекс 0–100',
 waterResources:'Поверхностный сток и доступность · не расход в м³/с',
 mineralResources:'Геологические обстановки · не подтверждённые месторождения',
 agriculture:'Доля суши в лучшем классе: '+(regionalSummary.agriculture_pct_by_class?.[0]??0).toLocaleString('ru-RU')+'% · условно пригодна: '+(regionalSummary.agriculture_pct_by_class?.[1]??0).toLocaleString('ru-RU')+'%'
};
const regionalNotes={
 precipitation:'Модель учитывает сезонно открытые моря, низкую влагоёмкость холодного воздуха, условный перенос с учётом ретроградного вращения, подъём воздуха и дождевую тень. На большей части суши осадки — снег. Это не миллиметры.',
 waterResources:'Модель учитывает осадки, снеготаяние, ледниковое накопление и сток по рельефу. Нажмите на тепловое поле: покажется ориентир потенциальной подпитки, а не доказанный дебит. Грунтовые воды, очистка и перекачка отдельно.',
 mineralResources:'Перспективные типы пород и тектонические обстановки, а не запас или качество руды. Метановые гидраты — лишь холодные осадочные области; подводный рифт — отдельная гипотеза. Границы условны.',
 agriculture:'Классы описывают только природную пригодность для открытого хозяйства. Фактическая пашня, почвы, культуры и урожайность ещё не установлены. Теплицы, орошение, резервное тепло и промысел позволяют хозяйству работать и на холодной суше.'
};
// Illustrative seasonal threshold within the established annual opening envelope.
// North summer/late summer expands northern openings; south counterpart expands southern ones.
// The phase and thermal lag are editorial choices, not inferred meteorology.
const phases=cl.seasonal_preview_parameters;
const isOptical=m=>['space','surface','day','night'].includes(m);
let raw=null, renderTex=null, yaw=-112,pitch=10,zoom=1, pending=false,natural=null,settlement=null,world=null,visibleMarkers=[],prevMode=layer.value,mapGrid=grid.checked,options=null;
if(typeof location!=='undefined'){options=new URLSearchParams(location.search);const view=options.get('view'),p=options.get('season');if(view&&Object.hasOwn(legends,view)){layer.value=view;prevMode=view;if(isOptical(view))grid.checked=false;}if(p&&['overview',...Object.keys(phases)].includes(p))season.value=p;const focus=options.get('focus');if(focus==='deradan'){yaw=54;pitch=4}else if(focus==='bunker'){yaw=-104;pitch=-13}}
function getNatural(){if(!natural)natural=BoreaNatural.create({W,H,isLand,isNorth,scores,lats,tect,surface,xy,phases,weights});return natural}
function getSettlement(){if(!settlement)settlement=BoreaSettlement.create({W,H,isLand,isNorth,lats,weights,natural:getNatural(),tect,nodes});return settlement}
function getWorld(){if(!world)world=BoreaWorld.create({W,H,isLand,lats,natural:getNatural(),nodes,waterFeatures,xy});return world}
function textureFor(){
 const mode=layer.value, phase=season.value, optical=isOptical(mode), relief=mode==='relief', pop=mode==='population', hydro=mode==='hydrology', region=isRegional(mode), hist=isHistorical(mode), seasonal=mode==='climate'||mode==='ice'||hydro||optical, overview=phase==='overview';
 season.disabled=!seasonal;if($('oasisMarks'))$('oasisMarks').disabled=!optical;
 const pixels=tc.createImageData(W,H), buf=pixels.data;let openWeight=0,opticalStatus=null,hydroStatus=null;
 if(optical){opticalStatus=getNatural().optical(buf,mode==='space'?'space':'surface',phase);openWeight=opticalStatus.visible;if(mode==='day'||mode==='night')getWorld().infrastructure(buf,mode==='night');}else if(hydro){hydroStatus=getNatural().cryosphere(buf,phase);openWeight=hydroStatus.open;}else if(relief)getNatural().relief(buf);else if(pop)getSettlement().draw(buf);else if(region)regionalPixels(mode,buf);else if(hist)historyBase(buf);else{
 for(let j=0;j<H;j++){const lat=lats[j],a=Math.abs(lat),[t1,t2,t3]=cl.land_zones_abs_lat_degrees;
   const landColor=a<=t1?'mild':a<=t2?'cold':a<=t3?'tundra':'polar';
   const ph=phases[phase],limit=overview?1:clamp(ph.bias+ph.hem*clamp(lat/20,-1,1),.18,1);
   for(let i=0;i<W;i++){const k=j*W+i;
     let name;if(isLand[k])name=mode==='climate'?landColor:'land';
     else if(['geography','tectonics','heat','cities','transport'].includes(mode))name=isNorth[k]?'north':'sea';
     else if(isNorth[k])name=mode==='ice'?'north':'ice';
     else if(scores[k]<=limit){name='open';openWeight+=weights[j]}
     else if(scores[k]<=1)name='seasonal';else name='ice';
     const c=rgba[name],q=k*4;buf[q]=c[0];buf[q+1]=c[1];buf[q+2]=c[2];buf[q+3]=255;
   }}
 }
 tc.putImageData(pixels,0,0);
 if(region)regionalOverlays(tc,mode);
 if(hist)historyOverlay(tc,mode);
 if(['hydrology','surface','day'].includes(mode))getWorld().water(tc,phase,mode);
 if(['day','night','transport'].includes(mode))getWorld().transport(tc,mode);
 if(grid.checked){tc.lineWidth=.85;tc.strokeStyle='#58778765';for(let la=-60;la<=60;la+=30){path(tc,[[-180,la],[180,la]]);tc.stroke()}for(let lo=-150;lo<180;lo+=30){path(tc,[[lo,-90],[lo,90]]);tc.stroke()}}
 // Borders of all actual continents and islands. The texture adheres to geographic coordinates.
 if(!optical&&!relief&&!pop){tc.lineWidth=1.2;tc.strokeStyle='#405867';for(const f of land){path(tc,f.geometry.coordinates[0],true);tc.stroke()}}
 if(['tectonics','climate','heat','cities'].includes(mode)){
  for(const f of tect){let p=f.properties,cat=p.category,coords=f.geometry.coordinates;
    if(['tectonics','heat','cities'].includes(mode)&&['stable_massif','lowland'].includes(cat)){tc.fillStyle=C[cat==='stable_massif'?'stable':'low'];tc.strokeStyle='#526f60';tc.lineWidth=.9;path(tc,coords[0],true);tc.fill();tc.stroke()}
  }
  for(const f of tect){let p=f.properties,cat=p.category,coords=f.geometry.coordinates;
    if(['mountain_belt','old_mountain_belt'].includes(cat)){tc.strokeStyle=C.mount;tc.lineWidth=5;tc.lineJoin='round';tc.lineCap='round';path(tc,coords);tc.stroke();tc.strokeStyle='#f1d9b7';tc.lineWidth=1.1;tc.stroke()}
    if(['tectonics','heat'].includes(mode)&&['convergence','ocean_rift','continental_rift','transform'].includes(cat)){tc.strokeStyle='#233b4d';tc.lineWidth=5;path(tc,coords);tc.stroke();tc.strokeStyle=C[{convergence:'converge',ocean_rift:'rift',continental_rift:'riftLand',transform:'transform'}[cat]];tc.lineWidth=2.8;if(cat==='transform')tc.setLineDash([7,4]);tc.stroke();tc.setLineDash([])}
    if(cat==='plate_extension'&&mode==='tectonics'){tc.strokeStyle='#27374a';tc.lineWidth=3.5;path(tc,coords);tc.stroke();tc.strokeStyle=C.plate;tc.lineWidth=1.8;tc.setLineDash([6,4]);path(tc,coords);tc.stroke();tc.setLineDash([])}
    if(cat==='volcanic_arc'&&['tectonics','heat'].includes(mode)){tc.strokeStyle=C.arc;tc.lineWidth=2.8;tc.setLineDash([5,3]);path(tc,coords);tc.stroke();tc.setLineDash([])}
    if(cat==='geothermal'&&mode==='climate'){const [x,y]=xy(...coords);tc.fillStyle=C[p.kind==='oasis'?'oasis':'geo'];tc.strokeStyle='#604929';tc.lineWidth=1;tc.beginPath();tc.moveTo(x,y-4);tc.lineTo(x+4,y);tc.lineTo(x,y+4);tc.lineTo(x-4,y);tc.closePath();tc.fill();tc.stroke()}
  }
 }
 raw=tc.getImageData(0,0,W,H).data;
 if(renderTex)renderTex();
 const items=legends[mode].filter(([key])=>!(overview&&key==='seasonal'));
 $('legend').replaceChildren(...items.map(([key,label])=>{const el=document.createElement('span');el.className='key';const sw=document.createElement('i');sw.className='swatch';sw.style.background=C[key];el.append(sw,document.createTextNode(label));if(overview&&(key==='open'||key==='waterOpen'))el.lastChild.textContent='Область возможного сезонного открытия';return el}));
 const rate=(100*(overview&&!optical?potentialWeight:openWeight)/oceanWeight);
 $('stats').textContent=hist?historyStats[mode]:region?regionalStats[mode]:hydro?(overview?'Годовая область возможного открытия моря: ':'Открытая морская вода: ')+(100*hydroStatus.open/hydroStatus.sea).toFixed(1).replace('.',',')+'% · наземный ледник '+(100*hydroStatus.glacier/hydroStatus.land).toFixed(1).replace('.',',')+'% суши':mode==='transport'?getWorld().counts.roads+' наземных коридоров · рабочая схема':pop?'Относительная плотность населения · индекс 0–100':mode==='cities'?nodes.length+' опорных узлов расселения · черновая схема':mode==='heat'?fields.filter(f=>f.properties.kind==='oasis').length+' кандидатов естественных оазисов · '+fields.length+' тепловых районов':relief?'Черновой диапазон: '+(getNatural().range[0]/1000).toFixed(2).replace('.',',')+'…+'+(getNatural().range[1]/1000).toFixed(2).replace('.',',')+' км':optical?(mode==='space'?'Орбитальный вид · облака и атмосферная дымка':mode==='day'?'Дневная поверхность · города и дороги':mode==='night'?'Ночная поверхность · огни городов и дорог':'Поверхность · без облаков и атмосферы')+(overview?' · лето на севере':''):seasonal?(overview?'Возможное сезонное открытие: ':'Условная открытая вода: ')+rate.toFixed(1).replace('.',',')+'% площади океана':mode==='tectonics'?'6 основных тектонических поясов · 4 рабочие макроплиты':'8 основных материков · 40% суши / 60% океана';
 $('note').textContent=hist?historyNotes[mode]:region?regionalNotes[mode]:hydro?((getWorld().counts.rivers+' речных систем, '+getWorld().counts.tributaries+' притоков и '+getWorld().counts.lakes+' кандидатов озёрных бассейнов. Лёд рек обозначает замерзающую поверхность, а не прекращение стока. ')+(overview?'Годовой ареал возможного открытия, не один момент.':'Сезоны указаны для северного полушария.')):mode==='transport'?'Рабочие связи между 28 узлами, привязанные к рельефу. Наземные коридоры не пересекают моря; мосты и ледовые маршруты отдельно не моделированы.':pop?'100 — наиболее плотная ячейка модели. Шкала нелинейная; это не число жителей и не процент населения.':mode==='cities'?'Узлы обозначают представительные агломерации и хозяйственные районы, не все города. Нажмите на маркер для описания.':mode==='heat'?'Зелёные точки — экваториальные кандидаты естественных оазисов; янтарные — тепловые ресурсы без гарантии тёплого микроклимата. Нажмите на маркер.':relief?'Цвета — высоты и глубины. Шесть редких вершин >8,85 км в поясах A, B и D; абсолютные отметки предварительные. Нажмите на маркер.':optical?(['day','night'].includes(mode)?'Рабочий рисунок застройки и узловых дорожных коридоров без атмосферы; ширины и огни для читаемости увеличены. Ночная карта — вся поверхность на условной ночной стороне, а не мгновенный снимок.':'Природный вид без городов; незамерзающие оазисы видны как контрастные тёмно-зеленовато-пурпурные участки. В облачном режиме часть может быть скрыта.'):seasonal?(overview?'Показан объединённый годовой ареал возможного открытия, а не одновременная свободная ото льда площадь.':'Приблизительная сезонная иллюстрация. Сезон обозначен для северного полушария; на юге он противоположный. Площади не утверждены.'):mode==='tectonics'?'Штриховые соединения условно замыкают четыре макроплиты; режимы и скорости этих участков неизвестны. F — внутриплитный рифт.':'Материки 1–5 соединены постоянным морским льдом, но лёд не считается сушей.';
 if($('downloadTexture'))$('downloadTexture').hidden=!(optical||relief||pop||hydro||region||hist);if($('selection')){$('selection').hidden=true;$('selection').textContent=''}
 requestDraw()
}
let gl=null;
try{gl=canvas.getContext('webgl',{alpha:true,antialias:true,premultipliedAlpha:false,preserveDrawingBuffer:true})}catch{}
let program,tex,uni={};
function shader(type,src){const s=gl.createShader(type);gl.shaderSource(s,src);gl.compileShader(s);if(!gl.getShaderParameter(s,gl.COMPILE_STATUS))throw Error(gl.getShaderInfoLog(s));return s}
function initGL(){
 const vs=shader(gl.VERTEX_SHADER,'attribute vec2 pos; void main(){gl_Position=vec4(pos,0.0,1.0);}');
 const fs=shader(gl.FRAGMENT_SHADER,`precision highp float;
 uniform vec2 size; uniform float radius; uniform float yaw; uniform float pitch; uniform sampler2D atlas; uniform float optical; uniform float atmosphere; uniform float night;
 const float PI=3.14159265359;
 void main(){vec2 p=(gl_FragCoord.xy-size/2.0)/radius;float r=dot(p,p);float rr=sqrt(r);if(r>1.0){float a=atmosphere*0.62*exp(-max(0.0,rr-1.0)*88.0)*(1.0-smoothstep(1.007,1.045,rr));gl_FragColor=vec4(0.40,0.70,0.92,a);return;}float z=sqrt(max(0.0,1.0-r));
 float s=sin(pitch),c=cos(pitch);float lat=asin(clamp(p.y*c+z*s,-1.0,1.0));float lon=yaw+atan(p.x,z*c-p.y*s);vec2 uv=vec2(fract(lon/(2.0*PI)+0.5),0.5-lat/PI);
 vec3 col=texture2D(atlas,uv).rgb;float sun=max(0.0,dot(vec3(p,z),normalize(vec3(-0.48,0.38,0.83))));float diffuse=mix(mix(0.76+0.24*sun,0.32+0.68*sun,optical),1.0,night);
 float rim=pow(1.0-z,2.6);col=col*diffuse+vec3(0.11,0.20,0.25)*rim*(1.0-optical);col=mix(col,vec3(0.38,0.64,0.84)*(0.62+0.38*sun),atmosphere*(0.06+0.61*rim));float edge=clamp((1.0-rr)*radius,0.0,1.0);float alpha=mix(edge,max(0.62,edge),atmosphere);gl_FragColor=vec4(col,alpha);}`);
 program=gl.createProgram();gl.attachShader(program,vs);gl.attachShader(program,fs);gl.linkProgram(program);if(!gl.getProgramParameter(program,gl.LINK_STATUS))throw Error(gl.getProgramInfoLog(program));gl.useProgram(program);
 const b=gl.createBuffer();gl.bindBuffer(gl.ARRAY_BUFFER,b);gl.bufferData(gl.ARRAY_BUFFER,new Float32Array([-1,-1,1,-1,-1,1,-1,1,1,-1,1,1]),gl.STATIC_DRAW);const loc=gl.getAttribLocation(program,'pos');gl.enableVertexAttribArray(loc);gl.vertexAttribPointer(loc,2,gl.FLOAT,false,0,0);
 for(const n of ['size','radius','yaw','pitch','atlas','optical','atmosphere','night'])uni[n]=gl.getUniformLocation(program,n);
 tex=gl.createTexture();gl.bindTexture(gl.TEXTURE_2D,tex);gl.texParameteri(gl.TEXTURE_2D,gl.TEXTURE_WRAP_S,gl.CLAMP_TO_EDGE);gl.texParameteri(gl.TEXTURE_2D,gl.TEXTURE_WRAP_T,gl.CLAMP_TO_EDGE);gl.texParameteri(gl.TEXTURE_2D,gl.TEXTURE_MIN_FILTER,gl.LINEAR);gl.texParameteri(gl.TEXTURE_2D,gl.TEXTURE_MAG_FILTER,gl.LINEAR);
 renderTex=()=>{gl.bindTexture(gl.TEXTURE_2D,tex);gl.texImage2D(gl.TEXTURE_2D,0,gl.RGBA,gl.RGBA,gl.UNSIGNED_BYTE,texture)}
}
let cpuCanvas=null,ctxCPU=null;
if(gl){try{initGL()}catch(e){console.warn('Globe WebGL fallback',e);gl=null}}
if(!gl){cpuCanvas=document.createElement('canvas');cpuCanvas.id='globe';canvas.replaceWith(cpuCanvas);ctxCPU=cpuCanvas.getContext('2d')}
function size(){return stage.getBoundingClientRect()}
function radius(w,h){return Math.min(w,h)*.445*zoom}
function syncSize(c,w,h){if(c.width!==w)c.width=w;if(c.height!==h)c.height=h}
function drawCPU(cssW,cssH){const factor=Math.min(1,780/Math.max(cssW,cssH)),w=Math.max(1,Math.round(cssW*factor)),h=Math.max(1,Math.round(cssH*factor));syncSize(cpuCanvas,w,h);const img=ctxCPU.createImageData(w,h),buf=img.data,r=radius(w,h),cx=w/2,cy=h/2,s=Math.sin(pitch*RAD),c=Math.cos(pitch*RAD),optical=isOptical(layer.value),atm=layer.value==='space',outer=atm?1.045:1;
 for(let j=Math.max(0,Math.floor(cy-r*outer));j<Math.min(h,Math.ceil(cy+r*outer));j++){const y=(cy-j-.5)/r,half=r*Math.sqrt(Math.max(0,outer*outer-y*y));for(let i=Math.max(0,Math.floor(cx-half));i<Math.min(w,Math.ceil(cx+half));i++){const x=(i+.5-cx)/r,rr=x*x+y*y,q=(j*w+i)*4;if(rr>1){if(atm){const a=Math.sqrt(rr)-1;buf[q]=102;buf[q+1]=178;buf[q+2]=235;buf[q+3]=Math.max(0,.62*Math.exp(-a*88)*(1-clamp((a-.007)/.038,0,1))*255)}continue}
 const z=Math.sqrt(1-rr),lat=Math.asin(clamp(y*c+z*s,-1,1)),lon=yaw+Math.atan2(x,z*c-y*s)/RAD,tx=Math.min(W-1,Math.floor((wrap(lon)+180)*W/360)),ty=clamp(Math.floor((.5-lat/PI)*H),0,H-1),k=(ty*W+tx)*4,sun=Math.max(0,-.48*x+.38*y+.83*z),light=layer.value==='night'?1:optical?.32+.68*sun:.76+.24*sun,rim=Math.pow(1-z,2.6),haze=atm?.06+.61*rim:0;
 buf[q]=raw[k]*light*(1-haze)+(atm?97*(.62+.38*sun)*haze:!optical?28*rim:0);buf[q+1]=raw[k+1]*light*(1-haze)+(atm?163*(.62+.38*sun)*haze:!optical?51*rim:0);buf[q+2]=raw[k+2]*light*(1-haze)+(atm?214*(.62+.38*sun)*haze:!optical?64*rim:0);buf[q+3]=(atm?Math.max(.62,clamp((1-Math.sqrt(rr))*r,0,1)):clamp((1-Math.sqrt(rr))*r,0,1))*255}}
 ctxCPU.putImageData(img,0,0)
}
function project(lon,lat,w,h){const l=lat*RAD,d=wrap(lon-yaw)*RAD,p=pitch*RAD, cos=Math.cos(l),x=cos*Math.sin(d),y=Math.sin(l)*Math.cos(p)-cos*Math.cos(d)*Math.sin(p),z=Math.sin(l)*Math.sin(p)+cos*Math.cos(d)*Math.cos(p),r=radius(w,h);return{x:w/2+x*r,y:h/2-y*r,z}}
function draw(){pending=false;if(!raw)return;const rect=size(),w=rect.width,h=rect.height;if(w<2||h<2)return;const dpr=Math.min(devicePixelRatio||1,1.7);
 if(gl){const pw=Math.round(w*dpr),ph=Math.round(h*dpr);syncSize(canvas,pw,ph);gl.viewport(0,0,pw,ph);gl.useProgram(program);gl.uniform2f(uni.size,pw,ph);gl.uniform1f(uni.radius,radius(w,h)*dpr);gl.uniform1f(uni.yaw,yaw*RAD);gl.uniform1f(uni.pitch,pitch*RAD);gl.uniform1i(uni.atlas,0);gl.uniform1f(uni.optical,isOptical(layer.value)?1:0);gl.uniform1f(uni.atmosphere,layer.value==='space'?1:0);gl.uniform1f(uni.night,layer.value==='night'?1:0);gl.drawArrays(gl.TRIANGLES,0,6)}
 else drawCPU(w,h);
 syncSize(overlay,Math.round(w*dpr),Math.round(h*dpr));ctxO.setTransform(dpr,0,0,dpr,0,0);ctxO.clearRect(0,0,w,h);
 if(!isOptical(layer.value)||grid.checked)for(let n=0;n<landLabels.length;n++){const q=project(...landLabels[n],w,h);if(q.z<.23||q.x<10||q.y<10||q.x>w-10||q.y>h-10)continue;ctxO.beginPath();ctxO.arc(q.x,q.y,9.2,0,TAU);ctxO.fillStyle='#f0eddaed';ctxO.fill();ctxO.strokeStyle='#4c6979';ctxO.lineWidth=1;ctxO.stroke();ctxO.fillStyle='#233747';ctxO.font='bold 11px system-ui';ctxO.textAlign='center';ctxO.textBaseline='middle';ctxO.fillText(n+1,q.x,q.y+.3)}
 visibleMarkers=[];
 const mode=layer.value;
 const markerSet=isHistorical(mode)?historyMarkers(mode):mode==='relief'?summits:mode==='tectonics'?[...fields,...summits]:mode==='heat'?fields:mode==='cities'?[...fields,...nodes]:mode==='transport'?nodes:mode==='hydrology'?lakeNodes:mode==='waterResources'?fields:mode==='agriculture'?[...fields,...nodes]:isOptical(mode)&&$('oasisMarks')?.checked?fields.filter(f=>f.properties.kind==='oasis'):[];
 for(const f of markerSet){const p=f.properties,q=project(...f.geometry.coordinates,w,h);if(q.z<.16||q.x<5||q.y<5||q.x>w-5||q.y>h-5)continue;
  if(isHistorical(mode)){
   const r=drawHistoryMarker(ctxO,q.x,q.y,p);
   if((p.kind==='bunker'||p.kind==='deradan'||(zoom>1.25&&mode!=='modernHeritage'))&&q.z>.28){
    ctxO.font='bold 10px system-ui';ctxO.textAlign='left';ctxO.textBaseline='middle';ctxO.strokeStyle='#162b3b';ctxO.lineWidth=3;
    const label=p.kind==='deradan'?'Дерадан':p.id;ctxO.strokeText(label,q.x+r+2,q.y);ctxO.fillStyle='#fff4de';ctxO.fillText(label,q.x+r+2,q.y);
   }visibleMarkers.push({f,x:q.x,y:q.y,r});continue;
  }const node=p.category==='settlement_node',peak=p.category==='high_peak',lake=p.category==='lake_candidate',r=node?(p.kind==='major'?6.5:p.kind==='regional'?5:3.8):peak?5.8:mode==='cities'?2.7:5;
  ctxO.beginPath();if(peak){ctxO.moveTo(q.x,q.y-r-2);ctxO.lineTo(q.x-r,q.y+r-1);ctxO.lineTo(q.x+r,q.y+r-1);ctxO.closePath()}else if(!node){ctxO.moveTo(q.x,q.y-r);ctxO.lineTo(q.x+r,q.y);ctxO.lineTo(q.x,q.y+r);ctxO.lineTo(q.x-r,q.y);ctxO.closePath()}else ctxO.arc(q.x,q.y,r,0,TAU);
  ctxO.fillStyle=lake?'#6cc4dd':peak?'#fff2d2':node?C[{major:'majorCity',regional:'regionalCity',specialized:'smallCity'}[p.kind]]:C[p.kind==='oasis'?'oasis':'geo'];ctxO.fill();ctxO.strokeStyle=node?'#fff6dc':'#263949';ctxO.lineWidth=node?1.65:1.15;ctxO.stroke();
  if(mode==='relief'&&q.z>.4&&zoom>1.23){ctxO.font='bold 10px system-ui';ctxO.textAlign='left';ctxO.textBaseline='middle';ctxO.strokeStyle='#27323a';ctxO.lineWidth=3;const label=(p.height_m/1000).toFixed(2).replace('.',',')+' км';ctxO.strokeText(label,q.x+r+2,q.y);ctxO.fillStyle='#fff4de';ctxO.fillText(label,q.x+r+2,q.y)}
  visibleMarkers.push({f,x:q.x,y:q.y,r});
 }
 if(mode==='tectonics'){for(const f of plateLabels){const q=project(...f.geometry.coordinates,w,h);if(q.z<.2||q.x<16||q.y<16||q.x>w-16||q.y>h-16)continue;ctxO.font='bold 12px system-ui';ctxO.textAlign='center';ctxO.textBaseline='middle';ctxO.strokeStyle='#213646';ctxO.lineWidth=3;ctxO.strokeText(f.properties.id,q.x,q.y);ctxO.fillStyle='#f8efbd';ctxO.fillText(f.properties.id,q.x,q.y)}}
 const formatted=(v,pos,neg)=>Math.abs(v).toFixed(0)+'° '+(v>=0?pos:neg);
 $('coords').textContent='Центр: '+formatted(pitch,'с.ш.','ю.ш.')+' · '+formatted(yaw,'в.д.','з.д.');
}
function requestDraw(){if(!pending){pending=true;requestAnimationFrame(draw)}}
function setZoom(z){zoom=clamp(z,.65,W>1536?4.0:2.4);requestDraw()}
function reset(){yaw=-112;pitch=10;zoom=1;requestDraw()}
function selectMarker(e){if(!$('selection'))return;const b=size(),x=e.clientX-b.left,y=e.clientY-b.top;const hits=visibleMarkers.map(m=>({m,d:Math.hypot(x-m.x,y-m.y)-(m.f.properties.category==='settlement_node'&&layer.value==='cities'?4:0)})).filter(m=>m.d<15).sort((a,b)=>a.d-b.d);if(!hits.length){$('selection').hidden=true;return}const p=hits[0].m.f.properties;let t;if(isHistorical(layer.value)){t=p.id+' · '+p.name+'. '+p.description+(p.view_notes?.[layer.value]?' '+p.view_notes[layer.value]:'');$('selection').textContent=t;$('selection').hidden=false;return}if(p.category==='high_peak')t=p.id+': '+p.height_m.toLocaleString('ru-RU')+' м, пояс '+p.zone+'; редкая вершина над утолщённой корой.';else if(p.category==='geothermal')t=p.id+' · '+p.name+' — '+(p.kind==='oasis'?'кандидат естественного оазиса':'тепловой ресурс, не естественный оазис')+'. '+p.heat_source+'.';else if(p.category==='lake_candidate')t=p.id+' · Кандидат озёрной котловины; условная высота '+p.height_m+' м. Точное зеркало воды и объём не установлены.';else t=p.id+' · '+p.role+(p.thermal_field?' (связь с '+p.thermal_field+')':'')+'. Рабочий узел, точная площадка и название не утверждены.';if(layer.value==='waterResources'&&p.category==='geothermal'){const a=regionalSummary.thermal_fields?.find(x=>x.id===p.id);if(a)t+=' Потенциальная подпитка: '+a.class+' (индекс '+a.screen_index+'/100); оценка в окрестности 400 км, не дебит.'}if(layer.value==='agriculture'&&p.category==='settlement_node'){const a=regionalSummary.settlement_nodes?.find(x=>x.id===p.id);if(a)t+=' Доля природно благоприятных и условных сельхозкандидатов в рабочей окрестности узла: '+a.near_open_candidate_pct.toLocaleString('ru-RU')+'%; это не фактическая пашня.'}$('selection').textContent=t;$('selection').hidden=false}
const pointers=new Map();function gap(){const [a,b]=[...pointers.values()];return Math.hypot(a.x-b.x,a.y-b.y)}
overlay.addEventListener('pointerdown',e=>{e.preventDefault();overlay.focus({preventScroll:true});overlay.setPointerCapture(e.pointerId);pointers.set(e.pointerId,{x:e.clientX,y:e.clientY,sx:e.clientX,sy:e.clientY});});
overlay.addEventListener('pointermove',e=>{if(!pointers.has(e.pointerId))return;const old=pointers.get(e.pointerId),before=pointers.size===2?gap():0;pointers.set(e.pointerId,{x:e.clientX,y:e.clientY,sx:old.sx,sy:old.sy});if(pointers.size===2&&before>0){setZoom(zoom*gap()/before);return}if(pointers.size!==1)return;const b=size(),r=radius(b.width,b.height);yaw=wrap(yaw-(e.clientX-old.x)*55/r);pitch=clamp(pitch+(e.clientY-old.y)*55/r,-89,89);requestDraw()});
function release(e){pointers.delete(e.pointerId)}overlay.addEventListener('pointerup',e=>{const p=pointers.get(e.pointerId);if(p&&pointers.size===1&&Math.hypot(e.clientX-p.sx,e.clientY-p.sy)<5)selectMarker(e);release(e)});overlay.addEventListener('pointercancel',release);overlay.addEventListener('lostpointercapture',release);
overlay.addEventListener('wheel',e=>{e.preventDefault();setZoom(zoom*Math.exp(-e.deltaY*.0015))},{passive:false});
overlay.addEventListener('keydown',e=>{let handled=true;switch(e.key){case 'ArrowLeft':yaw=wrap(yaw-10);break;case 'ArrowRight':yaw=wrap(yaw+10);break;case 'ArrowUp':pitch=clamp(pitch+10,-89,89);break;case 'ArrowDown':pitch=clamp(pitch-10,-89,89);break;case '+':case '=':setZoom(zoom*1.18);break;case '-':setZoom(zoom/1.18);break;case 'Home':reset();break;default:handled=false}if(handled){e.preventDefault();requestDraw()}});
$('zoomIn').onclick=()=>setZoom(zoom*1.2);$('zoomOut').onclick=()=>setZoom(zoom/1.2);$('reset').onclick=reset;
$('fullscreen').onclick=()=>{if(document.fullscreenElement){document.exitFullscreen();return}const a=document.querySelector('.atlas');if(a.requestFullscreen)a.requestFullscreen().catch(()=>{});else window.open(location.href,'_blank','noopener')};
layer.addEventListener('change',()=>{const optical=isOptical(layer.value),was=isOptical(prevMode);if(optical&&!was){mapGrid=grid.checked;grid.checked=false;}else if(!optical&&was)grid.checked=mapGrid;prevMode=layer.value;textureFor()});
for(const el of [season,grid])el.addEventListener('change',textureFor);if($('oasisMarks'))$('oasisMarks').addEventListener('change',requestDraw);
if($('resolution')){ $('resolution').value=W>1536?'high':'standard';$('resolution').addEventListener('change',()=>{const u=new URL(location.href);u.searchParams.set('view',layer.value);u.searchParams.set('season',season.value);if($('resolution').value==='high')u.searchParams.set('resolution','high');else u.searchParams.delete('resolution');location.href=u.toString()});}
if($('downloadTexture'))$('downloadTexture').onclick=()=>{const mode=layer.value;if(!isOptical(mode)&&mode!=='relief'&&mode!=='population'&&mode!=='hydrology'&&!isRegional(mode)&&!isHistorical(mode))return;const c=surface(),ctx=c.getContext('2d'),p=ctx.createImageData(W,H);if(mode==='relief')getNatural().relief(p.data);else if(mode==='population')getSettlement().draw(p.data);else if(mode==='hydrology')getNatural().cryosphere(p.data,season.value);else if(isRegional(mode))regionalPixels(mode,p.data);else if(isHistorical(mode))historyBase(p.data);else{getNatural().optical(p.data,mode==='space'?'space':'surface',season.value);if(mode==='day'||mode==='night')getWorld().infrastructure(p.data,mode==='night')}ctx.putImageData(p,0,0);if(isRegional(mode))regionalOverlays(ctx,mode);if(isHistorical(mode))historyOverlay(ctx,mode,true);if(['hydrology','surface','day'].includes(mode))getWorld().water(ctx,season.value,mode);if(['day','night'].includes(mode))getWorld().transport(ctx,mode);c.toBlob(blob=>{if(!blob)return;const u=URL.createObjectURL(blob),a=document.createElement('a');a.href=u;a.download='borea-'+mode+'-'+(isHistorical(mode)?'working':isRegional(mode)?'annual':season.value==='overview'&&isOptical(mode)?'summer':season.value)+'.png';a.click();setTimeout(()=>URL.revokeObjectURL(u),2000)},'image/png')};
if('ResizeObserver'in window)new ResizeObserver(requestDraw).observe(stage);else addEventListener('resize',requestDraw);
textureFor();
})();
