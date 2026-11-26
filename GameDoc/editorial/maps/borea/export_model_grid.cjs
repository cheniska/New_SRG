// Optional editorial reproducibility helper. Requires Node.js and @napi-rs/canvas.
// Run here: node export_model_grid.cjs [path/to/wiki-site/borea-natural.js]
// Binary outputs are intermediate files, not intended for Git.
const fs=require('fs'),path=require('path'),vm=require('vm'),{createCanvas}=require('@napi-rs/canvas');
const root=__dirname,naturalPath=process.argv[2]||path.resolve(root,'../../../../wiki-site/borea-natural.js');
const base=JSON.parse(fs.readFileSync(path.join(root,'continents_draft.geojson'),'utf8')),tect=JSON.parse(fs.readFileSync(path.join(root,'tectonics_draft.geojson'),'utf8')).features,cl=JSON.parse(fs.readFileSync(path.join(root,'climate_draft.json'),'utf8'));
const W=1536,H=768,RAD=Math.PI/180,wrap=x=>((x+180)%360+360)%360-180;
const land=base.features.filter(f=>['continent','island'].includes(f.properties.category)),north=base.features.find(f=>f.properties.category==='schematic_northern_perennial_ice_extent');
function surface(){return createCanvas(W,H)}
function xy(lon,lat){return[(lon+180)*W/360,(90-lat)*H/180]}
function mask(features){const c=surface(),ctx=c.getContext('2d');ctx.fillStyle='#fff';for(const f of features){ctx.beginPath();f.geometry.coordinates[0].forEach(([lo,la],i)=>{const q=xy(lo,la);if(i)ctx.lineTo(...q);else ctx.moveTo(...q)});ctx.closePath();ctx.fill()}return ctx.getImageData(0,0,W,H).data}
const lm=mask(land),nm=mask([north]),isLand=new Uint8Array(W*H),isNorth=new Uint8Array(W*H),scores=new Float32Array(W*H),lats=new Float32Array(H),weights=new Float32Array(H);
for(let j=0;j<H;j++){const la=90-(j+.5)*180/H;lats[j]=la;weights[j]=Math.cos(la*RAD);for(let i=0;i<W;i++){let k=j*W+i,lo=-180+(i+.5)*360/W;isLand[k]=lm[4*k+3]>127;isNorth[k]=nm[4*k+3]>127;scores[k]=99;if(isLand[k]||isNorth[k])continue;for(const b of cl.potential_seasonal_opening_basins){const a=wrap(lo-b.center[0])/b.axes[0],u=(la-b.center[1])/b.axes[1],theta=Math.atan2(u,a),ir=1+.085*Math.sin(3*theta+b.phase)+.045*Math.sin(5*theta-b.phase);scores[k]=Math.min(scores[k],(a*a+u*u)/(ir*ir))}}}
vm.runInThisContext(fs.readFileSync(naturalPath,'utf8'),{filename:naturalPath});const model=BoreaNatural.create({W,H,isLand,isNorth,scores,lats,tect,surface,xy,phases:cl.seasonal_preview_parameters,weights});
for(const [name,a] of Object.entries({height:model.height,hazard:model.hazard,land:isLand}))fs.writeFileSync(path.join(root,'model_'+name+'.bin'),Buffer.from(a.buffer,a.byteOffset,a.byteLength));
fs.writeFileSync(path.join(root,'model_grid.json'),JSON.stringify({W,H}));console.log('DEM export',W,H,model.range);
