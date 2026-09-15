import {STATES,AGENTS} from './states.js';
import {drawAgentLogo} from './agent-logos.js';
export function drawFace(ctx,state,time,agent='codex',age=0){
  const {width:w,height:h}=ctx.canvas;
  ctx.clearRect(0,0,w,h);ctx.fillStyle='#000000';ctx.fillRect(0,0,w,h);
  ctx.strokeStyle=ctx.fillStyle=STATES[state].color;ctx.lineWidth=9;ctx.lineCap='round';ctx.lineJoin='round';
  if(state==='waiting'){
    drawAgentLogo(ctx,agent,78,60,100);
    ctx.textAlign='center';
    ctx.font='bold 20px sans-serif';ctx.fillText(AGENTS[agent].name,128,191);
    return;
  }
  if(state==='startup'){
    ctx.lineWidth=7;ctx.beginPath();ctx.arc(128,108,34,-Math.PI*.35,Math.PI*1.35);ctx.stroke();
    ctx.beginPath();ctx.moveTo(128,64);ctx.lineTo(128,102);ctx.stroke();
    ctx.globalAlpha=.25;ctx.fillRect(72,175,112,7);ctx.globalAlpha=1;ctx.fillRect(72,175,112*Math.min(Math.max(age,0)/3,1),7);return;
  }
  if(state==='host_waiting'){
    ctx.lineWidth=6;for(let i=0;i<3;i++){ctx.globalAlpha=.25+.75*(.5+.5*Math.sin(time*2-i));ctx.beginPath();ctx.arc(128,134,18+i*22,Math.PI*1.18,Math.PI*1.82);ctx.stroke();}
    ctx.globalAlpha=1;ctx.beginPath();ctx.arc(128,143,5,0,Math.PI*2);ctx.fill();ctx.font='16px sans-serif';ctx.textAlign='center';ctx.fillText('LISTENING',128,190);return;
  }
  const blink=Math.sin(time*1.1)>0.993;
  const look=state==='running'?Math.sin(time*2)*8:Math.sin(time*.65)*3;
  const line=(points)=>{ctx.beginPath();points.forEach(([x,y],i)=>i?ctx.lineTo(x,y):ctx.moveTo(x,y));ctx.stroke();};
  for(const x of [83,173]){
    if(blink || state==='disconnected')line([[x-17,115],[x+17,115]]);
    else if(state==='completed'){ctx.beginPath();ctx.arc(x,116,20,Math.PI,Math.PI*2);ctx.stroke();}
    else if(state==='error'){line([[x-15,94],[x+15,110]]);line([[x-15,110],[x+15,94]]);}
    else {ctx.beginPath();ctx.roundRect(x-13+look,state==='waiting'?88:91,26,state==='waiting'?46:38,12);ctx.fill();}
  }
  if(state==='completed'){ctx.beginPath();ctx.arc(128,143,25,0,Math.PI);ctx.stroke();}
  else if(state==='error')line([[114,159],[128,152],[142,159]]);
  else if(state==='waiting'){ctx.beginPath();ctx.arc(130,158,7,0,Math.PI*2);ctx.stroke();}
  else if(state==='running'){for(let i=0;i<3;i++){ctx.globalAlpha=.3+.7*(.5+.5*Math.sin(time*5-i));ctx.beginPath();ctx.arc(112+i*16,157,4,0,Math.PI*2);ctx.fill();}ctx.globalAlpha=1;}
  else if(state!=='disconnected'){ctx.beginPath();ctx.arc(128,140,19,.15*Math.PI,.85*Math.PI);ctx.stroke();}
}
