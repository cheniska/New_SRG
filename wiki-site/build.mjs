import fs from 'node:fs';
import path from 'node:path';
import {marked} from './vendor/marked.mjs';
const input=fs.existsSync('content')?'content':'../GameDoc', out='dist';
fs.mkdirSync(out,{recursive:true});
const names=fs.readdirSync(input).filter(n=>/^(race_|reference_).*\.md$/.test(n));
const docs=names.map(file=>{let md=fs.readFileSync(path.join(input,file),'utf8');const title=md.match(/^title: "(.*)"$/m)?.[1]||file;md=md.replace(/^---\n[\s\S]*?\n---\n/,'');return {file,title,md,slug:file.replace(/\.md$/,'.html'),race:file.startsWith('race_')};});
const map=new Map(docs.map(d=>[d.file,d]));
const esc=s=>s.replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const source=n=>'https://github.com/cheniska/New_SRG/blob/main/GameDoc/'+n;
const plain=s=>s.replace(/<[^>]+>/g,'').replace(/\[([^\]]+)\]\([^)]*\)/g,'$1').replace(/[#*_>`]/g,'').replace(/\s+/g,' ').trim();
const url=u=>{if(u.startsWith('#')||/^(https?:|mailto:)/.test(u))return u;let [f,a]=u.split('#');if(map.has(f))return map.get(f).slug+(a?'#'+a:'');return source(f)+(a?'#'+a:'');};
function render(md){return marked.parse(md.replace(/<!-- section-status: [^>]*-->/g,'').replace(/\[([^\]\n]+)\]\(([^)\n]+)\)/g,(_,l,u)=>'['+l+']('+url(u)+')'));}
const high=/([\s\S]*?)(?=<a id=")/;
const terms=[[/\bNEVERMATCH\b/g,''],[/синапт(?:ы|ов|ам|ами|ах)(?![а-я])/gi,'race_sinapty.md'],[/ксенопод(?:ы|ов|ам|ами|ах)(?![а-я])/gi,'race_ksenopody.md'],[/грондар(?:ы|ов|ам|ами|ах)(?![а-я])/gi,'race_grondary.md'],[/этериан(?:е|ов|ам|ами|ах)(?![а-я])/gi,'race_etheriane.md'],[/тарна(?:и|ев|ям|ями|ях)(?![а-я])/gi,'race_propovedniki.md'],[/хамари/gi,'race_hamari.md'],[/заргаш(?:и|ей|ам|ами|ах)(?![а-я])/gi,'race_zargashi.md'],[/аэлит(?:ы|ов|ам|ами|ах)(?![а-я])/gi,'race_aelity.md'],[/дейми/gi,'race_deymi.md'],[/вравши/gi,'race_vravshi.md']];
function autoLinks(html,own){let inside=0;const used=new Set();return html.split(/(<[^>]+>)/g).map(t=>{if(t.startsWith('<')){if(/^<a\b/i.test(t))inside++;if(/^<\/a/i.test(t))inside--;return t;}if(inside)return t;for(const [rx,file] of terms){if(file===own||used.has(file)||!map.has(file))continue;rx.lastIndex=0;if(rx.test(t)){rx.lastIndex=0;let done=false;t=t.replace(rx,m=>{if(done)return m;done=true;return '<a class="entity-link" href="'+map.get(file).slug+'">'+m+'</a>';});used.add(file);}}return t;}).join('');}
const pretty=h=>h.replace(/^[IVX]+(?:[.А-Яа-я\d–-]+)?\.\s*/,'').replace(/^[А-ЯЁ\s/–:()\d.,-]+$/,x=>x[0]+x.slice(1).toLowerCase());
const group=d=>d.race?'Цивилизации':'Справочники';
function nav(){return '<a class="nav-home" href="index.html">Обзор энциклопедии</a>'+[true,false].map(r=>'<div class="nav-label">'+(r?'Цивилизации':'История и устройство мира')+'</div>'+docs.filter(d=>d.race===r).map(d=>'<a data-page="'+d.slug+'" href="'+d.slug+'">'+esc(d.title)+'</a>').join('')).join('');}
function shell(title,slug,body,toc=''){return `<!doctype html><html lang="ru"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>${esc(title)} · New SRG</title><meta name="description" content="Энциклопедия вселенной New SRG: цивилизации, история, технологии и миры."><link rel="stylesheet" href="style.css"><link rel="stylesheet" href="wiki-theme.css"></head><body data-page="${slug}"><header class="top"><button class="menu icon" aria-label="Открыть меню" aria-expanded="false">☰</button><a class="brand" href="index.html"><b>NS<span>·</span></b><span>NEW SRG<small>ЭНЦИКЛОПЕДИЯ</small></span></a><button class="search-open"><span>Поиск по энциклопедии</span><kbd>/</kbd></button><button class="theme icon" aria-label="Переключить тему">◐</button><a class="github" href="https://github.com/cheniska/New_SRG/tree/main/GameDoc">GitHub ↗</a></header><div class="shade"></div><aside class="sidebar"><div class="edition">МИРЫ · НАРОДЫ · ИСТОРИЯ</div>${nav()}<div class="sidefoot">New SRG<br><span>Материалы разработчика</span></div></aside><div class="layout"><main>${body}<footer>NEW SRG · ЭНЦИКЛОПЕДИЯ <a href="index.html">К оглавлению ↑</a></footer></main>${toc?'<aside class="toc"><div>НА ЭТОЙ СТРАНИЦЕ</div>'+toc+'</aside>':''}</div><dialog id="search"><div class="search-head"><input id="query" type="search" placeholder="Расы, события, технологии…" aria-label="Поиск по текстам"><button class="search-close icon" aria-label="Закрыть поиск">×</button></div><div id="results"><p>Введите название или несколько слов. Поиск работает по полным текстам статей.</p></div></dialog><script src="app.js" defer></script></body></html>`;}
const abstracts={};
for(const d of docs){
const parts=d.md.split(/(?=<a id=")/);let body='',toc='',technical=false,groupOpen=false;
const intro=parts.shift().trimStart().replace(/^# [^\n]+(?:\n|$)/,'').trim();
const firstOverview=parts.find(s=>s.startsWith('<a id="overview"'))||'';
const overviewText=firstOverview.replace(/<a[^>]+><\/a>/g,'').replace(/<!--.*?-->/g,'').replace(/^#{1,4} .*$/gm,'').trim();
abstracts[d.file]=plain(overviewText||intro||d.md).slice(0,230).replace(/\s+\S*$/, '')+'…';
for(const part of parts){const id=part.match(/^<a id="([^"]+)"/)?.[1],hm=part.match(/^(#{2,4}) (.+)$/m);if(!hm){body+=render(part);continue;}const level=hm[1].length,label=pretty(hm[2]),isTop=level===2;
if(isTop&&groupOpen){body+='</div></details>';groupOpen=false;}
let md=part.replace(/^<a id="[^"]+"><\/a>\s*/,'').replace(/<!-- section-status: [^>]*-->/g,'').replace(/^#{2,4} .+$/m,'').trim();
const tech=isTop&&((d.race&&['biology','architecture','homeworld','technology'].includes(id))||['reference_gipersfera.md','reference_spravochnik_sistem.md'].includes(d.file));
if(isTop){toc+='<a href="#'+id+'">'+esc(label)+'</a>';}
if(tech){body+='<details class="technical" id="'+id+'"><summary><span class="detail-kicker">СПРАВОЧНЫЙ РАЗДЕЛ</span><span>'+esc(label)+'</span><i>+</i></summary><div class="detail-body">';groupOpen=true;body+=render(md);}
else if(id==='overview.card'){body+='<details class="technical compact" id="'+id+'"><summary><span>Численные параметры и карточка расы</span><i>+</i></summary><div class="detail-body">'+render(md)+'</div></details>';}
else {body+='<section id="'+id+'" class="article-section"><h'+level+'>'+esc(label)+'<a class="anchor" href="#'+id+'" aria-label="Ссылка на раздел">#</a></h'+level+'>'+render(md)+'</section>';}
}
if(groupOpen)body+='</div></details>';
if(!parts.length)body=render(d.md.replace(/^# .+\n/,''));
body=autoLinks(body,d.file);
const related=d.race?['reference_period_2500_2800.md','reference_spravochnik_sistem.md','reference_gipersfera.md','reference_instituty_sodruzhestva.md']:['race_sinapty.md','race_ksenopody.md','reference_history_extended.md'];
const hero='<div class="wiki-tabs"><span class="selected">Статья</span><a href="'+source(d.file)+'">Исходник ↗</a></div><div class="crumb"><a href="index.html">Энциклопедия</a><span>/</span>'+group(d)+'</div><div class="article-title"><div class="eyebrow">'+group(d)+'</div><h1>'+esc(d.title)+'</h1><div class="article-tools"><a href="'+source(d.file)+'">Исходная статья ↗</a><button id="expand">Раскрыть справочные блоки</button></div></div>';
const lead=intro?'<div class="lead">'+render(intro)+'</div>':'';
const links='<section class="related"><div class="eyebrow">ПРОДОЛЖИТЬ ЧТЕНИЕ</div>'+related.filter(n=>n!==d.file&&map.has(n)).map(n=>'<a href="'+map.get(n).slug+'">'+esc(map.get(n).title)+' <span>↗</span></a>').join('')+'</section>';
fs.writeFileSync(path.join(out,d.slug),shell(d.title,d.slug,hero+lead+'<details class="mobile-toc"><summary>Содержание статьи</summary>'+toc+'</details><article>'+body+'</article>'+links,toc));
}
const races=docs.filter(d=>d.race),refs=docs.filter(d=>!d.race);
const home='<div class="wiki-tabs"><span class="selected">Главная</span><a href="reference_period_2500_2800.html">История</a><a href="reference_instituty_sodruzhestva.html">Содружество</a></div><div class="crumb">ЭНЦИКЛОПЕДИЯ ВСЕЛЕННОЙ</div><section class="homehero"><div class="eyebrow">NEW SRG / БАЗА ЗНАНИЙ</div><h1>Галактика.<br>Её народы и истории.</h1><p>От первых контактов до общего Содружества. Цивилизации, миры, технологии и события — в одной связанной энциклопедии.</p><div class="home-meta"><span>'+races.length+' цивилизаций</span><span>'+refs.length+' справочников</span><span>Полные тексты и перекрёстные ссылки</span></div></section><section class="feature"><div><div class="eyebrow">СВЯЗУЮЩАЯ ИСТОРИЯ</div><h2>Эпоха контактов<br>2500–2800</h2><p>Войны, первые встречи и путь шести цивилизаций к Содружеству.</p><a href="reference_period_2500_2800.html">Читать историю <span>→</span></a></div><div class="feature-orbit" aria-hidden="true"><i></i><i></i><i></i><b>2800</b><span>ОСНОВАНИЕ СОДРУЖЕСТВА</span></div></section><div class="section-top"><h2>Цивилизации</h2><input id="filter" placeholder="Найти цивилизацию…" aria-label="Фильтр цивилизаций"></div><div class="race-grid">'+races.map((d,i)=>'<a class="race-card" href="'+d.slug+'" data-title="'+esc(d.title.toLowerCase())+'"><div class="card-no">'+String(i+1).padStart(2,'0')+' / ЦИВИЛИЗАЦИЯ<span>↗</span></div><h3>'+esc(d.title)+'</h3><p>'+esc(abstracts[d.file])+'</p><div class="card-link">Открыть статью →</div></a>').join('')+'</div><p id="filter-empty" hidden>Цивилизация с таким названием не найдена.</p><div class="section-top"><h2>Устройство мира</h2><span>'+refs.length+' справочников</span></div><div class="ref-grid">'+refs.map(d=>'<a href="'+d.slug+'"><span>↗</span><h3>'+esc(d.title)+'</h3><p>'+esc(abstracts[d.file].slice(0,135))+'</p></a>').join('')+'</div>';
fs.writeFileSync(path.join(out,'index.html'),shell('Энциклопедия','index.html',home));
fs.writeFileSync(path.join(out,'search.json'),JSON.stringify(docs.map(d=>({title:d.title,url:d.slug,text:plain(d.md)}))));
for(const n of ['style.css','wiki-theme.css','app.js'])fs.copyFileSync(n,path.join(out,n));
console.log('Built '+docs.length+' articles and index.');
