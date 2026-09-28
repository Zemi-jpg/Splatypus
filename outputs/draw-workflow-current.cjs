// Editable SVG masters and PNG exports for the separate re-alignment workflow.
const fs = require('fs');
const path = require('path');
const sharp = require('C:/Users/basar/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/sharp');
const dir = __dirname;
const C = {ink:'#183650',muted:'#586f83',line:'#879cae',blue:'#2479ae',teal:'#188575',purple:'#8255b4',bg:'#f7f9fc'};
const esc = s => String(s).replaceAll('&','&amp;').replaceAll('<','&lt;').replaceAll('"','&quot;');
class Diagram {
  constructor(title,desc) {
    this.s = [`<svg xmlns="http://www.w3.org/2000/svg" width="4200" height="1400" viewBox="0 0 4200 1400"><title>${esc(title)}</title><desc>${esc(desc)}</desc><defs>`];
    for (const color of [C.line,C.teal,C.purple]) this.s.push(`<marker id="a${color.slice(1)}" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="8" markerHeight="8" orient="auto"><path d="M0 0 L10 5 L0 10Z" fill="${color}"/></marker>`);
    this.s.push(`</defs><rect width="4200" height="1400" fill="${C.bg}"/><g font-family="Segoe UI, Arial, sans-serif">`);
  }
  text(x,y,s,size=28,color=C.ink,weight=400,anchor='start') { this.s.push(`<text x="${x}" y="${y}" font-size="${size}" fill="${color}" font-weight="${weight}" text-anchor="${anchor}">${esc(s)}</text>`); }
  box(x,y,w,h,title,lines,fill='#fff',stroke='#d4dfeb',size=27) {
    this.s.push(`<rect x="${x}" y="${y}" width="${w}" height="${h}" rx="18" fill="${fill}" stroke="${stroke}" stroke-width="2"/>`);
    this.text(x+24,y+46,title,31,C.ink,600);
    lines.forEach((line,i)=>this.text(x+24,y+92+38*i,line,size,C.muted));
  }
  edge(d,color=C.line,dash=false) { this.s.push(`<path d="${d}" fill="none" stroke="${color}" stroke-width="3.5" ${dash?'stroke-dasharray="10 8"':''} marker-end="url(#a${color.slice(1)})"/>`); }
  image(file,x,y,w,h,source='') {
    const mime=file.endsWith('.svg')?'image/svg+xml':'image/png';
    const data=fs.readFileSync(path.resolve(dir,file)).toString('base64');
    this.s.push(`${source?`<a href="${esc(source)}">`:''}<image x="${x}" y="${y}" width="${w}" height="${h}" preserveAspectRatio="xMidYMid meet" href="data:${mime};base64,${data}"/>${source?'</a>':''}`);
  }
  local(name,x,y,w=100) { this.image('../Assets/Icons-Splatypus/svg/'+name+'.svg',x,y,w,w); }
  phase(x,n,label,w) { this.text(x,205,n,25,C.blue,600);this.text(x+48,205,label,29,C.ink,600);this.s.push(`<path d="M${x} 229 H${x+w}" stroke="#d5dfe9" stroke-width="2"/>`); }
  rule(y) { this.s.push(`<path d="M60 ${y} H4140" stroke="#d5dfe9" stroke-width="2"/>`); }
  svg() { return this.s.join('\n')+'</g></svg>'; }
}
const desc='Undistortion replaces the original base images and matching model. Alignment always reads that base and writes separate aligned BIN and TXT models. Brush uses base images with the base model by default, or the aligned model through its optional Sparse Model Override. Alignment never feeds back into the base.';

const detailed = require('./draw-workflow-reference.cjs').makeReferenceDiagram;

function formats() {
  const d=new Diagram('Splatypus data workflow — official icons and separate aligned model',desc);
  d.text(60,85,'Splatypus · tools and data formats',52,C.ink,600);
  d.text(60,137,'One active base pair · separate aligned model · official tool artwork',30,C.muted);
  d.text(4140,85,'UPDATED 28 SEP 2026',23,C.blue,600,'end');
  d.s.push('<rect x="2310" y="300" width="450" height="280" rx="18" fill="#edf7f3" stroke="#b6d8ca" stroke-width="2"/>');
  d.edge('M290 435 H405');d.edge('M685 435 H850');d.edge('M1050 435 H1270');
  d.edge('M1900 435 H2310',C.teal);d.edge('M2760 435 H2890');d.edge('M3090 435 H3390',C.purple);
  d.edge('M3390 435 V265 H3510',C.purple);d.edge('M3390 435 V665 H3510',C.purple);
  d.edge('M3390 665 V1010 H3510',C.purple,true);
  d.edge('M1350 560 V695 H1200 V810',C.teal,true);
  d.edge('M1560 915 H1650 V560',C.teal);
  d.edge('M1850 560 V705 H2090 V800',C.purple,true);
  d.edge('M2190 880 H2390',C.purple);
  d.edge('M2680 785 V655 H2545 V580',C.purple);

  d.text(170,361,'Photos / video',33,C.ink,600,'middle');
  d.text(170,418,'.MP4 · .MOV',30,C.blue,600,'middle');d.text(170,467,'.JPG · .PNG …',29,C.blue,600,'middle');
  d.image('official-icons/ffmpeg-trim.png',405,402,280,65,'https://ffmpeg.org/');
  d.text(545,590,'FFmpeg',32,C.ink,600,'middle');d.text(545,633,'SP Media Import',25,C.muted,400,'middle');
  d.text(765,374,'.JPG',36,C.blue,600,'middle');
  d.image('official-icons/colmap.svg',850,335,200,200,'https://github.com/colmap/colmap');
  d.text(950,590,'COLMAP',32,C.ink,600,'middle');d.text(950,633,'SP COLMAP All',25,C.muted,400,'middle');
  d.text(1155,365,'.JPG + .BIN',31,C.teal,600,'middle');
  d.box(1270,300,630,260,'ACTIVE BASE',['Original OR undistorted images','images/  +  colmap/sparse/0/','.JPG + matching .BIN cameras / points','Alignment always reads this base'],'#e5f3ed','#a3ccba',26);
  d.text(2130,359,'.JPG + .BIN',40,C.teal,600,'middle');d.text(2130,400,'Dataset input',26,C.muted,400,'middle');
  d.local('brush-dataset',2490,350,110);
  d.text(2545,510,'SP Brush Dataset',30,C.ink,600,'middle');
  d.text(2545,550,'Sparse empty → base .BIN',25,C.teal,400,'middle');
  d.image('official-icons/brush.png',2890,335,200,200,'https://github.com/ArthurBrussee/brush');
  d.text(2990,590,'Brush',32,C.ink,600,'middle');d.text(2990,633,'SP Brush Train',25,C.muted,400,'middle');
  d.text(3240,361,'.PLY',42,C.purple,600,'middle');d.text(3240,402,'sp_*.ply',26,C.muted,400,'middle');

  d.text(3800,181,'VISUALIZE IN RHINO',25,C.blue,600,'middle');
  d.image('official-icons/rhino-gaussian-splatting.png',3530,210,145,145,'https://www.food4rhino.com/en/app/rhino-gaussian-splatting');
  d.text(3710,266,'Rhino Gaussian',31,C.ink,600);d.text(3710,310,'Splatting',31,C.ink,600);
  d.text(3530,398,'Optional +90° X copy → _rgs.ply',25,C.muted);
  d.text(3800,495,'OR',24,C.muted,600,'middle');
  d.image('official-icons/krill-embedded-25.png',3530,577,145,165,'https://krill.verkare.com/');
  d.text(3710,649,'Krill',34,C.ink,600);d.text(3710,692,'Gaussian splat rendering',25,C.muted);
  d.local('splat-preview',3530,961,110);
  d.text(3690,995,'SP Splat Preview',31,C.ink,600);d.text(3690,1040,'Coloured point cloud',26,C.muted);

  d.box(1040,810,520,225,'OPTIONAL · UNDISTORT',['.JPG + .BIN → corrected pair','Overwrite the active base','Keep images + calibration paired'],'#edf7f3','#b6d8ca',26);
  d.local('undistort-images',960,870,64);
  d.text(1715,759,'BASE .BIN + transforms',27,C.purple,600);
  d.local('align-model',2035,820,110);
  d.text(2090,995,'SP Align Model',30,C.ink,600,'middle');
  d.text(2090,1036,'Move · rotate · scale',25,C.muted,400,'middle');
  d.box(2390,785,800,300,'SEPARATE ALIGNED MODEL',['.BIN → colmap/aligned/sparse/0/','.TXT → colmap/aligned/sparse-text/','Aligned Sparse Model → Brush Sparse override','Aligned Text Model → separate geometry preview','Every Write starts from the base; no accumulated transform'],'#f2edf8','#c8b2df',25);
  d.text(2900,704,'Optional aligned .BIN',29,C.purple,600,'middle');
  d.rule(1175);
  d.text(60,1225,'COLMAP MODEL',23,C.blue,600);d.text(345,1225,'cameras + images + points3D (.bin / .txt)',27,C.ink);
  d.text(1850,1225,'Base images stay paired with either the base model or its aligned derivative.',27,C.muted);
  d.text(60,1277,'After another reconstruction or undistortion, write alignment again before training the aligned branch.',27,C.muted);
  d.text(60,1340,'Official artwork: FFmpeg · COLMAP · Brush · Rhino Gaussian Splatting · Krill. SP component icons are from this project.',23,C.muted);
  return d.svg();
}

async function save(svg,names) {
  const png=await sharp(Buffer.from(svg)).png().toBuffer();
  for(const name of names) { fs.writeFileSync(path.join(dir,name+'.svg'),svg);fs.writeFileSync(path.join(dir,name+'.png'),png); }
}
async function main() {
  await save(detailed(),['splatypus-workflow-horizontal','splatypus-workflow-optional-steps']);
  await save(formats(),['splatypus-official-tools-workflow','splatypus-data-formats']);
  console.log('Updated workflow diagrams: Picture1 layout at 3200 × 1120 and official tools at 4200 × 1400; PNG and SVG.');
}
main().catch(error=>{console.error(error);process.exitCode=1;});
