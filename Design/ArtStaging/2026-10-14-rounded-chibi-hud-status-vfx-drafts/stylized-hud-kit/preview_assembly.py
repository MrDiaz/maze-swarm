from pathlib import Path
from PIL import Image,ImageDraw,ImageFont
R=Path(__file__).parent
im=Image.new('RGBA',(1600,900),'#799FC7');d=ImageDraw.Draw(im)
for y in range(0,900,100):d.line((0,y,1600,y),fill='#87AFD2',width=1)
for x in range(0,1600,100):d.line((x,0,x,900),fill='#87AFD2',width=1)
def put(n,x,y,w=None,h=None,frac=1):
 a=Image.open(R/'png'/f'{n}.png').convert('RGBA')
 if w:a=a.resize((w,h or w),Image.Resampling.LANCZOS)
 if frac<1:a=a.crop((0,0,int(a.width*frac),a.height))
 im.alpha_composite(a,(x,y))
def label(s,x,y,sz=25,col='#F4FFFF',anchor='mm'):
 ImageDraw.Draw(im).text((x,y),s,font=ImageFont.truetype('/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf',sz),fill=col,anchor=anchor,stroke_width=1,stroke_fill='#17304D')
put('panel_status',125,45,366,79);put('health_track',160,60,305,38);put('health_fill',160,60,305,38,frac=.86);label('86 / 100',307,79,23)
put('panel_level',156,112,107,45);label('LV 3',209,132,19);put('xp_track',273,121,190,18);put('xp_fill',273,121,190,18,frac=.63)
put('portrait_hero_exact_crop',35,38,124,124);put('portrait_frame',28,31,138,138)
put('panel_objective',619,38,362,101);label('PURGE',705,70,24);label('12 / 30',879,70,25,'#63F4FF');put('objective_track',643,105,310,16);put('objective_fill',643,105,310,16,frac=.4)
put('panel_currency',1270,40,183,76);put('icon_coin',1283,52,51,51);label('240',1384,77,29);put('button_pause',1476,38,82,82);put('icon_pause_cyan',1493,54,48,48)
put('joystick_base',60,582,250,250);put('joystick_thumb',128,650,114,114)
# Button layers are the exact exported sprites. Text is preview-only.
for n,x,y,sz,icon,title in [('normal',1310,530,220,'rifle','RIFLE'),('normal',1175,682,150,'dash','DASH'),('disabled',1035,731,126,'freeze','FREEZE')]:
 put('button_circle_'+n,x,y,sz,sz)
 if icon=='freeze':put('cooldown_dim_overlay',x,y,sz,sz)
 put('icon_'+icon+('_disabled' if icon=='freeze' else '_ivory'),x+int(sz*.26),y+int(sz*.16),int(sz*.48),int(sz*.48))
 label(title,x+sz//2,y+int(sz*.74),max(14,int(sz*.105)))
 if icon=='freeze':label('4s',x+sz//2,y+int(sz*.60),16)
 else:put('cooldown_ready_ring',x,y,sz,sz)
label('LAYERED ASSEMBLY PREVIEW',800,369,31)
label('Reusable sprites + runtime text / fills / cooldowns',800,415,21)
label('Visual approximation · exact source-crop portrait · no gameplay implementation',800,459,16)
im.convert('RGB').save(R/'preview'/'assembly-preview.png')
