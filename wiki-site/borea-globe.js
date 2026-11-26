'use strict';
(() => {
const $=id=>document.getElementById(id), data=globalThis.BOREA_DATA;
if(!data){$('error').hidden=false;return}
const stage=$('stage'), canvas=$('globe'), overlay=$('overlay'), ctxO=overlay.getContext('2d');
const layer=$('layer'), season=$('season'), grid=$('grid');
const PI=Math.PI, RAD=PI/180, TAU=2*PI, W=1152,H=576;
const wrap=x=>((x+180)%360+360)%360-180, clamp=(x,a,b)=>Math.max(a,Math.min(b,x));
const land=data.continents.features.filter(f=>['continent','island'].includes(f.properties.category));
const north=data.continents.features.find(f=>f.properties.category==='schematic_northern_perennial_ice_extent');
const tect=data.tectonics.features, cl=data.climate;
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
const C={land:'#d8d0b1',sea:'#365d74',north:'#b4d5df',mild:'#a7b17a',cold:'#d2bf94',tundra:'#b9c5cd',polar:'#e8e4e0',ice:'#9fc5d6',seasonal:'#d3e7ec',open:'#30647e',stable:'#8eb18e',low:'#e8e2a0',mount:'#957256',converge:'#f07150',rift:'#38ccea',riftLand:'#159cbf',transform:'#c99ce8',geo:'#ffbd50'};
const rgba=Object.fromEntries(Object.entries(C).map(([k,v])=>[k,[parseInt(v.slice(1,3),16),parseInt(v.slice(3,5),16),parseInt(v.slice(5,7),16)]]));
const legends={
 geography:[['land','Суша'],['sea','Океан'],['north','Северный ледовый регион']],
 tectonics:[['converge','Сближение плит'],['rift','Океанический рифт'],['riftLand','Континентальный рифт'],['transform','Сдвиговой пояс'],['mount','Горы'],['stable','Устойчивые массивы'],['low','Низменности'],['geo','Геотермальные поля']],
 climate:[['mild','Наиболее мягкая суша'],['cold','Холодная сезонная суша'],['tundra','Тундра / мерзлота'],['polar','Полярная суша'],['ice','Многолетний морской лёд'],['seasonal','Сезонный лёд'],['open','Открытие моря'],['geo','Геотермальные поля']],
 ice:[['land','Суша'],['ice','Многолетний лёд'],['seasonal','Сезонный лёд'],['open','Открытие моря'],['north','Северный ледовый регион']]
};
// Illustrative seasonal threshold within the established annual opening envelope.
// North summer/late summer expands northern openings; south counterpart expands southern ones.
// The phase and thermal lag are editorial choices, not inferred meteorology.
const phases=cl.seasonal_preview_parameters;
let raw=null, renderTex=null, yaw=-112,pitch=10,zoom=1, pending=false;
function textureFor(){
 const mode=layer.value, phase=season.value, seasonal=mode==='climate'||mode==='ice', overview=phase==='overview';
 season.disabled=!seasonal;
 const pixels=tc.createImageData(W,H), buf=pixels.data;let openWeight=0;
 for(let j=0;j<H;j++){const lat=lats[j],a=Math.abs(lat),[t1,t2,t3]=cl.land_zones_abs_lat_degrees;
   const landColor=a<=t1?'mild':a<=t2?'cold':a<=t3?'tundra':'polar';
   const ph=phases[phase],limit=overview?1:clamp(ph.bias+ph.hem*clamp(lat/20,-1,1),.18,1);
   for(let i=0;i<W;i++){const k=j*W+i;
     let name;if(isLand[k])name=mode==='climate'?landColor:'land';
     else if(mode==='geography'||mode==='tectonics')name=isNorth[k]?'north':'sea';
     else if(isNorth[k])name=mode==='ice'?'north':'ice';
     else if(scores[k]<=limit){name='open';openWeight+=weights[j]}
     else if(scores[k]<=1)name='seasonal';else name='ice';
     const c=rgba[name],q=k*4;buf[q]=c[0];buf[q+1]=c[1];buf[q+2]=c[2];buf[q+3]=255;
   }}
 tc.putImageData(pixels,0,0);
 if(grid.checked){tc.lineWidth=.85;tc.strokeStyle='#58778765';for(let la=-60;la<=60;la+=30){path(tc,[[-180,la],[180,la]]);tc.stroke()}for(let lo=-150;lo<180;lo+=30){path(tc,[[lo,-90],[lo,90]]);tc.stroke()}}
 // Borders of all actual continents and islands. The texture adheres to geographic coordinates.
 tc.lineWidth=1.2;tc.strokeStyle='#405867';for(const f of land){path(tc,f.geometry.coordinates[0],true);tc.stroke()}
 if(mode==='tectonics'||mode==='climate'){
  for(const f of tect){let p=f.properties,cat=p.category,coords=f.geometry.coordinates;
    if(mode==='tectonics'&&['stable_massif','lowland'].includes(cat)){tc.fillStyle=C[cat==='stable_massif'?'stable':'low'];tc.strokeStyle='#526f60';tc.lineWidth=.9;path(tc,coords[0],true);tc.fill();tc.stroke()}
  }
  for(const f of tect){let p=f.properties,cat=p.category,coords=f.geometry.coordinates;
    if(['mountain_belt','old_mountain_belt'].includes(cat)){tc.strokeStyle=C.mount;tc.lineWidth=5;tc.lineJoin='round';tc.lineCap='round';path(tc,coords);tc.stroke();tc.strokeStyle='#f1d9b7';tc.lineWidth=1.1;tc.stroke()}
    if(mode==='tectonics'&&['convergence','ocean_rift','continental_rift','transform'].includes(cat)){tc.strokeStyle='#233b4d';tc.lineWidth=5;path(tc,coords);tc.stroke();tc.strokeStyle=C[{convergence:'converge',ocean_rift:'rift',continental_rift:'riftLand',transform:'transform'}[cat]];tc.lineWidth=2.8;if(cat==='transform')tc.setLineDash([7,4]);tc.stroke();tc.setLineDash([])}
    if(cat==='geothermal'){const [x,y]=xy(...coords);tc.fillStyle=C.geo;tc.strokeStyle='#604929';tc.lineWidth=1;tc.beginPath();tc.moveTo(x,y-4);tc.lineTo(x+4,y);tc.lineTo(x,y+4);tc.lineTo(x-4,y);tc.closePath();tc.fill();tc.stroke()}
  }
 }
 raw=tc.getImageData(0,0,W,H).data;
 if(renderTex)renderTex();
 const items=legends[mode].filter(([key])=>!(overview&&key==='seasonal'));
 $('legend').replaceChildren(...items.map(([key,label])=>{const el=document.createElement('span');el.className='key';const sw=document.createElement('i');sw.className='swatch';sw.style.background=C[key];el.append(sw,document.createTextNode(label));if(overview&&key==='open')el.lastChild.textContent='Область возможного сезонного открытия';return el}));
 const rate=(100*(overview?potentialWeight:openWeight)/oceanWeight);
 $('stats').textContent=seasonal?(overview?'Возможное сезонное открытие: ':'Условная открытая вода: ')+rate.toFixed(1).replace('.',',')+'% площади океана':mode==='tectonics'?'6 основных тектонических поясов · рабочая схема':'8 основных материков · 40% суши / 60% океана';
 $('note').textContent=seasonal?(overview?'Показан объединённый годовой ареал возможного открытия, а не одновременная свободная ото льда площадь.':'Приблизительная сезонная иллюстрация. Сезон обозначен для северного полушария; на юге он противоположный. Площади не утверждены.'):mode==='tectonics'?'Геотермальные поля не равны целым тёплым биомам. Границы плит, высоты и сейсмичность уточняются.':'Материки 1–5 соединены постоянным морским льдом, но лёд не считается сушей.';
 requestDraw()
}
let gl=null;
try{gl=canvas.getContext('webgl',{alpha:true,antialias:true,premultipliedAlpha:false,preserveDrawingBuffer:true})}catch{}
let program,tex,uni={};
function shader(type,src){const s=gl.createShader(type);gl.shaderSource(s,src);gl.compileShader(s);if(!gl.getShaderParameter(s,gl.COMPILE_STATUS))throw Error(gl.getShaderInfoLog(s));return s}
function initGL(){
 const vs=shader(gl.VERTEX_SHADER,'attribute vec2 pos; void main(){gl_Position=vec4(pos,0.0,1.0);}');
 const fs=shader(gl.FRAGMENT_SHADER,`precision highp float;
 uniform vec2 size; uniform float radius; uniform float yaw; uniform float pitch; uniform sampler2D atlas;
 const float PI=3.14159265359;
 void main(){vec2 p=(gl_FragCoord.xy-size/2.0)/radius;float r=dot(p,p);if(r>1.0){gl_FragColor=vec4(0.0);return;}float z=sqrt(max(0.0,1.0-r));
 float s=sin(pitch),c=cos(pitch);float lat=asin(clamp(p.y*c+z*s,-1.0,1.0));float lon=yaw+atan(p.x,z*c-p.y*s);vec2 uv=vec2(fract(lon/(2.0*PI)+0.5),0.5-lat/PI);
 vec3 col=texture2D(atlas,uv).rgb;float diffuse=0.76+0.24*max(0.0,dot(vec3(p,z),normalize(vec3(-0.32,0.42,0.9))));
 float rim=pow(1.0-z,3.0);col=col*diffuse+vec3(0.11,0.20,0.25)*rim;float alpha=clamp((1.0-sqrt(r))*radius,0.0,1.0);gl_FragColor=vec4(col,alpha);}`);
 program=gl.createProgram();gl.attachShader(program,vs);gl.attachShader(program,fs);gl.linkProgram(program);if(!gl.getProgramParameter(program,gl.LINK_STATUS))throw Error(gl.getProgramInfoLog(program));gl.useProgram(program);
 const b=gl.createBuffer();gl.bindBuffer(gl.ARRAY_BUFFER,b);gl.bufferData(gl.ARRAY_BUFFER,new Float32Array([-1,-1,1,-1,-1,1,-1,1,1,-1,1,1]),gl.STATIC_DRAW);const loc=gl.getAttribLocation(program,'pos');gl.enableVertexAttribArray(loc);gl.vertexAttribPointer(loc,2,gl.FLOAT,false,0,0);
 for(const n of ['size','radius','yaw','pitch','atlas'])uni[n]=gl.getUniformLocation(program,n);
 tex=gl.createTexture();gl.bindTexture(gl.TEXTURE_2D,tex);gl.texParameteri(gl.TEXTURE_2D,gl.TEXTURE_WRAP_S,gl.CLAMP_TO_EDGE);gl.texParameteri(gl.TEXTURE_2D,gl.TEXTURE_WRAP_T,gl.CLAMP_TO_EDGE);gl.texParameteri(gl.TEXTURE_2D,gl.TEXTURE_MIN_FILTER,gl.LINEAR);gl.texParameteri(gl.TEXTURE_2D,gl.TEXTURE_MAG_FILTER,gl.LINEAR);
 renderTex=()=>{gl.bindTexture(gl.TEXTURE_2D,tex);gl.texImage2D(gl.TEXTURE_2D,0,gl.RGBA,gl.RGBA,gl.UNSIGNED_BYTE,texture)}
}
let cpuCanvas=null,ctxCPU=null;
if(gl){try{initGL()}catch(e){console.warn('Globe WebGL fallback',e);gl=null}}
if(!gl){cpuCanvas=document.createElement('canvas');cpuCanvas.id='globe';canvas.replaceWith(cpuCanvas);ctxCPU=cpuCanvas.getContext('2d')}
function size(){return stage.getBoundingClientRect()}
function radius(w,h){return Math.min(w,h)*.445*zoom}
function syncSize(c,w,h){if(c.width!==w)c.width=w;if(c.height!==h)c.height=h}
function drawCPU(cssW,cssH){const factor=Math.min(1,780/Math.max(cssW,cssH)),w=Math.max(1,Math.round(cssW*factor)),h=Math.max(1,Math.round(cssH*factor));syncSize(cpuCanvas,w,h);const img=ctxCPU.createImageData(w,h),buf=img.data,r=radius(w,h),cx=w/2,cy=h/2,s=Math.sin(pitch*RAD),c=Math.cos(pitch*RAD);
 for(let j=Math.max(0,Math.floor(cy-r));j<Math.min(h,Math.ceil(cy+r));j++){const y=(cy-j-.5)/r,half=r*Math.sqrt(Math.max(0,1-y*y));for(let i=Math.max(0,Math.floor(cx-half));i<Math.min(w,Math.ceil(cx+half));i++){const x=(i+.5-cx)/r,rr=x*x+y*y;if(rr>1)continue;const z=Math.sqrt(1-rr),lat=Math.asin(clamp(y*c+z*s,-1,1)),lon=yaw+Math.atan2(x,z*c-y*s)/RAD,tx=Math.min(W-1,Math.floor((wrap(lon)+180)*W/360)),ty=clamp(Math.floor((.5-lat/PI)*H),0,H-1),k=(ty*W+tx)*4,q=(j*w+i)*4,light=.76+.24*Math.max(0,-.31*x+.41*y+.86*z);buf[q]=raw[k]*light;buf[q+1]=raw[k+1]*light;buf[q+2]=raw[k+2]*light;buf[q+3]=255}}
 ctxCPU.putImageData(img,0,0)
}
function project(lon,lat,w,h){const l=lat*RAD,d=wrap(lon-yaw)*RAD,p=pitch*RAD, cos=Math.cos(l),x=cos*Math.sin(d),y=Math.sin(l)*Math.cos(p)-cos*Math.cos(d)*Math.sin(p),z=Math.sin(l)*Math.sin(p)+cos*Math.cos(d)*Math.cos(p),r=radius(w,h);return{x:w/2+x*r,y:h/2-y*r,z}}
function draw(){pending=false;if(!raw)return;const rect=size(),w=rect.width,h=rect.height;if(w<2||h<2)return;const dpr=Math.min(devicePixelRatio||1,1.7);
 if(gl){const pw=Math.round(w*dpr),ph=Math.round(h*dpr);syncSize(canvas,pw,ph);gl.viewport(0,0,pw,ph);gl.useProgram(program);gl.uniform2f(uni.size,pw,ph);gl.uniform1f(uni.radius,radius(w,h)*dpr);gl.uniform1f(uni.yaw,yaw*RAD);gl.uniform1f(uni.pitch,pitch*RAD);gl.uniform1i(uni.atlas,0);gl.drawArrays(gl.TRIANGLES,0,6)}
 else drawCPU(w,h);
 syncSize(overlay,Math.round(w*dpr),Math.round(h*dpr));ctxO.setTransform(dpr,0,0,dpr,0,0);ctxO.clearRect(0,0,w,h);
 for(let n=0;n<landLabels.length;n++){const q=project(...landLabels[n],w,h);if(q.z<.23||q.x<10||q.y<10||q.x>w-10||q.y>h-10)continue;ctxO.beginPath();ctxO.arc(q.x,q.y,9.2,0,TAU);ctxO.fillStyle='#f0eddaed';ctxO.fill();ctxO.strokeStyle='#4c6979';ctxO.lineWidth=1;ctxO.stroke();ctxO.fillStyle='#233747';ctxO.font='bold 11px system-ui';ctxO.textAlign='center';ctxO.textBaseline='middle';ctxO.fillText(n+1,q.x,q.y+.3)}
 const formatted=(v,pos,neg)=>Math.abs(v).toFixed(0)+'° '+(v>=0?pos:neg);
 $('coords').textContent='Центр: '+formatted(pitch,'с.ш.','ю.ш.')+' · '+formatted(yaw,'в.д.','з.д.');
}
function requestDraw(){if(!pending){pending=true;requestAnimationFrame(draw)}}
function setZoom(z){zoom=clamp(z,.65,2.4);requestDraw()}
function reset(){yaw=-112;pitch=10;zoom=1;requestDraw()}
const pointers=new Map();function gap(){const [a,b]=[...pointers.values()];return Math.hypot(a.x-b.x,a.y-b.y)}
overlay.addEventListener('pointerdown',e=>{e.preventDefault();overlay.focus({preventScroll:true});overlay.setPointerCapture(e.pointerId);pointers.set(e.pointerId,{x:e.clientX,y:e.clientY});});
overlay.addEventListener('pointermove',e=>{if(!pointers.has(e.pointerId))return;const old=pointers.get(e.pointerId),before=pointers.size===2?gap():0;pointers.set(e.pointerId,{x:e.clientX,y:e.clientY});if(pointers.size===2&&before>0){setZoom(zoom*gap()/before);return}if(pointers.size!==1)return;const b=size(),r=radius(b.width,b.height);yaw=wrap(yaw-(e.clientX-old.x)*55/r);pitch=clamp(pitch+(e.clientY-old.y)*55/r,-89,89);requestDraw()});
function release(e){pointers.delete(e.pointerId)}overlay.addEventListener('pointerup',release);overlay.addEventListener('pointercancel',release);overlay.addEventListener('lostpointercapture',release);
overlay.addEventListener('wheel',e=>{e.preventDefault();setZoom(zoom*Math.exp(-e.deltaY*.0015))},{passive:false});
overlay.addEventListener('keydown',e=>{let handled=true;switch(e.key){case 'ArrowLeft':yaw=wrap(yaw-10);break;case 'ArrowRight':yaw=wrap(yaw+10);break;case 'ArrowUp':pitch=clamp(pitch+10,-89,89);break;case 'ArrowDown':pitch=clamp(pitch-10,-89,89);break;case '+':case '=':setZoom(zoom*1.18);break;case '-':setZoom(zoom/1.18);break;case 'Home':reset();break;default:handled=false}if(handled){e.preventDefault();requestDraw()}});
$('zoomIn').onclick=()=>setZoom(zoom*1.2);$('zoomOut').onclick=()=>setZoom(zoom/1.2);$('reset').onclick=reset;
$('fullscreen').onclick=()=>{if(document.fullscreenElement){document.exitFullscreen();return}const a=document.querySelector('.atlas');if(a.requestFullscreen)a.requestFullscreen().catch(()=>{});else window.open(location.href,'_blank','noopener')};
for(const el of [layer,season,grid])el.addEventListener('change',textureFor);
if('ResizeObserver'in window)new ResizeObserver(requestDraw).observe(stage);else addEventListener('resize',requestDraw);
textureFor();
})();
