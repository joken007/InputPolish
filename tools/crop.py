
from PIL import Image
im = Image.open(r"C:\Users\Administrator\AppData\Local\InputPolishTest\settings4.png")
print(im.size)
w,h = im.size
# bottom strip of the settings window
crop = im.crop((int(w*0.02), int(h*0.72), int(w*0.52), int(h*0.82)))
crop = crop.resize((crop.width*2, crop.height*2), Image.LANCZOS)
crop.save(r"C:\Users\Administrator\AppData\Local\InputPolishTest\label4.png")
print("saved", crop.size)
