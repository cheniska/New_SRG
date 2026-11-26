/* Relative Borea settlement model, 3290. No census, no fixed city names.
   The index is normalized to the highest modeled cell. All centers are proposals. */
'use strict';
globalThis.BoreaSettlement=(()=>{
const clamp=(x,a=0,b=1)=>Math.max(a,Math.min(b,x)),mix=(a,b,t)=>a+(b-a)*t;
const stops=[[0,[232,235,232]],[.01,[207,223,218]],[.1,[164,196,190]],[.5,[238,222,139]],[2,[244,167,82]],[8,[208,79,70]],[25,[173,65,88]],[60,[139,43,95]],[100,[66,24,83]]];
function color(q){for(let i=1;i<stops.length;i++){const [b,cb]=stops[i],[a,ca]=stops[i-1];if(q<=b){const t=clamp((q-a)/(b-a));return ca.map((v,j)=>mix(v,cb[j],t))}}return stops.at(-1)[1]}
const delta=(x,y)=>((x-y+540)%360)-180;
function create({W,H,isLand,isNorth,lats,weights,natural,tect,nodes}){
 const h=natural.height,risk=natural.hazard,N=W*H,field=tect.filter(f=>f.properties.category==='geothermal').map(f=>({x:f.geometry.coordinates[0],y:f.geometry.coordinates[1],kind:f.properties.kind}));
 const n=nodes.map(f=>{const [x,y]=f.geometry.coordinates;const kind=f.properties.kind;
 // Broad kernels represent an agglomerated region at planetary atlas scale, not individual streets.
 return{x,y,amp:kind==='major'?1:kind==='regional'?.24:.055,sigma:kind==='major'?1.17:kind==='regional'?.83:.67,kind,thermal:!!f.properties.thermal_field}});
 const value=new Float32Array(N);let max=0,total=0,eq=0,urban=0,major=0;
 for(let y=0;y<H;y++){const lat=lats[y],a=Math.abs(lat),metric=Math.max(.12,Math.cos(lat*Math.PI/180)),climate=.018+.982*Math.exp(-Math.pow(a/27,1.65));for(let x=0;x<W;x++){let k=y*W+x;if(!isLand[k]||h[k]>=6000)continue;const lon=-180+(x+.5)*360/W;
 let heat=1e5;for(const f of field){const dx=delta(lon,f.x)*metric,dy=lat-f.y;heat=Math.min(heat,dx*dx+dy*dy)}
 const supply=.13+.87*Math.exp(-Math.pow(Math.sqrt(heat)/5.5,1.3));
 const stable=.13+.87*Math.pow(1-clamp(risk[k]),2),alt=Math.exp(-Math.max(0,h[k]-600)/1200);
 // A nonzero floor allows modern remote settlement, but not in high mountain cores.
 const baseline=(.00013+.0023*climate*supply)*alt*stable;
 let urb=0,mj=0,halo=0;
 for(const u of n){const dx=delta(lon,u.x)*metric,dy=lat-u.y,d2=dx*dx+dy*dy;if(d2>180)continue;
   // Low-density periurban / service / rural hinterlands, distinct from dense city cores.
   halo+=.013*u.amp*Math.exp(-d2/(2*3.1*3.1))*climate*alt*stable*(.45+.55*supply);
   if(d2>56)continue;
   const v=u.amp*Math.exp(-d2/(2*u.sigma*u.sigma))*(.51+.49*climate)*Math.pow(alt,.55)*Math.pow(stable,.6);
   urb+=v;if(u.kind==='major')mj+=v;
 }
 const q=baseline+halo+urb;value[k]=q;max=Math.max(max,q);const w=weights[y];total+=q*w;urban+=urb*w;major+=mj*w;if(a<=18)eq+=q*w;
 }}
 function draw(buf){for(let k=0;k<N;k++){let c=isLand[k]?color(max?100*value[k]/max:0):isNorth[k]?[169,197,208]:[50,80,102],q=k*4;buf[q]=c[0];buf[q+1]=c[1];buf[q+2]=c[2];buf[q+3]=255}}
 return{draw,value,max,diagnostics:{equatorialShare:eq/total,urbanShare:urban/total,majorShare:major/total}};
}
return{create};
})();
