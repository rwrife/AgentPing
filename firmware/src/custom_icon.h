#pragma once
#include <cstdint>
#include <cstring>
#include "esp_timer.h"

namespace agentping {
// Fixed 48x48, row-major, least-significant bit first. Two-phase publication
// keeps a failed or incomplete upload from changing the displayed artwork.
struct CustomIcon {
  uint8_t pixels[288]{},pending[288]{};
  uint16_t color=0xffff,pending_color=0xffff;
  int64_t expires=0;
  bool active=false;
  static int digit(char c) {
    return c>='0'&&c<='9'?c-'0':c>='a'&&c<='f'?c-'a'+10:c>='A'&&c<='F'?c-'A'+10:-1;
  }
  static int base64(char c) {
    return c>='A'&&c<='Z'?c-'A':c>='a'&&c<='z'?c-'a'+26:c>='0'&&c<='9'?c-'0'+52:c=='+'?62:c=='/'?63:-1;
  }
  bool stage(const char* line) {
    expires=0;
    if(strlen(line)!=400||line[15]!=' ')return false;
    uint32_t rgb=0;
    for(int i=9;i<15;i++){int v=digit(line[i]);if(v<0)return false;rgb=(rgb<<4)|v;}
    for(int i=0;i<384;i+=4) {
      uint32_t value=0;
      for(int j=0;j<4;j++){int v=base64(line[16+i+j]);if(v<0)return false;value=(value<<6)|v;}
      pending[i/4*3]=value>>16;pending[i/4*3+1]=value>>8;pending[i/4*3+2]=value;
    }
    pending_color=((rgb>>8)&0xf800)|((rgb>>5)&0x07e0)|((rgb>>3)&0x001f);
    expires=esp_timer_get_time()+5000000;
    return true;
  }
  bool commit() {
    const bool ready=expires&&esp_timer_get_time()<expires;
    expires=0;
    if(!ready)return false;
    memcpy(pixels,pending,sizeof(pixels));color=pending_color;active=true;return true;
  }
};
}
