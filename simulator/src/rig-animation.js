import * as THREE from 'three';
import {ease} from './motion.js';

const axisZ=new THREE.Vector3(0,0,1);
function worldQuaternion(bone,world){
  const parent=bone.parent.getWorldQuaternion(new THREE.Quaternion());
  bone.quaternion.copy(parent.invert().multiply(world));
  bone.updateWorldMatrix(false,true);
}
function aimAt(bone,child,target){
  const origin=bone.getWorldPosition(new THREE.Vector3());
  const current=child.getWorldPosition(new THREE.Vector3()).sub(origin).normalize();
  const direction=target.clone().sub(origin).normalize();
  const turn=new THREE.Quaternion().setFromUnitVectors(current,direction);
  worldQuaternion(bone,turn.multiply(bone.getWorldQuaternion(new THREE.Quaternion())));
}
function reach(arm,forearm,hand,target,pole){
  if(!arm||!forearm||!hand)return;
  const shoulder=arm.getWorldPosition(new THREE.Vector3());
  const elbow=forearm.getWorldPosition(new THREE.Vector3());
  const wrist=hand.getWorldPosition(new THREE.Vector3());
  const upper=shoulder.distanceTo(elbow),lower=elbow.distanceTo(wrist);
  const offset=target.clone().sub(shoulder);
  const distance=THREE.MathUtils.clamp(offset.length(),Math.abs(upper-lower)+.001,upper+lower-.001);
  const direction=offset.normalize();
  const bend=pole.clone().sub(shoulder);bend.addScaledVector(direction,-bend.dot(direction)).normalize();
  const along=(upper*upper-lower*lower+distance*distance)/(2*distance);
  const goal=shoulder.clone().addScaledVector(direction,along).addScaledVector(bend,Math.sqrt(Math.max(0,upper*upper-along*along)));
  aimAt(arm,forearm,goal);aimAt(forearm,hand,shoulder.addScaledVector(direction,distance));
}
export function createRigAnimation(model,clips=[]){
  const bones=[];model.traverse(o=>{if(o.isBone)bones.push(o);});
  const bone=name=>bones.find(b=>b.name===`mixamorig${name}`);
  const clip=model.animations.find(c=>c.duration>1 && c.name.includes('Idle_11'));
  if(!clip)throw new Error('Expected Meshy Idle_11 clip is missing');
  const mixer=new THREE.AnimationMixer(model);const idle=mixer.clipAction(clip);idle.play();
  const actions=Object.fromEntries(clips.map(c=>[c.name,mixer.clipAction(c).play()]));
  const base=bones.map(b=>({q:b.quaternion.clone(),p:b.position.clone(),s:b.scale.clone()}));
  // Shuffle once, so pause and scrubbing reproduce the same pose without frame jitter.
  const dances=['jumping-jacks','twirl','side-bounce','disco','shimmy','robot'];
  for(let i=dances.length-1;i>0;i--){const j=Math.floor(Math.random()*(i+1));[dances[i],dances[j]]=[dances[j],dances[i]];}
  let activeDance=null;
  let blendStart=-10,snapshot=null;
  return {
    clip,bones,mixer,clips,get dance(){return activeDance;},
    idleMotion(age){
      const t=age%11-3,name=dances[Math.floor(age/11)%dances.length];
      const weight=ease(t/.6)*(1-ease((t-3.3)/.7));
      if(t<0||t>=4)return {x:0,y:0,yaw:0};
      return {x:name==='side-bounce'?Math.sin(t*Math.PI*2)*.12*weight:0,
        y:['jumping-jacks','side-bounce'].includes(name)?Math.sin(t*Math.PI*2)**2*.12*weight:0,
        yaw:name==='twirl'?Math.PI*2*ease(t/4):0};
    },
    transition(time){blendStart=time;snapshot=bones.map(b=>({q:b.quaternion.clone(),p:b.position.clone()}));},
    update(time,state,age,stanceDegrees,reduced,preview='auto'){
      // The mixer skips unchanged tracks when paused. Restore its last base pose
      // before additive edits so corrections never accumulate across renders.
      bones.forEach((b,i)=>{b.quaternion.copy(base[i].q);b.position.copy(base[i].p);b.scale.copy(base[i].s);});
      const phase=age%12;
      const wave=state==='waiting' && !reduced && actions.wave && preview==='auto';
      const waveWeight=wave?ease(phase/.4)*(1-ease((phase-(actions.wave.getClip().duration-.5))/.5)):0;
      idle.setEffectiveWeight(preview==='auto'||reduced?1-waveWeight:0);
      for(const [name,action] of Object.entries(actions)){
        action.enabled=true;
        action.setEffectiveWeight(reduced?0:preview===name?1:name==='wave'?waveWeight:0);
      }
      mixer.setTime(reduced?0:time);
      if(wave){actions.wave.time=Math.min(phase,actions.wave.getClip().duration-.001);mixer.update(0);}
      bones.forEach((b,i)=>{base[i].q.copy(b.quaternion);base[i].p.copy(b.position);base[i].s.copy(b.scale);});
      model.updateWorldMatrix(true,true);
      // Additive abduction with ankle orientation preserved. Zero is the untouched clip.
      for(const [side,sign] of [['Left',1],['Right',-1]]){
        const hip=bone(`${side}UpLeg`),foot=bone(`${side}Foot`);
        if(!hip || !foot)continue;
        const footRotation=foot.getWorldQuaternion(new THREE.Quaternion());
        const correction=new THREE.Quaternion().setFromAxisAngle(axisZ,THREE.MathUtils.degToRad(stanceDegrees)*sign);
        worldQuaternion(hip,correction.multiply(hip.getWorldQuaternion(new THREE.Quaternion())));
        worldQuaternion(foot,footRotation);
      }
      activeDance=null;
      if(state==='idle' && !reduced && preview==='auto'){
        // A four-second dance after a short quiet interval; attention interrupts immediately.
        const cycle=Math.floor(age/11),danceTime=age%11-3;
        if(danceTime>=0 && danceTime<4){
          activeDance=dances[cycle%dances.length];
          const weight=ease(danceTime/.6)*(1-ease((danceTime-3.3)/.7));
          const beat=Math.sin(danceTime*Math.PI*3);
          bone('Spine1')?.rotateZ(.12*beat*weight);
          bone('Head')?.rotateZ(-.09*beat*weight);
          for(const [side,sign] of [['Left',1],['Right',-1]]){
            const arm=bone(`${side}Arm`),elbow=bone(`${side}ForeArm`),hand=bone(`${side}Hand`);
            if(activeDance==='jumping-jacks'){
              const open=(.5-.5*Math.cos(danceTime*Math.PI*2))*weight;
              arm?.rotateZ(sign*2.2*open);
              const hip=bone(`${side}UpLeg`);
              if(hip){
                model.updateWorldMatrix(true,true);
                worldQuaternion(hip,new THREE.Quaternion().setFromAxisAngle(axisZ,sign*.25*open).multiply(hip.getWorldQuaternion(new THREE.Quaternion())));
              }
              bone(`${side}Leg`)?.rotateX(.12*(1-open)*weight);
            }else if(activeDance==='twirl'){
              arm?.rotateZ(sign*.7*weight);elbow?.rotateX(-.35*weight);
            }else if(activeDance==='side-bounce'){
              arm?.rotateZ(sign*.3*weight);elbow?.rotateX(-.6*weight);
              bone(`${side}UpLeg`)?.rotateX(-.2*Math.max(0,beat*sign)*weight);
              bone(`${side}Leg`)?.rotateX(.4*Math.max(0,beat*sign)*weight);
            }else if(activeDance==='disco'){
              arm?.rotateZ(sign*(side==='Left'?.85+.22*beat:.2)*weight);
              elbow?.rotateX(-(.35+.2*beat*sign)*weight);
            }else if(activeDance==='shimmy'){
              arm?.rotateZ(sign*.45*weight);
              arm?.rotateX(.3*beat*sign*weight);
              elbow?.rotateX(-.65*weight);
              hand?.rotateY(.25*beat*weight);
            }else{
              const tick=Math.tanh(3*beat);
              arm?.rotateZ(sign*.55*weight);
              elbow?.rotateX(-(.65+.3*tick*sign)*weight);
              hand?.rotateZ(.3*tick*sign*weight);
            }
          }
        }
      }
      if(state==='startup' && !reduced && preview==='auto'){
        const airborne=ease(age/.3)*(1-ease((age-1.05)/.3));
        const crouch=Math.sin(Math.PI*Math.max(0,Math.min((age-1.35)/.55,1)));
        const bend=.5*airborne+.65*crouch;
        // Local rotations are additive to the Meshy idle and return to zero before listening.
        for(const side of ['Left','Right']){
          bone(`${side}UpLeg`)?.rotateX(-bend*.55);
          bone(`${side}Leg`)?.rotateX(bend);
          bone(`${side}Foot`)?.rotateX(-bend*.45);
          bone(`${side}Arm`)?.rotateZ((side==='Left'?1:-1)*(.45*airborne+.15*crouch));
          bone(`${side}ForeArm`)?.rotateX(-.4*airborne);
        }
        bone('Spine1')?.rotateX(.12*crouch);
      }
      if(state==='error' && !reduced && preview==='auto'){
        const phase=age%12;
        const weight=ease(phase/.5)*(1-ease((phase-3)/.6));
        for(const [side,sign] of [['Left',1],['Right',-1]]){
          const arm=bone(`${side}Arm`),forearm=bone(`${side}ForeArm`),hand=bone(`${side}Hand`);
          if(!arm||!forearm||!hand)continue;
          const joints=[arm,forearm,hand],original=joints.map(b=>b.quaternion.clone());
          model.updateWorldMatrix(true,true);
          const shoulder=arm.getWorldPosition(new THREE.Vector3());
          aimAt(arm,forearm,shoulder.clone().add(new THREE.Vector3(sign*.12,.12,.25)));
          const elbow=forearm.getWorldPosition(new THREE.Vector3());
          aimAt(forearm,hand,elbow.add(new THREE.Vector3(sign*(-.12+.05*Math.sin(phase*10)),.3,.05)));
          hand.rotateY(sign*Math.sin(phase*10)*.2);
          joints.forEach((b,i)=>b.quaternion.slerpQuaternions(original[i],b.quaternion.clone(),weight));
        }
      }
      if(state==='running' && preview==='auto'){
        const weight=reduced?1:ease(age/.9);
        const joints=['Spine1','Head','LeftArm','LeftForeArm','LeftHand'].map(bone).filter(Boolean);
        const original=joints.map(b=>b.quaternion.clone());
        bone('Spine1')?.rotateX(.08);bone('Head')?.rotateX(.12);
        model.updateWorldMatrix(true,true);
        const head=bone('Head');
        if(head){
          const chin=head.getWorldPosition(new THREE.Vector3()).add(new THREE.Vector3(.08,-.16,.25));
          const elbowPole=chin.clone().add(new THREE.Vector3(.25,-.5,.05));
          reach(bone('LeftArm'),bone('LeftForeArm'),bone('LeftHand'),chin,elbowPole);
          // The right arm retains the natural idle pose at the side.
        }
        joints.forEach((b,i)=>b.quaternion.slerpQuaternions(original[i],b.quaternion.clone(),weight));
      }
      const blend=reduced?1:ease((time-blendStart)/.5);
      if(snapshot && blend<1)bones.forEach((b,i)=>{
        b.quaternion.slerpQuaternions(snapshot[i].q,b.quaternion.clone(),blend);
        b.position.lerpVectors(snapshot[i].p,b.position.clone(),blend);
      });
      model.updateWorldMatrix(true,true);
    },
  };
}
