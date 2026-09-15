import fs from 'node:fs';
import * as THREE from 'three';
import {GLTFExporter} from 'three/addons/exporters/GLTFExporter.js';
import {FBXLoader} from 'three/addons/loaders/FBXLoader.js';
globalThis.window={URL};
THREE.TextureLoader.prototype.load=function(){return new THREE.Texture()};
const source='public/character/rigged/Meshy_AI_Pixel_Pal_biped_Animation_Idle_11_withSkin.fbx';
const data=fs.readFileSync(source);
const model=new FBXLoader().parse(data.buffer.slice(data.byteOffset,data.byteOffset+data.byteLength),'');
model.getObjectByName('Icosphere')?.removeFromParent();
model.traverse(o=>{if(o.isMesh)o.material=new THREE.MeshStandardMaterial();});
model.animations=model.animations.filter(c=>c.duration>1);
fs.mkdirSync('public/character/optimized',{recursive:true});
globalThis.FileReader=class {
  readAsArrayBuffer(blob){blob.arrayBuffer().then(result=>{this.result=result;this.onloadend?.();});}
  readAsDataURL(blob){blob.arrayBuffer().then(result=>{this.result='data:'+blob.type+';base64,'+Buffer.from(result).toString('base64');this.onloadend?.();});}
};
const glb=await new GLTFExporter().parseAsync(model,{binary:true,animations:model.animations});
fs.writeFileSync('public/character/optimized/robot.glb',Buffer.from(glb));
console.log('Optimized model bytes:',glb.byteLength);
