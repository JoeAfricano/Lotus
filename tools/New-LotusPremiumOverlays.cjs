// Native OverlayEditor layouts: only RTSS HAL sources; no CapFrameX HTTP API or background Lotus process.
const fs=require('fs'),path=require('path');
const root=path.join(__dirname,'..'),out=path.join(root,'src/Lotus.Infrastructure/Integrations/Premium');
const old=fs.readFileSync(path.join(root,'src/Lotus.Infrastructure/Integrations/Lotus-Neon.ovl'),'latin1');
const sources=old.slice(old.indexOf('[Source0]'),old.indexOf('[Layer0]'));
for(const id of ['equilibrio','neon','pulse']){
 const top=id==='equilibrio',neon=id==='neon';const x=top?576:neon?1602:18,y=18;
 // Keep the OSD coordinate origin explicit when RTSS's automatic background fill is disabled.
 const layers=[{name:'Transparent origin',text:'<B=1,1>',x:0,y:0,w:1,h:1,size:100,color:'00000000'}];
 function layer(name,text,px,py,w,h,size=100,color='FFFFFF'){layers.push({name,text,x:x+px,y:y+py,w,h,size,color});}
 function number(name,text,px,py,w,font){layer(name,text,px,py,w,Math.ceil(font*1.25),Math.round(font/64*100));}
 function graph(px,py,w,h){layer('Framepacer',`<G=Frametime,${w},${h},0,0,33.3,0>`,px,py,w,h,100,'F078AA');}
 function bar(name,px,py,w){layer(name+' usage bar',`<G=${name} Load,${w},2,0,0,100,9>`,px,py,w,2,100,'F078AA');}
 if(top){
  layer('Artwork left','<I=384,152,0,0,768,304>',0,0,384,152);layer('Artwork right','<I=384,152,0,320,768,304>',384,0,384,152);
  number('FPS','<FR>',28,28,112,58);number('AVG','%FPS Avg Live%',74,119,55,16);
  number('Frametime','<FT>',171,28,80,29);number('1 percent low','%FPS Low1 Live%',246,70,48,14);number('0.1 percent low','%FPS Low01 Live%',246,90,48,14);
  number('GPU','%GPU Load%%',337,33,103,32);number('CPU','%CPU Load%%',486,33,103,32);
  number('GPU temperature','%GPU Temp%',364,84,44,19);number('CPU temperature','%CPU Temp%',513,84,44,19);
  number('VRAM','%VRAM Used%',678,38,46,20);number('RAM','%RAM Used%',678,75,46,20);
  bar('GPU',327,81,102);bar('CPU',476,81,102);graph(161,128,578,22);
 }else if(neon){
  layer('Artwork','<I=300,304,0,0,600,608>',0,0,300,304);
  number('FPS','<FR>',25,20,115,66);number('Frametime','<FT>',187,38,75,29);
  number('AVG','%FPS Avg Live%',30,117,60,21);number('1 percent low','%FPS Low1 Live%',128,117,60,21);number('0.1 percent low','%FPS Low01 Live%',226,117,60,21);
  number('GPU','%GPU Load%%',71,154,56,21);number('CPU','%CPU Load%%',71,181,56,21);
  number('GPU temperature','%GPU Temp%',243,156,36,18);number('CPU temperature','%CPU Temp%',243,183,36,18);
  number('RAM','%RAM Used%',66,209,45,18);number('VRAM','%VRAM Used%',221,209,45,18);
  bar('GPU',129,169,95);bar('CPU',129,196,95);graph(18,260,264,30);
 }else{
  layer('Artwork','<I=220,300,0,0,440,600>',0,0,220,300);
  number('FPS','<FR>',20,20,105,64);number('Frametime','<FT>',138,46,58,25);
  number('AVG','%FPS Avg Live%',20,124,55,19);number('1 percent low','%FPS Low1 Live%',91,124,55,19);number('0.1 percent low','%FPS Low01 Live%',162,124,52,19);
  graph(16,168,188,34);number('GPU','%GPU Load%%',71,210,53,21);number('CPU','%CPU Load%%',71,237,53,21);
  number('GPU temperature','%GPU Temp%',161,209,35,18);number('CPU temperature','%CPU Temp%',161,236,35,18);
  bar('GPU',16,234,176);bar('CPU',16,261,176);number('RAM','%RAM Used%',54,269,44,15);number('VRAM','%VRAM Used%',161,269,44,15);
 }
 let ini=`[Master]\r\nImplementation=2\r\nFontFace=Segoe UI\r\nFontHeight=-64\r\nFontWeight=600\r\nZoomRatio=1\r\n[Settings]\r\nName=Lotus Premium ${id}\r\nLockUserSettings=1\r\nRefreshPeriod=250\r\nEmbeddedImage=Lotus-atlas-${id}.png\r\n[General]\r\nSources=16\r\nTables=0\r\nLayers=${layers.length}\r\n`+sources;
 for(const [i,l] of layers.entries())ini+=`[Layer${i}]\r\nName=${l.name}\r\nText=${l.text}\r\nPositionX=${l.x}\r\nPositionY=${l.y}\r\nExtentX=${l.w}\r\nExtentY=${l.h}\r\nExtentOrigin=0\r\nFixedAlignment=1\r\nSize=${l.size}\r\nTextColor=${l.color}\r\n`;
 fs.writeFileSync(path.join(out,`Lotus-Premium-${id}.ovl`),ini,'latin1');
}
console.log('Generated 3 native RTSS layouts, with RTSS HAL sensors. Reference resolution: 1920x1080.');
