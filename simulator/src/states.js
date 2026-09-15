export const STATES = {
  startup: {name:'Waking up', label:'Hello, world', detail:'A short power-on face, then settle into listening for the host.', color:'#00dfff'},
  host_waiting: {name:'Listening', label:'Listening for your host', detail:'Quiet and alert while waiting for the host application to send an event.', color:'#66aaff'},
  idle: {name:'Hanging out', label:'Ready when you are', detail:'A gentle blink and a little breathing room.', color:'#00dfff'},
  running: {name:'Thinking', label:'Thinking it through...', detail:'Focused eyes and a gentle thinking rhythm while an agent is working.', color:'#00dfff'},
  waiting: {name:'Needs you', label:'Needs your attention', detail:'Wave, zoom to the upper body, and hold the requesting agent badge.', color:'#ffbf00'},
  completed: {name:'Nailed it', label:'All done!', detail:'Happy eyes and a small celebratory hop.', color:'#70ff38'},
  error: {name:'Uh-oh', label:'Let’s try again', detail:'A concerned expression when something needs attention.', color:'#ff4268'},
  disconnected: {name:'Taking a nap', label:'Waiting for connection', detail:'Sleepy eyes while the bridge is disconnected.', color:'#66aaff'},
};
export const AGENTS = {codex:{name:'Codex'},claude:{name:'Claude Code'},copilot:{name:'Copilot'}};
export const SEQUENCE = ['startup','host_waiting','idle','running','waiting'];
export function sequenceState(seconds){return SEQUENCE[Math.min(Math.floor(seconds/4),SEQUENCE.length-1)];}
export function attentionZoom(age,reduced=false){const t=reduced?1:Math.max(0,Math.min(age/.85,1));return t*t*(3-2*t);}
