// Extends the existing vector icon family. Usage: node generate-pipeline.cjs [sharp module path]
const fs = require('fs'), path = require('path');
async function generate(sharp) {
  const root = __dirname;
  const body = `<path d="M12 2C6.5 2 3.5 6.3 3.5 11.5Q3.5 17.8 12 19Q20.5 17.8 20.5 11.5C20.5 6.3 17.5 2 12 2Z" fill="#A0ABB8"/>
<path d="M12 2Q18 2 20 8.7L13.7 7.8ZM20.5 11.5Q20.5 17.8 12 19L13.7 11.4ZM3.5 11.5Q3.5 5.2 8.4 2.8L10.3 7.8Z" fill="#6B778A"/>
<path d="m12 6 3.2 1.8v3.7L12 13.3l-3.2-1.8V7.8Z" fill="#FFFFFF"/>
<path d="m12 7.3 2.1 1.2v2.2L12 12l-2.1-1.3V8.5Z" fill="#F04448"/>
<g fill="#FFFFFF"><circle cx="6.6" cy="12.1" r="1.25"/><circle cx="17.4" cy="12.1" r="1.25"/></g>
<path d="M12 14Q7 13 3.4 15.8L3.4 18.5Q5.4 21.5 12 21.5T20.6 18.5V15.8Q17 13 12 14Z" fill="#B99766"/>
<g fill="#776042"><circle cx="9" cy="16.8" r=".7"/><circle cx="15" cy="16.8" r=".7"/></g>`;
  const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24">${body}</svg>`;
  fs.writeFileSync(path.join(root, 'svg/colmap-pipeline.svg'), svg);
  for (const size of [24, 48, 96])
    await sharp(Buffer.from(svg), {density: 288}).resize(size, size).png().toFile(path.join(root, `png${size}/colmap-pipeline.png`));
  const file = path.join(root, 'manifest.json');
  const entries = JSON.parse(fs.readFileSync(file, 'utf8')).filter(x => x.id !== 'colmap-pipeline');
  entries.push({id:'colmap-pipeline', label:'Splatypus COLMAP Pipeline', category:'03 / COLMAP',
    svg:'svg/colmap-pipeline.svg', png:'png24/colmap-pipeline.png', motif:'Front-facing platypus head formed from COLMAP aperture facets and a broad bill'});
  fs.writeFileSync(file, JSON.stringify(entries, null, 2) + '\n');
  const review = `<svg xmlns="http://www.w3.org/2000/svg" width="640" height="250" viewBox="0 0 640 250">
<rect width="640" height="250" rx="18" fill="#F3F5F7"/>
<text x="28" y="36" font-family="Segoe UI, sans-serif" font-size="20" fill="#374151">COLMAP ALL / ONE RUN</text>
<g transform="translate(34 65) scale(6)">${body}</g>
<rect x="221" y="70" width="100" height="100" rx="12" fill="white"/>
<rect x="337" y="70" width="100" height="100" rx="12" fill="#222934"/>
<rect x="453" y="70" width="100" height="100" rx="12" fill="#FFA128"/>
<g transform="translate(259 108)">${body}</g><g transform="translate(375 108)">${body}</g><g transform="translate(491 108)">${body}</g>
<text x="234" y="196" font-family="Segoe UI, sans-serif" font-size="13" fill="#526477">Native 24 px · light / dark / selected</text>
<text x="28" y="231" font-family="Segoe UI, sans-serif" font-size="13" fill="#526477">Platypus head + COLMAP aperture. One simple mark.</text></svg>`;
  fs.writeFileSync(path.join(root, 'review/colmap-pipeline.svg'), review);
  await sharp(Buffer.from(review)).png().toFile(path.join(root, 'review/colmap-pipeline.png'));
}
module.exports = generate;
if (require.main === module) generate(require(process.argv[2] || 'sharp')).catch(e => { console.error(e); process.exit(1); });
