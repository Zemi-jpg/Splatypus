// Preserve Picture1's original SVG layout; revise only the dataset section.
const fs = require('fs');
const path = require('path');
const sharp = require('C:/Users/basar/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/sharp');

function makeReferenceDiagram() {
  let svg = fs.readFileSync(path.join(__dirname, 'archive/2026-09-28-before-realignment/splatypus-workflow-horizontal.svg'), 'utf8');
  svg = svg.replace('Choose a dataset route', 'Prepare dataset · optional steps');
  // Remove the historical three independent routes from COLMAP to Brush.
  for (const d of [
    'M990 550 H1030 V355 H1080', 'M1030 550 H1080', 'M1030 550 V785 H1080',
    'M1830 355 H1880 V550 H1920', 'M1830 550 H1920', 'M1830 785 H1880 V550'
  ]) svg = svg.split(/\r?\n/).filter(line => !line.includes(`d="${d}"`)).join('\n');
  const start = svg.indexOf('<rect x="1080" y="275"');
  const end = svg.indexOf('<rect x="2180" y="450"');
  if (start < 0 || end <= start) throw new Error('Historical SVG layout changed.');
  const s = [];
  const esc = t => t.replaceAll('&','&amp;').replaceAll('<','&lt;');
  function text(x,y,t,size=24,color='#43546b',weight=400) {
    s.push(`<text x="${x}" y="${y}" font-size="${size}" fill="${color}" font-weight="${weight}">${esc(t)}</text>`);
  }
  function edge(d,dashed=false) {
    s.push(`<path d="${d}" fill="none" stroke="#7a8ca1" stroke-width="3" ${dashed?'stroke-dasharray="9 7"':''} marker-end="url(#arrow)"/>`);
  }
  function box(x,y,w,h,title,lines,size=24) {
    s.push(`<rect x="${x}" y="${y}" width="${w}" height="${h}" rx="15" fill="#eef6f4" stroke="#bfd9d0" stroke-width="2"/>`);
    text(x+22,y+40,title,28,'#142b46',600);
    lines.forEach((line,i)=>text(x+22,y+80+i*34,line,size));
  }
  // Reconstruct once; the base supplies Brush directly unless an alignment is connected.
  edge('M990 550 H1030 V355 H1080');
  edge('M1830 355 H1880 V550 H1920');
  // Optional undistortion reads and replaces the SAME base image/model pair.
  edge('M1510 435 V470');
  edge('M1160 550 H1105 V435');
  // Base feeds previews and, separately, every alignment write. No aligned-to-base loop.
  edge('M1800 435 V660 H1045 V785 H1080', true);
  edge('M1045 785 V923 H1725 V895', true);
  edge('M1295 785 H1330');
  edge('M1585 785 H1620');
  edge('M1830 785 H1880 V600 H1920');

  box(1080,275,750,160,'Base dataset · original or undistorted',[
    'Starts with original images + matching camera model.',
    'Undistortion replaces this pair; alignment preserves it.'
  ]);
  box(1160,470,575,160,'Optional · Undistort',[
    'Correct images and calibration together.',
    'Overwrite the base; no separate dataset.'
  ]);
  text(1122,461,'Overwrite base',21,'#3877a6');
  text(1080,694,'Optional · Alignment',28,'#142b46',600);
  text(1460,694,'Base images + separate aligned model',21);
  box(1080,720,215,145,'Export model',['Readable text','base model']);
  box(1330,720,255,175,'Preview + move',['Cloud + cameras','Move → rotate','X / Y / Z → scale']);
  box(1620,720,210,175,'Align model',['Transform base','Save separate','aligned model']);
  text(1080,960,'Each Write starts from the base; transforms do not accumulate.',22);
  box(1920,470,210,165,'Brush Dataset',['Active images +','base or aligned','camera model'],22);
  text(1920,685,'Sparse empty:',21,'#3877a6');
  text(1920,715,'use base model',21);
  text(1920,765,'Aligned output →',21,'#3877a6');
  text(1920,795,'Sparse override',21);
  svg = svg.slice(0,start)+s.join('\n')+'\n'+svg.slice(end);
  svg = svg.replace('Do not combine Undistorted with an aligned Sparse override.',
    'Keep images paired with their matching base or aligned model.');
  svg = svg.replace('<defs>', '<title>Splatypus workflow — Picture1 layout with updated dataset logic</title><desc>Original reconstruction forms the base dataset. Optional undistortion overwrites its images and camera calibration together. Optional alignment always reads the base and saves a separate aligned model. Brush uses the base by default, or the aligned model through Sparse override. Setup, reconstruction, training and preview match Picture1.</desc><defs>');
  return svg;
}
async function saveReference() {
  const svg = makeReferenceDiagram();
  const png = await sharp(Buffer.from(svg)).png().toBuffer();
  for (const name of ['splatypus-workflow-horizontal','splatypus-workflow-optional-steps']) {
    fs.writeFileSync(path.join(__dirname,name+'.svg'),svg);
    fs.writeFileSync(path.join(__dirname,name+'.png'),png);
  }
  console.log('Restored Picture1 layout and revised dataset logic. Exported 3200 × 1120 PNG and SVG.');
}
module.exports = {makeReferenceDiagram};
if(require.main===module) saveReference().catch(error=>{console.error(error);process.exitCode=1;});
