// Preview family extension. node generate-video.cjs [sharp module path]
const fs = require('fs'), path = require('path');
async function generate(sharp) {
  const root = __dirname;
  const stroke = d => `<path d="${d}" fill="none" stroke="#6B778A" stroke-width="1.65" stroke-linecap="round" stroke-linejoin="round"/>`;
  const icons = [
    ['video-frames', 'SP Video Frames', 'Stacked image frames with recognizable mountains and sun',
      stroke('M8 2H22V16M5 5H19V19M2 8H16V22H2Z') +
      '<path d="M4 19 8 13.5 11 17 13 15 14 19Z" fill="#398A94"/>' +
      '<circle cx="12.5" cy="11.5" r="1.35" fill="#B99766"/>'],
    ['video-playback', 'SP Video Playback', 'Filmstrip enclosing a swimming platypus with motion trails',
      stroke('M2 3.5H22V20.5H2ZM2 7H22M2 17H22') +
      '<path d="M5 4V6.5M9.5 4V6.5M14 4V6.5M18.5 4V6.5M5 17.5V20M9.5 17.5V20M14 17.5V20M18.5 17.5V20" stroke="#6B778A" stroke-width="1.6"/>' +
      '<path d="M3.5 9H7M3.5 11H5.5" fill="none" stroke="#A0ABB8" stroke-width="1.1" stroke-linecap="round"/>' +
      '<path d="M9 12Q5 10 4 12Q3 14 5 15Q7 16 10 13Z" fill="#398A94"/>' +
      '<path d="M8 11.5Q9 9 13 10Q15 8 17 10V13Q15 15 11 14.5L9 16H7.5L8.5 13.5Q7 13 8 11.5Z" fill="#398A94"/>' +
      '<path d="M16 11Q19 10 21 11.5V13Q19 14.5 16 13.5Z" fill="#B99766"/>' +
      '<circle cx="15.5" cy="10.7" r=".7" fill="#FFFFFF"/>'],
    ['view-capture', 'SP View Capture', 'Viewfinder corners surrounding a camera with a circular lens',
      stroke('M2 7V3H6M18 3H22V7M22 17V21H18M6 21H2V17') +
      '<path d="M5 9H8L9.5 7H14.5L16 9H19V17H5Z" fill="none" stroke="#398A94" stroke-width="1.65" stroke-linejoin="round"/>' +
      '<circle cx="12" cy="12.5" r="2.5" fill="none" stroke="#398A94" stroke-width="1.65"/>']
  ];
  for (const [id, , , body] of icons) {
    const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24">${body}</svg>`;
    fs.writeFileSync(path.join(root, 'svg', id + '.svg'), svg);
    for (const size of [24, 48, 96]) await sharp(Buffer.from(svg), {density:288}).resize(size,size).png().toFile(path.join(root, `png${size}`, id+'.png'));
  }
  const file = path.join(root, 'manifest.json');
  const entries = JSON.parse(fs.readFileSync(file, 'utf8')).filter(x => !icons.some(i => i[0] === x.id));
  entries.push(...icons.map(([id,label,motif]) => ({id,label,category:'06 / PREVIEW',svg:`svg/${id}.svg`,png:`png24/${id}.png`,motif})));
  fs.writeFileSync(file, JSON.stringify(entries,null,2)+'\n');
  let review = '<svg xmlns="http://www.w3.org/2000/svg" width="960" height="290"><rect width="960" height="290" fill="#F5F4F0"/><g font-family="Segoe UI,Arial,sans-serif" fill="#253D45"><text x="24" y="34" font-size="20">Splatypus / Video and capture</text>';
  icons.forEach(([id,label,motif,body], i) => {
    const x=24+i*312;
    review += `<rect x="${x}" y="54" width="296" height="212" rx="12" fill="white"/><g transform="translate(${x+18} 74) scale(4)">${body}</g><rect x="${x+140}" y="88" width="40" height="40" rx="5" fill="#F3F5F7"/><rect x="${x+194}" y="88" width="40" height="40" rx="5" fill="#222934"/><g transform="translate(${x+148} 96)">${body}</g><g transform="translate(${x+202} 96)">${body}</g><text x="${x+140}" y="150" font-size="11">24 px / light + dark</text><text x="${x+18}" y="210" font-size="18">${label}</text><text x="${x+18}" y="242" font-size="12" fill="#6B778A">${id}</text>`;
  });
  review += '</g></svg>';
  fs.writeFileSync(path.join(root,'review/video-components.svg'),review);
  await sharp(Buffer.from(review)).png().toFile(path.join(root,'review/video-components.png'));
}
module.exports = generate;
if (require.main === module) generate(require(process.argv[2] || 'sharp')).catch(e => {console.error(e);process.exit(1);});
