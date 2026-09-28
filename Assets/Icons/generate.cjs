// Regenerate editable SVG masters, transparent PNGs, and the review sheet.
// Usage: node Assets/Icons/generate.cjs [path-to-sharp]
const fs = require('fs');
const path = require('path');
const sharp = require(process.argv[2] || 'sharp');
const root = __dirname;
const C = {ink:'#6B778A',soft:'#9CA5B3',green:'#368D3C',red:'#F04448',blue:'#3989BE',teal:'#398A94',paper:'#FFFFFF',rust:'#C56B50',tip:'#B90D2B'};
const p=(d,c=C.ink,w=1.65)=>`<path d="${d}" fill="none" stroke="${c}" stroke-width="${w}" stroke-linecap="round" stroke-linejoin="round"/>`;
const fill=(d,c)=>`<path d="${d}" fill="${c}"/>`;
const dot=(x,y,r=1.5,c=C.red)=>`<circle cx="${x}" cy="${y}" r="${r}" fill="${c}"/>`;
const rect=(x,y,w,h,c=C.ink,rad=1.2)=>`<rect x="${x}" y="${y}" width="${w}" height="${h}" rx="${rad}" fill="none" stroke="${c}" stroke-width="1.65"/>`;
const group=(s,t)=>`<g transform="${t}">${s}</g>`;
const folder=()=>p('M2.5 7 V5.5 Q2.5 4 4 4 H9 L11 6.5 H20 Q21.5 6.5 21.5 8 V19 Q21.5 20.5 20 20.5 H4 Q2.5 20.5 2.5 19 Z');
const page=()=>p('M5 3 H14 L19 8 V21 H5 Z M14 3 V8 H19');
const ffmpeg=()=>p('M3 3 H9 L3 9 V15 L15 3 H21 L3 21 H9 L21 9 V21 H15',C.green,3);
const aperture=()=>[
 p('M18.4 5 A9 9 0 1 0 18.4 19',C.soft,2.1),
 fill('M12 5.5 L18 9 V16 L12 19.5 L6 16 V9 Z',C.soft),
 fill('M12 5.5 L18 9 L14.5 11.5 Z M18 16 L12 19.5 L12 15 Z M6 16 V9 L10 9.5 Z',C.ink),
 fill('M12 9 L14.6 10.5 V13.5 L12 15 L9.4 13.5 V10.5 Z',C.paper),
 fill('M12 10.2 L13.6 11.1 V12.9 L12 13.8 L10.4 12.9 V11.1 Z',C.red),
 dot(21,8,1.25),dot(21,12,1.25),dot(21,16,1.25)
].join('');
const brush=()=>[
 fill('M10 12 C13 7 18 3 21 2 Q23 1.3 22 3.7 C20.5 7.2 17.3 11 13 14 Z',C.blue),
 fill('M9.7 11.8 L13.3 14.4 L11.5 16.5 L8 13.6 Z','#C8D5DC'),
 fill('M8.1 13.5 Q4.6 14.8 4 18.2 Q3.5 21 1.5 21.8 Q7 23 11.5 16.4 Z',C.rust),
 fill('M4 18 Q3.5 21 1.5 21.8 Q6 22.8 9 19.3 Q6.7 19.7 6.2 17.6 Q4.6 16.4 4 18 Z',C.tip)
].join('');
const check=(x,y,c=C.green)=>p(`M${x} ${y+2.5} l2.2 2.2 l4.5 -5`,c,2);
// Remove the backing glyph beneath an action badge without painting a white tile.
const knockout=(body,shape,action,id)=>`<defs><mask id="${id}"><rect x="-2" y="-2" width="28" height="28" fill="white"/>${fill(shape,'black')}</mask></defs><g mask="url(#${id})">${body}</g>${action}`;
const icons=[
 ['hello-splat','Hello Splat','01 / SETUP',C.teal,
  fill('M11 2.5 L13.1 8.4 L19 10.5 L13.1 12.6 L11 18.5 L8.9 12.6 L3 10.5 L8.9 8.4 Z',C.teal)+dot(19.5,4.5,1.5,C.blue)+dot(20,18.5,2,C.red)+dot(4.5,20,1.3,C.green)],
 ['project','RS Project','01 / SETUP',C.teal,folder()+p('M8 15 L12 11 L16 15 L12 18 Z',C.teal,1.5)+p('M12 11 V18',C.teal,1.4)],
 ['download-tools','RS Download Tools','01 / SETUP',C.teal,p('M12 3 V14 M7.5 9.5 L12 14 L16.5 9.5',C.ink,2)+p('M3 14 V20 H21 V14')+dot(7,18,1.35,C.green)+dot(12,18,1.35,C.red)+dot(17,18,1.35,C.blue)],
 ['ffmpeg-setup','RS FFmpeg Setup','02 / DATASET',C.green,knockout(group(ffmpeg(),'translate(1 1) scale(.82)'),'M15 16 H24 V24 H15 Z',check(16,18),'ffmpeg-badge')],
 ['media-import','RS Media Import','02 / DATASET',C.green,knockout(rect(2.5,3.5,13,16.5,C.green)+p('M5.5 4 V20 M2.5 8 H5.5 M2.5 12 H5.5 M2.5 16 H5.5',C.green,1.3)+fill('M8 8 L12.5 11 L8 14 Z',C.green),'M13 13 H24 V20 H13 Z',p('M13 16.5 H22 M19 13.5 L22 16.5 L19 19.5',C.green,1.9),'import-badge')],
 ['colmap-setup','RS COLMAP Setup','03 / COLMAP',C.red,aperture()],
 ['feature-extract','RS COLMAP Feature Extract','03 / COLMAP',C.red,p('M3 8 V3 H8 M16 3 H21 V8 M21 16 V21 H16 M8 21 H3 V16')+p('M6 17 L10 11 L14 14 L18 7',C.soft,1.35)+rect(5,15,3,3,C.red,.3)+rect(8.5,9.5,3,3,C.red,.3)+rect(12.5,12.5,3,3,C.red,.3)+rect(16.5,5.5,3,3,C.red,.3)],
 ['exhaustive-match','RS COLMAP Exhaustive Match','03 / COLMAP',C.red,rect(2,3.5,7,17)+rect(15,3.5,7,17)+p('M6 8 L18 15 M6 16 L18 7',C.red,1.45)+dot(6,8,1.65)+dot(6,16,1.65)+dot(18,15,1.65)+dot(18,7,1.65)],
 ['sparse-reconstruct','RS COLMAP Sparse Reconstruct','03 / COLMAP',C.red,p('M12 3 L3.5 17 L20.5 17 Z M12 3 V13 M3.5 17 L12 13 L20.5 17 M12 13 V21',C.soft,1.4)+dot(12,3,2)+dot(3.5,17,2)+dot(20.5,17,2)+dot(12,13,1.9)+dot(12,21,1.5,C.ink)],
 ['undistort-images','RS COLMAP Undistort Images','03 / COLMAP',C.red,p('M2 5 Q6 7 9 4 Q8 12 9 20 Q6 17 2 19 Q4 12 2 5 Z M2.7 12 H8.5 M5.5 6 Q7 12 5.5 18',C.soft,1.4)+rect(15,4.5,7,15,C.ink,.3)+p('M18.5 5 V19 M15 12 H22',C.soft,1.1)+p('M9.5 12 H14 M12 10 L14 12 L12 14',C.red,1.6)],
 ['export-model','RS COLMAP Export Model','03 / COLMAP',C.red,knockout(page()+p('M8 10 H11 M8 13 H13 M8 16 H11',C.soft,1.4),'M13 12 H24 V20 H13 Z',p('M13 16 H22 M19 13 L22 16 L19 19',C.red,1.9),'export-badge')],
 ['sparse-point-cloud','RS Sparse Point Cloud','04 / REALIGNMENT',C.red,[[4,9,1.5],[8,5,1.3],[13,3,1.3],[17,6,1.5],[21,10,1.4],[11,10,1.8],[6,15,1.8],[13,16,1.6],[19,17,1.7],[9,21,1.3],[16,21,1.2]].map(([x,y,r],i)=>dot(x,y,r,i%3===0?C.ink:C.red)).join('')],
 ['camera-planes','RS Camera Planes','04 / REALIGNMENT',C.red,p('M2 3 H8 V7 H2 Z M16 3 H22 V7 H16 Z M9 18 H15 V22 H9 Z')+p('M3 7 L12 12 L7 7 M17 7 L12 12 L21 7 M9 18 L12 12 L15 18',C.soft,1.25)+dot(12,12,2)],
 ['geometry-translator','RS Geometry Translator','04 / REALIGNMENT',C.teal,p('M3 13 L8 10 L13 13 V19 L8 22 L3 19 Z M3 13 L8 16 L13 13 M8 16 V22',C.ink,1.45)+p('M11 8 H21 M18 5 L21 8 L18 11 M16 19 V3 M13 6 L16 3 L19 6',C.teal,1.65)],
 ['align-model','RS Align COLMAP Model','04 / REALIGNMENT',C.red,p('M12 2 V6 M12 18 V22 M2 12 H6 M18 12 H22',C.ink)+`<circle cx="12" cy="12" r="7" fill="none" stroke="${C.soft}" stroke-width="1.4"/>`+p('M8 15 L12 8 L16 15 Z',C.red,1.4)+dot(12,8,1.6)+dot(8,15,1.6)+dot(16,15,1.6)],
 ['training-dataset-check','RS Training Dataset Check','05 / TRAINING',C.blue,knockout(page()+dot(8.5,10,1.2,C.red)+dot(12.5,10,1.2,C.blue)+p('M8 14 H13 M8 17 H11',C.soft,1.4),'M13 13 H24 V24 H13 Z',check(14.5,16),'dataset-badge')],
 ['brush-dataset','RS Brush Dataset','05 / TRAINING',C.blue,p('M3 6 V3 H16 M6 9 V6 H19 M9 9 H22 V22 H9 Z',C.ink,1.5)+group(brush(),'translate(8.4 9) scale(.55)')],
 ['brush-train','RS Brush Train','05 / TRAINING',C.blue,p('M3.5 11 A8.5 8.5 0 0 1 13 3.7',C.soft,1.65)+p('M20.5 14 A8.5 8.5 0 0 1 11 21',C.blue,1.65)+p('M10 1.8 L13 3.7 L10 5.5',C.soft,1.5)+group(brush(),'translate(3 2) scale(.79)')],
 ['splat-preview','RS Splat Preview','06 / PREVIEW',C.blue,p('M1.5 12 Q6 4.5 12 4.5 Q18 4.5 22.5 12 Q18 19.5 12 19.5 Q6 19.5 1.5 12 Z',C.ink,1.7)+`<ellipse cx="10" cy="11" rx="3" ry="2.3" transform="rotate(-30 10 11)" fill="${C.blue}"/>`+`<ellipse cx="14.3" cy="13.3" rx="2.5" ry="1.8" transform="rotate(-30 14.3 13.3)" fill="${C.red}"/>`+dot(14.6,8.6,1.25,C.soft)+dot(8.5,15.3,1,C.soft)],
 ['model-report','RS COLMAP Model Report','LEGACY / HIDDEN',C.red,p('M4 3 V21 H22')+fill('M7 13 H10 V18 H7 Z',C.soft)+fill('M12 9 H15 V18 H12 Z',C.ink)+fill('M17 5 H20 V18 H17 Z',C.red)]
];
function svg(body,size=24){return `<svg xmlns="http://www.w3.org/2000/svg" width="${size}" height="${size}" viewBox="0 0 24 24">${body}</svg>`;}
const esc=s=>s.replaceAll('&','&amp;').replaceAll('<','&lt;');
async function main(){
 for(const [id,label,category,color,body] of icons){
  fs.writeFileSync(path.join(root,'svg',id+'.svg'),svg(body));
  for(const size of [24,48,96]) await sharp(Buffer.from(svg(body,size*4))).resize(size,size).png().toFile(path.join(root,'png'+size,id+'.png'));
 }
 fs.writeFileSync(path.join(root,'manifest.json'),JSON.stringify(icons.map(([id,label,category])=>({id,label,category,svg:`svg/${id}.svg`,png:`png24/${id}.png`})),null,2)+'\n');
 const W=1440,H=1370;
 let board=`<svg xmlns="http://www.w3.org/2000/svg" width="${W}" height="${H}" viewBox="0 0 ${W} ${H}"><rect width="${W}" height="${H}" fill="#F3F5F7"/><style>text{font-family:Segoe UI,Arial,sans-serif}</style>`;
 board+=`<text x="52" y="65" font-size="14" font-weight="700" letter-spacing="2.8" fill="${C.teal}">RHINOSPLAT / COMPONENT ICONS</text><text x="50" y="116" font-size="38" font-weight="650" fill="#253246">One workflow. Twenty distinct actions.</text><text x="52" y="151" font-size="17" fill="#627084">Tool-inspired colours / shared geometry / transparent assets / designed for 24 px</text>`;
 [[C.green,'FFmpeg'],[C.red,'COLMAP'],[C.blue,'Brush'],[C.teal,'Workflow']].forEach(([color,label],i)=>{let x=54+i*174;board+=dot(x,188,5,color)+`<text x="${x+15}" y="194" font-size="15" fill="#455367">${label}</text>`;});
 icons.forEach(([id,label,cat,color,body],i)=>{
  let x=50+(i%4)*339,y=223+Math.floor(i/4)*210;
  board+=`<rect x="${x}" y="${y}" width="323" height="194" rx="13" fill="white" stroke="#DFE5EB"/><text x="${x+18}" y="${y+26}" font-size="10" font-weight="700" letter-spacing="1.2" fill="${color}">${cat}</text>`;
  board+=group(body,`translate(${x+22} ${y+47}) scale(3)`);
  board+=`<rect x="${x+158}" y="${y+55}" width="42" height="42" rx="6" fill="#F3F5F7"/><rect x="${x+214}" y="${y+55}" width="42" height="42" rx="6" fill="#222934"/>`;
  board+=group(body,`translate(${x+167} ${y+64})`)+group(body,`translate(${x+223} ${y+64})`);
  board+=`<text x="${x+165}" y="${y+115}" font-size="10" fill="#7B8795">24 px / light + dark</text><text x="${x+18}" y="${y+157}" font-size="16" font-weight="600" fill="#253246">${esc(label.replace(/^RS /,''))}</text><text x="${x+18}" y="${y+179}" font-size="11" fill="#7B8795">${String(i+1).padStart(2,'0')} / ${id}</text>`;
 });
 board+=`<text x="52" y="1321" font-size="13" fill="#667487">SVG masters + transparent PNGs at 24 / 48 / 96 px. Enlarged glyphs shown at left; actual-size samples at right.</text><text x="52" y="1345" font-size="12" fill="#89929F">Original interpretations of the supplied tool references. Current component names retained.</text></svg>`;
 fs.writeFileSync(path.join(root,'review','component-icons.svg'),board);
 await sharp(Buffer.from(board)).png().toFile(path.join(root,'review','component-icons.png'));
 let html=`<!doctype html><html lang="en"><meta charset="utf-8"><title>RhinoSplat component icons</title><style>body{margin:0;background:#F3F5F7}img{width:100%;height:auto;max-width:1440px;display:block;margin:auto}</style><img src="component-icons.svg" alt="All twenty RhinoSplat component icons, with enlarged and actual-size previews"></html>`;
 fs.writeFileSync(path.join(root,'review','index.html'),html);
 console.log(`Generated ${icons.length} SVG masters, ${icons.length*3} PNGs, manifest, and review sheet.`);
}
main().catch(e=>{console.error(e);process.exit(1);});
