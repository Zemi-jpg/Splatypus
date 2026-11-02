// Matching marker shapes/colors identify each pair; the target triangle is rotated and scaled.
const fs = require('fs'), path = require('path');
async function generate(sharp) {
  const root = __dirname;
  // SVG paint order: source triangle and markers, pair links, target triangle and markers.
  const body = `<g id="source-background">
<path d="M3 4V10H9Z" fill="none" stroke="#9CA5B3" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"/>
<circle cx="3" cy="4" r="1.6" fill="#F04448"/>
<rect x="1.5" y="8.5" width="3" height="3" fill="#3989BE"/>
<path d="M9 8 11 10 9 12 7 10Z" fill="#398A94"/>
</g><g id="pair-links-middle" fill="none" stroke-width=".75" stroke-linecap="round">
<path d="M3 4 13 21" stroke="#F04448"/>
<path d="M3 10 21 21" stroke="#3989BE"/>
<path d="M9 10 21 13" stroke="#398A94"/>
</g><g id="target-foreground">
<path d="M13 21H21V13Z" fill="none" stroke="#6B778A" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"/>
<circle cx="13" cy="21" r="1.6" fill="#F04448"/>
<rect x="19.5" y="19.5" width="3" height="3" fill="#3989BE"/>
<path d="M21 11 23 13 21 15 19 13Z" fill="#398A94"/>
</g>`;
  const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24">${body}</svg>`;
  fs.writeFileSync(path.join(root,'svg/quick-align.svg'),svg);
  for (const size of [24,48,96]) await sharp(Buffer.from(svg),{density:288}).resize(size,size).png().toFile(path.join(root,`png${size}/quick-align.png`));
  const file = path.join(root,'manifest.json');
  const entries = JSON.parse(fs.readFileSync(file,'utf8')).filter(x=>x.id!=='quick-align');
  entries.push({id:'quick-align',label:'SP Quick Align',category:'04 / REALIGNMENT',svg:'svg/quick-align.svg',png:'png24/quick-align.png',motif:'Rotated/scaled triangles with thin colored lines directly connecting matching red circles, blue squares and teal diamonds'});
  fs.writeFileSync(file,JSON.stringify(entries,null,2)+'\n');
  const review = `<svg xmlns="http://www.w3.org/2000/svg" width="480" height="210"><rect width="480" height="210" rx="12" fill="#F3F5F7"/><text x="24" y="34" font-family="Segoe UI,Arial" font-size="20" fill="#253D45">SP Quick Align / Three-point alignment</text><g transform="translate(30 66) scale(4)">${body}</g><rect x="186" y="82" width="48" height="48" rx="6" fill="white"/><rect x="250" y="82" width="48" height="48" rx="6" fill="#222934"/><g transform="translate(198 94)">${body}</g><g transform="translate(262 94)">${body}</g><text x="186" y="155" font-family="Segoe UI,Arial" font-size="12" fill="#6B778A">24 px / light + dark</text></svg>`;
  fs.writeFileSync(path.join(root,'review/quick-align.svg'),review);
  await sharp(Buffer.from(review)).png().toFile(path.join(root,'review/quick-align.png'));
}
module.exports = generate;
if(require.main===module) generate(require(process.argv[2]||'sharp')).catch(e=>{console.error(e);process.exit(1);});
