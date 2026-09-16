// Rasterize a local SVG to the robot's 48x48, row-major, LSB-first icon format.
import fs from 'node:fs';
import {chromium} from '@playwright/test';
const [source, target] = process.argv.slice(2);
if (!source || !target) throw Error('Usage: node pack-face-icon.mjs source.svg output.bin');
const svg = fs.readFileSync(source, 'utf8');
if (!svg.trimStart().startsWith('<svg')) throw Error('Expected an SVG document');
const browser = await chromium.launch({headless:true, channel:process.env.PLAYWRIGHT_CHANNEL || 'msedge'});
try {
  const page = await browser.newPage();
  const bytes = await page.evaluate(async svg => {
    const image = new Image();
    image.src = 'data:image/svg+xml;base64,' + btoa(svg);
    await image.decode();
    const canvas = document.createElement('canvas');
    canvas.width = canvas.height = 48;
    const ctx = canvas.getContext('2d');
    const scale = 48 / Math.max(image.width, image.height);
    const w = image.width * scale, h = image.height * scale;
    ctx.drawImage(image, (48-w)/2, (48-h)/2, w, h);
    const rgba = ctx.getImageData(0,0,48,48).data;
    const data = Array(288).fill(0);
    for(let i=0;i<2304;i++) if(rgba[i*4+3]>=128) data[i>>3] |= 1<<(i&7);
    if(!data.some(Boolean)) throw Error('Empty icon');
    return data;
  }, svg);
  fs.writeFileSync(target, Buffer.from(bytes));
  console.log(`Wrote ${target}: ${bytes.length} bytes`);
} finally { await browser.close(); }
