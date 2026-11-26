/* Borea working hydrography and infrastructure on the common atlas grid.
   Visual widths and urban pixels are planetary-scale cartographic exaggerations. */
'use strict';
globalThis.BoreaWorld=(()=>{
const clamp=(x,a=0,b=1)=>Math.max(a,Math.min(b,x)),mix=(a,b,t)=>a+(b-a)*t,delta=(x,y)=>((x-y+540)%360)-180;
const hems={spring:-.32,summer:1,autumn:.50,winter:-1};
function create({W,H,isLand,lats,natural,nodes,waterFeatures,xy}){
 const N=W*H,S=W/1536,roads=waterFeatures.filter(f=>f.properties.category==='transport_corridor'),rivers=waterFeatures.filter(f=>['river','tributary'].includes(f.properties.category)),lakes=waterFeatures.filter(f=>f.properties.category==='lake_candidate');
 let urban=null;
 function noise(x,y){let a=Math.imul(x+5123,374761393)^Math.imul(y+1297,668265263);a=Math.imul(a^(a>>>13),1274126177);return((a^(a>>>16))>>>0)/4294967295}
 function buildUrban(){if(urban)return;urban=new Float32Array(N);
  for(const f of nodes){const [lo,la]=f.geometry.coordinates,kind=f.properties.kind,R=kind==='major'?1.04:kind==='regional'?.65:.39,metric=Math.max(.12,Math.cos(la*Math.PI/180));
   const xmin=Math.floor((lo-2.1*R/metric+180)*W/360),xmax=Math.ceil((lo+2.1*R/metric+180)*W/360),ymin=Math.max(0,Math.floor((90-la-2.1*R)*H/180)),ymax=Math.min(H-1,Math.ceil((90-la+2.1*R)*H/180));
   for(let y=ymin;y<=ymax;y++)for(let xi=xmin;xi<=xmax;xi++){const x=(xi+W)%W,k=y*W+x;if(!isLand[k]||natural.height[k]>2200)continue;
    const d2=(delta(-180+(x+.5)*360/W,lo)*metric/R)**2+((lats[y]-la)/R)**2;
    const q=Math.exp(-d2*1.9),tex=.35+.65*noise(x,y);
    // Discrete textured industrial districts with a clear center, not a uniform built-up circle.
    let m=q*(d2<.22?1:tex)*(kind==='specialized'?.76:1);
    if(d2>1.2)m*=.22;urban[k]=Math.max(urban[k],m);
   }
  }
 }
 function infrastructure(buf,night){buildUrban();
  for(let y=0;y<H;y++)for(let x=0;x<W;x++){const k=y*W+x,q=k*4,u=urban[k];
   if(night){let base=[buf[q]*.095+5,buf[q+1]*.115+6,buf[q+2]*.145+12];const lights=clamp(u*1.48),f=lights*(.72+.28*noise(x*3,y*5)),halo=Math.pow(lights,.66);
    buf[q]=clamp(base[0]+208*f+32*halo,0,255);buf[q+1]=clamp(base[1]+170*f+28*halo,0,255);buf[q+2]=clamp(base[2]+96*f+22*halo,0,255);
   }else if(u>.035){let shade=83+85*noise(x,y),t=clamp(u*.93);
    buf[q]=mix(buf[q],shade,t);buf[q+1]=mix(buf[q+1],shade+5,t);buf[q+2]=mix(buf[q+2],shade+8,t);
   }
  }
 }
 function landIndex(lon,lat){const x=((Math.floor((lon+180)*W/360)%W)+W)%W,y=clamp(Math.floor((90-lat)*H/180),0,H-1);return y*W+x}
 function frozen(lon,lat,phase){const k=landIndex(lon,lat),h=Math.max(0,natural.height[k]),ab=Math.abs(lat),p=phase==='overview'?(lat>=0?'summer':'winter'):phase;
  const t=2-.49*ab-.0045*h+(10+10*ab/90)*hems[p]*Math.tanh(lat/6)+16*natural.warm[k];
  return t<0;
 }
 // Seam-safe line segment for atlas projection (all current routes are on individual continents).
 function path(ctx,points){ctx.beginPath();points.forEach(([lo,la],i)=>{const q=xy(lo,la);if(i===0||Math.abs(lo-points[i-1][0])>180)ctx.moveTo(...q);else ctx.lineTo(...q)})}
 function water(ctx,phase,mode){const map=mode==='hydrology',optical=mode==='surface'||mode==='day';
  if(!map&&!optical)return;
  ctx.save();ctx.lineCap='round';ctx.lineJoin='round';
  for(const f of rivers){let a=f.geometry.coordinates,p=f.properties;const w=(p.category==='river'?1.55:.95)*S;
   for(let i=0;i<a.length-1;i++){const u=a[i],v=a[i+1],steps=Math.max(1,Math.ceil(Math.hypot(delta(v[0],u[0])*Math.cos(u[1]*Math.PI/180),v[1]-u[1])/.8));for(let j=0;j<steps;j++){const b=[mix(u[0],v[0],j/steps),mix(u[1],v[1],j/steps)],c=[mix(u[0],v[0],(j+1)/steps),mix(u[1],v[1],(j+1)/steps)],m=[mix(b[0],c[0],.5),mix(b[1],c[1],.5)],ice=frozen(...m,phase);if(optical&&ice)continue;
    ctx.strokeStyle=map?(ice?'#4d7791':'#12659d'):'#235264b8';ctx.lineWidth=map?w:.66*S;if(map&&ice)ctx.setLineDash([3*S,2*S]);else ctx.setLineDash([]);path(ctx,[b,c]);ctx.stroke();
   }}
  }
  ctx.setLineDash([]);
  for(const f of lakes){let [lo,la]=f.geometry.coordinates,ice=frozen(lo,la,phase),[x,y]=xy(lo,la),r=f.properties.radius_deg*W/360;
   if(optical&&ice)continue;ctx.beginPath();ctx.ellipse(x,y,Math.max(map?2.0*S:0.8*S,r*.88),Math.max(map?1.6*S:.7*S,r*.62),.22,0,Math.PI*2);ctx.fillStyle=map?(ice?'#cbe5ef':'#146ea0'):'#245d72';ctx.fill();if(map){ctx.strokeStyle='#36566c';ctx.lineWidth=.7*S;ctx.stroke()}
  }ctx.restore();
 }
 function transport(ctx,mode){const day=mode==='day',night=mode==='night',map=mode==='transport';if(!day&&!night&&!map)return;
  ctx.save();ctx.lineCap='round';ctx.lineJoin='round';
  for(const f of roads){const p=f.properties,main=p.kind==='trunk',a=f.geometry.coordinates;
   ctx.setLineDash([]);if(day){ctx.strokeStyle=main?'#33383c9c':'#53514f8c';ctx.lineWidth=(main?1.05:.72)*S;path(ctx,a);ctx.stroke()}
   else if(night){ctx.strokeStyle=main?'#db9f6555':'#ad885133';ctx.lineWidth=(main?.9:.55)*S;ctx.setLineDash([2.1*S,2.9*S]);path(ctx,a);ctx.stroke();
    // Lights on short stretches by the urban endpoints and junctions; no permanently illuminated planetary highways.
    for(const seq of [a.slice(0,Math.min(3,a.length)),a.slice(Math.max(0,a.length-3))]){
     if(seq.length<2)continue;let [u,v]=seq.length===a.length?seq:[seq[0],seq[seq.length-1]],dx=delta(v[0],u[0])*Math.cos(u[1]*Math.PI/180),dy=v[1]-u[1];if(Math.hypot(dx,dy)>4.5)continue;ctx.setLineDash([1.0*S,1.7*S]);ctx.strokeStyle='#ffd39a88';ctx.lineWidth=1.05*S;path(ctx,seq);ctx.stroke()
    }
   }else{ctx.strokeStyle='#fff5d8a0';ctx.lineWidth=(main?3.5:2.6)*S;path(ctx,a);ctx.stroke();ctx.strokeStyle=main?'#2e586c':'#b0784c';ctx.lineWidth=(main?1.9:1.35)*S;path(ctx,a);ctx.stroke()}
  }ctx.restore()
 }
 return{infrastructure,water,transport,frozen,counts:{roads:roads.length,rivers:rivers.filter(f=>f.properties.category==='river').length,tributaries:rivers.filter(f=>f.properties.category==='tributary').length,lakes:lakes.length}};
}
return{create};
})();
