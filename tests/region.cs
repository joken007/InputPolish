using System;
using System.Drawing;
using System.Drawing.Imaging;

class Region
{
    static void Main(string[] a)
    {
        try
        {
            int x = int.Parse(a[1]), y = int.Parse(a[2]), w = int.Parse(a[3]), h = int.Parse(a[4]);
            Bitmap bmp = new Bitmap(w, h);
            using (Graphics g = Graphics.FromImage(bmp)) g.CopyFromScreen(x, y, 0, 0, new Size(w, h));
            bmp.Save(a[0], ImageFormat.Png);
            bmp.Dispose();
            Console.WriteLine("ok");
        }
        catch (Exception ex) { Console.WriteLine("ERR " + ex.Message); }
    }
}
