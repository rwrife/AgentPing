import {chromium} from '@playwright/test';
import fs from 'node:fs/promises';
const clips=[
 {name:'boot',state:'startup',count:24,seconds:2.4,agent:'codex',mode:'once'},
 {name:'listening',state:'host_waiting',count:16,seconds:1.5,agent:'codex',mode:'pingpong'},
 {name:'idle',state:'idle',count:16,seconds:1.5,agent:'codex',mode:'loop'},
 ...['codex','claude','copilot'].map(agent=>({name:`wave-${agent}`,state:'waiting',count:36,seconds:4.76,agent,mode:'loop'}))
];
const browser=await chromium.launch({channel:'msedge',headless:true});
try{
 const page=await browser.newPage();await page.goto('http://127.0.0.1:5173');await page.waitForFunction(()=>window.pal);
 await page.selectOption('#motion','full');await page.click('#pause');
 const dir='test-results/firmware-linked';await fs.mkdir(dir,{recursive:true});
 await page.evaluate(()=>{
  pal.renderAt(100);pal.setState('host_waiting',true);pal.renderAt(103);
  const objects=[];pal.scene.traverse(o=>objects.push({o,p:o.position.clone(),q:o.quaternion.clone(),s:o.scale.clone()}));
  const face=document.createElement('canvas');face.width=pal.faceCanvas.width;face.height=pal.faceCanvas.height;face.getContext('2d').drawImage(pal.faceCanvas,0,0);
  window.bakeAnchor={objects,face};
 });
 for(const clip of clips){
  await page.selectOption('#agent',clip.agent);
  await page.evaluate(state=>{pal.renderAt(100);pal.setState(state,true);},clip.state);
  for(let i=0;i<clip.count;i++){
   const data=await page.evaluate(({clip,i})=>{
    pal.renderAt(100+i*clip.seconds/(clip.count-1));
    const ease=t=>{t=Math.max(0,Math.min(1,t));return t*t*(3-2*t)};
    const weight=Math.min(clip.mode==='once'?1:ease(i/5),ease((clip.count-1-i)/5));
    for(const a of bakeAnchor.objects){a.o.position.lerpVectors(a.p,a.o.position.clone(),weight);a.o.quaternion.slerpQuaternions(a.q,a.o.quaternion.clone(),weight);a.o.scale.lerpVectors(a.s,a.o.scale.clone(),weight);}
    const ctx=pal.faceCanvas.getContext('2d');ctx.save();ctx.globalAlpha=1-weight;ctx.drawImage(bakeAnchor.face,0,0);ctx.restore();
    pal.faceTexture.needsUpdate=true;
    const r=pal.renderer,c=pal.camera;r.setPixelRatio(1);r.setSize(240,384);r.setViewport(0,0,240,384);r.setClearColor(0x03070b,1);
    const h=1.3+(clip.name==='idle'?-0.25*weight:0);c.left=-h*240/384;c.right=h*240/384;c.top=h;c.bottom=-h;c.position.set(0,0,5);c.lookAt(0,0,0);c.updateProjectionMatrix();
    r.clear();r.render(pal.scene,c);return r.domElement.toDataURL('image/png').split(',')[1];
   },{clip,i});
   await fs.writeFile(`${dir}/${clip.name}-${String(i).padStart(2,'0')}.png`,Buffer.from(data,'base64'));
  }
 }
 await fs.writeFile(`${dir}/clips.json`,JSON.stringify(clips,null,2));
}finally{await browser.close()}
