// Reuses repository-native vector artwork. No generated scene imagery or external logos.
const fs = require('fs');
const path = require('path');
const sharp = require(process.argv[2] || 'sharp');
const root = path.resolve(__dirname, '..');
const out = path.join(__dirname, 'assets');
fs.mkdirSync(out, { recursive: true });
const brand = fs.readFileSync(path.join(root, 'Assets/Icons-Splatypus/brand/splatypus.svg'), 'utf8');
const body = svg => svg.replace(/^.*?<svg[^>]*>/s, '').replace(/<\/svg>\s*$/, '');
const esc = s => s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/"/g, '&quot;');
const C = { ink: '#173D43', teal: '#398A94', muted: '#566F73', paper: '#F5F3ED', gold: '#B99766', line: '#D8E2DF' };
const text = (x,y,size,s,extra='') => `<text x="${x}" y="${y}" font-family="Arial, sans-serif" font-size="${size}" ${extra.includes('fill=')?'':`fill="${C.ink}"`} ${extra}>${esc(s)}</text>`;
const card = (x,y,w,h) => `<rect x="${x}" y="${y}" width="${w}" height="${h}" rx="20" fill="white" stroke="${C.line}"/>`;
const logo = (x,y,s) => `<g transform="translate(${x} ${y}) scale(${s/24})">${body(brand)}</g>`;
const svg = (w,h,content) => `<svg xmlns="http://www.w3.org/2000/svg" width="${w}" height="${h}" viewBox="0 0 ${w} ${h}"><rect width="${w}" height="${h}" fill="${C.paper}"/>${content}</svg>`;
const icon = (id,x,y,s=60) => `<g transform="translate(${x} ${y}) scale(${s/24})">${body(fs.readFileSync(path.join(root, `Assets/Icons-Splatypus/svg/${id}.svg`),'utf8'))}</g>`;
async function save(name,w,h,content) {
  const source = svg(w,h,content);
  fs.writeFileSync(path.join(out,name+'.svg'),source);
  await sharp(Buffer.from(source)).png().toFile(path.join(out,name+'.png'));
}
(async () => {
  await sharp(Buffer.from(brand)).resize(64,64).png().toFile(path.join(out,'splatypus-icon-64.png'));
  await save('splatypus-logo-512',512,512,logo(66,66,380));
  let hero = `<rect x="1060" y="0" width="540" height="900" fill="#E2EBE6"/>`;
  hero += text(82,108,20,'GRASSHOPPER / RHINO 8', 'letter-spacing="4"');
  hero += text(78,278,114,'Splatypus','font-weight="700"');
  hero += text(84,365,42,'Photogrammetry and');
  hero += text(84,423,42,'Gaussian splat training');
  hero += text(84,510,25,'Connect media, cameras and training in one local workflow.');
  const tags = ['Import media', 'Reconstruct', 'Train', 'Inspect'];
  tags.forEach((label,i) => { hero += card(84+i*225,610,208,74)+text(103+i*225,655,21,label); });
  hero += logo(1095,280,460);
  hero += text(84,808,23,'FFmpeg  /  COLMAP  /  Brush');
  hero += text(84,850,17,'Rhino 8.34+ and .NET 8  |  Separate external tools  |  Point-cloud preview');
  await save('splatypus-hero-1600x900',1600,900,hero);
  let flow = text(70,106,57,'From media to a trained model','font-weight="700"');
  flow += text(74,160,25,'A connected local workflow in Grasshopper. Each stage exposes status and logs.');
  const stages = [
    ['project','Project','Create a writable Root'],['media-import','Media import','Photographs or video'],
    ['colmap-pipeline','COLMAP','Cameras + sparse points'],['brush-dataset','Dataset','Prepare active data'],
    ['brush-train','Brush training','Export PLY models'],['splat-preview','Inspect','Colored point preview']
  ];
  stages.forEach((s,i) => {
    const x = 70+i*248;
    flow += card(x,255,224,310)+text(x+22,300,18,String(i+1).padStart(2,'0'),'fill="#566F73"');
    flow += icon(s[0],x+67,325,88)+text(x+20,470,25,s[1],'font-weight="700"')+text(x+20,513,16,s[2]);
    if(i<5) flow += `<path d="M${x+228} 403 h12 m-6 -6 l6 6 l-6 6" stroke="${C.teal}" fill="none" stroke-width="3"/>`;
  });
  flow += text(74,642,20,'OPTIONAL OPERATIONS','letter-spacing="2"');
  [['undistort-images','Undistort images + cameras'],['align-model','Align points + camera poses']].forEach((s,i) => {
    const x=70+i*744; flow += card(x,675,720,86)+icon(s[0],x+18,690,52)+text(x+86,725,20,s[1]);
  });
  flow += text(74,830,18,'Connect Root → Active Dataset → Brush Dataset; refresh after reconstruction, undistortion or alignment.');
  flow += text(74,869,16,'Diagram, not a product screenshot. No Gaussian splat renderer included. Splatypus is licensed under MIT.');
  await save('splatypus-workflow-1600x900',1600,900,flow);
  const order=JSON.parse(fs.readFileSync(path.join(root,'Assets/Icons-Splatypus/manifest.json'),'utf8')).map(x=>x.id);
  const components = fs.readdirSync(root).filter(n=>/^GH\..*\.cs$/.test(n)).map(n=>{
    const source=fs.readFileSync(path.join(root,n),'utf8');
    const id=source.match(/ComponentIcons\.Get\("([^"]+)"\)/)?.[1];
    const metadata=source.match(/: base\("([^"]+)",\s*"([^"]+)",\s*"[^"]*",\s*"Splatypus",\s*"([^"]+)"\)/);
    return id && metadata ? {id,name:metadata[1],category:metadata[3]} : null;
  }).filter(c=>c && order.includes(c.id));
  if(components.length!==21) throw new Error(`Expected 21 current source components; found ${components.length}`);
  components.sort((a,b)=>order.indexOf(a.id)-order.indexOf(b.id));
  const groups=[...new Set(components.map(x=>x.category))].sort();
  let sheet=text(70,100,56,'The Splatypus toolkit','font-weight="700"')+text(74,153,25,'21 components, grouped by their Grasshopper workflow stage.');
  groups.forEach((category,index)=>{
    const x=70+(index%3)*496, y=210+Math.floor(index/3)*365;
    sheet += card(x,y,474,344)+text(x+22,y+44,22,category.toUpperCase(),'font-weight="700"');
    components.filter(c=>c.category===category).forEach((c,i)=>{
      sheet += icon(c.id,x+24,y+65+i*29,24)+text(x+64,y+84+i*29,17,c.name);
    });
  });
  sheet+=text(74,971,18,'Existing Splatypus artwork. Component names are read from the plugin source; this is an icon sheet, not a screenshot.');
  await save('splatypus-components-1600x1000',1600,1000,sheet);
  fs.writeFileSync(path.join(out,'component-inventory.json'),JSON.stringify(components,null,2)+'\n');
  console.log('Generated package icon, logo, hero, workflow and 21-component sheet.');
})().catch(e=>{console.error(e);process.exitCode=1});
