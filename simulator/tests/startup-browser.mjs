import {chromium} from '@playwright/test';import assert from 'node:assert/strict';
const b=await chromium.launch({channel:'msedge',headless:true});const p=await b.newPage();await p.goto('http://127.0.0.1:5173');await p.waitForFunction(()=>window.pal);await p.selectOption('#motion','full');await p.click('#pause');
for(const previous of ['idle','waiting','startup']){
 await p.evaluate(s=>{pal.setState(s);pal.renderAt(pal.time+2);pal.setState('startup',true);pal.renderAt(pal.time);},previous);
 const visible=await p.evaluate(()=>{const c=document.createElement('canvas');c.width=280;c.height=456;const ctx=c.getContext('2d');ctx.drawImage(pal.renderer.domElement,0,0);return ctx.getImageData(0,0,280,456).data.some((v,i)=>i%4===3&&v>0);});assert.equal(visible,false,`boot must start entirely offscreen from ${previous}`);
 await p.evaluate(()=>pal.renderAt(pal.time+.85));assert.ok(await p.evaluate(()=>pal.pose.y>0));
}
await b.close();console.log('PASS: boot starts invisible from idle, attention, and restart; jumps into view');
