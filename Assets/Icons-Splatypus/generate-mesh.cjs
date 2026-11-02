// Extend the existing COLMAP webbed-foot motif with filled triangular surfaces.
const fs = require('fs'), path = require('path');
async function generate(sharp) {
  const root = __dirname;
  const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24">
<path d="M12 21 3 10 5 4 10 8 12 2 15 8 20 4 21 10Z" fill="#A0ABB8" stroke="#536171" stroke-width="1" stroke-linejoin="round"/>
<path d="m3 10 9 2-7-8Zm9-8v10l8-8Zm0 10v9l9-11Z" fill="#6B778A"/>
<path d="m3 10 9 2 9-2M12 2v19M5 4l7 8 8-8" fill="none" stroke="#EDF1F4" stroke-width="1"/>
<g fill="#F04448"><circle cx="12" cy="2" r="1.4"/><circle cx="12" cy="12" r="1.8"/><circle cx="12" cy="21" r="1.4"/></g></svg>`;
  fs.writeFileSync(path.join(root, 'svg/mesh-reconstruct.svg'), svg);
  for (const size of [24, 48, 96])
    await sharp(Buffer.from(svg), {density: 288}).resize(size, size).png().toFile(path.join(root, `png${size}/mesh-reconstruct.png`));
  const file = path.join(root, 'manifest.json');
  const entries = JSON.parse(fs.readFileSync(file, 'utf8')).filter(x => x.id !== 'mesh-reconstruct');
  entries.push({id:'mesh-reconstruct', label:'SP COLMAP Mesh Reconstruction', category:'03 / COLMAP',
    svg:'svg/mesh-reconstruct.svg', png:'png24/mesh-reconstruct.png', motif:'Filled triangular webbed foot with COLMAP red feature points'});
  fs.writeFileSync(file, JSON.stringify(entries, null, 2) + '\n');
}
module.exports = generate;
if (require.main === module) generate(require(process.argv[2] || 'sharp')).catch(e => { console.error(e); process.exit(1); });
