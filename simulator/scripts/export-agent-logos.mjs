import fs from 'node:fs';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {chromium} from '@playwright/test';
const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'../..');
const names=['codex','claude','githubcopilot'];
const browser=await chromium.launch({headless:true,channel:process.env.PLAYWRIGHT_CHANNEL||"msedge"});
try {
  const page=await browser.newPage();
  const masks=[];
  for(const name of names) {
    const svg=fs.readFileSync(path.join(root,'simulator/public/agents',name+'.svg'),'utf8');
    masks.push(await page.evaluate(svg=>{
      const doc=new DOMParser().parseFromString(svg,'image/svg+xml');
      const canvas=document.createElement('canvas');canvas.width=canvas.height=48;
      const ctx=canvas.getContext('2d');ctx.scale(2,2);ctx.fillStyle='white';
      for(const p of doc.querySelectorAll('path'))ctx.fill(new Path2D(p.getAttribute('d')),'evenodd');
      const rgba=ctx.getImageData(0,0,48,48).data,bytes=new Array(288).fill(0);
      for(let i=0;i<2304;i++)if(rgba[i*4+3]>=128)bytes[i>>3]|=1<<(i&7);
      if(!bytes.some(Boolean))throw Error('Empty logo');
      return bytes;
    },svg));
  }
  const header='// Generated from simulator/public/agents/*.svg; see its README and LICENSE.\n#pragma once\n#include <cstdint>\nnamespace agent_logos {\ninline constexpr uint8_t masks[3][288]={\n'+masks.map(row=>'{'+row.join(',')+'}').join(',\n')+'\n};\n}\n';
  fs.writeFileSync(path.join(root,'firmware/assets/agent_logos.h'),header);
  console.log('Generated 3 real logo masks: 864 bytes in flash.');
} finally {await browser.close();}
