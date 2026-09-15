// Low-luminance wash. Move gently with the scene; no fixed lines or ornaments.
export function drawBackground(ctx,time=0){
  const x=140+Math.sin(time*.035)*65;
  const y=245+Math.sin(time*.027)*100;
  const wash=ctx.createRadialGradient(x,y,0,x,y,360);
  wash.addColorStop(0,'#081522');
  wash.addColorStop(.55,'#030a12');
  wash.addColorStop(1,'#010205');
  ctx.fillStyle=wash;ctx.fillRect(0,0,280,456);
}
