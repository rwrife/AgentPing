#pragma once
#include "usb_motion.h"

namespace agentping {
// Offsets are relative to the rendered pose captured when manual mode begins.
// Writing current[] keeps the next animation's existing transition continuous.
struct ManualPose {
  bool active=false;
  float base[UsbMotion::bones][4]{},from[UsbMotion::bones][4]{},target[UsbMotion::bones][4]{};
  int64_t started=0,duration=600000;
  static void multiply(const float* a,const float* b,float* q) {
    q[0]=a[3]*b[0]+a[0]*b[3]+a[1]*b[2]-a[2]*b[1];
    q[1]=a[3]*b[1]-a[0]*b[2]+a[1]*b[3]+a[2]*b[0];
    q[2]=a[3]*b[2]+a[0]*b[1]-a[1]*b[0]+a[2]*b[3];
    q[3]=a[3]*b[3]-a[0]*b[0]-a[1]*b[1]-a[2]*b[2];
  }
  int command(const char* line,UsbMotion& motion) {
    if(strncmp(line,"joint ",6))return 0;
    char name[24]{},extra;float x,y,z;unsigned ms;
    const char* names[]={"head","torso","left_shoulder","right_shoulder","left_elbow","right_elbow","left_hip","right_hip","left_knee","right_knee"};
    const unsigned bones[]={5,2,8,12,9,13,17,22,18,23};
    int joint=-1;
    if(sscanf(line,"joint %23s %f %f %f %u %c",name,&x,&y,&z,&ms,&extra)==5)
      for(int i=0;i<10;i++)if(!strcmp(name,names[i]))joint=bones[i];
    if(joint<0||!std::isfinite(x)||!std::isfinite(y)||!std::isfinite(z)||fabsf(x)>90||fabsf(y)>90||fabsf(z)>90||ms<100||ms>5000||!motion.has_pose) {
      printf("PAL ERROR invalid joint command\n");return -1;
    }
    if(!active){memcpy(base,motion.current,sizeof(base));memcpy(target,base,sizeof(target));}
    memcpy(from,motion.current,sizeof(from));
    constexpr float half_degree=3.14159265359f/360;
    const float qx[]={sinf(x*half_degree),0,0,cosf(x*half_degree)};
    const float qy[]={0,sinf(y*half_degree),0,cosf(y*half_degree)};
    const float qz[]={0,0,sinf(z*half_degree),cosf(z*half_degree)};
    float xy[4],xyz[4];multiply(qx,qy,xy);multiply(xy,qz,xyz);multiply(base[joint],xyz,target[joint]);
    active=true;motion.playing=false;motion.finished=false;
    started=esp_timer_get_time();duration=int64_t(ms)*1000;
    printf("PAL JOINT %s OK\n",name);return 1;
  }
  void update(int64_t now,UsbMotion& motion) {
    if(!active)return;
    float t=std::clamp(float(now-started)/duration,0.0f,1.0f);t=t*t*(3-2*t);
    for(unsigned b=0;b<UsbMotion::bones;b++) {
      float dot=0,norm=0;
      for(int k=0;k<4;k++)dot+=from[b][k]*target[b][k];
      for(int k=0;k<4;k++){float q=from[b][k]*(1-t)+target[b][k]*t*(dot<0?-1:1);motion.current[b][k]=q;norm+=q*q;}
      const float scale=1/sqrtf(norm);for(float& q:motion.current[b])q*=scale;
    }
  }
};
}
