/* Borea optical and hypsometric textures. Deterministic editorial relief; not measured terrain.
   All coordinates are the same equirectangular grid as the map layers. No settlements. */
'use strict';
globalThis.BoreaNatural=(()=>{
const clamp=(x,a=0,b=1)=>Math.max(a,Math.min(b,x));
const smooth=(a,b,x)=>{const t=clamp((x-a)/(b-a));return t*t*(3-2*t)};
const mix=(a,b,t)=>a+(b-a)*t;
const rgb=s=>[parseInt(s.slice(1,3),16),parseInt(s.slice(3,5),16),parseInt(s.slice(5,7),16)];
const DEPTH=[[-8500,'#102951'],[-6000,'#194775'],[-3000,'#377da4'],[-1200,'#6eb4cf'],[-200,'#addce1'],[0,'#d0ede4']].map(([h,c])=>[h,rgb(c)]);
const ELEV=[[0,'#6eac75'],[250,'#a8bd76'],[750,'#d5cc84'],[1500,'#d4b064'],[2500,'#cd8848'],[3800,'#a75f36'],[5500,'#a65c35'],[7500,'#844527'],[10000,'#60311f']].map(([h,c])=>[h,rgb(c)]);
function palette(stops,h){for(let i=1;i<stops.length;i++){const [b,cb]=stops[i],[a,ca]=stops[i-1];if(h<=b){let t=clamp((h-a)/(b-a));return ca.map((v,j)=>mix(v,cb[j],t))}}return stops.at(-1)[1]}
function noiseGrid(nx,ny,seed){
 let x=seed>>>0;const p=new Float32Array(nx*(ny+1));
 for(let i=0;i<p.length;i++){x^=x<<13;x^=x>>>17;x^=x<<5;p[i]=(x>>>0)/4294967295}
 // longitude wraps and the ends collapse to a single north/south polar value
 for(let j of [0,ny]){let c=p[j*nx];for(let i=1;i<nx;i++)p[j*nx+i]=c}
 return (u,v)=>{u=((u%1)+1)%1*nx;v=clamp(v)*ny;const ax=Math.floor(u),ay=Math.min(ny-1,Math.floor(v)),bx=(ax+1)%nx,tx=u-ax,ty=v-ay,fx=tx*tx*(3-2*tx),fy=ty*ty*(3-2*ty);return mix(mix(p[ay*nx+ax],p[ay*nx+bx],fx),mix(p[(ay+1)*nx+ax],p[(ay+1)*nx+bx],fx),fy)};
}
function create({W,H,isLand,isNorth,scores,lats,tect,surface,xy,phases,weights}){
 const N=W*H,S=W/1152,dat=new Float32Array(N),hazard=new Float32Array(N),shade=new Float32Array(N),macro=new Float32Array(N),detail=new Float32Array(N),filament=new Float32Array(N);
 const n1=noiseGrid(12,6,487127),n2=noiseGrid(30,15,492483),n3=noiseGrid(67,34,82191),n4=noiseGrid(142,71,33489),n5=noiseGrid(310,155,78194),n6=noiseGrid(650,325,941377);
 const c1=noiseGrid(9,5,13515),c2=noiseGrid(23,12,82731),c3=noiseGrid(59,30,88327),c4=noiseGrid(138,69,79161),c5=noiseGrid(330,165,62192);
 function raster(cats,stroke=false){let c=surface(),ctx=c.getContext('2d',{willReadFrequently:true});ctx.fillStyle='#fff';ctx.strokeStyle='#fff';ctx.lineWidth=2*S;ctx.lineCap='round';ctx.lineJoin='round';
 for(const f of tect){if(!cats.includes(f.properties.category))continue;const pts=f.geometry.type==='Polygon'?f.geometry.coordinates[0]:f.geometry.coordinates;ctx.beginPath();pts.forEach(([lon,lat],i)=>{let p=xy(lon,lat);if(i)ctx.lineTo(...p);else ctx.moveTo(...p)});if(stroke)ctx.stroke();else{ctx.closePath();ctx.fill()}}
 const a=ctx.getImageData(0,0,W,H).data,m=new Uint8Array(N);for(let k=0;k<N;k++)m[k]=a[4*k+3]>100;return m}
 function distance(seed){const d=new Float32Array(N);for(let k=0;k<N;k++)d[k]=seed[k]?0:1e6;
 // Periodic chamfer distances. The horizontal metric shrinks toward poles.
 for(let pass=0;pass<2;pass++){
 for(let y=0;y<H;y++){const s=Math.max(.18,Math.cos(lats[y]*Math.PI/180)),diag=Math.sqrt(s*s+1),row=y*W;for(let x=0;x<W;x++){let k=row+x,left=row+(x+W-1)%W,right=row+(x+1)%W;let v=Math.min(d[k],d[left]+s);if(y)v=Math.min(v,d[k-W]+1,d[left-W]+diag,d[right-W]+diag);d[k]=v}}
 for(let y=H-1;y>=0;y--){const s=Math.max(.18,Math.cos(lats[y]*Math.PI/180)),diag=Math.sqrt(s*s+1),row=y*W;for(let x=W-1;x>=0;x--){let k=row+x,left=row+(x+W-1)%W,right=row+(x+1)%W;let v=Math.min(d[k],d[right]+s);if(y<H-1)v=Math.min(v,d[k+W]+1,d[left+W]+diag,d[right+W]+diag);d[k]=v}}}return d}
 const water=Uint8Array.from(isLand,v=>1-v),toLand=distance(isLand),toSea=distance(water),young=distance(raster(['mountain_belt'],true)),old=distance(raster(['old_mountain_belt'],true)),arc=distance(raster(['volcanic_arc'],true)),trench=distance(raster(['convergence'],true)),ridge=distance(raster(['ocean_rift'],true)),rift=distance(raster(['continental_rift'],true)),transform=distance(raster(['transform'],true)),massif=raster(['stable_massif']),low=raster(['lowland']);
 const massEdge=distance(Uint8Array.from(massif,v=>1-v)),lowEdge=distance(Uint8Array.from(low,v=>1-v));
 const extrema=tect.filter(f=>f.properties.category==='high_peak').map(f=>{const [lon,lat]=f.geometry.coordinates,i=Math.floor((lon+180)*W/360),j=Math.floor((90-lat)*H/180);return {lon:-180+(i+.5)*360/W,lat:90-(j+.5)*180/H,h:f.properties.height_m,i,j}});
 const oases=tect.filter(f=>f.properties.category==='geothermal'&&f.properties.kind==='oasis').map(f=>({lon:f.geometry.coordinates[0],lat:f.geometry.coordinates[1]}));
 const delta=(x,y)=>((x-y+540)%360)-180;
 let min=0,max=0,sumLand=0,landCount=0;
 for(let y=0;y<H;y++){let v=(y+.5)/H,lat=lats[y],metric=Math.max(.2,Math.cos(lat*Math.PI/180));for(let x=0;x<W;x++){let u=(x+.5)/W,k=y*W+x,lon=-180+(x+.5)*360/W;
  const a=n1(u,v),b=n2(u,v),c=n3(u,v),d=n4(u,v),e=n5(u,v),f=n6(u,v);
  macro[k]=a*.5+b*.32+c*.18;detail[k]=c*.31+d*.34+e*.23+f*.12;
  const ridged=1-Math.abs(2*d-1);filament[k]=1-Math.abs(2*n4(u+.018*(b-.5),v+.025*(a-.5))-1);
  if(isLand[k]){
   hazard[k]=Math.max(.92*Math.exp(-rift[k]/(2.7*S)),.96*Math.exp(-arc[k]/(2.8*S)),.85*Math.exp(-trench[k]/(3.7*S)),.72*Math.exp(-young[k]/(4.0*S)),.78*Math.exp(-transform[k]/(3.0*S)));
   const dc=toSea[k]/S,coast=smooth(0,12,dc),landBase=110+coast*(390+950*macro[k]+300*(detail[k]-.5));
   const youngInfluence=Math.exp(-Math.pow(young[k]/(9.5*S),1.4)),oldInfluence=Math.exp(-Math.pow(old[k]/(13.0*S),1.35)),arcInfluence=Math.exp(-Math.pow(arc[k]/(4.2*S),1.65));
   let height=landBase+(massif[k]?coast*(410+240*b)*smooth(0,11,massEdge[k]/S):0)+coast*(youngInfluence*(2250+1550*(.62*ridged+.38*detail[k]))+oldInfluence*(1150+980*detail[k])+arcInfluence*(850+730*detail[k]));
   if(low[k])height=mix(height,95+260*macro[k]+100*detail[k],.82*smooth(0,10,lowEdge[k]/S));
   height-=210*Math.exp(-Math.pow(rift[k]/(3.2*S),2))*coast;
   height+=coast*(detail[k]-.5)*(160+850*youngInfluence+380*oldInfluence);
   height=mix(50+110*detail[k],height,smooth(0,3.1,dc));
   // Rare high peaks sit on locally thickened, wide collision massifs; not solitary sea-level columns.
   for(const q of extrema){const dy=lat-q.lat,dx=delta(lon,q.lon)*metric;if(Math.abs(dy)>9||Math.abs(dx)>9)continue;const ds=dx*dx+dy*dy,plateau=Math.exp(-ds/10.9),summit=Math.exp(-ds/.51);height+=plateau*.9*Math.max(0,5200-height);height+=summit*Math.max(0,q.h-height);}
   // At low-latitude heat districts, a sheltered valley floor is distinct from its surrounding mountains.
   for(const q of oases){const dy=lat-q.lat,dx=delta(lon,q.lon)*metric;if(Math.abs(dy)>4||Math.abs(dx)>4)continue;const v=Math.exp(-(dx*dx+dy*dy)/1.5);height-=v*.93*Math.max(0,height-(340+180*macro[k]));}
   dat[k]=clamp(height,15,10000);sumLand+=dat[k];landCount++;
  }else{
   const dc=toLand[k]/S,trenchFactor=Math.exp(-Math.pow(trench[k]/(5.0*S),1.5)),shelf=smooth(1,mix(27,13,trenchFactor),dc);
   let depth=-(65+4450*shelf+850*shelf*(macro[k]-.5)+260*shelf*(detail[k]-.5));
   if(isNorth[k])depth=mix(depth,-(85+1300*smooth(0,30,dc)+400*macro[k]),.87);
   depth+=1750*Math.exp(-Math.pow(ridge[k]/(9*S),1.6))*shelf;
   depth=mix(depth,-(6800+1450*macro[k]),trenchFactor*smooth(1.5,5.0,dc));
   dat[k]=clamp(depth,-8500,-30);
  }
  min=Math.min(min,dat[k]);max=Math.max(max,dat[k]);
 }}
 for(let y=0;y<H;y++){for(let x=0;x<W;x++){let k=y*W+x,left=y*W+(x+W-1)%W,right=y*W+(x+1)%W,up=Math.max(0,y-1)*W+x,dn=Math.min(H-1,y+1)*W+x;
 const dx=isLand[left]===isLand[k]&&isLand[right]===isLand[k]?(dat[right]-dat[left])*.5:0,dy=isLand[up]===isLand[k]&&isLand[dn]===isLand[k]?(dat[dn]-dat[up])*.5:0;
 shade[k]=Math.tanh((dx+dy)/(140/S));
 }}
 // Cloud field is geographically continuous, warped and banded rather than arbitrary white circles.
 const cloudField=new Float32Array(N);
 for(let y=0;y<H;y++){const v=(y+.5)/H,lat=lats[y];for(let x=0;x<W;x++){const u=(x+.5)/W,k=y*W+x;
 let a=c1(u,v),b=c2(u,v),u2=u+.075*(a-.5)+.024*(b-.5)+.045*Math.sin(lat*Math.PI/28),v2=v+.052*(c1(u+.29,v)-.5)+.015*(b-.5);
 let cloud=.5*c2(u2,v2)+.25*c3(u2*1.0,v2)+.15*c4(u2,v2)+.10*c5(u2,v2);
 cloud+=.035*Math.cos(lat*Math.PI/30)+.025*Math.cos(lat*Math.PI/11+4*a);cloudField[k]=cloud;
 }}
 function relief(buf){for(let k=0;k<N;k++){const c=palette(dat[k]<0?DEPTH:ELEV,dat[k]),q=4*k,f=1+.13*shade[k];buf[q]=c[0]*f;buf[q+1]=c[1]*f;buf[q+2]=c[2]*f;buf[q+3]=255}}
 function optical(buf,mode,phase){
  // An annual envelope is not a physical snapshot. Use northern summer for the optical overview.
  const p=phase==='overview'?'summer':phase,ph=phases[p],hems={spring:-.32,summer:1,autumn:.50,winter:-1};let visible=0,cloudCount=0;
  const cloudMode=mode==='space',cloudAlpha=new Float32Array(N);
  for(let y=0;y<H;y++){const lat=lats[y],ab=Math.abs(lat),seasonal=hems[p]*Math.sin(lat*Math.PI/180),limit=clamp(ph.bias+ph.hem*clamp(lat/20,-1,1),.18,1);for(let x=0;x<W;x++){
   const k=y*W+x,q=k*4,m=macro[k],d=detail[k],z=dat[k],sh=shade[k];let R,G,B;
   if(isLand[k]){
    let snowPotential=(ab-13)/48+z/5300-.32*seasonal+.5*(m-.48)+.36*(d-.48);
    const snow=smooth(.11,.85,snowPotential);
    const rock=smooth(.25,.85,z/3200),wet=smooth(0,1,(m-.3)*2.2),earth=[mix(118,105,rock)-12*wet,mix(86,99,rock)-7*wet,mix(84,102,rock)-2*wet];
    let speck=1+(d-.5)*.66+.22*sh;
    // Red-brown/purplish cold flora on exposed ground, not Earth-green forests.
    const tundra=smooth(9,30,ab)*(1-smooth(54,72,ab))*(1-rock)*wet;
    earth[0]+=12*tundra;earth[1]-=13*tundra;earth[2]-=3*tundra;
    if(ab>70)speck*=.92+.18*d;
    let snowTone=[211+28*d,218+26*d,224+23*d],glacier=.12*smooth(.75,1,snow)*smooth(.35,.8,filament[k]);
    snowTone[0]-=25*glacier;snowTone[1]-=13*glacier;
    R=mix(earth[0]*speck,snowTone[0]*(1+.20*sh),snow);G=mix(earth[1]*speck,snowTone[1]*(1+.16*sh),snow);B=mix(earth[2]*speck,snowTone[2]*(1+.11*sh),snow);
   }else{
    const open=!isNorth[k]&&scores[k]<=limit;
    if(open)visible+=weights[y];
    const coast=smooth(-2200,-60,z),blue=[mix(24,54,coast),mix(47,103,coast),mix(69,118,coast)];
    const waterNoise=.84+.28*d+.07*(m-.5);blue[0]*=waterNoise;blue[1]*=waterNoise;blue[2]*=waterNoise;
    let ice= isNorth[k]?1:open?0:1;
    // A narrow broken-floe fringe around a seasonal edge; permanent ice stays intact.
    if(!isNorth[k]&&Math.abs(scores[k]-limit)<.13){const edge=(scores[k]-limit)/.13;ice=smooth(-.6,.65,edge+.48*(d-.5));if(scores[k]>1.0)ice=Math.max(ice,.82)}
    const edgeCool=clamp((1-smooth(0,1.2,Math.max(0,scores[k]-limit)))*.36);
    const crack=smooth(.89,.99,filament[k])*smooth(.32,.60,m)*.16;
    const packed=[mix(165,200,smooth(0,1,ab/70))+35*d,mix(184,217,smooth(0,1,ab/70))+26*d,mix(199,231,smooth(0,1,ab/70))+18*d];
    const decor=clamp(crack+edgeCool*(.18+.44*(1-d))+.20*smooth(.45,.78,m)*smooth(.4,.75,filament[k]),0,.46);
    R=mix(blue[0],mix(packed[0],111,decor),ice);G=mix(blue[1],mix(packed[1],157,decor),ice);B=mix(blue[2],mix(packed[2],175,decor),ice);
   }
   const thresh=.49+.018*smooth(55,88,ab)+(isLand[k]?.025:0)-(!isLand[k]&&scores[k]<=limit?.025:0);
   const clouds=.87*smooth(thresh,thresh+.16,cloudField[k]);cloudAlpha[k]=clouds;cloudCount+=clouds*weights[y];
   buf[q]=clamp(R,0,255);buf[q+1]=clamp(G,0,255);buf[q+2]=clamp(B,0,255);buf[q+3]=255;
  }}
  if(cloudMode){for(let y=0;y<H;y++)for(let x=0;x<W;x++){let k=y*W+x,q=4*k,c=cloudAlpha[k],shadow=cloudAlpha[Math.max(0,y-2)*W+(x+W-4)%W]*(1-c)*.14;
   const cR=239+12*detail[k],cG=243+8*detail[k],cB=247+5*detail[k];
   buf[q]=mix(buf[q]*(1-shadow),cR,c);buf[q+1]=mix(buf[q+1]*(1-shadow),cG,c);buf[q+2]=mix(buf[q+2]*(1-shadow),cB,c);
  }}
  return {visible,cloudCount,phase:p,cloudAlpha};
 }
 return {relief,optical,height:dat,hazard,range:[min,max],meanLand:sumLand/landCount};
}
return {create};
})();
