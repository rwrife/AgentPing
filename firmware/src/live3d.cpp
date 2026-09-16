#ifdef AGENTPING_LIVE3D
// Favor throughput in the software renderer without changing system libraries.
#pragma GCC optimize("O3")
#include "../assets/live_model.h"
#include "board.h"
#include "esp_heap_caps.h"
#include "esp_system.h"
#include "esp_timer.h"
#include "driver/usb_serial_jtag_vfs.h"
#include "freertos/FreeRTOS.h"
#include "freertos/task.h"
#include "lvgl.h"
#include <algorithm>
#include <cmath>
#include <cstdio>
#include <cstring>
#include <fcntl.h>
#include <unistd.h>

namespace agentping {
namespace {
constexpr unsigned kVertices=sizeof(live_model::vertices)/sizeof(live_model::vertices[0]);
constexpr unsigned kBones=sizeof(live_model::parents)/sizeof(int);
struct Point { int16_t x,y,z; };
Point projected[kVertices];
uint16_t strip_masks[sizeof(live_model::indices)/6];
float world[kBones][16];
int32_t skin[kBones][12];
uint16_t *pixels=nullptr,*depth=nullptr;
int width=0,height=0,depth_rows=0;
int buffer_width=0,buffer_height=0,pixel_scale=1;
bool small_texture=false;
uint16_t* texture64=nullptr;
lv_image_dsc_t descriptor{};
lv_obj_t* picture=nullptr;
void multiply(const float* a,const float* b,float* c) {
  for(int col=0;col<4;col++)for(int row=0;row<4;row++) {
    float sum=0;for(int k=0;k<4;k++)sum+=a[k*4+row]*b[col*4+k];
    c[col*4+row]=sum;
  }
}
void rotation(float* m,float angle,bool yaw=false) {
  memset(m,0,64);m[0]=m[5]=m[10]=m[15]=1;
  float c=cosf(angle),s=sinf(angle);
  if(yaw){m[0]=m[10]=c;m[2]=-s;m[8]=s;}
  else {m[0]=m[5]=c;m[1]=s;m[4]=-s;}
}
void pose(float t) {
  float spin[16];rotation(spin,0.45f*sinf(t*0.7f),true);
  for(unsigned b=0;b<kBones;b++) {
    float r[16],local[16],out[16];float angle=0;
    // This export has no clips: exercise its actual arm bones procedurally.
    if(b==8)angle=-0.5f+0.65f*sinf(t*2);
    if(b==12)angle=0.5f-0.65f*sinf(t*2);
    if(b==9)angle=0.3f*sinf(t*2+1);
    if(b==13)angle=-0.3f*sinf(t*2+1);
    rotation(r,angle);multiply(live_model::local[b],r,local);
    const int parent=live_model::parents[b];
    multiply(parent<0?spin:world[parent],local,world[b]);
    multiply(world[b],live_model::inverse[b],out);
    for(int row=0;row<3;row++)for(int col=0;col<4;col++)
      skin[b][row*4+col]=lroundf(out[col*4+row]*16384);
  }
  const int scale=width*38/100;
  for(unsigned i=0;i<kVertices;i++) {
    const auto& v=live_model::vertices[i];int32_t p[3]={};
    for(int j=0;j<4;j++)if(v.weight[j]) {
      const auto* m=skin[v.bone[j]];
      for(int r=0;r<3;r++) {
        const auto* a=m+r*4;
        int32_t q=(a[0]*v.x+a[1]*v.y+a[2]*v.z)/16384+a[3]/4;
        p[r]+=q*v.weight[j];
      }
    }
    for(int r=0;r<3;r++)p[r]/=255;
    projected[i]={static_cast<int16_t>(width/2+p[0]*scale/4096),
      static_cast<int16_t>(height/2-p[1]*scale/4096),
      static_cast<int16_t>(std::clamp<int32_t>(16000-p[2],0,32000))};
  }
}
int edge(const Point& a,const Point& b,int x,int y) {
  return (b.x-a.x)*(y-a.y)-(b.y-a.y)*(x-a.x);
}
void rasterize() {
  const int count=buffer_width*buffer_height;
  for(int i=0;i<count;i++)pixels[i]=0x0021;
  for(unsigned tri=0;tri<sizeof(live_model::indices)/sizeof(uint16_t);tri+=3) {
    const auto &a=projected[live_model::indices[tri]],&b=projected[live_model::indices[tri+1]],&c=projected[live_model::indices[tri+2]];
    uint16_t mask=0;
    if(edge(a,b,c.x,c.y)<0) {
      const int lo=std::max(0,int(std::min({a.y,b.y,c.y})));
      const int hi=std::min(height-1,int(std::max({a.y,b.y,c.y})));
      if(lo<=hi)for(int s=lo/depth_rows;s<=hi/depth_rows;s++)mask|=1u<<s;
    }
    strip_masks[tri/3]=mask;
  }
  // Reuse a strip of depth storage at native resolution. Each triangle is
  // clipped to the strip; the full color frame remains available to LVGL.
  for(int strip=0;strip<height;strip+=depth_rows) {
  memset(depth,255,width*depth_rows*2);
  for(unsigned tri=0;tri<sizeof(live_model::indices)/sizeof(uint16_t);tri+=3) {
    if(!(strip_masks[tri/3]&(1u<<(strip/depth_rows))))continue;
    int ia=live_model::indices[tri],ib=live_model::indices[tri+1],ic=live_model::indices[tri+2];
    int area=edge(projected[ia],projected[ib],projected[ic].x,projected[ic].y);
    // Front faces are clockwise after projection flips the screen Y axis.
    if(area>=0)continue;
    if(area<0){std::swap(ib,ic);area=-area;}
    const auto &a=projected[ia],&b=projected[ib],&c=projected[ic];
    int x0=std::max(0,int(std::min({a.x,b.x,c.x}))),x1=std::min(width-1,int(std::max({a.x,b.x,c.x})));
    int y0=std::max(strip,int(std::min({a.y,b.y,c.y}))),y1=std::min({height-1,strip+depth_rows-1,int(std::max({a.y,b.y,c.y}))});
    if(x0>x1||y0>y1)continue;
    int ex[3]={b.y-c.y,c.y-a.y,a.y-b.y},ey[3]={c.x-b.x,a.x-c.x,b.x-a.x};
    int er[3]={edge(b,c,x0,y0),edge(c,a,x0,y0),edge(a,b,x0,y0)};
    const auto &va=live_model::vertices[ia],&vb=live_model::vertices[ib],&vc=live_model::vertices[ic];
    int attrs[3][3]={{va.u/2,vb.u/2,vc.u/2},{va.v/2,vb.v/2,vc.v/2},{a.z*256,b.z*256,c.z*256}};
    int start[3],dx[3],dy[3];
    for(int k=0;k<3;k++) {
      auto dot=[&](const int* e){return (int64_t(e[0])*attrs[k][0]+int64_t(e[1])*attrs[k][1]+int64_t(e[2])*attrs[k][2])/area;};
      start[k]=dot(er);dx[k]=dot(ex);dy[k]=dot(ey);
    }
    for(int y=y0;y<=y1;y++) {
      int e0=er[0],e1=er[1],e2=er[2],u=start[0],v=start[1],z=start[2];
      for(int x=x0;x<=x1;x++) {
        const int offset=y*pixel_scale*buffer_width+x*pixel_scale;
        const int depth_offset=(y-strip)*width+x;
        if(e0>=0&&e1>=0&&e2>=0&&z>=0&&(z>>8)<depth[depth_offset]) {
          depth[depth_offset]=z>>8;
          const uint16_t color=small_texture?
            texture64[(63-std::clamp(v>>9,0,63))*64+std::clamp(u>>9,0,63)]:
            live_model::texture[(127-std::clamp(v>>8,0,127))*128+std::clamp(u>>8,0,127)];
          pixels[offset]=color;
          if(pixel_scale==2){pixels[offset+1]=color;pixels[offset+buffer_width]=color;pixels[offset+buffer_width+1]=color;}
        }
        e0+=ex[0];e1+=ex[1];e2+=ex[2];u+=dx[0];v+=dx[1];z+=dx[2];
      }
      for(int k=0;k<3;k++){er[k]+=ey[k];start[k]+=dy[k];}
    }
  }
  }
}
bool resolution(int w,int h,bool doubled=false) {
  // A single contiguous allocation makes the two buffer sizes auditable.
  if(pixels){lv_image_set_src(picture,nullptr);free(pixels);pixels=nullptr;depth=nullptr;}
  const int rows=w==board::kWidth||doubled?32:h;
  const int scale=doubled?2:1,bw=w*scale,bh=h*scale;
  const size_t bytes=(bw*bh+w*rows)*2;
  const size_t available=heap_caps_get_free_size(MALLOC_CAP_INTERNAL|MALLOC_CAP_8BIT);
  if(available<bytes+24576) {
    printf("LIVE3D REJECT %dx%d bytes=%u free=%u reserve=24576\n",w,h,unsigned(bytes),unsigned(available));
    return false;
  }
  pixels=static_cast<uint16_t*>(heap_caps_malloc(bytes,MALLOC_CAP_INTERNAL|MALLOC_CAP_8BIT));
  if(!pixels){printf("LIVE3D REJECT fragmented allocation %dx%d\n",w,h);return false;}
  width=w;height=h;depth_rows=rows;pixel_scale=scale;buffer_width=bw;buffer_height=bh;depth=pixels+bw*bh;
  memset(pixels,0,bw*bh*2);
  descriptor.header.magic=LV_IMAGE_HEADER_MAGIC;descriptor.header.cf=LV_COLOR_FORMAT_RGB565;
  descriptor.header.w=bw;descriptor.header.h=bh;descriptor.header.stride=bw*2;
  descriptor.data_size=bw*bh*2;descriptor.data=reinterpret_cast<const uint8_t*>(pixels);
  lv_image_set_src(picture,&descriptor);
  // Uniformly fill the portrait panel; low-resolution modes remain visibly testable.
  lv_image_set_scale(picture,board::kWidth*256/bw);
  lv_image_set_antialias(picture,false);
  lv_obj_center(picture);
  printf("LIVE3D RES %dx%d output=%dx%d buffers=%u heap=%u\n",w,h,bw,bh,unsigned(bytes),unsigned(esp_get_free_heap_size()));
  return true;
}
}
void run_live3d() {
  usb_serial_jtag_vfs_set_tx_line_endings(ESP_LINE_ENDINGS_LF);
  auto* screen=lv_screen_active();lv_obj_set_style_bg_color(screen,lv_color_hex(0x03070b),0);
  picture=lv_image_create(screen);
  // Touch is unused during profiling; suspend its periodic I2C reads.
  for(auto* input=lv_indev_get_next(nullptr);input;input=lv_indev_get_next(input))lv_indev_enable(input,false);
  if(!resolution(280,456)&&!resolution(160,260))return;
  setvbuf(stdin,nullptr,_IONBF,0);fcntl(STDIN_FILENO,F_SETFL,fcntl(STDIN_FILENO,F_GETFL,0)|O_NONBLOCK);
  printf("LIVE3D READY vertices=%u triangles=%u bones=%u commands=low,medium,high,max,native,fast,tex64,tex128,pause,resume,status,frame\n",kVertices,unsigned(sizeof(live_model::indices)/6),kBones);
  char line[32]{};unsigned used=0;bool paused=false;
  int64_t epoch=esp_timer_get_time(),stats=epoch;unsigned frames=0;int64_t skin_us=0,draw_us=0,screen_us=0;
  while(true) {
    char ch;
    for(int n=0;n<64&&read(STDIN_FILENO,&ch,1)==1;n++) {
      if(ch=='\n'||ch=='\r') {
        if(used) {
          line[used]=0;bool changed=true,ok=true;
          if(!strcmp(line,"low"))ok=resolution(140,228);
          else if(!strcmp(line,"medium"))ok=resolution(160,260);
          else if(!strcmp(line,"high"))ok=resolution(192,312);
          else if(!strcmp(line,"max"))ok=resolution(208,338);
          else if(!strcmp(line,"native"))ok=resolution(280,456);
          else if(!strcmp(line,"fast"))ok=resolution(140,228,true);
          else if(!strcmp(line,"tex64")) {
            if(!texture64&&esp_get_free_heap_size()>=24576+8192)
              texture64=static_cast<uint16_t*>(heap_caps_malloc(8192,MALLOC_CAP_INTERNAL|MALLOC_CAP_8BIT));
            if(texture64){memcpy(texture64,live_model::texture64,8192);small_texture=true;}
            else printf("LIVE3D REJECT texture cache allocation\n");
          }
          else if(!strcmp(line,"tex128")){small_texture=false;free(texture64);texture64=nullptr;}
          else {
            changed=false;
            if(!strcmp(line,"pause"))paused=true;
            else if(!strcmp(line,"resume"))paused=false;
            else if(!strcmp(line,"frame")) {
              printf("LIVE3D FRAME %d %d\n",buffer_width,buffer_height);fflush(stdout);
              fwrite(pixels,2,buffer_width*buffer_height,stdout);fflush(stdout);
              printf("\nLIVE3D FRAME END\n");
            }
          }
          if(!ok&&!resolution(160,260))return;
          printf("LIVE3D STATUS resolution=%dx%d texture=%d heap=%u min_heap=%u paused=%d\n",width,height,small_texture?64:128,unsigned(esp_get_free_heap_size()),unsigned(esp_get_minimum_free_heap_size()),paused);
          if(changed){frames=0;skin_us=draw_us=screen_us=0;stats=esp_timer_get_time();}
        }
        used=0;
      }else if(used<sizeof(line)-1)line[used++]=ch;
    }
    auto now=esp_timer_get_time();
    if(!paused) {
      pose((now-epoch)/1000000.0f);auto posed=esp_timer_get_time();
      rasterize();auto drawn=esp_timer_get_time();
      lv_obj_invalidate(picture);lv_refr_now(nullptr);auto shown=esp_timer_get_time();
      skin_us+=posed-now;draw_us+=drawn-posed;screen_us+=shown-drawn;frames++;
    }
    lv_timer_handler();vTaskDelay(pdMS_TO_TICKS(1));
    now=esp_timer_get_time();
    if(now-stats>=5000000) {
      if(frames)printf("LIVE3D FPS %.2f resolution=%dx%d texture=%d skin_ms=%.2f raster_ms=%.2f display_ms=%.2f heap=%u min_heap=%u\n",frames*1000000.0/(now-stats),width,height,small_texture?64:128,skin_us/1000.0/frames,draw_us/1000.0/frames,screen_us/1000.0/frames,unsigned(esp_get_free_heap_size()),unsigned(esp_get_minimum_free_heap_size()));
      stats=now;frames=0;skin_us=draw_us=screen_us=0;
    }
  }
}
}
#endif
