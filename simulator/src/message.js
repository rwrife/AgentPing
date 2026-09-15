export const DEFAULT_MESSAGES = {
  waiting: 'I need your approval before I can continue. Can I run the project tests?',
  error: 'The build failed. I found a missing dependency and need your help to fix it.',
};
export const THINKING_MESSAGES = [
  'Untangling the brain cables...',
  'Consulting the tiny robot council...',
  'Reticulating my little robot thoughts...',
  'Looking for the missing brain cell...',
  'Turning coffee into a cunning plan...',
  'Asking my imaginary rubber duck...',
  'Putting two and two in the same room...',
  'Polishing a suspiciously good idea...',
  'Checking behind the mental sofa...',
  'Waiting for the light bulb to warm up...',
  'Teaching my neurons to cooperate...',
  'Doing some very important pondering...',
];
export function thinkingMessage(previous){
  const choices=THINKING_MESSAGES.filter(message=>message!==previous);
  return choices[Math.floor(Math.random()*choices.length)];
}
export const hasMessage = state => ['waiting','error','running'].includes(state);

// Wrap by measured pixels, splitting long identifiers and respecting newlines.
// Keep the font size fixed; paginate instead of shrinking important messages.
export function messagePages(text, measure, width = 220, linesPerPage = 5) {
  const lines = [];
  for (const paragraph of (text.trim() || 'No message supplied.').split('\n')) {
    let line = '';
    for (const word of paragraph.split(/\s+/).filter(Boolean)) {
      const candidate = line ? `${line} ${word}` : word;
      if (measure(candidate) <= width) { line = candidate; continue; }
      if (line) { lines.push(line); line = ''; }
      for (const char of Array.from(word)) {
        if (line && measure(line + char) > width) { lines.push(line); line = ''; }
        line += char;
      }
    }
    lines.push(line);
  }
  const pages = [];
  for (let i = 0; i < lines.length; i += linesPerPage) pages.push(lines.slice(i, i + linesPerPage));
  return pages;
}

export function drawMessage(ctx, {state, agentName, text, color, page}) {
  ctx.save();
  // Match the 14 px side margins: move the original 36 px top edge up by 22 px.
  ctx.translate(0,-22);
  ctx.fillStyle = '#08121e'; ctx.strokeStyle = color; ctx.lineWidth = 1.5;
  ctx.beginPath(); ctx.moveTo(32,36); ctx.lineTo(248,36);
  ctx.quadraticCurveTo(266,36,266,54); ctx.lineTo(266,192);
  ctx.quadraticCurveTo(266,210,248,210); ctx.lineTo(152,210);
  if(state!=='running'){ctx.lineTo(140,225);ctx.lineTo(132,210);}
  ctx.lineTo(32,210);
  ctx.quadraticCurveTo(14,210,14,192); ctx.lineTo(14,54);
  ctx.quadraticCurveTo(14,36,32,36); ctx.closePath();ctx.fill();ctx.stroke();
  if(state==='running'){for(const [x,y,r] of [[145,220,5],[139,234,3]]){ctx.beginPath();ctx.arc(x,y,r,0,Math.PI*2);ctx.fill();ctx.stroke();}}
  ctx.textAlign='left';ctx.font='bold 12px sans-serif';ctx.fillStyle=color;
  ctx.fillText(state==='running'?'Thinking...':agentName,30,58);
  ctx.font='16px sans-serif';ctx.fillStyle='#f4f8ff';
  const pages=messagePages(text,t=>ctx.measureText(t).width);
  const index=Math.min(page,pages.length-1);
  pages[index].forEach((line,i)=>ctx.fillText(line,30,85+i*20));
  ctx.font='11px sans-serif';ctx.fillStyle='#a7bdcf';
  ctx.fillText(pages.length>1?`Tap bubble for more Â· ${index+1}/${pages.length}`:state==='running'?'': 'Waiting for your response',30,194);
  ctx.restore();return {pages:pages.length,page:index};
}
