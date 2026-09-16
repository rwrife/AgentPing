// Retarget Mixamo world-space rotation deltas onto the existing Pixel Pal rig.
import fs from 'node:fs';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import * as T from 'three';
import {FBXLoader} from 'three/addons/loaders/FBXLoader.js';
const base=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'../test-results/live3d');
const source=process.argv[2];
const namespaceArg=process.argv[3];
const namespace=namespaceArg&&!namespaceArg.startsWith('--')?namespaceArg:'wave_motion';
if(!/^[a-z][a-z0-9_]*_motion$/.test(namespace))throw Error('Invalid output namespace');
const rootMotion=process.argv.includes('--root');
// Ad-hoc clips uploaded over USB never become baked assets.
const skipHeader=process.argv.includes('--no-header');
// The renderer runs near 10-12 FPS, so sampling faster only inflates the clip.
const rateFlag=process.argv.indexOf('--rate');
const rate=rateFlag<0?12:Number(process.argv[rateFlag+1]);
if(!Number.isInteger(rate)||rate<1||rate>60)throw Error('--rate must be a whole number of frames per second between 1 and 60');
if(!source)throw Error('Usage: node simulator/scripts/export-motion.mjs animation.fbx');
const bytes=fs.readFileSync(source);
const root=new FBXLoader().parse(bytes.buffer.slice(bytes.byteOffset,bytes.byteOffset+bytes.byteLength),'');
root.updateMatrixWorld(true);
if(root.animations.length!==1)throw Error('Expected exactly one animation');
const model=JSON.parse(fs.readFileSync(path.join(base,'model.json'),'utf8'));
const expected=['spine','spine001','spine002','spine003','spine004','spine005','spine006','shoulderL','upper_armL','forearmL','handL','shoulderR','upper_armR','forearmR','handR','pelvisL','pelvisR','thighL','shinL','footL','toeL','heel02L','thighR','shinR','footR','toeR','heel02R'];
if(model.bones.length!==expected.length||model.bones.some((b,i)=>b.name!==expected[i]))throw Error('Target skeleton differs from the validated Pixel Pal mapping');
const names=['Hips','Spine','Spine1','Spine2','Neck','Head',null,'LeftShoulder','LeftArm','LeftForeArm','LeftHand','RightShoulder','RightArm','RightForeArm','RightHand',null,null,'LeftUpLeg','LeftLeg','LeftFoot','LeftToeBase',null,'RightUpLeg','RightLeg','RightFoot','RightToeBase',null];
const sources=names.map(n=>n?root.getObjectByName('mixamorig'+n):null);
names.forEach((n,i)=>{if(n&&!sources[i])throw Error('Missing Mixamo bone '+n);});
const sourceRest=sources.map(b=>b?.getWorldQuaternion(new T.Quaternion()));
const hipsRest=sources[0].getWorldPosition(new T.Vector3());
const targetFloor=Math.min(...model.vertices.map(v=>v[1]/4096));
const rootScale=(model.bones[0].local[13]-targetFloor)/hipsRest.y;
const targetRest=model.bones.map(b=>{const p=new T.Vector3(),q=new T.Quaternion(),s=new T.Vector3();new T.Matrix4().fromArray(b.inverse).invert().decompose(p,q,s);return q;});
const localRest=model.bones.map(b=>{const p=new T.Vector3(),q=new T.Quaternion(),s=new T.Vector3();new T.Matrix4().fromArray(b.local).decompose(p,q,s);return q;});
const clip=root.animations[0],count=Math.ceil(clip.duration*rate-1e-5)+1;
const mixer=new T.AnimationMixer(root),action=mixer.clipAction(clip);action.setLoop(T.LoopOnce,1);action.clampWhenFinished=true;action.play();
const frames=[],translations=[];
for(let f=0;f<count;f++) {
  mixer.setTime(Math.min(f/rate,clip.duration));root.updateMatrixWorld(true);
  translations.push(sources[0].getWorldPosition(new T.Vector3()).sub(hipsRest).multiplyScalar(rootScale).toArray().map(v=>Math.round(v*4096)));
  const posed=[];const row=[];
  for(let i=0;i<model.bones.length;i++) {
    const parent=model.bones[i].parent;
    const parentQ=parent<0?new T.Quaternion():posed[parent];
    const desired=sources[i]?sources[i].getWorldQuaternion(new T.Quaternion()).multiply(sourceRest[i].clone().invert()).multiply(targetRest[i]):parentQ.clone().multiply(localRest[i]);
    const local=parentQ.clone().invert().multiply(desired);
    const delta=localRest[i].clone().invert().multiply(local).normalize();
    posed.push(desired);
    // A consistent hemisphere makes fixed-point interpolation continuous.
    if(f&&new T.Quaternion().fromArray(frames[f-1],i*4).dot(delta)<0)delta.set(-delta.x,-delta.y,-delta.z,-delta.w);
    row.push(...delta.toArray());
  }
  frames.push(row);
}
const result={source:path.basename(source),duration:clip.duration,rate,bones:model.bones.length,frames:frames.map(row=>row.map(v=>Math.round(v*32767))),rootMotion:rootMotion?'scaled hip translation':'in-place; translation omitted',translations:rootMotion?translations:undefined};
// Remove quantization noise where Mixamo already supplies a closed loop.
if(Math.max(...result.frames[0].map((v,i)=>Math.abs(v-result.frames.at(-1)[i])))<=2)result.frames[result.frames.length-1]=[...result.frames[0]];
if(translations.some(row=>row.some(v=>v<-32768||v>32767)))throw Error('Root movement exceeds Q12 range');
fs.writeFileSync(path.join(base,namespace==='wave_motion'?'waving.json':namespace+'.json'),JSON.stringify(result));
let header='#pragma once\n#include <cstdint>\nnamespace '+namespace+' {\ninline constexpr unsigned count='+count+', rate='+rate+';\ninline constexpr int16_t keys[]={\n'+result.frames.map(row=>row.join(',')).join(',\n')+'\n};\n';
if(rootMotion)header+='inline constexpr int16_t roots[]={\n'+translations.map(row=>row.join(',')).join(',\n')+'\n};\n';
header+='}\n';
if(!skipHeader)fs.writeFileSync(path.resolve(base,'../../../firmware/assets/'+namespace+'.h'),header);
console.log(JSON.stringify({source:result.source,duration:result.duration,frames:count,bones:result.bones,bytes:count*(result.bones*8+(rootMotion?6:0))},null,2));
