from PIL import Image, ImageDraw, ImageFont
from pathlib import Path
import json, hashlib
D=Path(__file__).resolve().parent
S=D.parent.parent/'generated_images/exec-c3967dd4-1985-4916-9df2-f7dfdeb0c7cc.png'
im=Image.open(S).convert('RGBA')
# Hand-traced silhouette in 2x inspection coordinates; original image offset (1100,140).
p=[(200,864),(219,839),(228,827),(207,837),(182,834),(161,823),(145,805),(143,794),(151,779),(130,780),(111,774),(99,765),(86,744),(73,721),(61,708),(48,686),(53,661),(48,643),(50,626),(56,607),(68,587),(79,573),(104,552),(129,542),(143,542),(156,546),(170,541),(178,531),(189,525),(200,525),(211,532),(226,527),(240,516),(243,508),(237,501),(264,466),(280,444),(269,436),(272,420),(283,396),(291,379),(291,366),(297,351),(314,330),(333,308),(353,291),(370,282),(392,263),(419,247),(437,244),(453,238),(470,237),(488,244),(504,249),(520,236),(534,233),(548,222),(557,215),(578,208),(578,198),(568,185),(564,168),(568,145),(576,134),(578,114),(585,88),(597,67),(614,50),(636,37),(657,30),(681,26),(708,24),(730,27),(749,35),(764,47),(777,64),(786,83),(790,103),(794,117),(799,123),(799,138),(793,151),(793,163),(799,177),(795,188),(794,199),(812,201),(821,193),(837,190),(854,189),(856,174),(861,160),(873,155),(894,156),(913,153),(927,159),(938,164),(942,181),(943,190),(959,191),(972,199),(988,212),(994,223),(1004,229),(1012,240),(1008,267),(1004,279),(1017,285),(1018,300),(1012,319),(1008,340),(1024,354),(1042,377),(1055,402),(1071,431),(1085,462),(1089,486),(1085,496),(1090,524),(1094,551),(1094,579),(1098,612),(1097,642),(1088,651),(1070,655),(1065,650),(1065,672),(1070,697),(1087,700),(1098,709),(1113,739),(1127,752),(1138,766),(1144,784),(1144,979),(1122,977),(1108,971),(1095,972),(1081,969),(1066,959),(1046,954),(1023,950),(1006,950),(988,946),(969,946),(949,943),(929,936),(911,925),(901,910),(887,899),(879,884),(870,864)]
# Supersampling affects alpha only; RGB is copied at native resolution.
mask=Image.new('L',(im.width*4,im.height*4),0)
ImageDraw.Draw(mask).polygon([((1100+x/2)*4,(140+y/2)*4) for x,y in p],fill=255)
mask=mask.resize(im.size,Image.Resampling.LANCZOS)
a=im.copy();a.putalpha(mask)
bbox=(1122,151,1672,630)
a.crop(bbox).save(D/'iron-vanguard-hero-cutout.png')
mask.crop(bbox).save(D/'iron-vanguard-hero-alpha.png')
# A UI-free crop of the original head and collar, with unchanged background pixels.
headbox=(1381,151,1510,301)
im.crop(headbox).save(D/'iron-vanguard-source-head-crop.png')
# Alpha head/collar uses the identical traced silhouette, squared off at collar.
a.crop((1377,151,1515,301)).save(D/'iron-vanguard-head-cutout.png')
# Preserve larger head-and-armor original raster crop without UI lettering.
bustbox=(1377,151,1627,431)
a.crop(bustbox).save(D/'iron-vanguard-armor-portrait.png')
assets=[('iron-vanguard-hero-cutout.png','Original hero and weapon silhouette; native-size transparent PNG',bbox),('iron-vanguard-source-head-crop.png','Head and collar source crop; original opaque background and thin panel border retained',headbox),('iron-vanguard-head-cutout.png','Transparent head and collar crop; flat lower crop boundary',(1377,151,1515,301)),('iron-vanguard-armor-portrait.png','Clean transparent head and chest crop; same original RGB and traced alpha',bustbox)]
manifest={'source':str(S),'source_sha256':hashlib.sha256(S.read_bytes()).hexdigest(),'source_size':list(im.size),'also_inspected':'exec-6813dac1-caf3-4473-ae33-efc9e48483af.png','selection_reason':'Approved two-column design provides larger hero raster and fewer overlapping interface elements. Earlier three-column concept was inspected but not combined.','method':'Deterministic crop and manually traced polygon alpha mask. Supersampled edge alpha; no RGB retouching, synthesis, inpainting, upscaling, or hidden-pixel reconstruction.','limitations':['Only one existing hero, Iron Vanguard, is present in these sources. Assets are views of the same hero, not invented additional characters.','Minigun continues beyond the right source frame and remains clipped.','Lower torso is occluded by Abilities panel at source y=572, so cutout has a straight boundary there.','The large weapon silhouette remains visible over the panel; its native lower edge is retained to the available frame extent.','Head and armor crops are intentionally cropped at collar/chest; full shoulders and body are not present in those crops.','Mask is hand-traced, antialiased, and may retain a very thin source-colored fringe. No original edge colors were repainted.'],'assets':[]}
for name,desc,box in assets:
 f=D/name; z=Image.open(f);manifest['assets'].append({'file':name,'description':desc,'size':list(z.size),'mode':z.mode,'source_crop_box_xyxy':list(box),'sha256':hashlib.sha256(f.read_bytes()).hexdigest()})
(D/'manifest.json').write_text(json.dumps(manifest,indent=2))
(D/'README.txt').write_text('IRON VANGUARD — faithful raster extractions\n\nSource: approved two-column mission briefing concept.\nNo generation, inpainting, invented pixels, or RGB retouching.\nPNG outputs retain native source resolution.\n\nhero-cutout: larger original silhouette including minigun, transparent.\nsource-head-crop: head/collar source crop, including thin panel border.\nhead-cutout: head/collar crop with silhouette alpha.\narmor-portrait: transparent head/chest portrait crop.\nhero-alpha: reusable grayscale alpha mask matching hero-cutout.\n\nIMPORTANT: gun is clipped at right source edge; lower torso is hidden\nby the Abilities panel and has a straight crop boundary. These cannot be\nrecovered from this image without inventing pixels. The earlier three-column\nconcept was inspected but not combined with this source.\n\nSee manifest.json for coordinates, checksums, provenance and limitations.\nPreview contact sheet is for inspection only, with resized display copies.\n')
# Two rows show identical assets against dark/light backgrounds.
W,H=1320,1060
sheet=Image.new('RGB',(W,H),'#11161b'); dr=ImageDraw.Draw(sheet)
font=ImageFont.truetype('/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf',18)
for row,bg in enumerate(['#15212d','#eef0f1']):
 y=row*530;dr.rectangle((0,y,W,y+530),fill=bg);color='white' if row==0 else '#15212d'
 dr.text((20,y+12),'ORIGINAL PIXELS / '+('DARK' if row==0 else 'LIGHT')+' BACKGROUND',font=font,fill=color)
 x=20
 for n,label,w in [('iron-vanguard-hero-cutout.png','Hero + weapon (clipped at right)',550),('iron-vanguard-source-head-crop.png','Source head crop',170),('iron-vanguard-head-cutout.png','Head alpha',170),('iron-vanguard-armor-portrait.png','Head + armor alpha',300)]:
  z=Image.open(D/n);z.thumbnail((w,460),Image.Resampling.LANCZOS)
  sheet.paste(z,(x,y+64),z)
  dr.text((x,y+40),label,font=font,fill=color)
  x+=w+20
sheet.save(D/'portrait-contact-sheet.png')
(D/'inspection.png').unlink(missing_ok=True)
