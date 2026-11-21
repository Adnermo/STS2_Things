import json, sys
from PIL import Image, ImageDraw

meta = json.load(open('parts/meta.json'))
order = json.load(open('order.json'))['order']
mode = sys.argv[1]
if mode == 'body':
    names = [n for n in meta if n in order or n.endswith('_glow')]
else:
    names = [n for n in meta if n not in order and not n.endswith('_glow')]

tiles = []
for n in names:
    im = Image.open(f'parts/{n}.png').copy()
    im.thumbnail((440, 300))
    t = Image.new('RGBA', (450, 320), (48, 50, 62, 255))
    t.alpha_composite(im, (5, 5))
    ImageDraw.Draw(t).text((8, 302), n, fill='white')
    tiles.append(t)

C = 3
R = (len(tiles) + C - 1) // C
s = Image.new('RGB', (450 * C, 320 * R))
for i, t in enumerate(tiles):
    s.paste(t, ((i % C) * 450, (i // C) * 320))
s.save(f'sheet_{mode}.jpg', quality=88)
print(f'saved sheet_{mode}.jpg ({len(names)} parts)')
