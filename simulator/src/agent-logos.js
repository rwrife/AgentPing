const files={codex:'codex',claude:'claude',copilot:'githubcopilot'};
const logos=new Map();
export async function loadAgentLogos(){
  await Promise.all(Object.entries(files).map(async([agent,file])=>{
    const response=await fetch(`/agents/${file}.svg`);
    if(!response.ok)throw new Error(`Agent logo unavailable: ${file}`);
    const svg=new DOMParser().parseFromString(await response.text(),'image/svg+xml');
    const paths=Array.from(svg.querySelectorAll('path')).map(path=>new Path2D(path.getAttribute('d')));
    if(!paths.length)throw new Error(`Agent logo has no paths: ${file}`);
    logos.set(agent,paths);
  }));
}
export function drawAgentLogo(ctx,agent,x,y,size){
  const paths=logos.get(agent);
  if(!paths)return;
  ctx.save();ctx.translate(x,y);ctx.scale(size/24,size/24);
  for(const path of paths)ctx.fill(path,'evenodd');
  ctx.restore();
}
