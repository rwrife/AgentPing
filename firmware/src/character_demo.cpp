#include "character_demo.h"
#ifdef AGENTPING_CHARACTER_DEMO
#include "../assets/pal_frames.h"
#include "board.h"
#include "notification_command.h"
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
  auto* bubble=lv_label_create(screen);
  lv_obj_set_width(bubble,board::kWidth-32);
  lv_obj_align(bubble,LV_ALIGN_TOP_MID,0,16);
  lv_obj_set_style_pad_all(bubble,12,0);
  lv_obj_set_style_radius(bubble,14,0);
  lv_obj_set_style_bg_color(bubble,lv_color_hex(0x132232),0);
  lv_obj_set_style_bg_opa(bubble,LV_OPA_COVER,0);
  lv_obj_set_style_text_color(bubble,lv_color_hex(0xf3f8ff),0);
  lv_obj_set_style_text_font(bubble,&lv_font_montserrat_20,0);
  lv_label_set_text(bubble,"");
  lv_obj_add_flag(bubble,LV_OBJ_FLAG_HIDDEN);
  setvbuf(stdin,nullptr,_IONBF,0);
  fcntl(STDIN_FILENO,F_SETFL,fcntl(STDIN_FILENO,F_GETFL,0)|O_NONBLOCK);
  unsigned first=0,count=24,frame=0,period=82;bool paused=false;bool dirty=true;
  char line[96]{};unsigned used=0;bool overflow=false;
  char last_event[17]{};
  int64_t bubble_until=0,host_until=0;
  auto next=esp_timer_get_time();auto next_stats=next+5000000;unsigned frames_played=0;
  ESP_LOGI("pal","READY USB-only character demo; commands: idle, wave, pause, resume, status");
  while(true){
    char ch;
    for(unsigned incoming=0;incoming<64 && read(STDIN_FILENO,&ch,1)==1;incoming++){
      if(ch=='\n'||ch=='\r'){
        if(used&&!overflow){
          line[used]=0;
          if(!strcmp(line,"host")) {
            host_until=esp_timer_get_time()+15000000;
            lv_label_set_text(label,"");
            printf("PAL HOST OK\n");used=0;continue;
          }
          if(!strncmp(line,"notify ",7)) {
            NotificationCommand event{};
            if(!parse_notification(line,event)){printf("PAL ERROR invalid notification\n");used=0;continue;}
            if(strcmp(last_event,event.id)) {
              strcpy(last_event,event.id);
              const char* names[]={"Codex","Claude Code","GitHub Copilot"};
              const char* messages[]={"Your input is needed.","Your task is complete.","Something needs a look."};
              lv_label_set_text_fmt(bubble,"%s\n%s",names[event.provider],messages[event.kind]);
              lv_obj_remove_flag(bubble,LV_OBJ_FLAG_HIDDEN);
              lv_obj_align(image,LV_ALIGN_CENTER,0,8);
              lv_label_set_text(label,"");
              first=24+event.provider*36;count=36;period=132;frame=0;paused=false;dirty=true;
              bubble_until=esp_timer_get_time()+30000000;
            }
            printf("PAL EVENT %s OK\n",event.id);used=0;continue;
          }
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
    if(bubble_until && now>=bubble_until){
      bubble_until=0;lv_obj_add_flag(bubble,LV_OBJ_FLAG_HIDDEN);
      lv_obj_align(image,LV_ALIGN_CENTER,0,-22);
      first=0;count=24;period=82;frame=0;paused=false;dirty=true;
    }
    if(host_until && now>=host_until){
      host_until=0;lv_label_set_text(label,"Trying to connect to\nthe host agent...");
    }
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
