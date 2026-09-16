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
  void stored(const int16_t* data,unsigned frames,unsigned hz) {
    playing=false;free(upload);upload=nullptr;keys=data;count=received=frames;rate=hz;
    hash=2166136261u;auto* bytes=reinterpret_cast<const uint8_t*>(keys);
    for(unsigned i=0;i<count*bytes_per_frame;i++)hash=(hash^bytes[i])*16777619u;
    seek=-1;started=esp_timer_get_time();playing=true;
    printf("MOTION STORED frames=%u rate=%u bytes=%u heap=%u\n",count,rate,count*bytes_per_frame,unsigned(esp_get_free_heap_size()));
  }
  bool command(const char* line) {
    if(!strcmp(line,"motionstatus")) {
      printf("MOTION STATUS source=%s playing=%d frames=%u rate=%u speed=%.3f received=%u heap=%u\n",upload?"usb":"flash",playing,count,rate,double(speed),received,unsigned(esp_get_free_heap_size()));return true;
    }
    if(!strncmp(line,"motion ",7)) {
      unsigned n,hz,checksum;char tail;
      if(sscanf(line,"motion %u %u %x %c",&n,&hz,&checksum,&tail)!=3||n<2||n>64||hz<1||hz>60){printf("MOTION ERROR header\n");return true;}
      playing=false;free(upload);upload=nullptr;keys=nullptr;count=received=0;seek=-1;
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
      playing=true;seek=-1;started=esp_timer_get_time();printf("MOTION PLAY frames=%u rate=%u bytes=%u heap=%u\n",count,rate,count*bytes_per_frame,unsigned(esp_get_free_heap_size()));return true;
    }
    if(!strncmp(line,"seek ",5)) {
      unsigned frame;char tail;
      if(!playing||sscanf(line,"seek %u %c",&frame,&tail)!=1||frame>=count)printf("MOTION ERROR seek\n");
      else {seek=frame;printf("MOTION SEEK %u\n",frame);}return true;
    }
    return false;
  }
  float blend() const {return playing?std::clamp((esp_timer_get_time()-started)/400000.0f,0.0f,1.0f):0;}
  void rotation(unsigned b,float* out,const float* fallback) const {
    const float elapsed=(esp_timer_get_time()-started)/1000000.0f;
    // The final key is the loop endpoint, not another held sample interval.
    float frame=seek>=0?float(seek):fmodf(elapsed*rate*speed,float(count-1));
    unsigned a=static_cast<unsigned>(frame),next=std::min(a+1,count-1);float mix=frame-a;
    float q[4],norm=0,dot=0;
    for(int k=0;k<4;k++)dot+=float(keys[(a*bones+b)*4+k])*keys[(next*bones+b)*4+k];
    for(int k=0;k<4;k++){q[k]=(keys[(a*bones+b)*4+k]*(1-mix)+keys[(next*bones+b)*4+k]*mix*(dot<0?-1:1))/32767.0f;norm+=q[k]*q[k];}
    const float inv=1/sqrtf(norm);for(float& v:q)v*=inv;
    // Convert the procedural Z rotation to a quaternion for a smooth entry.
    float base[4]={0,0,copysignf(sqrtf(std::max(0.0f,(1-fallback[0])*0.5f)),fallback[1]),sqrtf(std::max(0.0f,(1+fallback[0])*0.5f))};
    dot=0;for(int k=0;k<4;k++)dot+=base[k]*q[k];
    float weight=blend();weight=weight*weight*(3-2*weight);norm=0;
    for(int k=0;k<4;k++){q[k]=base[k]*(1-weight)+q[k]*weight*(dot<0?-1:1);norm+=q[k]*q[k];}
    const float normalize=1/sqrtf(norm);for(float& v:q)v*=normalize;
    const float x=q[0],y=q[1],z=q[2],w=q[3];memset(out,0,64);
    out[0]=1-2*(y*y+z*z);out[1]=2*(x*y+z*w);out[2]=2*(x*z-y*w);
    out[4]=2*(x*y-z*w);out[5]=1-2*(x*x+z*z);out[6]=2*(y*z+x*w);
    out[8]=2*(x*z+y*w);out[9]=2*(y*z-x*w);out[10]=1-2*(x*x+y*y);out[15]=1;
  }
};
}
