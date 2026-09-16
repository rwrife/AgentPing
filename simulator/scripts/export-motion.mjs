// Retarget Mixamo world-space rotation deltas onto the existing Pixel Pal rig.
import fs from 'node:fs';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import * as T from 'three';
import {FBXLoader} from 'three/addons/loaders/FBXLoader.js';
const base=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'../test-results/live3d');
const source=process.argv[2];
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
const targetRest=model.bones.map(b=>{const p=new T.Vector3(),q=new T.Quaternion(),s=new T.Vector3();new T.Matrix4().fromArray(b.inverse).invert().decompose(p,q,s);return q;});
const localRest=model.bones.map(b=>{const p=new T.Vector3(),q=new T.Quaternion(),s=new T.Vector3();new T.Matrix4().fromArray(b.local).decompose(p,q,s);return q;});
const clip=root.animations[0],rate=24,count=Math.ceil(clip.duration*rate-1e-5)+1;
const mixer=new T.AnimationMixer(root),action=mixer.clipAction(clip);action.setLoop(T.LoopOnce,1);action.clampWhenFinished=true;action.play();
const frames=[];
for(let f=0;f<count;f++) {
  mixer.setTime(Math.min(f/rate,clip.duration));root.updateMatrixWorld(true);
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
const result={source:path.basename(source),duration:clip.duration,rate,bones:model.bones.length,frames:frames.map(row=>row.map(v=>Math.round(v*32767))),rootMotion:'in-place; translation omitted'};
// Remove quantization noise where Mixamo already supplies a closed loop.
if(Math.max(...result.frames[0].map((v,i)=>Math.abs(v-result.frames.at(-1)[i])))<=2)result.frames[result.frames.length-1]=[...result.frames[0]];
fs.writeFileSync(path.join(base,'waving.json'),JSON.stringify(result));
const header='#pragma once\n#include <cstdint>\nnamespace wave_motion {\ninline constexpr unsigned count='+count+', rate='+rate+';\ninline constexpr int16_t keys[]={\n'+result.frames.map(row=>row.join(',')).join(',\n')+'\n};\n}\n';
fs.writeFileSync(path.resolve(base,'../../../firmware/assets/wave_motion.h'),header);
console.log(JSON.stringify({duration:result.duration,frames:count,bones:result.bones,bytes:count*result.bones*8,mapping:names},null,2));
