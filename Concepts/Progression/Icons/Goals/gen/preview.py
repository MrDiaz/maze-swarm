# goals_sheet.png (256 icons on the game's dark panel colour) + goals_sheet_small_check.png (64 px and 40 px) + small/goals_sheet.jpg
import sys
from PIL import Image, ImageDraw
names = ["goal_purge","goal_survive","goal_escape","goal_hold","goal_hunt","goal_boss"]
ims = [Image.open(f"{n}.png") for n in names]
BG = (16, 30, 48)
sheet = Image.new("RGB", (6*272+16, 300), BG); d = ImageDraw.Draw(sheet)
for i,(n,im) in enumerate(zip(names,ims)):
    x = 16+i*272; d.rounded_rectangle((x-4,12,x+260,272), 10, outline=(50,110,160), width=2)
    sheet.paste(im, (x,14), im); d.text((x+90,280), n, fill=(190,220,240))
sheet.save("goals_sheet.png")
sm = Image.new("RGB", (6*80+16, 80+56+16), BG)
for i,im in enumerate(ims):
    a = im.resize((64,64), Image.LANCZOS); sm.paste(a, (16+i*80, 8), a)
    b = im.resize((40,40), Image.LANCZOS); sm.paste(b, (28+i*80, 96), b)
sm.save("goals_sheet_small_check.png")
sheet.convert("RGB").resize((sheet.width//2, sheet.height//2), Image.LANCZOS).save("small/goals_sheet.jpg", quality=85)
