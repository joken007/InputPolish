using System;
using System.Drawing;
using System.Drawing.Imaging;

class Zoom
{
    static int Main(string[] a)
    {
        try
        {
            string src = a[0], dst = a[1];
            int x = int.Parse(a[2]), y = int.Parse(a[3]), w = int.Parse(a[4]), h = int.Parse(a[5]), k = int.Parse(a[6]);
            using (Image im = Image.FromFile(src))
            {
                Bitmap bmp = new Bitmap(w * k, h * k);
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                    g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                    g.DrawImage(im, new Rectangle(0, 0, w * k, h * k), new Rectangle(x, y, w, h), GraphicsUnit.Pixel);
                }
                bmp.Save(dst, ImageFormat.Png);
                bmp.Dispose();
            }
            Console.WriteLine("ok " + dst);
            return 0;
        }
        catch (Exception ex) { Console.WriteLine("ERR " + ex.Message); return 1; }
    }
}
