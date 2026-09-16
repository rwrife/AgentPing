import {chromium} from '@playwright/test';
import fs from 'node:fs/promises';
const browser=await chromium.launch({channel:'msedge',headless:true});
try{
 const page=await browser.newPage();await page.goto('http://127.0.0.1:5173');await page.waitForFunction(()=>window.pal);
 await page.selectOption('#motion','full');await page.click('#pause');
 await fs.mkdir('test-results/firmware-frames',{recursive:true});
 for(const [name,state,count,seconds] of [['idle','idle',24,1.958333],['wave','waiting',36,4.76]]){
  await page.evaluate(s=>{pal.renderAt(100);pal.setState(s,true);},state);
  for(let i=0;i<count;i++){
   const data=await page.evaluate(({t,name})=>{
    pal.renderAt(t);
    const r=pal.renderer,c=pal.camera;r.setPixelRatio(1);r.setSize(240,384);r.setViewport(0,0,240,384);r.setClearColor(0x03070b,1);
    const halfHeight=name==='wave'?1.3:1.05;c.left=-halfHeight*240/384;c.right=halfHeight*240/384;c.top=halfHeight;c.bottom=-halfHeight;c.position.set(0,0,5);c.lookAt(0,0,0);c.updateProjectionMatrix();
    r.clear();r.render(pal.scene,c);return r.domElement.toDataURL('image/png').split(',')[1];
   },{t:100+i*seconds/count,name});
   await fs.writeFile(`test-results/firmware-frames/${name}-${String(i).padStart(2,'0')}.png`,Buffer.from(data,'base64'));
  }
 }
}finally{await browser.close()}
