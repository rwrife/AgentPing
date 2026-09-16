#pragma once
#include <algorithm>
#include <cmath>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include "esp_system.h"
#include "esp_timer.h"

namespace agentping {
// Test transport: a bounded clip in RAM, with integrity validation before play.
// No FBX parser, asset flash writes, or per-frame allocations on the MCU.
struct UsbMotion {
  const int16_t* keys=nullptr;
  int16_t* upload=nullptr;
  unsigned count=0,received=0,rate=24,hash=0;
  int64_t started=0;
  bool playing=false;
  float speed=2.0f/3.0f;
  int seek=-1;
  static constexpr unsigned bones=27,bytes_per_frame=bones*8;
  const int16_t* roots=nullptr;
  float current[bones][4]{},previous[bones][4]{},root[3]{},previous_root[3]{};
  float frame_position=0;
  int64_t transition_us=0;
  bool looping=true,finished=false,has_pose=false;
  UsbMotion(){for(auto& q:current)q[3]=1;}
  void begin(bool transition=true) {
    memcpy(previous,current,sizeof(current));memcpy(previous_root,root,sizeof(root));
    transition_us=transition&&has_pose?400000:0;
    seek=-1;started=esp_timer_get_time();playing=true;finished=false;
  }
  void stored(const int16_t* data,unsigned frames,unsigned hz,const int16_t* positions=nullptr,bool repeat=true,bool transition=true,float playback_speed=2.0f/3.0f) {
    playing=false;free(upload);upload=nullptr;keys=data;count=received=frames;rate=hz;
    roots=positions;looping=repeat;speed=playback_speed;
    hash=2166136261u;auto* bytes=reinterpret_cast<const uint8_t*>(keys);
    for(unsigned i=0;i<count*bytes_per_frame;i++)hash=(hash^bytes[i])*16777619u;
    begin(transition);
    printf("MOTION STORED frames=%u rate=%u bytes=%u heap=%u\n",count,rate,count*bytes_per_frame,unsigned(esp_get_free_heap_size()));
  }
  bool command(const char* line) {
    if(!strcmp(line,"motionstatus")) {
      printf("MOTION STATUS source=%s playing=%d frames=%u rate=%u speed=%.3f received=%u heap=%u\n",upload?"usb":"flash",playing,count,rate,double(speed),received,unsigned(esp_get_free_heap_size()));return true;
    }
    if(!strncmp(line,"motion ",7)) {
      unsigned n,hz,checksum;char tail;
      if(sscanf(line,"motion %u %u %x %c",&n,&hz,&checksum,&tail)!=3||n<2||n>64||hz<1||hz>60){printf("MOTION ERROR header\n");return true;}
      playing=false;free(upload);upload=nullptr;keys=nullptr;roots=nullptr;count=received=0;seek=-1;finished=false;
      if(esp_get_free_heap_size()<n*bytes_per_frame+24576){printf("MOTION ERROR memory\n");return true;}
      upload=static_cast<int16_t*>(malloc(n*bytes_per_frame));keys=upload;
      if(!upload){printf("MOTION ERROR allocation\n");return true;}
      count=n;rate=hz;hash=checksum;printf("MOTION READY %u\n",count);return true;
    }
    if(!strncmp(line,"key ",4)) {
      unsigned index;int offset=0;
      if(sscanf(line,"key %u %n",&index,&offset)!=1||!offset||!keys||index!=received||index>=count||strlen(line+offset)!=bytes_per_frame*2){printf("MOTION ERROR key\n");return true;}
      auto* dst=reinterpret_cast<uint8_t*>(upload)+index*bytes_per_frame;
      auto digit=[](char c){return c>='0'&&c<='9'?c-'0':c>='a'&&c<='f'?c-'a'+10:-1;};
      for(unsigned i=0;i<bytes_per_frame;i++){int hi=digit(line[offset+i*2]),lo=digit(line[offset+i*2+1]);if(hi<0||lo<0){printf("MOTION ERROR hex\n");return true;}dst[i]=(hi<<4)|lo;}
      for(unsigned b=0;b<bones;b++) {
        float norm=0;for(int k=0;k<4;k++){float v=keys[index*bones*4+b*4+k]/32767.0f;norm+=v*v;}
        if(norm<0.98f||norm>1.02f){printf("MOTION ERROR quaternion\n");return true;}
      }
      received++;printf("MOTION KEY %u\n",index);return true;
    }
    if(!strcmp(line,"play")) {
      if(!keys||received!=count){printf("MOTION ERROR incomplete\n");return true;}
      uint32_t actual=2166136261u;auto* data=reinterpret_cast<const uint8_t*>(keys);
      for(unsigned i=0;i<count*bytes_per_frame;i++)actual=(actual^data[i])*16777619u;
      if(actual!=hash){printf("MOTION ERROR checksum\n");return true;}
      looping=true;begin();printf("MOTION PLAY frames=%u rate=%u bytes=%u heap=%u\n",count,rate,count*bytes_per_frame,unsigned(esp_get_free_heap_size()));return true;
    }
    if(!strncmp(line,"seek ",5)) {
      unsigned frame;char tail;
      if(!playing||sscanf(line,"seek %u %c",&frame,&tail)!=1||frame>=count)printf("MOTION ERROR seek\n");
      else {seek=frame;printf("MOTION SEEK %u\n",frame);}return true;
    }
    return false;
  }
  void update(int64_t now) {
    if(!playing)return;
    const int64_t elapsed=now-started;
    const float raw=std::max<int64_t>(0,elapsed-transition_us)/1000000.0f*rate*speed;
    frame_position=seek>=0?float(seek):looping?fmodf(raw,float(count-1)):std::min(raw,float(count-1));
    finished=!looping&&seek<0&&raw>=count-1;
    const unsigned a=static_cast<unsigned>(frame_position),next=std::min(a+1,count-1);
    const float mix=frame_position-a;
    float weight=transition_us?std::clamp(float(elapsed)/transition_us,0.0f,1.0f):1;
    weight=weight*weight*(3-2*weight);
    for(unsigned b=0;b<bones;b++) {
      float q[4],norm=0,dot=0;
      for(int k=0;k<4;k++)dot+=float(keys[(a*bones+b)*4+k])*keys[(next*bones+b)*4+k];
      for(int k=0;k<4;k++){q[k]=(keys[(a*bones+b)*4+k]*(1-mix)+keys[(next*bones+b)*4+k]*mix*(dot<0?-1:1))/32767.0f;norm+=q[k]*q[k];}
      const float inv=1/sqrtf(norm);for(float& v:q)v*=inv;
      dot=0;for(int k=0;k<4;k++)dot+=previous[b][k]*q[k];
      norm=0;for(int k=0;k<4;k++){q[k]=previous[b][k]*(1-weight)+q[k]*weight*(dot<0?-1:1);norm+=q[k]*q[k];}
      const float normalize=1/sqrtf(norm);for(int k=0;k<4;k++)current[b][k]=q[k]*normalize;
    }
    for(int k=0;k<3;k++){float value=roots?(roots[a*3+k]*(1-mix)+roots[next*3+k]*mix)/4096.0f:0;root[k]=previous_root[k]*(1-weight)+value*weight;}
    has_pose=true;
  }
  void rotation(unsigned b,float* out) const {
    const float* q=current[b];
    const float x=q[0],y=q[1],z=q[2],w=q[3];memset(out,0,64);
    out[0]=1-2*(y*y+z*z);out[1]=2*(x*y+z*w);out[2]=2*(x*z-y*w);
    out[4]=2*(x*y-z*w);out[5]=1-2*(x*x+z*z);out[6]=2*(y*z+x*w);
    out[8]=2*(x*z+y*w);out[9]=2*(y*z-x*w);out[10]=1-2*(x*x+y*y);out[15]=1;
  }
};
}
