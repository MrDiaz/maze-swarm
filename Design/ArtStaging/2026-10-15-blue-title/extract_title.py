"""Reproduce nine independent visible title glyph layers from the approved source.
No generative processing. Every RGB value is retained from title.png; alpha is inferred.
"""
from PIL import Image, ImageDraw, ImageFilter
from scipy import ndimage as ndi
import numpy as np,json,hashlib
from pathlib import Path
_here=Path(__file__).resolve().parent
if (_here/'reference/title.png').exists():
 src=_here/'reference/title.png'; root=_here; preview=_here/'preview'
else:
 src=Path('blue-ui/reference/title.png');root=Path('blue-ui/title/source');preview=Path('blue-ui/title/preview')
preview.mkdir(parents=True,exist_ok=True)
(root/'glyphs').mkdir(parents=True,exist_ok=True);(root/'ui').mkdir(exist_ok=True)
im=Image.open(src).convert('RGB');a=np.asarray(im).astype(float);r,g,b=a.transpose(2,0,1);ys,xs=np.indices(r.shape)
poly=[(275,276),(283,166),(291,151),(374,135),(390,144),(401,158),(416,132),(498,117),(514,126),(527,145),(535,134),(620,128),(636,143),(641,112),(655,104),(776,102),(790,112),(796,133),(807,130),(911,135),(927,147),(921,193),(918,223),(927,240),(921,288),(925,305),(980,312),(1009,326),(1011,345),(1002,489),(990,511),(970,530),(884,519),(872,508),(860,510),(816,492),(805,499),(779,513),(740,508),(706,514),(676,505),(662,493),(620,506),(593,500),(580,491),(562,503),(526,500),(513,493),(491,503),(470,498),(459,488),(436,501),(393,500),(374,479),(354,499),(315,515),(269,522),(227,513),(205,496),(189,470),(200,412),(210,400),(197,383),(194,359),(201,328),(220,304),(246,286)]
pm=Image.new('L',im.size);ImageDraw.Draw(pm).polygon(poly,fill=255)
m=(np.asarray(pm)>0)&(r<108)&(g<166)&(b>r*1.24)&(b>g*.94)
lab,n=ndi.label(m);sizes=np.bincount(lab.ravel());m=lab==int(np.argmax(sizes[1:])+1);m=ndi.binary_fill_holes(ndi.binary_closing(m,iterations=1))
# Boundary paths follow visible navy valleys between touching letter extrusions.
row=[(180,275),(278,278),(340,294),(355,303),(395,318),(434,325),(480,305),(530,309),(560,313),(590,307),(650,304),(676,299),(730,294),(780,295),(817,311),(880,310),(920,310),(1014,308)]
top_cuts=[[(100,524),(150,525),(200,522),(250,508),(290,501),(330,500)],[(100,636),(150,643),(200,654),(250,655),(285,659),(330,659)],[(100,794),(140,794),(200,786),(250,789),(330,789)]]
bottom_cuts=[[(270,349),(330,350),(380,356),(425,370),(461,369),(491,345),(540,338)],[(280,554),(318,562),(350,562),(410,543),(462,522),(488,512),(540,512)],[(280,668),(330,669),(385,677),(437,690),(478,682),(540,679)],[(280,811),(328,815),(378,818),(408,818),(445,819),(480,809),(540,784)]]
row_y=np.interp(xs,[p[0] for p in row],[p[1] for p in row]);top=ys<row_y
labels=np.zeros(r.shape,dtype=np.uint8)
for path in top_cuts: labels+=(top & (xs>=np.interp(ys,[p[0] for p in path],[p[1] for p in path]))).astype('uint8')
labels[~top]=4
for path in bottom_cuts: labels+=((~top)&(xs>=np.interp(ys,[p[0] for p in path],[p[1] for p in path]))).astype('uint8')
# Protect visible colored faces at touching valleys: no yellow SWARM face belongs to MAZE,
# nor does an adjacent letter face belong to its neighbor. Only shared navy pixels use cuts.
face_components=[]
face_seeds=np.full(r.shape,-1,dtype=np.int16)
for face in [m&(r>140)&(g>70)&(b<170)&(r>b*1.3),m&(r>80)&(g>160)&(b>190)&(b>=r*.95)&(ys<300)]:
 fl,fn=ndi.label(face); fs=np.bincount(fl.ravel())
 for fi in np.where(fs>400)[0]:
  if fi==0:continue
  fy,fx=np.where(fl==fi); cx=float(fx.mean());cy=float(fy.mean())
  if cy<285: target=0 if cx<500 else 1 if cx<650 else 2 if cx<790 else 3
  else: target=4 if cx<345 else 5 if cx<525 else 6 if cx<665 else 7 if cx<815 else 8
  fm=ndi.binary_fill_holes(fl==fi);labels[fm]=target;face_seeds[fm]=target;face_components.append((target,len(fx)))
# Assign shared navy extrusion pixels to the closest original colored letter face.
# This eliminates unrelated neighboring face slivers while retaining every visible source pixel.
nearest=ndi.distance_transform_edt(face_seeds<0,return_distances=False,return_indices=True)
labels=face_seeds[nearest[0],nearest[1]].astype('uint8')
names=['maze_01_M','maze_02_A','maze_03_Z','maze_04_E','swarm_01_S','swarm_02_W','swarm_03_A','swarm_04_R','swarm_05_M']
manifest={'schema_version':2,'canvas':{'width':1672,'height':941,'origin':'top-left','units':'pixels'},'background_resource':'BlueUI/Art/scene_title','resource_root':'BlueUI/TitleArt','background':'','source_reference':{'library_file_id':'libfile_7f0d6e09ae288191bee3b5c15aea4897','sha256':hashlib.sha256(src.read_bytes()).hexdigest()},'glyphs':[],'ui':[],'mask_notes':'Original RGB retained, manually bounded blue-contour mask plus closed-hole fill. Visible overlapping glyphs are partitioned by distance to their source colored faces across shared navy valleys. Hidden backs of touching letters are not recoverable from the concept. Independent transforms support assembly; arbitrary separation exposes these inferred ownership cuts.'}
def layer(id,mask,file,letter=None,word=None,index=None):
 yy,xx=np.where(mask);x0,y0,x1,y1=int(xx.min())-2,int(yy.min())-2,int(xx.max())+3,int(yy.max())+3
 rgba=im.convert('RGBA');rgba.putalpha(Image.fromarray((mask*255).astype('uint8')));rgba.crop((x0,y0,x1,y1)).save(root/file)
 d={'id':id,'file':file,'rect_top_left':[x0,y0,x1-x0,y1-y0],'center_on_canvas':[(x0+x1)/2,(y0+y1)/2],'pivot_normalized':[.5,.5]}
 if letter:d.update(letter=letter,word=word,index_in_word=index)
 return d
for i,id in enumerate(names):manifest['glyphs'].append(layer(id,m&(labels==i),'glyphs/'+id+'.png',id[-1],'MAZE' if i<4 else 'SWARM',i if i<4 else i-4))
# Button isolates its approved yellow/orange extrusion; no baked environmental floor.
roi=(xs>=398)&(xs<910)&(ys>=668)&(ys<835)
bm=roi&(r>170)&(g>72)&(b<150)&(r>b*1.5)
lab,n=ndi.label(bm);sizes=np.bincount(lab.ravel());bm=lab==int(np.argmax(sizes[1:])+1);bm=ndi.binary_fill_holes(ndi.binary_closing(bm,iterations=1))
# Preserve the approved ground-contact shadow within the independently movable Play art.
# Its original alpha is not available; this soft matte is inferred, source RGB unmodified.
sm=Image.new('L',im.size);ImageDraw.Draw(sm).ellipse((405,810,910,841),fill=255)
sm=sm.filter(ImageFilter.GaussianBlur(3));sa=np.asarray(sm).astype(float)/255
sa[ys<818]=0
play_alpha=np.maximum(bm.astype(float),sa)
manifest['ui'].append(layer('play',play_alpha,'ui/play.png'))
(root/'manifest.json').write_text(json.dumps(manifest,indent=2)+'\n')
recipe={'source':str(src),'source_sha256':manifest['source_reference']['sha256'],'logo_polygon':poly,'logo_contour_threshold':'r<108,g<166,b>1.24*r,b>.94*g; largest connected component; binary closing1; binary fill holes','partition':'Connected colored face components with filled letter counters; nearest-face ownership of shared navy extrusion via distance_transform_edt. Manual cut paths are retained below as initial diagnostic only.','between_words_y_for_x':row,'top_x_for_y':top_cuts,'bottom_x_for_y':bottom_cuts,'play_roi':[398,668,512,167],'play_threshold':'r>170,g>72,b<150,r>1.5*b; largest connected component; binary closing1; fill holes','contact_shadow':'Play ground contact shadow uses source RGB and inferred ellipse(405,810,910,841),GaussianBlur3,clippedabove818; original shadow alpha unavailable.','alpha_policy':'Logo binary silhouette inferred from approved source. RGB unmodified wherever alpha>0. No generated pixels, no texture scaling. 2px transparent margin around each native crop.'}
(root/'extraction-recipe.json').write_text(json.dumps(recipe,indent=2)+'\n')
# Reassemble in original coordinates, verify exact source RGB and union masks.
assembled=Image.new('RGBA',im.size);union=np.zeros(m.shape,dtype='uint8');maxerr=0
for d in manifest['glyphs']+manifest['ui']:
 cut=Image.open(root/d['file']).convert('RGBA');x,y,w,h=d['rect_top_left'];assembled.alpha_composite(cut,(x,y));q=np.asarray(cut);source=np.asarray(im.crop((x,y,x+w,y+h)));nz=q[:,:,3]>0;maxerr=max(maxerr,int(np.max(np.abs(q[:,:,:3][nz].astype(int)-source[nz].astype(int)))))
 union[y:y+h,x:x+w]+=(q[:,:,3]>0).astype('uint8')
assert int(union.max())==1 and maxerr==0
assembled.save(preview/'layers-assembled.png')
white=Image.new('RGBA',im.size,'white');white.alpha_composite(assembled);white.crop((180,95,1020,540)).save(preview/'logo-on-white.png')
plate=Image.new('RGB',(1250,710),(218,243,255));d=ImageDraw.Draw(plate)
for i,rec in enumerate(manifest['glyphs']+manifest['ui']):
 col=i%5;row=i//5;x=col*250;y=row*355;d.text((x+10,y+10),rec['id'],fill=(7,17,81));cut=Image.open(root/rec['file']);cut.thumbnail((235,300));plate.paste(cut,(x+(250-cut.width)//2,y+40+(300-cut.height)//2),cut)
plate.save(preview/'individual-layers.png')
proof={'glyph_count':len(manifest['glyphs']),'play_layers':len(manifest['ui']),'original_rgb_max_error':maxerr,'max_layer_overlap_pixels':int(union.max()),'all_glyphs_nonempty':all(np.asarray(Image.open(root/x['file']))[:,:,3].max()>0 for x in manifest['glyphs']),'independent_visible_alpha_pixels':int((union>0).sum()),'rendered_visual_acceptance':'pending root Unity capture','hidden_glyph_backs_recovered':False}
(root/'extraction-proof.json').write_text(json.dumps(proof,indent=2)+'\n');print(json.dumps(proof))
