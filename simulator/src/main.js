import './style.css';
import '@fontsource/plus-jakarta-sans/400.css';
import '@fontsource/plus-jakarta-sans/700.css';
import * as THREE from 'three';
import {FBXLoader} from 'three/addons/loaders/FBXLoader.js';
import {STATES,AGENTS,SEQUENCE,sequenceState} from './states.js';
import {drawFace} from './face.js';
import {loadAgentLogos} from './agent-logos.js';
import {DEFAULT_MESSAGES,hasMessage,drawMessage} from './message.js';
import {drawBackground} from './background.js';
import {ease,mixPose,characterPose,viewTarget,roamingView} from './motion.js';

const $=id=>document.getElementById(id);
const stem='/character/Meshy_AI_Pixel_Pal_0915023021_texture';
const reduced=matchMedia('(prefers-reduced-motion: reduce)').matches;
let state='startup',paused=reduced,time=0,stateStart=0,sequenceStart=null,helloUntil=0,loaded=false;
let agent='codex';
let view=viewTarget('startup'),viewFrom={...view},viewStart=0;
let pose=characterPose('startup',0,0,reduced),poseFrom={...pose},poseStart=0;
let bubbleContent=null;
let backgroundTime=0;
let bubbleVisible=false,bubbleDeadline=0;
function showBubble(){
  if(!hasMessage(state))return;
  bubbleVisible=true;bubbleDeadline=performance.now()+30000;
  viewFrom={...view};viewStart=time;updateAccessibleMessage();
}
const messages={...DEFAULT_MESSAGES};let messagePage=0,messagePageCount=1;
let renderer,root,mesh,triangles=0;
const scene=new THREE.Scene();
const camera=new THREE.OrthographicCamera(-.64,.64,1.04,-1.04,.01,20);camera.position.set(0,.45,5);camera.lookAt(0,.45,0);
scene.add(new THREE.HemisphereLight(0xe6f4ff,0x556374,2.4));
const light=new THREE.DirectionalLight(0xfff4de,3);light.position.set(-2,4,4);scene.add(light);
const fill=new THREE.DirectionalLight(0xb2dbff,1.5);fill.position.set(3,1,2);scene.add(fill);
const actor=new THREE.Group();scene.add(actor);
const faceCanvas=document.createElement('canvas');faceCanvas.width=faceCanvas.height=256;
const faceContext=faceCanvas.getContext('2d');
const oldFace=document.createElement('canvas');oldFace.width=oldFace.height=256;
const oldFaceContext=oldFace.getContext('2d');
const faceTexture=new THREE.CanvasTexture(faceCanvas);faceTexture.colorSpace=THREE.SRGBColorSpace;
const faceEnabled={value:true};
const material=new THREE.MeshStandardMaterial({roughness:.72,metalness:.06});
// FBX vertex coordinates: X across, Z up, negative Y toward the viewer.
// Project onto the existing front surface; the depth/normal gate excludes the back of the head.
material.onBeforeCompile=shader=>{
  shader.uniforms.palFace={value:faceTexture};shader.uniforms.palFaceEnabled=faceEnabled;
  shader.vertexShader=shader.vertexShader.replace('#include <common>','#include <common>\nvarying vec3 vPalPosition; varying vec3 vPalNormal;')
    .replace('#include <begin_vertex>','#include <begin_vertex>\nvPalPosition=position; vPalNormal=normal;');
  shader.fragmentShader=shader.fragmentShader.replace('#include <common>','#include <common>\nvarying vec3 vPalPosition; varying vec3 vPalNormal; uniform sampler2D palFace; uniform bool palFaceEnabled;')
    .replace('#include <emissivemap_fragment>',`#include <emissivemap_fragment>
      vec2 faceUV=vec2(vPalPosition.x / 0.205 + 0.5, (vPalPosition.z - 0.282) / 0.205 + 0.5);
      vec2 q=abs(faceUV-0.5)-vec2(0.5-0.14);
      float d=length(max(q,0.0))+min(max(q.x,q.y),0.0)-0.14;
      float mask=(1.0-smoothstep(-0.012,0.0,d))*step(vPalPosition.y,-0.063)*step(vPalNormal.y,-0.25);
      if(palFaceEnabled && mask>0.0){
        vec3 faceColor=texture2D(palFace,faceUV).rgb;
        diffuseColor.rgb=mix(diffuseColor.rgb,vec3(0.0),mask);
        totalEmissiveRadiance=mix(totalEmissiveRadiance,faceColor,mask);
        roughnessFactor=mix(roughnessFactor,1.0,mask);
      }`);
};
function setState(next,restart=false){
  if(!STATES[next])return;
  const changed=state!==next || restart;
  if(changed){
    stateStart=time;viewFrom={...view};viewStart=time;poseFrom={...pose};poseStart=time;messagePage=0;
    oldFaceContext.clearRect(0,0,256,256);oldFaceContext.drawImage(faceCanvas,0,0);
  }
  state=next;$('state-label').textContent=STATES[state].label;$('description').textContent=STATES[state].detail;
  if(changed){bubbleVisible=hasMessage(state);bubbleDeadline=performance.now()+30000;}
  $('resolve').hidden=state!=='waiting';
  $('message-controls').hidden=!hasMessage(state);
  $('message').value=messages[state]??'';updateAccessibleMessage();
  document.querySelectorAll('[data-state]').forEach(b=>b.setAttribute('aria-pressed',String(b.dataset.state===state)));
}
for(const [key,value] of Object.entries(AGENTS)){const option=document.createElement('option');option.value=key;option.textContent=value.name;$('agent').append(option);}
$('agent').onchange=()=>{agent=$('agent').value;showBubble();updateAccessibleMessage();};
$('message').oninput=()=>{if(hasMessage(state)){messages[state]=$('message').value;messagePage=0;showBubble();updateAccessibleMessage();}};
function updateAccessibleMessage(){
  $('screen').setAttribute('aria-label',hasMessage(state)?`${AGENTS[agent].name}. ${STATES[state].label}. ${messages[state]}. ${bubbleVisible?'Activate to advance message pages.':'Activate to show message again.'}`:'Pixel Pal character preview. Activate to say hello.');
}
$('resolve').onclick=()=>{stopSequence();setState('running');};
for(const [key,value] of Object.entries(STATES)){
  const button=document.createElement('button');button.textContent=value.name;button.dataset.state=key;button.onclick=()=>{stopSequence();setState(key,key==='startup');};$('states').append(button);
}
function stopSequence(){sequenceStart=null;$('sequence').textContent='Play a little workday ↗';}
$('sequence').onclick=()=>{if(sequenceStart!==null){stopSequence();return;}paused=false;updatePause();sequenceStart=time;setState(SEQUENCE[0]);$('sequence').textContent='Stop workday';};
function updatePause(){$('pause').textContent=paused?'Resume':'Pause';$('pause').setAttribute('aria-pressed',String(paused));}
$('pause').onclick=()=>{paused=!paused;updatePause();};updatePause();
$('screen').onclick=event=>{
  if(hasMessage(state)){
    if(!bubbleVisible){showBubble();return;}
    const bounds=$('screen').getBoundingClientRect();
    if(event.detail===0 || (event.clientY-bounds.top)/bounds.height*456<228)messagePage=(messagePage+1)%messagePageCount;
    return;
  }
  helloUntil=time+1.5;if(paused){paused=false;updatePause();}
};
$('screen').onkeydown=event=>{if(event.key==='Enter'||event.key===' '){event.preventDefault();$('screen').click();}};
$('face').onchange=()=>faceEnabled.value=$('face').checked;
$('scale').onchange=()=>{const scale=Number($('scale').value);document.querySelector('.showcase').style.maxWidth=`${280*scale}px`;};
new ResizeObserver(()=>{$('scale-label').textContent=`${($('screen').getBoundingClientRect().width/280).toFixed(2)}× preview`;}).observe($('display'));
$('framing').onchange=()=>{viewFrom={...view};viewStart=time;};
function updateCamera(){
  const base=viewTarget(hasMessage(state)&&!bubbleVisible?'idle':state,$('framing').value==='full');
  const target=roamingView(base,state,Math.max(0,time-stateStart),reduced);
  view=mixPose(viewFrom,target,reduced?1:ease((time-viewStart)/1.2));
  camera.left=-view.height*280/view.viewport/2;camera.right=-camera.left;camera.top=view.height/2;camera.bottom=-view.height/2;camera.position.set(view.x,view.center,5);camera.lookAt(view.x,view.center,0);camera.updateProjectionMatrix();
}
$('reset').onclick=()=>{$('yaw').value=0;$('speed').value=1;$('framing').value='portrait';$('face').checked=true;faceEnabled.value=true;viewFrom={...view};viewStart=time;};
// Only the moving robot and temporary messages belong in the device framebuffer.
const output=$('screen').getContext('2d');
function compose(){
  if(!reduced && !hasMessage(state))backgroundTime=time;
  drawBackground(output,backgroundTime);
  output.drawImage(renderer.domElement,0,0,280,456);
  if(hasMessage(state)&&bubbleVisible)bubbleContent={state,agentName:AGENTS[agent].name,text:messages[state],color:STATES[state].color,page:messagePage};
  if(view.bubble>0 && bubbleContent){
    output.save();output.globalAlpha=view.bubble;
    output.translate(0,-16*(1-view.bubble));
    const result=drawMessage(output,bubbleContent);
    output.restore();
    messagePageCount=result.pages;messagePage=result.page;
  }
}
$('capture').disabled=true;
$('capture').onclick=()=>{const link=document.createElement('a');link.download=`pixel-pal-${state}-280x456.png`;link.href=$('screen').toDataURL('image/png');link.click();};
async function init(){
  try{
    await loadAgentLogos();
    renderer=new THREE.WebGLRenderer({alpha:true,antialias:true,preserveDrawingBuffer:true});renderer.setClearColor(0x000000,0);renderer.setPixelRatio(1);renderer.setSize(280,456);renderer.outputColorSpace=THREE.SRGBColorSpace;renderer.toneMapping=THREE.ACESFilmicToneMapping;renderer.toneMappingExposure=1.05;
    const loader=new THREE.TextureLoader();
    const [model,base,normal,roughness,metalness]=await Promise.all([new FBXLoader().loadAsync(stem+'.fbx'),loader.loadAsync(stem+'.png'),loader.loadAsync(stem+'_normal.png'),loader.loadAsync(stem+'_roughness.png'),loader.loadAsync(stem+'_metallic.png')]);
    base.colorSpace=THREE.SRGBColorSpace;material.map=base;material.normalMap=normal;material.normalScale.set(.5,.5);material.roughnessMap=roughness;material.metalnessMap=metalness;material.metalness=.3;material.needsUpdate=true;
    root=model;const bounds=new THREE.Box3().setFromObject(root);const center=bounds.getCenter(new THREE.Vector3());const size=bounds.getSize(new THREE.Vector3());
    const factor=1.6/size.y;root.scale.multiplyScalar(factor);root.position.copy(center).multiplyScalar(-factor);
    let meshes=0,bones=0;root.traverse(o=>{if(o.isBone)bones++;if(o.isMesh){mesh=o;meshes++;o.material=material;triangles+=(o.geometry.index?.count??o.geometry.attributes.position.count)/3;}});actor.add(root);
    $('asset-info').textContent=`FBX imported · ${meshes} mesh · ${Math.round(triangles).toLocaleString()} triangles · ${bones} bones · ${root.animations.length} clips. Motion is procedural; source remains in T-pose.`;
    loaded=true;stateStart=time;viewStart=time;poseStart=time;$('loading').hidden=true;$('loading').style.display='none';$('capture').disabled=false;updateCamera();
    window.pal={get state(){return state},get time(){return time},get paused(){return paused},get view(){return {...view}},get pose(){return {...pose}},get bubbleVisible(){return bubbleVisible},get bubbleDeadline(){return bubbleDeadline},get messagePages(){return messagePageCount},get messagePage(){return messagePage},setState,renderer,scene,camera,root,faceCanvas,triangles,renderAt(t,wallNow){time=t;render(wallNow);}};
  }catch(error){console.error(error);$('loading').textContent='Your pal could not load. Check WebGL support and the local character assets, then reload.';}
}
function render(wallNow=performance.now()){
  if(!loaded)return;
  if(bubbleVisible && hasMessage(state) && wallNow>=bubbleDeadline){
    bubbleVisible=false;bubbleContent=null;viewFrom={...view};viewStart=time;updateAccessibleMessage();
  }
  updateCamera();drawFace(faceContext,state,time,agent,time-stateStart);
  faceContext.save();faceContext.globalAlpha=reduced?0:1-ease((time-stateStart)/.3);faceContext.drawImage(oldFace,0,0);faceContext.restore();faceTexture.needsUpdate=true;
  const hello=time<helloUntil;
  const target=characterPose(state,Math.max(0,time-stateStart),time,reduced);
  if(!hasMessage(state))target.yaw+=THREE.MathUtils.degToRad(Number($('yaw').value));
  if(hello && !hasMessage(state) && !reduced)target.roll+=Math.sin((helloUntil-time)*Math.PI/1.5)**2*Math.sin(time*9)*.08;
  pose=mixPose(poseFrom,target,reduced?1:ease((time-poseStart)/.5));
  actor.position.set(pose.x,pose.y,0);actor.rotation.set(0,pose.yaw,pose.roll);actor.scale.set(pose.scaleX,pose.scaleY,1);
  renderer.setViewport(0,0,280,456);renderer.clear();
  renderer.setViewport(0,0,280,Math.round(view.viewport));
  renderer.render(scene,camera);compose();
}
let previous=performance.now(),frames=0,lastStats=previous;
function tick(now){requestAnimationFrame(tick);const dt=Math.min((now-previous)/1000,.1);previous=now;if(!paused)time+=dt*Number($('speed').value);
  if(loaded && sequenceStart!==null){setState(sequenceState(time-sequenceStart));if(time-sequenceStart>=(SEQUENCE.length-1)*4)stopSequence();}
  else if(loaded && !paused && state==='startup' && time-stateStart>=3)setState('host_waiting');
  render();frames++;if(now-lastStats>1000){$('stats').textContent=`${Math.round(frames*1000/(now-lastStats))} preview fps · fixed 280 × 456 framebuffer`;frames=0;lastStats=now;}}
setState('startup');init();requestAnimationFrame(tick);
