import sys, glob
from PIL import Image, ImageDraw
files = sorted(glob.glob("still_*.png"), key=lambda f: float(f[6:-4]))
if len(sys.argv) > 1:
    files = [f for f in files if float(f[6:-4]) in [float(a) for a in sys.argv[1:]]]
cols, w, h = 3, 640, 360
rows = (len(files) + cols - 1) // cols
sheet = Image.new("RGB", (cols * w, rows * h), "white")
d = ImageDraw.Draw(sheet)
for i, f in enumerate(files):
    im = Image.open(f).resize((w, h), Image.LANCZOS)
    x, y = (i % cols) * w, (i // cols) * h
    sheet.paste(im, (x, y))
    d.text((x + 6, y + 4), f[6:-4], fill="yellow")
sheet.save("sheet.png")
