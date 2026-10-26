"""Original code-drawn audio/anvil mark. Rebuild all Windows icon sizes."""
from pathlib import Path
from PIL import Image, ImageDraw

dest = Path(__file__).resolve().parents[1]/'src/StemForge/Assets'
dest.mkdir(exist_ok=True)
svg = '''<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256">
<rect x="8" y="8" width="240" height="240" rx="48" fill="#0b1510"/>
<path d="M42 139H214L196 163H161L150 180V193H179V209H80V193H108V180L96 163H71Z" fill="#00e676"/>
<path d="M43 109H68L83 79L104 131L128 48L150 123L169 88L184 109H213" fill="none" stroke="#00e676" stroke-width="12" stroke-linecap="round" stroke-linejoin="round"/>
</svg>'''
(dest/'SunoAudioForge.svg').write_text(svg, encoding='utf-8')
scale=4
im=Image.new('RGBA',(256*scale,256*scale))
d=ImageDraw.Draw(im)
d.rounded_rectangle((32,32,992,992), radius=192, fill='#0b1510')
def pts(values): return [(int(x*scale),int(y*scale)) for x,y in values]
d.polygon(pts([(42,139),(214,139),(196,163),(161,163),(150,180),(150,193),(179,193),(179,209),(80,209),(80,193),(108,193),(108,180),(96,163),(71,163)]), fill='#00e676')
line=pts([(43,109),(68,109),(83,79),(104,131),(128,48),(150,123),(169,88),(184,109),(213,109)])
d.line(line,fill='#00e676',width=48,joint='curve')
for x,y in line: d.ellipse((x-24,y-24,x+24,y+24),fill='#00e676')
im=im.resize((256,256),Image.Resampling.LANCZOS)
im.save(dest/'SunoAudioForge.png')
im.save(dest/'SunoAudioForge.ico',sizes=[(n,n) for n in (16,20,24,32,40,48,64,128,256)])
