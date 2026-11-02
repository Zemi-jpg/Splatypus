// Extend the existing preview icon family without regenerating other artwork.
const fs = require('fs'), path = require('path');
const sharp = require(process.argv[2] || 'sharp');
const root = __dirname;
const body = '<defs><linearGradient id="backdrop" x2="0" y2="1"><stop stop-color="#91B3CE"/><stop offset="1" stop-color="#B99766"/></linearGradient></defs>' +
  '<rect x="2.5" y="3.5" width="19" height="17" rx="2" fill="url(#backdrop)" stroke="#6B778A" stroke-width="1.5"/>' +
  '<path d="M4 16Q8 11 12 15T20 13" fill="none" stroke="#FFFFFF" stroke-width="1.2" opacity=".8"/>' +
  '<circle cx="7" cy="8" r="1.6" fill="#FFFFFF"/><circle cx="12" cy="10.5" r="1.8" fill="#398A94"/><circle cx="17" cy="7.5" r="1.3" fill="#398A94"/>';
const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24">${body}</svg>`;
(async () => {
  fs.writeFileSync(path.join(root,'svg/auto-background.svg'),svg);
  for(const size of [24,48,96]) await sharp(Buffer.from(svg),{density:288}).resize(size,size).png().toFile(path.join(root,`png${size}/auto-background.png`));
  const file=path.join(root,'manifest.json');
  const entries=JSON.parse(fs.readFileSync(file,'utf8')).filter(x=>x.id!=='auto-background');
  entries.push({id:'auto-background',label:'SP Auto Background',category:'06 / PREVIEW',svg:'svg/auto-background.svg',png:'png24/auto-background.png',motif:'Baked splat colors over a sky-to-ground gradient'});
  fs.writeFileSync(file,JSON.stringify(entries,null,2)+'\n');
})().catch(e=>{console.error(e);process.exit(1);});
