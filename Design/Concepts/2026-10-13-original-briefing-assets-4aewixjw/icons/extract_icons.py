from PIL import Image, ImageDraw, ImageFont
import numpy as np, json, os
from scipy import ndimage
ROOT=os.path.dirname(__file__)
SRC=os.path.abspath(os.path.join(ROOT,'../../generated_images/exec-c3967dd4-1985-4916-9df2-f7dfdeb0c7cc.png'))
im=Image.open(SRC).convert('RGB'); os.makedirs(ROOT+'/native',exist_ok=True);os.makedirs(ROOT+'/padded_64',exist_ok=True)
# Bounding boxes use original 1672 x 941 image coordinates, right/bottom exclusive.
specs=[
('purge',(152,690,192,731),'cyan',None),
('purge_badge',(145,682,200,738),'cyan',(27,27,25.5)),
('hunt',(340,691,375,729),'gold',None),
('hunt_badge',(329,682,386,738),'gold',(28,28,26)),
('hold',(511,683,554,728),'cyan',None),
('hold_badge',(507,681,559,738),'cyan',(26,29,25)),
('survive',(691,683,738,738),'cyan',None),
('boss',(882,690,922,732),'red',None),
('boss_badge',(874,681,930,738),'red',(28,29,26)),
('dash',(1171,624,1211,677),'white',None),
('lock',(1351,629,1388,674),'lock',None),
('lock_small',(734,832,768,873),'lock',None),
('selected_check',(641,841,664,865),'check',None),
('selected_badge',(632,834,671,873),'disk',(19,19,15.5)),
('back_arrow',(84,826,110,864),'cyan',None),
('launch_triangle',(1307,815,1349,864),'black',None),
('modifier_emblem',(652,90,681,119),'emblem',None),
('difficulty_bars',(475,836,508,871),'cyan',None),
]
records=[]; pictures=[]
for name,box,kind,region in specs:
 a=np.array(im.crop(box)); f=a.astype(float);r,g,b=f[:,:,0],f[:,:,1],f[:,:,2]
 if kind=='cyan': score=np.minimum(b-70,b-r-22);alpha=np.clip(score/22,0,1)
 elif kind=='gold': score=np.minimum(r-90,g-65);alpha=np.clip(score/32,0,1)
 elif kind=='red': score=np.minimum(r-65,r-g-28);alpha=np.clip(score/35,0,1)
 elif kind in ('white','check'): alpha=np.clip((np.minimum(np.minimum(r,g),b)-(90 if kind=='white' else 155))/65,0,1)
 elif kind=='lock': alpha=np.clip(np.minimum(b-65,g-55)/26,0,1)
 elif kind=='black': alpha=np.clip((115-g)/45,0,1)
 elif kind=='emblem':
  mask=(r>135)&(g>75)&(r>b*1.7);mask=ndimage.binary_fill_holes(mask); alpha=mask.astype(float)
 elif kind=='disk': alpha=np.ones(r.shape)
 if region:
  cx,cy,rad=region;y,x=np.indices(r.shape);alpha*=np.clip(rad+.6-np.sqrt((x-cx)**2+(y-cy)**2),0,1)
 if name=='purge':
  y,x=np.indices(r.shape);alpha*=((x-20)**2+(y-20)**2<19**2)
 if name=='hold':
  polygon=[(531,684),(527,689),(526,694),(522,692),(517,695),(516,701),(520,704),(515,708),(511,717),(516,722),(523,716),(527,715),(527,722),(530,727),(534,727),(538,716),(543,720),(548,723),(553,718),(548,708),(543,704),(547,701),(546,695),(541,692),(537,695),(536,690)]
  pm=Image.new('L',(a.shape[1],a.shape[0]));ImageDraw.Draw(pm).polygon([(x-box[0],y-box[1]) for x,y in polygon],fill=255);alpha*=np.array(pm)/255
 if name in ('hunt','boss','selected_check'):
  ll,nn=ndimage.label(alpha>.03);ss=np.bincount(ll.ravel());ss[0]=0; main=ss.argmax();alpha*=ll==main
 # Keep genuine small antialiasing pixels, discard detached noise.
 labels,n=ndimage.label(alpha>.02); sizes=np.bincount(labels.ravel());keep=sizes>=3;keep[0]=False;alpha*=keep[labels]
 rgba=np.dstack((a,np.rint(alpha*255).astype('uint8')))
 nonzero=np.argwhere(rgba[:,:,3]>0)
 y0,x0=nonzero.min(0); y1,x1=nonzero.max(0)+1
 result=Image.fromarray(rgba[y0:y1,x0:x1]); result.save(ROOT+'/native/'+name+'.png')
 canvas=Image.new('RGBA',(64,64)); offset=((64-result.width)//2,(64-result.height)//2);canvas.paste(result,offset);canvas.save(ROOT+'/padded_64/'+name+'.png');pictures.append((name,canvas))
 records.append({'name':name,'source_crop_xyxy':list(box),'source_visible_bounds_xyxy':[box[0]+int(x0),box[1]+int(y0),box[0]+int(x1),box[1]+int(y1)],'native_size':list(result.size),'canvas_size':[64,64],'canvas_offset':list(offset),'native_file':'native/'+name+'.png','padded_file':'padded_64/'+name+'.png','mask_method':kind,'atlas_xywh':[0,0,64,64]})
cols=6;rows=(len(pictures)+cols-1)//cols
atlas=Image.new('RGBA',(cols*64,rows*64))
for i,(name,pic) in enumerate(pictures):
 x=(i%cols)*64;y=(i//cols)*64;atlas.paste(pic,(x,y));records[i]['atlas_xywh']=[x,y,64,64]
atlas.save(ROOT+'/icon_atlas_64.png')
# Two backgrounds expose dark fringes and accidental retained background.
sheet=Image.new('RGB',(cols*190,rows*120),(44,48,56));d=ImageDraw.Draw(sheet)
for i,(name,pic) in enumerate(pictures):
 x=i%cols*190;y=i//cols*120
 d.rectangle((x+6,y+6,x+88,y+88),fill='#081621');d.rectangle((x+94,y+6,x+176,y+88),fill='#d9dfe5')
 sheet.paste(pic,(x+15,y+15),pic);sheet.paste(pic,(x+103,y+15),pic);d.text((x+7,y+94),name,fill='white')
sheet.save(ROOT+'/contact_sheet.png')
manifest={'source':os.path.basename(SRC),'source_size':list(im.size),'coordinate_convention':'Original image pixels; x/y origin top left; xyxy right/bottom exclusive. No scaling or regenerated pixels.','limitations':['Source is a flattened RGB mockup, not layered art; exact original foreground alpha and colors cannot be recovered.','RGB channels of retained pixels are unchanged source pixels. Alpha masks are deterministic color/luminance selections; source anti-aliasing contains dark-background contamination and can show dark rims on light backgrounds.','Native resolution is only 20–54 pixels for these symbols. Do not expect high-resolution vector quality.','Stage connector lines and text excluded by crop/circular masks. Badge ring borders are approximate at connector junctions.','Selected badge preserves its original blue disk; modifier emblem preserves its original orange disk and black design.','The UI contains a back arrow, not a separate cancel/X icon; back_arrow is the extracted available symbol.','hold includes stylized group/figures; survive includes shield surround. Separate alternate glyphs were not invented.'],'icons':records}
open(ROOT+'/manifest.json','w').write(json.dumps(manifest,indent=2))
open(ROOT+'/README.txt','w').write('EXTRACTED MISSION BRIEFING ICONS\n\n18 transparent PNG assets. native/ contains tight crops at original pixel size; padded_64/ centers identical pixels on 64 x 64 transparent canvases. icon_atlas_64.png is a 6 x 3, 384 x 192 atlas with no extrusion. Use manifest.json for coordinates and source bounds.\n\nNo generation, redraw, upscaling or recoloring. Source RGB remains unchanged. Since this is flattened artwork, alpha is approximate and anti-alias pixels retain source-background contamination. Use against dark game UI backgrounds for closest match. Check contact_sheet.png for both dark and light backgrounds. The source only has a back arrow, no separate cancel symbol.\n')
print(ROOT)
