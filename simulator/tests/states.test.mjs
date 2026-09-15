import {test} from 'node:test';
import assert from 'node:assert/strict';
import {sequenceState,STATES,attentionZoom} from '../src/states.js';
import {messagePages} from '../src/message.js';
import {characterPose,ease,mixPose,viewTarget,roamingView} from '../src/motion.js';
test('lifecycle starts with startup and holds at attention',()=>{
  for(const [t,s] of [[0,'startup'],[3.999,'startup'],[4,'host_waiting'],[8,'idle'],[12,'running'],[16,'waiting'],[1000,'waiting']])assert.equal(sequenceState(t),s);
});
test('attention zoom settles and respects reduced motion',()=>{
  assert.equal(attentionZoom(-1),0);assert.equal(attentionZoom(0),0);
  assert.ok(attentionZoom(.4)>0 && attentionZoom(.4)<1);
  assert.equal(attentionZoom(.85),1);assert.equal(attentionZoom(100),1);assert.equal(attentionZoom(0,true),1);
});
test('eight lifecycle states have accessible labels',()=>{
  assert.equal(Object.keys(STATES).length,8);
  for(const value of Object.values(STATES))assert.ok(value.label && value.detail);
});
test('bubble wraps long identifiers and paginates without losing text',()=>{
  const text='A'.repeat(53);
  const pages=messagePages(text,s=>s.length,10,2);
  assert.equal(pages.length,3);
  assert.equal(pages.flat().join(''),text);
  assert.ok(pages.flat().every(line=>line.length<=10));
  assert.deepEqual(messagePages('First\nSecond',s=>s.length,20,5),[['First','Second']]);
  assert.equal(messagePages('',s=>s.length,40,5)[0][0],'No message supplied.');
});
test('boot begins offstage and settles; reduced motion skips physical motion',()=>{
  assert.ok(characterPose('startup',0,0).y < -3);
  assert.ok(Math.abs(characterPose('startup',3,3).y)<.001);
  assert.equal(characterPose('startup',0,0,true).y,0);
  assert.notEqual(characterPose('idle',2,2).x,characterPose('idle',4,4).x);
  assert.ok(characterPose('waiting',.2,.2).y>0);
  assert.equal(characterPose('waiting',3,3).roll,0);
});
test('view interpolation preserves continuity when interrupted and reaches exact endpoints',()=>{
  const start=viewTarget('idle'),end=viewTarget('waiting');
  assert.deepEqual(mixPose(start,end,ease(0)),start);
  const midway=mixPose(start,end,ease(.5));
  assert.ok(midway.viewport>228 && midway.viewport<456);
  assert.deepEqual(mixPose(midway,start,ease(0)),midway);
  assert.deepEqual(mixPose(start,end,ease(1)),end);
});
test('non-critical camera roams broadly but reading and reduced motion remain stable',()=>{
  const base=viewTarget('idle');
  const samples=[0,12,24,36,48,60].map(t=>roamingView(base,'idle',t));
  assert.ok(Math.max(...samples.map(v=>v.center))-Math.min(...samples.map(v=>v.center))>1.4);
  assert.ok(new Set(samples.map(v=>v.x)).size>3);
  assert.deepEqual(roamingView(base,'waiting',20),base);
  assert.deepEqual(roamingView(base,'idle',20,true),base);
});
