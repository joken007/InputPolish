
from PIL import Image
import os
A = r"E:\脚本\input-polish\assets"
sheet = Image.new("RGBA", (16+24+32+48+64+128+256 + 8*8, 300), (245,245,248,255))
x = 8
for s in (16,24,32,48,64):
    im = Image.open(os.path.join(A, "icon-%d.png" % s))
    sheet.alpha_composite(im, (x, 8))
    x += s + 8
x = 8
y = 20 + 64
for s in (128, 256):
    im = Image.open(os.path.join(A, "icon-%d.png" % s))
    sheet.alpha_composite(im, (x, y))
    x += s + 8
sheet.save(r"C:\Users\Administrator\AppData\Local\InputPolishTest\iconsheet.png")
print(sheet.size)
print(Image.open(os.path.join(A,"icon.ico")).size)
