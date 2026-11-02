// Plan B: independent Splatypus concept. Never writes Plan A or plugin sources.
// node Assets/Icons-Splatypus/generate.cjs [path-to-sharp]
const fs=require('fs'),path=require('path');
const sharp=require(process.argv[2]||'sharp');
const root=__dirname;
const A=path.join(root,'../Icons');
const C={ink:'#6B778A',soft:'#A0ABB8',teal:'#398A94',bill:'#B99766',green:'#368D3C',red:'#F04448',blue:'#3989BE',rust:'#C56B50',tip:'#B90D2B'};
const p=(d,c=C.ink,w=1.65)=>`<path d="${d}" fill="none" stroke="${c}" stroke-width="${w}" stroke-linecap="round" stroke-linejoin="round"/>`;
const f=(d,c)=>`<path d="${d}" fill="${c}"/>`;
const dot=(x,y,r=1.4,c=C.red)=>`<circle cx="${x}" cy="${y}" r="${r}" fill="${c}"/>`;
const g=(body,t)=>`<g transform="${t}">${body}</g>`;
const rect=(x,y,w,h,c=C.ink,r=1)=>`<rect x="${x}" y="${y}" width="${w}" height="${h}" rx="${r}" fill="none" stroke="${c}" stroke-width="1.6"/>`;
// Shared anatomical primitives: flattened bill, broad paddle tail, three-toed web.
const bill=(c=C.bill)=>f('M4 1 Q7 0 10 1 L10.5 4 Q7.5 6 3 5.5 Q0 5.4 0 3.2 Q0 1 4 1 Z',c);
const tail=(c=C.ink,w=1.65)=>p('M8 20 C5 17 2.8 12 3.5 7 C4.2 2.5 9.6 1 13.5 3 C18.8 5.5 20.5 11 20 15 C19.7 18 15.5 20.7 11 21 Z',c,w);
const web=(c=C.ink)=>p('M12 21 L3 11 L4 4 L10 8 L12 2 L15 8 L21 4 L21 11 Z',c,1.6);
const billPage=()=>p('M5 3 H14 L19 8 V17 Q19 21 13 21 H7 Q3 21 3 18.5 Q3 16 7 16 H15 M5 16 V3 M14 3 V8 H19');
const check=(x,y,c=C.green)=>p(`M${x} ${y+2.5} l2.2 2.2 l4.5 -5`,c,2);
const mask=(body,d,action,id)=>`<defs><mask id="${id}" maskUnits="userSpaceOnUse" x="-2" y="-2" width="28" height="28"><rect x="-2" y="-2" width="28" height="28" fill="white"/>${f(d,'black')}</mask></defs><g mask="url(#${id})">${body}</g>${action}`;
const ff=()=>p('M3 3 H8 L3 8 V13 L13 3 H18 L3 18 H8 L18 8 V18 H13',C.green,2.5);
const aperture=()=>[
 p('M17 4.5 A8.2 8.2 0 1 0 16 19.5',C.soft,2),
 f('M10 5.5 L16 9 V15.5 L10 19 L4.5 15.5 V9 Z',C.soft),
 f('M10 5.5 L16 9 L12.5 11 Z M16 15.5 L10 19 V14.5 Z M4.5 15.5 V9 L8 9.5 Z',C.ink),
 f('M10 9 L13 10.7 V14 L10 15.6 L7.2 14 V10.7 Z','#FFF'),
 f('M10 10.2 L11.9 11.3 V13.4 L10 14.5 L8.3 13.4 V11.3 Z',C.red),
 dot(20.5,5,1.2),dot(22,9,1.2)
].join('');
// Brush's blue handle broadens into a platypus paddle, retaining ferrule and paint.
const paddleBrush=()=>[
 f('M10 12 Q10.5 5.5 17 2 Q20 -.1 22 2.5 Q24 6 17.5 11.5 L13 15 Z',C.blue),
 p('M15 8 L19 4', '#8DBEE0',1),
 f('M9.7 11.8 L13.5 14.5 L11.5 16.5 L8 13.7 Z','#CAD8DF'),
 f('M8 13.7 Q4.5 14.5 4 18.5 Q3.4 21 1.5 22 Q7 22.5 11.5 16.5 Z',C.rust),
 f('M4 18 Q3.5 21 1.5 22 Q6 22.7 9 19.3 Q6.2 20 6 17.5 Q4.6 16.5 4 18 Z',C.tip)
].join('');
const brand=()=>[
 f('M7.5 12.5 C3.5 10.5 .6 12.6 1 16 C1.2 19.6 3.5 21 6 18.7 L9 15 Z',C.teal),
 f('M6.5 10.5 C7 7.5 12 7.5 14 8 Q16 5.5 18 8 L18 14 Q16 17 11 17 H8 Q5.5 16 6.5 10.5 Z',C.teal),
 f('M17 9.5 Q21 9 23 10.7 Q24 13.5 20.3 14.5 H16.5 L15 12.5 Z',C.bill),
 f('M8 15.5 L7 19 L8.5 18.2 L10 19 L11 16 Z M14 15.5 L13.5 19 L15 18.2 L16.5 19 L17 15 Z',C.teal),
 dot(16.3,8.5,.85,'#FFFFFF'),
 p('M18.2 12.1 H21.4','#7E694E',.85),
 dot(6,3,1.2,C.green),dot(10.5,1.8,1.1,C.red),dot(15,3,1.2,C.blue)
].join('');
const icons=[
 ['hello-splat','Head + bill + paddle tail',brand()],
 ['project','Folder folds into a flattened bill',p('M3 8 V5 Q3 3.5 4.5 3.5 H9 L11 6 H20 Q21 6 21 7.5 V17 Q21 21 14 21 H6 Q2 21 2 18 Q2 15.5 6 15.5 H15 M3 8 V15.8')+g(bill(C.teal),'translate(7 9) scale(.8)')],
 ['download-tools','Webbed foot becomes a receiving tray',p('M12 2 V12 M8.5 8.5 L12 12 L15.5 8.5',C.teal,2)+p('M3 13 L3 19 L7 17 L12 21 L17 17 L21 19 V13')+dot(6,14.5,1,C.green)+dot(12,16,1,C.red)+dot(18,14.5,1,C.blue)],
 ['ffmpeg-setup','FFmpeg ribbon ends in a green bill',g(ff(),'translate(1 .5)')+g(bill(C.green),'translate(11 16) scale(1.05)')],
 ['media-import','Film frame with a bill-shaped exit',rect(2,3,12,17,C.green)+p('M5 3.5 V19.5 M2 8 H5 M2 12 H5 M2 16 H5',C.green,1.25)+f('M7.5 8 L11.8 11 L7.5 14 Z',C.green)+g(bill(C.green),'translate(13 14) scale(.95)')+p('M16 16.8 H20 M18.5 15.4 L20 16.8 L18.5 18.2','#FFFFFF',1.15)],
 ['colmap-setup','COLMAP aperture acquires a red bill',aperture()+g(bill(C.red),'translate(13 15) scale(.95)')],
 ['feature-extract','Detected features on a paddle-tail outline',tail()+p('M7 14 L10 7 L16 11 L14 17',C.soft,1.1)+rect(5.7,12.7,2.6,2.6,C.red,.25)+rect(8.7,5.7,2.6,2.6,C.red,.25)+rect(14.7,9.7,2.6,2.6,C.red,.25)+dot(14,17,1.3)],
 ['exhaustive-match','Matching across two bill profiles',p('M4 4 H7 V19 H4 Q1.5 19 1.5 16.5 V6.5 Q1.5 4 4 4 Z M20 4 H17 V19 H20 Q22.5 19 22.5 16.5 V6.5 Q22.5 4 20 4 Z')+p('M5 8 L19 15 M5 15 L19 8',C.red,1.4)+dot(5,8,1.6)+dot(5,15,1.6)+dot(19,8,1.6)+dot(19,15,1.6)],
 ['sparse-reconstruct','A webbed foot becomes a spatial graph',p('M12 21 L3 10 L5 4 L10 8 L12 2 L15 8 L20 4 L21 10 Z',C.soft,1.35)+p('M3 10 L12 12 L21 10 M12 2 V21 M5 4 L12 12 L20 4',C.soft,1.1)+dot(12,2,1.6)+dot(5,4,1.4)+dot(20,4,1.4)+dot(3,10,1.5)+dot(21,10,1.5)+dot(12,12,1.9)+dot(12,21,1.6)],
 ['undistort-images','Curved paddle grid straightens into an image',p('M3 4 Q8 2 9 7 L9 18 Q5 23 2 17 Q4 12 3 4 Z M3 11 H9 M6 4 Q8 12 5.5 20',C.soft,1.3)+rect(15,4,7,16,C.ink,.5)+p('M18.5 4.5 V19.5 M15 12 H22',C.soft,1.1)+p('M9.5 12 H14 M12 10 L14 12 L12 14',C.red,1.6)],
 ['export-model','Bill-shaped document with export arrow',mask(billPage()+p('M8 10 H12 M8 13 H11',C.soft,1.3),'M13 11 H24 V21 H13 Z',p('M13 16 H22 M19 13 L22 16 L19 19',C.red,1.9),'export')],
 ['sparse-point-cloud','A point cloud traces the paddle-tail silhouette',[[8,3,1.3],[13,3,1.5],[17.5,6,1.5],[20,10.5,1.35],[20,15,1.4],[16.5,19,1.6],[11.5,21,1.3],[7.5,18.5,1.4],[4.5,14,1.5],[3.8,9,1.3],[4.5,5.5,1.3],[9,10,1.7],[14,13.5,1.7]].map(([x,y,r],i)=>dot(x,y,r,i%3===0?C.ink:C.red)).join('')],
 ['camera-planes','Camera frustums fan out like webbed toes',p('M2 4 H7 V8 H2 Z M9.5 2 H14.5 V6 H9.5 Z M17 4 H22 V8 H17 Z')+p('M2 8 L12 21 L7 8 M9.5 6 L12 21 L14.5 6 M17 8 L12 21 L22 8',C.soft,1.15)+dot(12,21,1.7)+dot(12,12,1.4)],
 ['geometry-translator','Paddle form with move axes',g(tail(C.ink,2),'translate(.3 8) scale(.62)')+p('M5 17 L10 14',C.soft,1.1)+p('M11 8 H22 M19 5 L22 8 L19 11 M16 19 V2 M13 5 L16 2 L19 5',C.teal,1.7)],
 ['align-model','Webbed landmark triangle inside alignment target',p('M12 2 V5 M12 19 V22 M2 12 H5 M19 12 H22')+`<circle cx="12" cy="12" r="7.5" fill="none" stroke="${C.soft}" stroke-width="1.4"/>`+p('M7 9 L9 13 L12 7 L15 13 L17 9 L16 15 L12 18 L8 15 Z',C.red,1.35)+dot(12,7,1.35)+dot(7,9,1.35)+dot(17,9,1.35)],
 ['training-dataset-check','Bill-shaped document with paired data and check',mask(billPage()+dot(8.5,10,1.2,C.red)+dot(12.5,10,1.2,C.blue)+p('M8 13 H13',C.soft,1.3),'M13 13 H24 V24 H13 Z',check(14.5,16),'dataset-check')],
 ['brush-dataset','Paddle brush over stacked bill-shaped pages',p('M3 7 V3 H16 M6 10 V6 H19 M9 9 H22 V18 Q22 22 17 22 H11 Q8 22 8 19.5 V9 H9',C.ink,1.5)+g(paddleBrush(),'translate(8.4 9) scale(.55)')],
 ['brush-train','The blue brush handle becomes a paddle tail',p('M3 11 A8.7 8.7 0 0 1 13 3.2',C.soft,1.65)+p('M20.5 14 A8.5 8.5 0 0 1 11 21',C.blue,1.65)+p('M10 1.5 L13 3.2 L10 5.2',C.soft,1.5)+g(paddleBrush(),'translate(3 2) scale(.79)')],
 ['splat-preview','Splat-filled bill inside a viewing eye',p('M1.5 12 Q6 5 12 5 Q18 5 22.5 12 Q18 19 12 19 Q6 19 1.5 12 Z',C.ink,1.7)+g(bill(C.blue),'translate(6.5 9) scale(1.05)')+dot(9.5,12,1.1,'#FFFFFF')+dot(15,13,1.7,C.red)+dot(16,9,1,C.soft)],
 ['model-report','Bar chart with a flattened bill baseline',p('M4 3 V16 Q1.5 16 1.5 18.5 Q1.5 21 6 21 H16 Q21 21 22 18 H7',C.ink,1.6)+f('M7 12 H10 V16 H7 Z',C.soft)+f('M12 8 H15 V16 H12 Z',C.ink)+f('M17 4 H20 V16 H17 Z',C.red)]
];
const svg=(body,size=24)=>`<svg xmlns="http://www.w3.org/2000/svg" width="${size}" height="${size}" viewBox="0 0 24 24">${body}</svg>`;
const esc=s=>s.replaceAll('&','&amp;').replaceAll('<','&lt;');
const meta=JSON.parse(fs.readFileSync(path.join(A,'manifest.json'),'utf8'));
const accent=cat=>cat.startsWith('02')?C.green:cat.startsWith('03')||cat.startsWith('04')||cat.startsWith('LEGACY')?C.red:cat.startsWith('05')||cat.startsWith('06')?C.blue:C.teal;
const header=(w,h)=>`<svg xmlns="http://www.w3.org/2000/svg" width="${w}" height="${h}" viewBox="0 0 ${w} ${h}"><style>text{font-family:Segoe UI,Arial,sans-serif}</style><rect width="${w}" height="${h}" fill="#F5F4F0"/>`;
const text=(x,y,str,size=16,color='#526477',weight=400)=>`<text x="${x}" y="${y}" font-size="${size}" fill="${color}" font-weight="${weight}">${esc(str)}</text>`;
async function saveSheet(name,body){fs.writeFileSync(path.join(root,'review',name+'.svg'),body);await sharp(Buffer.from(body)).png().toFile(path.join(root,'review',name+'.png'));}
async function main(){
 for(const dir of ['svg','png24','png48','png96','brand','review'])fs.mkdirSync(path.join(root,dir),{recursive:true});
 for(const [id,motif,body] of icons){
  fs.writeFileSync(path.join(root,'svg',id+'.svg'),svg(body));
  for(const size of [24,48,96])await sharp(Buffer.from(svg(body,size*4))).resize(size,size).png().toFile(path.join(root,'png'+size,id+'.png'));
 }
 fs.writeFileSync(path.join(root,'brand','splatypus.svg'),svg(brand(),240));
 await sharp(Buffer.from(svg(brand(),1024))).resize(256,256).png().toFile(path.join(root,'brand','splatypus.png'));
 fs.writeFileSync(path.join(root,'manifest.json'),JSON.stringify(icons.map(([id,motif])=>({...meta.find(x=>x.id===id),motif})),null,2)+'\n');
 let board=header(1440,1570);
 board+=text(50,52,'PLAN B / SPLATYPUS',14,C.teal,700)+text(48,103,'A little animal. The same tools.',38,'#253D45',650)+text(50,136,'Twenty component icons with a shared bill, paddle-tail and webbed-foot vocabulary.',17);
 board+=g(brand(),'translate(1180 35) scale(6)');
 const motifs=[['Flattened bill',bill(C.bill),'Sensing / import / export'],['Paddle tail',tail(C.teal),'Datasets / features / Brush'],['Webbed foot',web(C.teal),'Matching / cameras / alignment']];
 motifs.forEach(([label,body,desc],i)=>{const x=50+i*447;board+=`<rect x="${x}" y="177" width="422" height="101" rx="12" fill="#FFFFFF" stroke="#E0E4DF"/>`+g(body,`translate(${x+19} 195) scale(${i===0?3.5:2.5})`)+text(x+114,213,label,18,'#253D45',600)+text(x+114,242,desc,13);});
 [[C.green,'FFmpeg'],[C.red,'COLMAP'],[C.blue,'Brush'],[C.teal,'Splatypus']].forEach(([c,l],i)=>{let x=56+i*167;board+=dot(x,309,5,c)+text(x+14,314,l,14);});
 icons.forEach(([id,motif,body],i)=>{
  const m=meta.find(x=>x.id===id),x=50+(i%4)*339,y=339+Math.floor(i/4)*224;
  board+=`<rect x="${x}" y="${y}" width="323" height="208" rx="13" fill="#FFFFFF" stroke="#E0E4DF"/>`+text(x+18,y+25,m.category,10,accent(m.category),700);
  board+=g(body,`translate(${x+23} ${y+43}) scale(3)`);
  board+=`<rect x="${x+164}" y="${y+55}" width="40" height="40" rx="6" fill="#F3F5F7"/><rect x="${x+218}" y="${y+55}" width="40" height="40" rx="6" fill="#222934"/>`+g(body,`translate(${x+172} ${y+63})`)+g(body,`translate(${x+226} ${y+63})`)+text(x+167,y+113,'24 px / light + dark',10,'#7B8795');
  board+=text(x+18,y+149,m.label.replace(/^RS /,''),16,'#253D45',600);
  const words=motif.split(' ');let lines=[''];for(const w of words){if((lines[lines.length-1]+' '+w).trim().length>42)lines.push(w);else lines[lines.length-1]=(lines[lines.length-1]+' '+w).trim();}
  lines.forEach((line,j)=>{board+=text(x+18,y+174+j*15,line,11,'#70817C');});
 });
 board+=text(50,1502,'Separate concept set. Plan A and the active plugin artwork are preserved.',14,'#526477',600)+text(50,1529,'Editable SVGs + transparent 24 / 48 / 96 px PNGs. The Splatypus name is provisional.',13);
 await saveSheet('splatypus-components',board+'</svg>');
 let comparison=header(1440,1260);
 comparison+=text(50,55,'PLAN A / PLAN B',14,C.teal,700)+text(48,106,'Tool-first or Splatypus?',38,'#253D45',650)+text(50,140,'Original at left. Platypus interpretation at right. Both shown at 48 px and native 24 px.',16);
 icons.forEach(([id,motif,body],i)=>{
  const m=meta.find(x=>x.id===id),x=50+i%4*339,y=176+Math.floor(i/4)*203;
  let a=fs.readFileSync(path.join(A,'svg',id+'.svg'),'utf8').replace(/^<svg[^>]*>/,'').replace(/<\/svg>$/,'');
  a=a.replaceAll('id="','id="a-').replaceAll('url(#','url(#a-');
  comparison+=`<rect x="${x}" y="${y}" width="323" height="187" rx="12" fill="#FFFFFF" stroke="#E0E4DF"/>`+text(x+17,y+26,m.label.replace(/^RS /,''),15,'#253D45',600);
  comparison+=text(x+44,y+51,'A',11,'#84918D',700)+text(x+196,y+51,'B',11,C.teal,700)+g(a,`translate(${x+36} ${y+63}) scale(2)`)+g(body,`translate(${x+188} ${y+63}) scale(2)`);
  comparison+=g(a,`translate(${x+98} ${y+78})`)+g(body,`translate(${x+250} ${y+78})`)+text(x+35,y+156,'Original tool glyph',11)+text(x+186,y+156,'Platypus + tool',11,C.teal);
 });
 comparison+=text(50,1225,'Plan A remains embedded in the plugin. Plan B is a separate design option.',13);
 await saveSheet('plan-a-vs-b',comparison+'</svg>');
 fs.writeFileSync(path.join(root,'review','index.html'),`<!doctype html><html lang="en"><meta charset="utf-8"><title>Splatypus / Plan B</title><style>body{margin:0;background:#F5F4F0;font-family:Segoe UI,Arial}nav{padding:20px;text-align:center}a{color:#287681;margin:0 20px}img{display:block;width:100%;max-width:1440px;height:auto;margin:auto}</style><nav><a href="#b">Splatypus set</a><a href="#compare">Compare with Plan A</a></nav><img id="b" src="splatypus-components.svg" alt="Splatypus icons"><img id="compare" src="plan-a-vs-b.svg" alt="All twenty original and Splatypus icons side by side"></html>`);
 await require('./generate-pipeline.cjs')(sharp);
  await require('./generate-video.cjs')(sharp);
  await require('./generate-quick-align.cjs')(sharp);
 console.log('Splatypus: 21 current SVGs, 63 PNGs, brand mark, and review sheets. Plan A unchanged.');
}
main().catch(e=>{console.error(e);process.exit(1)});
