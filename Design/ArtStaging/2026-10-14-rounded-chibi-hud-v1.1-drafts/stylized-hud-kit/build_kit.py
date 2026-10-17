from pathlib import Path
import json, subprocess, math, concurrent.futures, hashlib
from PIL import Image, ImageDraw, ImageFont
ROOT=Path(__file__).parent
assets=[]
def defs():
 return '''<defs><linearGradient id="navy" x1="0" y1="0" x2="0" y2="1"><stop stop-color="#355477"/><stop offset=".5" stop-color="#1B3659"/><stop offset="1" stop-color="#11253F"/></linearGradient><linearGradient id="ivory" x1="0" y1="0" x2="0" y2="1"><stop stop-color="#FFFEF9"/><stop offset="1" stop-color="#B9D5E4"/></linearGradient><linearGradient id="cyan" x1="0" y1="0" x2="0" y2="1"><stop stop-color="#A4FFFF"/><stop offset=".4" stop-color="#4CF2FF"/><stop offset="1" stop-color="#00B9DE"/></linearGradient><linearGradient id="green" x1="0" y1="0" x2="0" y2="1"><stop stop-color="#C2FF7A"/><stop offset=".45" stop-color="#70ED42"/><stop offset="1" stop-color="#2CBD30"/></linearGradient><linearGradient id="gold" x1="0" y1="0" x2="0" y2="1"><stop stop-color="#FFF08C"/><stop offset=".5" stop-color="#FFCD36"/><stop offset="1" stop-color="#F09E13"/></linearGradient></defs>'''
def add(name,w,h,body,category,slice=None,pivot=(.5,.5),note='Vector approximation of approved HUD visual language; not an exact extraction.'):
 svg=f'<svg xmlns="http://www.w3.org/2000/svg" width="{w}" height="{h}" viewBox="0 0 {w} {h}">{defs()}{body}</svg>'
 (ROOT/'svg'/f'{name}.svg').write_text(svg)
 assets.append(dict(name=name,category=category,size_px=[w,h],pivot_normalized=list(pivot),svg=f'svg/{name}.svg',png=f'png/{name}.png',nine_slice_lbrt_px=([min(slice[0],w//2-2),min(slice[1],h//2-2),min(slice[2],w//2-2),min(slice[3],h//2-2)] if slice else [0,0,0,0]),provenance=note))
def rect(x,y,w,h,r,fill,stroke=None,sw=1,extra=''):
 return f'<rect x="{x}" y="{y}" width="{w}" height="{h}" rx="{r}" fill="{fill}"'+(f' stroke="{stroke}" stroke-width="{sw}"' if stroke else '')+f' {extra}/>'
def panel(w,h,r=24,state='normal'):
 border={'normal':'url(#ivory)','selected':'#59EEFF','pressed':'#83C7DB','disabled':'#758B9E'}[state]
 fill={'normal':'url(#navy)','selected':'url(#navy)','pressed':'#102C47','disabled':'#304354'}[state]
 return rect(4,8,w-8,h-10,r,'#07172C',extra='opacity=".35"')+rect(4,3,w-8,h-10,r,fill,border,5)+rect(10,9,w-20,h-22,max(1,r-6),'none','#67879E',1,extra='opacity=".35"')
for name,w,h,r in [('panel_status',480,104,32),('panel_objective',400,112,28),('panel_currency',192,80,25),('panel_level',144,60,22),('button_rectangle',240,80,24)]:
 add(name,w,h,panel(w,h,r),'background',[r+8,r+8,r+8,r+8])
for state in ['selected','pressed','disabled']:
 add('button_rectangle_'+state,240,80,panel(240,80,24,state),'button_state',[32,32,32,32])
for state in ['normal','selected','pressed','disabled']:
 border={'normal':'url(#ivory)','selected':'#61F5FF','pressed':'#83C7DB','disabled':'#738C9F'}[state]
 fill={'normal':'url(#navy)','selected':'url(#navy)','pressed':'#112D46','disabled':'#304354'}[state]
 body=f'<circle cx="128" cy="132" r="120" fill="#07172C" opacity=".35"/><circle cx="128" cy="126" r="119" fill="{fill}" stroke="{border}" stroke-width="6"/><circle cx="128" cy="126" r="110" fill="none" stroke="#8FAFC4" stroke-width="2" opacity=".27"/>'
 add('button_circle_'+state,256,256,body,'button_state')
add('button_pause',96,96,panel(96,96,27),'background',[34,34,34,34])
add('portrait_frame',160,160,rect(4,9,152,148,32,'none','#07172C',5,extra='opacity=".4"')+rect(5,4,150,148,30,'none','url(#ivory)',7)+rect(11,10,138,136,24,'none','#6B879F',2),'portrait')
for name,w,h in [('health',384,48),('xp',256,24),('objective',320,24)]:
 add(name+'_track',w,h,rect(0,0,w,h,h/2,'#11253C')+rect(2,2,w-4,h-4,h/2-2,'#172E49','#4A6580',2),'progress',[h//2,h//2,h//2,h//2])
 grad='green' if name=='health' else 'cyan'
 add(name+'_fill',w,h,rect(0,0,w,h,h/2,f'url(#{grad})')+rect(5,3,w-10,max(3,h*.18),max(2,h*.09),'#FFFFFF',extra='opacity=".15"'),'progress',pivot=(0,.5))
add('joystick_base',288,288,'<circle cx="144" cy="144" r="136" fill="#22466D" fill-opacity=".36" stroke="#D8F5FF" stroke-opacity=".48" stroke-width="5"/><circle cx="144" cy="144" r="119" fill="none" stroke="#96D5ED" stroke-opacity=".12" stroke-width="13"/>'+''.join(f'<path d="M 144 26 L 129 43 L 159 43 Z" fill="#D4F2FF" opacity=".5" transform="rotate({a} 144 144)"/>' for a in [0,90,180,270]),'joystick')
add('joystick_thumb',128,128,'<circle cx="64" cy="68" r="57" fill="#091C31" opacity=".25"/><circle cx="64" cy="62" r="55" fill="url(#ivory)" stroke="#F5FBFF" stroke-width="4"/><circle cx="64" cy="62" r="49" fill="#ADBFCE" opacity=".25"/>','joystick')
icons={
'rifle':'<path d="M14 58 H35 V45 H77 V40 H99 V45 H116 V53 H101 V60 H78 L73 68 H60 L66 94 H54 L47 69 H41 L36 88 H26 L31 62 L14 76 Z"/><rect x="44" y="36" width="25" height="7" rx="2"/>',
'barrage':'<path d="M14 51 H39 V42 H74 V46 H106 V54 H78 V60 H115 V68 H78 V74 H106 V82 H73 V87 H43 L38 105 H25 L30 80 H14 Z"/><rect x="49" y="28" width="20" height="9" rx="3"/><rect x="43" y="32" width="7" height="17" rx="2"/><path d="M86 40 V88" fill="none" stroke="currentColor" stroke-width="7"/>',
'decoy':'<g opacity=".38" transform="translate(19 -10)"><circle cx="64" cy="36" r="17"/><path d="M34 96 V82 Q34 59 64 59 Q94 59 94 82 V96 Z"/></g><circle cx="54" cy="44" r="17"/><path d="M24 104 V90 Q24 67 54 67 Q84 67 84 90 V104 Z"/><path d="M18 114 H96 M103 26 V40 M96 33 H110" fill="none" stroke="currentColor" stroke-width="5" stroke-linecap="round"/>',
'flare':'<path d="M38 93 L54 56 L72 64 L56 101 Z"/><path d="M61 50 Q42 38 60 15 Q60 32 73 27 Q92 36 77 53 L69 43 Z"/><path d="M24 49 L34 52 M90 58 L102 60 M88 16 L81 26 M40 17 L46 29" fill="none" stroke="currentColor" stroke-width="6" stroke-linecap="round"/>',
'dash':'<path d="M77 10 L34 70 H60 L49 118 L96 56 H69 Z"/>',
'freeze':'<g fill="none" stroke="currentColor" stroke-width="7" stroke-linecap="round" stroke-linejoin="round">'+''.join(f'<path d="M64 64 V17 M48 29 L64 41 L80 29" transform="rotate({a} 64 64)"/>' for a in range(0,360,60))+'</g>',
'pause':'<rect x="31" y="24" width="22" height="80" rx="5"/><rect x="75" y="24" width="22" height="80" rx="5"/>',
'shield':'<path d="M64 16 L102 31 V64 Q100 94 64 112 Q28 94 26 64 V31 Z"/><path d="M64 36 L82 43 V64 Q82 81 64 91 Q46 81 46 64 V43 Z" fill="#3585A3"/>'}
for name,body in icons.items():
 for variant,color in [('ivory','#FAFEFC'),('cyan','#54F2FF'),('disabled','#8DA7B8')]:
  add('icon_'+name+'_'+variant,128,128,f'<g fill="{color}" color="{color}">{body}</g>','icon')
pts=' '.join(f'{64+51*math.cos(math.radians(a-22.5)):.2f},{64+51*math.sin(math.radians(a-22.5)):.2f}' for a in range(0,360,45))
add('icon_coin',128,128,f'<polygon points="{pts}" fill="url(#gold)" stroke="#FFF39E" stroke-width="5"/><polygon points="{pts}" fill="none" stroke="#EBA722" stroke-width="4" transform="translate(64 64) scale(.77) translate(-64 -64)"/>','icon')
add('cooldown_ring_track',256,256,'<circle cx="128" cy="126" r="101" fill="none" stroke="#68899E" stroke-opacity=".3" stroke-width="10"/>','cooldown')
add('cooldown_ring_fill',256,256,'<circle cx="128" cy="126" r="101" fill="none" stroke="url(#cyan)" stroke-width="10"/>','cooldown')
add('cooldown_ready_ring',256,256,'<circle cx="128" cy="126" r="101" fill="none" stroke="#48EDFF" stroke-opacity=".12" stroke-width="20"/><circle cx="128" cy="126" r="101" fill="none" stroke="url(#cyan)" stroke-width="7"/>','cooldown')
add('cooldown_dim_overlay',256,256,'<circle cx="128" cy="126" r="111" fill="#091E34" fill-opacity=".55"/>','cooldown')
add('button_selected_accent',256,256,'<path d="M91 27 A106 106 0 0 1 165 27 M91 225 A106 106 0 0 0 165 225" fill="none" stroke="#6AFAFF" stroke-width="7" stroke-linecap="round"/>','cooldown')
def render(a):
 subprocess.run(['inkscape',str(ROOT/a['svg']),'--export-type=png','--export-filename='+str(ROOT/a['png'])],check=True,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
with concurrent.futures.ThreadPoolExecutor(max_workers=6) as pool:list(pool.map(render,assets))
# Exact source crop only. Background intentionally remains inside portrait; no face repainting.
source=Path('/workspace/scratch/920969c68c03/generated_images/exec-bb302eff-2cdd-4208-b135-4f2994dd094a.png')
hero=Image.open(source).convert('RGBA'); box=(55,0,930,875)
portrait=hero.crop(box).resize((512,512),Image.Resampling.LANCZOS)
mask=Image.new('L',(512,512));ImageDraw.Draw(mask).rounded_rectangle((0,0,511,511),radius=82,fill=255)
portrait.putalpha(mask);portrait.save(ROOT/'png'/'portrait_hero_exact_crop.png')
assets.append(dict(name='portrait_hero_exact_crop',category='portrait',size_px=[512,512],pivot_normalized=[.5,.5],svg=None,png='png/portrait_hero_exact_crop.png',nine_slice_lbrt_px=[0,0,0,0],provenance='Exact source crop, resized and rounded-alpha-masked. Face is not redrawn. Source blue background retained.',source_file=source.name,source_sha256=hashlib.sha256(source.read_bytes()).hexdigest(),source_crop_xyxy=list(box)))
for a in assets:
 im=Image.open(ROOT/a['png']).convert('RGBA'); ex=im.getchannel('A').getextrema();a['alpha_min_max']=list(ex)
 assert ex[0]==0, a['name']
for a in assets:
 n=a['name'];a['runtime_role']='Reusable visual layer; requires explicit runtime binding.'
 a['availability']='Existing HUD visual replacement candidate'
 if n.startswith('icon_rifle'):a['runtime_role']='Hud.cs weapon/reload status/control only. Mobile aim and firing remain automatic; never bind as manual fire.'
 if n.startswith('icon_freeze'):a['runtime_role']='Hud.cs shared hero Skill slot: Sentinel Freeze'
 if n.startswith('icon_barrage'):a['runtime_role']='Hud.cs shared hero Skill slot: Vanguard Barrage (generic heavy rotary gun silhouette)'
 if n.startswith('icon_decoy'):a['runtime_role']='Hud.cs shared hero Skill slot: Scout Decoy'
 if n.startswith('icon_flare'):a['runtime_role']='Hud.cs conditional Flare control; preserve existing visibility condition'
 if n.startswith('icon_dash'):a['runtime_role']='Hud.cs Dash control'
 if n.startswith('joystick'):a['runtime_role']='Hud.cs floating left joystick; preserve dynamic activation/position'
 if n in ['panel_currency','icon_coin'] or n.startswith('portrait') or n.startswith('icon_shield'):
  a['availability']='Optional mockup proposal; not an existing live HUD feature'
  a['runtime_role']='Optional decoration/status proposal, not a new action. Shield is not an action.' if n.startswith('icon_shield') else 'Optional mockup artwork only; do not add live portrait or coin features without approval.'
manifest=dict(version='1.1',design_reference='Approved rounded chibi HUD mockup',reference_canvas_px=[1846,852],coordinate_system='Top-left pixel origin. Pivots normalized from bottom-left for Unity UI. Nine slice order: left,bottom,right,top.',color_space='sRGB',alpha='Straight alpha RGBA PNG; transparent outside visible shapes.',assets=assets)
manifest['runtime_inventory']={
 'source':'Read-only Mac inventory supplied by parent; no implementation performed',
 'canvas':'uGUI Screen Space Overlay', 'runtime_reference_resolution_px':[1920,1080],
 'scaling':'Height-based scaling with safe-area handling', 'text':'Legacy UnityEngine.UI.Text; preserve existing font/text components',
 'Hud.cs':['floating left joystick','HP','pause','weapon/reload','Dash','shared hero Skill','conditional Flare'],
 'RunUi.cs':['XP/level','objective','boss HP','chain','timed buffs','upgrade/cache cards','evolution'],
 'hero_skill_icons':{'Sentinel':'freeze','Vanguard':'barrage','Scout':'decoy'},
 'integration_warning':'HoloButton.Update overwrites tints every frame. PNG replacement alone will not replace procedural art; explicit style/render wiring is required.',
 'preserve_behavior':['automatic mobile firing and aiming','floating joystick behavior','existing skill and conditional Flare visibility','existing text and runtime values'],
 'optional_mockup_proposals':['portrait','live coin display','numeric ammo','shield icon (not an action)']}
(ROOT/'manifest.json').write_text(json.dumps(manifest,indent=2))
# Contact sheet: actual exported assets, transparency against checkerboard.
fontfile='/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf'
font=ImageFont.truetype(fontfile,15); title=ImageFont.truetype(fontfile,30);small=ImageFont.truetype(fontfile,16)
cols=5;cw=284;ch=210;rows=math.ceil(len(assets)/cols)
sheet=Image.new('RGB',(cols*cw,110+rows*ch),(19,36,55));d=ImageDraw.Draw(sheet)
d.text((24,20),'ROUNDED CHIBI HUD  /  REUSABLE LAYERS',font=title,fill='#E9F7F9');d.text((24,65),f'{len(assets)} assets • navy / ivory / cyan • labels are preview only • exact hero crop noted below',font=small,fill='#9DD1E0')
for i,a in enumerate(assets):
 x=(i%cols)*cw;y=110+(i//cols)*ch
 for yy in range(y,y+160,16):
  for xx in range(x+8,x+cw-8,16):d.rectangle((xx,yy,min(xx+15,x+cw-9),yy+15),fill=('#29435B' if ((xx-x)//16+(yy-y)//16)%2 else '#314D65'))
 im=Image.open(ROOT/a['png']).convert('RGBA');im.thumbnail((cw-38,145),Image.Resampling.LANCZOS)
 sheet.paste(im,(x+(cw-im.width)//2,y+(160-im.height)//2),im)
 name=a['name'];d.text((x+12,y+167),name,font=font,fill='#F2F8F7');d.text((x+12,y+187),f"{a['size_px'][0]} × {a['size_px'][1]}  |  {a['category']}",font=small,fill='#90BDCF')
sheet.save(ROOT/'preview'/'contact-sheet.png')
print(f'Created {len(assets)} assets')
