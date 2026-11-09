// Run from wiki-site/: node export-github.mjs [output-directory]
// Default output is the repository root: index.html + wiki/.
import './build.mjs';
import fs from 'node:fs';
import path from 'node:path';
const target=path.resolve(process.argv[2]||'..');
const pages=path.join(target,'wiki');
fs.mkdirSync(pages,{recursive:true});
let index=fs.readFileSync('dist/index.html','utf8');
index=index.replace(/(href|src)="([^"]+)"/g,(m,attr,url)=>{
  if(/^(https?:|#|mailto:)/.test(url)||url==='index.html')return m;
  return attr+'="wiki/'+url+'"';
});
// The root page and article pages use different relative search-result paths.
index=index.replace('<body data-page=', '<body data-search-prefix="wiki/" data-page=');
fs.writeFileSync(path.join(target,'index.html'),index);
for(const entry of fs.readdirSync('dist')){
 if(entry==='index.html'||entry==='qa-phone.html')continue;
 let data=fs.readFileSync(path.join('dist',entry));
 if(entry.endsWith('.html'))data=Buffer.from(data.toString().replaceAll('href="index.html"','href="../index.html"'));
 fs.writeFileSync(path.join(pages,entry),data);
}
fs.writeFileSync(path.join(target,'.nojekyll'),'');
console.log('Exported '+target+'/index.html and '+target+'/wiki/');
