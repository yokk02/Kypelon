from pathlib import Path
from PIL import Image, ImageDraw
p = Path('assets/images'); p.mkdir(parents=True, exist_ok=True)
im = Image.new('RGB', (320, 180), '#edf4fa'); d = ImageDraw.Draw(im)
for i, h in enumerate([55, 90, 75, 125, 140]): d.rectangle((25+i*55, 160-h, 60+i*55, 160), fill=(30, 90+i*15, 150+i*10))
im.save(p/'sample-rgb.png'); im.save(p/'sample.jpg', quality=90)
im.save(p/'sample-progressive.jpg', quality=90, progressive=True)
alpha = Image.new('RGBA', (200, 120)); d = ImageDraw.Draw(alpha)
d.ellipse((10,10,110,110), fill=(53, 169, 230, 190)); d.ellipse((85,10,185,110), fill=(245, 170, 45, 150))
alpha.save(p/'sample-rgba.png')
