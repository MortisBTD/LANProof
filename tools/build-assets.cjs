// npm install --prefix tools; node tools/build-assets.cjs
const fs = require('node:fs');
const path = require('node:path');
const sharp = require(process.env.LANPROOF_SHARP_PATH || 'sharp');
const root = path.resolve(__dirname, '..');
const assets = path.join(root, 'assets');
for (const folder of ['logo', 'icons', 'app']) fs.mkdirSync(path.join(assets, folder), { recursive: true });

const shield = 'M32 7 L52 14 V29 C52 42 44 51 32 57 C20 51 12 42 12 29 V14 Z';
const route = 'M23 24 V39 H41 M32 24 V31 H41';
const symbol = `<path d="${shield}" fill="url(#glass)" stroke="white" stroke-opacity=".8" stroke-width="1.8"/><path d="${route}" fill="none" stroke="white" stroke-width="3.5" stroke-linecap="round" stroke-linejoin="round"/><g fill="white"><circle cx="23" cy="23" r="2.8"/><circle cx="41" cy="31" r="2.8"/><circle cx="41" cy="39" r="2.8"/></g>`;
const definitions = '<defs><linearGradient id="blue" x1="0" y1="0" x2="1" y2="1"><stop stop-color="#75BBFF"/><stop offset="1" stop-color="#1268EA"/></linearGradient><linearGradient id="glass" x1="0" y1="0" x2="1" y2="1"><stop stop-color="white" stop-opacity=".25"/><stop offset="1" stop-color="white" stop-opacity=".02"/></linearGradient></defs>';
const svg = (body, width=64, height=64) => `<svg xmlns="http://www.w3.org/2000/svg" width="${width}" height="${height}" viewBox="0 0 ${width} ${height}">${definitions}${body}</svg>`;
const mark = svg('<rect x="1" y="1" width="62" height="62" rx="18" fill="url(#blue)" stroke="white" stroke-opacity=".55"/>'+symbol);
fs.writeFileSync(path.join(assets,'logo','lanproof-mark.svg'), mark);
for (const [theme, ink, muted] of [['dark','#EAF0FA','#A6B2C7'],['light','#202839','#67758A']])
  fs.writeFileSync(path.join(assets,'logo',`lanproof-wordmark-${theme}.svg`), svg(`<g><rect x="1" y="1" width="62" height="62" rx="18" fill="url(#blue)"/>${symbol}</g><text x="80" y="32" font-family="Segoe UI,Arial,sans-serif" font-weight="600" font-size="29" letter-spacing="-.7" fill="${ink}">LANProof</text><text x="81" y="51" font-family="Segoe UI,Arial,sans-serif" font-size="11" fill="${muted}">NETWORK DIAGNOSTICS</text>`, 260,64));
const icons = {
  overview: '<rect x="5" y="5" width="8" height="8" rx="2"/><rect x="19" y="5" width="8" height="8" rx="2"/><rect x="5" y="19" width="8" height="8" rx="2"/><rect x="19" y="19" width="8" height="8" rx="2"/>',
  history: '<path d="M3 23L10 15L16 19L23 8L29 12"/>',
  diagnostics: '<circle cx="14" cy="14" r="9"/><path d="M21 21L28 28M10 14H18M14 10V18"/>',
  reports: '<rect x="7" y="3" width="18" height="26" rx="3"/><path d="M11 11H21M11 16H21M11 21H17"/>',
  settings: '<path d="M5 8H27M5 16H27M5 24H27"/><circle cx="11" cy="8" r="3"/><circle cx="22" cy="16" r="3"/><circle cx="14" cy="24" r="3"/>',
  wifi: '<path d="M4 12C11 5 21 5 28 12M9 18C13 14 19 14 23 18M13 23C15 21 17 21 19 23"/><circle cx="16" cy="27" r="1"/>',
  pulse: '<path d="M4 17H10L14 7L19 25L23 14H28"/>',
  download: '<path d="M16 5V25M9 18L16 25L23 18"/>',
  clock: '<circle cx="16" cy="16" r="11"/><path d="M16 9V16L21 19"/>',
  sun: '<circle cx="16" cy="16" r="6"/><path d="M16 2V5M16 27V30M2 16H5M27 16H30M6 6L8 8M24 24L26 26M6 26L8 24M24 8L26 6"/>',
  moon: '<path d="M25 20A11 11 0 0 1 12 7A11 11 0 1 0 25 20Z"/>'
};
for (const [name,body] of Object.entries(icons)) fs.writeFileSync(path.join(assets,'icons',`${name}.svg`),`<svg xmlns="http://www.w3.org/2000/svg" width="32" height="32" viewBox="0 0 32 32" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round">${body}</svg>`);
(async()=>{
  await sharp(Buffer.from(mark)).resize(512,512).png().toFile(path.join(assets,'logo','lanproof-mark.png'));
  const sizes=[16,24,32,48,64,128,256];
  const images=await Promise.all(sizes.map(size=>sharp(Buffer.from(mark)).resize(size,size).png().toBuffer()));
  const header=Buffer.alloc(6+16*sizes.length); header.writeUInt16LE(1,2); header.writeUInt16LE(sizes.length,4);
  let offset=header.length;
  sizes.forEach((size,i)=>{const start=6+i*16; header[start]=size===256?0:size; header[start+1]=size===256?0:size; header.writeUInt16LE(1,start+4); header.writeUInt16LE(32,start+6); header.writeUInt32LE(images[i].length,start+8); header.writeUInt32LE(offset,start+12); offset+=images[i].length;});
  fs.writeFileSync(path.join(assets,'app','AppIcon.ico'),Buffer.concat([header,...images]));
  console.log('Generated: SVG mark, two wordmarks, 11 SVG icons, 512px PNG, 7-size Windows ICO.');
})();
