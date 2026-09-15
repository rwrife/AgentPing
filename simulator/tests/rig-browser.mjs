import {chromium} from '@playwright/test';
import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
const browser=await chromium.launch({channel:'msedge',headless:true});
const page=await browser.newPage({viewport:{width:1280,height:1000},reducedMotion:'reduce'});
await fs.mkdir('test-results',{recursive:true});
try{
  await page.goto('http://127.0.0.1:5173/');await page.waitForFunction(()=>window.pal);
  assert.ok(await page.evaluate(()=>pal.rig.bones.length>=30));
  assert.ok(await page.evaluate(()=>pal.rig.clip.duration>1.9));
  assert.equal(await page.evaluate(()=>!!pal.root.getObjectByName('Icosphere')),false);
  await page.locator('summary').click();await page.locator('[data-state=idle]').click();
  const footGap=()=>page.evaluate(()=>{
    const left=pal.rig.bones.find(b=>b.name==='mixamorigLeftFoot');
    const right=pal.rig.bones.find(b=>b.name==='mixamorigRightFoot');
    return Math.abs(left.matrixWorld.elements[12]-right.matrixWorld.elements[12]);
  });
  await page.locator('#stance').fill('0');await page.evaluate(()=>pal.renderAt(4));const narrow=await footGap();
  await page.locator('#screen').screenshot({path:'test-results/rig-stance-0.png'});
  await page.locator('#stance').fill('6');await page.evaluate(()=>pal.renderAt(4));const corrected=await footGap();
  assert.ok(corrected>narrow+.04,'stance correction must widen the feet');
  const bonesBefore=await page.evaluate(()=>pal.rig.bones.map(b=>b.quaternion.toArray()));
  await page.evaluate(()=>{for(let i=0;i<60;i++)pal.renderAt(4);});
  assert.deepEqual(await page.evaluate(()=>pal.rig.bones.map(b=>b.quaternion.toArray())),bonesBefore,'additive rig corrections must not accumulate while paused');
  await page.locator('#screen').screenshot({path:'test-results/rig-stance-6.png'});
  await page.locator('#stance').fill('12');await page.evaluate(()=>pal.renderAt(4));
  await page.locator('#screen').screenshot({path:'test-results/rig-stance-12.png'});
  await page.locator('#stance').fill('0');await page.evaluate(()=>pal.renderAt(4));
  assert.ok(Math.abs(await footGap()-narrow)<1e-6,'zero must restore original idle');
  // A separate normal-motion page allows deterministic sampling of the wave.
  const moving=await browser.newPage({viewport:{width:1280,height:1000}});
  await moving.goto('http://127.0.0.1:5173/');await moving.waitForFunction(()=>window.pal);
  await moving.locator('#pause').click();await moving.evaluate(()=>{pal.setState('idle');pal.renderAt(10);});
  const handIdle=await moving.evaluate(()=>Math.max(...pal.rig.bones.filter(b=>/mixamorig(Left|Right)Hand$/.test(b.name)).map(b=>b.matrixWorld.elements[13])));
  await moving.evaluate(()=>pal.setState('waiting'));
  for(const age of [.6,1.2,1.8,3]){
    await moving.evaluate(t=>pal.renderAt(10+t),age);
    await moving.locator('#screen').screenshot({path:`test-results/rig-wave-${age}.png`});
    if(age===1.2){
      const handRaised=await moving.evaluate(()=>Math.max(...pal.rig.bones.filter(b=>/mixamorig(Left|Right)Hand$/.test(b.name)).map(b=>b.matrixWorld.elements[13])));
      assert.ok(handRaised>handIdle+.2,'wave must raise the actual hand bone');
    }
  }
  await moving.locator('summary').click();
  for(const clip of ['walk','run','wave']){
    await moving.locator('#clip').selectOption(clip);
    if(!await moving.evaluate(()=>pal.paused))await moving.locator('#pause').click();
    await moving.evaluate(()=>pal.renderAt(pal.time+2));
    const before=await moving.evaluate(()=>pal.rig.bones.map(b=>b.quaternion.toArray()));
    await moving.evaluate(()=>pal.renderAt(pal.time+.3));
    assert.notDeepEqual(await moving.evaluate(()=>pal.rig.bones.map(b=>b.quaternion.toArray())),before);
    await moving.locator('#screen').screenshot({path:`test-results/clip-${clip}.png`});
  }
  console.log('PASS: real rig/idle, helper excluded, stance widened/reversible, paused bones stable, skeletal hand raised.');
}finally{await browser.close();}
