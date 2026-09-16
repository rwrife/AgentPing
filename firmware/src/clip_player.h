#pragma once

namespace agentping {
// Clip 0 is boot. Every other clip begins and ends at the shared neutral frame.
struct ClipPlayer {
  unsigned clip=0, requested=1, frame=0;
  int direction=1;
  constexpr void request(unsigned next) { requested=next; }
  constexpr void advance(unsigned count, bool pingpong) {
    const bool end=frame==count-1;
    const bool start=frame==0 && clip!=0;
    if ((end || start) && requested!=clip) {
      clip=requested;frame=0;direction=1;return;
    }
    if (pingpong) {
      if(end)direction=-1;
      else if(start)direction=1;
      frame=static_cast<unsigned>(static_cast<int>(frame)+direction);
    } else {
      // Last and first are identical: don't display the neutral frame twice.
      frame=end?1:frame+1;
    }
  }
};
namespace playback_checks {
constexpr bool boot_once() {
  ClipPlayer p;
  for(unsigned i=0;i<3;++i)p.advance(4,false);
  if(p.clip!=0 || p.frame!=3)return false;
  p.advance(4,false);
  if(p.clip!=1 || p.frame!=0)return false;
  for(unsigned i=0;i<30;++i){p.advance(4,true);if(p.clip==0)return false;}
  return true;
}
constexpr bool pingpong_and_handoff() {
  ClipPlayer p;p.clip=1;
  const unsigned expected[]={1,2,3,2,1,0,1};
  for(auto frame:expected){p.advance(4,true);if(p.frame!=frame)return false;}
  p.request(3);p.advance(4,true);
  if(p.clip!=1 || p.frame!=2)return false;
  p.advance(4,true);if(p.clip!=1 || p.frame!=3)return false;
  p.advance(4,true);return p.clip==3 && p.frame==0 && p.direction==1;
}
constexpr bool notification_during_boot() {
  ClipPlayer p;p.request(4);
  p.advance(3,false);if(p.clip!=0)return false;
  p.advance(3,false);if(p.clip!=0)return false;
  p.advance(3,false);return p.clip==4 && p.frame==0;
}
static_assert(boot_once(),"Boot must finish once and never loop");
static_assert(pingpong_and_handoff(),"Ping-pong and state changes must use boundary frames");
static_assert(notification_during_boot(),"Early notifications must wait for boot to finish");
}
}
