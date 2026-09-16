#include "character_demo.h"
#ifdef AGENTPING_CHARACTER_DEMO
#include "../assets/pal_frames.h"
#include "board.h"
#include "esp_heap_caps.h"
#include "esp_log.h"
#include "esp_timer.h"
#include "esp_system.h"
#include <cstdlib>
#include "freertos/FreeRTOS.h"
#include "freertos/task.h"
#include "lvgl.h"
#include <cstdio>
#include <cstring>
#include <fcntl.h>
#include <unistd.h>
namespace agentping {
void run_character_demo() {
  auto* pixels=static_cast<std::uint16_t*>(heap_caps_malloc(pal_assets::width*pal_assets::height*2,MALLOC_CAP_INTERNAL|MALLOC_CAP_8BIT));
  if(!pixels){ESP_LOGE("pal","frame allocation failed");return;}
  lv_image_dsc_t descriptor{};
  descriptor.header.magic=LV_IMAGE_HEADER_MAGIC;
  descriptor.header.cf=LV_COLOR_FORMAT_RGB565;
  descriptor.header.w=pal_assets::width;descriptor.header.h=pal_assets::height;
  descriptor.header.stride=pal_assets::width*2;
  descriptor.data_size=pal_assets::width*pal_assets::height*2;
  descriptor.data=reinterpret_cast<const std::uint8_t*>(pixels);
  auto* screen=lv_screen_active();
  lv_obj_set_style_bg_color(screen,lv_color_hex(0x03070b),0);
  lv_obj_t* image=lv_image_create(screen);lv_obj_align(image,LV_ALIGN_CENTER,0,-22);
  lv_obj_t* label=lv_label_create(screen);
  lv_label_set_text(label,"Trying to connect to\nthe host agent...");
  lv_obj_set_style_text_color(label,lv_color_hex(0x99bbdd),0);
  lv_obj_set_style_text_align(label,LV_TEXT_ALIGN_CENTER,0);
  lv_obj_align(label,LV_ALIGN_BOTTOM_MID,0,-20);
  setvbuf(stdin,nullptr,_IONBF,0);
  fcntl(STDIN_FILENO,F_SETFL,fcntl(STDIN_FILENO,F_GETFL,0)|O_NONBLOCK);
  unsigned first=0,count=24,frame=0,period=82;bool paused=false;bool dirty=true;
  char line[32]{};unsigned used=0;bool overflow=false;
  auto next=esp_timer_get_time();auto next_stats=next+5000000;unsigned frames_played=0;
  ESP_LOGI("pal","READY USB-only character demo; commands: idle, wave, pause, resume, status");
  while(true){
    char ch;
    for(unsigned incoming=0;incoming<64 && read(STDIN_FILENO,&ch,1)==1;incoming++){
      if(ch=='\n'||ch=='\r'){
        if(used&&!overflow){
          line[used]=0;
          if(!strcmp(line,"idle")){first=0;count=24;period=82;frame=0;paused=false;dirty=true;lv_label_set_text(label,"");}
          else if(!strcmp(line,"wave")){first=24;count=36;period=132;frame=0;paused=false;dirty=true;lv_label_set_text(label,"");}
          else if(!strcmp(line,"pause"))paused=true;
          else if(!strcmp(line,"resume"))paused=false;
          else if(strcmp(line,"status")){printf("PAL ERROR unknown command\n");used=0;continue;}
          printf("PAL OK clip=%s paused=%d heap=%u\n",first?"wave":"idle",paused,static_cast<unsigned>(esp_get_free_heap_size()));
        }
        used=0;overflow=false;
      }else if(used<sizeof(line)-1&&!overflow)line[used++]=ch;
      else overflow=true;
    }
    const auto now=esp_timer_get_time();
    if(dirty||(!paused&&now>=next)){
      unsigned dest=0;const unsigned index=first+frame;
      for(unsigned i=pal_assets::offsets[index];i<pal_assets::offsets[index+1];i+=2){
        const unsigned run=pal_assets::runs[i];
        if(dest+run>pal_assets::width*pal_assets::height){ESP_LOGE("pal","invalid frame");abort();}
        for(unsigned j=0;j<run;j++)pixels[dest++]=pal_assets::runs[i+1];
      }
      if(dest!=pal_assets::width*pal_assets::height)abort();
      lv_image_set_src(image,&descriptor);lv_obj_invalidate(image);
      frame=(frame+1)%count;next=now+period*1000;dirty=false;++frames_played;
    }
    lv_timer_handler();
    if(now>=next_stats){ESP_LOGI("pal","playing=%s frames_5s=%u heap=%u min_heap=%u",first?"wave":"idle",frames_played,static_cast<unsigned>(esp_get_free_heap_size()),static_cast<unsigned>(esp_get_minimum_free_heap_size()));next_stats=now+5000000;frames_played=0;}
    vTaskDelay(pdMS_TO_TICKS(5));
  }
}
}
#endif
