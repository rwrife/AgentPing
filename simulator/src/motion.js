export const ease = t => {t=Math.max(0,Math.min(t,1));return t*t*t*(t*(t*6-15)+10);};
export function mixPose(from,to,amount){
  if(amount<=0)return {...from};
  if(amount>=1)return {...to};
  return Object.fromEntries(Object.keys(to).map(key=>[key,from[key]+(to[key]-from[key])*amount]));
}
export const REST = {x:0,y:0,yaw:0,roll:0,scaleX:1,scaleY:1};

// Procedural root motion for the unrigged FBX. Replace with authored clips later.
export function characterPose(state,age,time,reduced=false){
  if(reduced)return {...REST};
  const pose={...REST};
  if(state==='startup'){
    // Rise from below the display, crest above rest, then absorb the landing.
    const enter=ease(age/.85);
    const drop=ease((age-.85)/.5);
    const landing=Math.sin(Math.PI*Math.max(0,Math.min((age-1.35)/.55,1)));
    pose.y=age<.85?-3.4+3.85*enter:.45*(1-drop)-.1*landing;
    pose.x=.25*(1-ease(age/1.35));pose.roll=-.12*(1-ease(age/1.35));
  }else if(state==='idle' || state==='host_waiting'){
    const energy=state==='idle'?1:.4;
    pose.x=Math.sin(time*.6)*.11*energy;
    pose.y=Math.sin(time*1.6)*.025 + Math.pow(Math.max(0,Math.sin(time*.8)),12)*.12*energy;
    pose.yaw=Math.sin(time*.85)*.14*energy;
    pose.roll=Math.sin(time*1.2)*.045*energy;
  }else if(state==='waiting' || state==='error'){
    // Two small attention bounces overlapping the zoom, then a quiet hold.
    const beat=age<1.4?age:(age-1.4)%10;
    const active=age<1.4 || (age>11.4 && beat<1.4);
    const envelope=active?Math.sin(Math.PI*Math.min(beat/1.4,1))**2:0;
    pose.x=Math.sin(time*.31)*.012;
    pose.y=Math.sin(time*.23)*.01+Math.sin(beat*Math.PI/ .35)**2*.027*envelope;
    pose.roll=Math.sin(beat*Math.PI/.35)*.07*envelope;
    pose.yaw=Math.sin(beat*Math.PI/.7)*.07*envelope;
  }else if(state==='running'){
    pose.y=Math.sin(time*2)*.016;pose.roll=Math.sin(time*.9)*.018;pose.yaw=Math.sin(time*.7)*.08;
  }else if(state==='completed'){
    pose.y=(1-Math.cos(age*6))*.045;pose.roll=Math.sin(age*3)*.04;
  }else pose.y=Math.sin(time)*.005;
  return pose;
}

export function viewTarget(state,full=false){
  const bubble=['waiting','error','running'].includes(state);
  return {x:0,height:bubble?.72:full?3.8:state==='idle'?2.5:['startup','host_waiting'].includes(state)?3.65:2.8,
    center:bubble?.49:-.1,viewport:bubble?228:456,bubble:bubble?1:0};
}

export function roamingView(base,state,age,reduced=false){
  if(reduced || ['startup','waiting','error','running'].includes(state))return base;
  const envelope=ease(age/5);
  const closeIdle=state==='idle' && base.height<3;
  const height=(closeIdle?base.height:Math.max(base.height,3.65))+(closeIdle?.2:.55)*(.5-.5*Math.cos(age*.11))*envelope;
  const margin=Math.max(0,height*280/456/2-(closeIdle?.6:1.12));
  return {...base,height,x:Math.sin(age*.19)*margin*envelope,
    center:base.center+Math.sin(age*.13)*(closeIdle||state==='host_waiting'?.3:.88)*envelope};
}
