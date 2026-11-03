using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

// 一次性 UI 自检：用真的窗口类离屏渲染，验证划词胶囊 / 翻译卡片 / 悬浮球各状态长什么样。
// 编译（仓库根目录，编译整个 src 但指定自己的 Main）：
//   csc /nologo /target:exe /main:UiCheck /out:tests\out\uicheck.exe /r:System.Core.dll
//       /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll
//       /r:"%WPF%\UIAutomationClient.dll" /r:"%WPF%\UIAutomationTypes.dll" /r:"%WPF%\WindowsBase.dll"
//       src\*.cs tests\uicheck.cs
public static class UiCheck
{
    [STAThread]
    public static void Main(string[] args)
    {
        string dir = args.Length > 0 ? args[0] : ".";
        Application.EnableVisualStyles();
        Directory.CreateDirectory(dir);

        try { SelectionBubbleShot(Path.Combine(dir, "selection-bubble.png")); }
        catch (Exception ex) { Console.WriteLine("bubble ERR " + ex.Message); }

        try { PopupCardShot(Path.Combine(dir, "popup-card.png")); }
        catch (Exception ex) { Console.WriteLine("card ERR " + ex.Message); }

        try { WidgetStatesShot(Path.Combine(dir, "widget-states.png")); }
        catch (Exception ex) { Console.WriteLine("states ERR " + ex.Message); }

        Console.WriteLine("done");
    }

    private static void SelectionBubbleShot(string path)
    {
        using (InputPolish.SelectionBubble b = new InputPolish.SelectionBubble())
        {
            Capture(b, 220, 220, path);
        }
    }

    private static void PopupCardShot(string path)
    {
        Type t = typeof(InputPolish.PopupCard);
        object card = Activator.CreateInstance(t, BindingFlags.Instance | BindingFlags.NonPublic, null,
            new object[]
            {
                "划词翻译 → 中文",
                "The quick brown fox jumps over the lazy dog.",
                "例句：这条提示词模板适合把一段口语化的输入整理成结构清晰的书面表达。"
            }, null);
        using (Form f = (Form)card)
        {
            Capture(f, 260, 300, path);
        }
    }

    /// <summary>
    /// 真开窗然后抓屏。DrawToBitmap 对带子控件的无边框窗口不可靠（子控件会漏），
    /// 所以这里闪一下真窗口再 CopyFromScreen。
    /// </summary>
    private static void Capture(Form f, int x, int y, string path)
    {
        f.StartPosition = FormStartPosition.Manual;
        f.Location = new Point(x, y);
        f.Show();
        Application.DoEvents();
        Thread.Sleep(400);
        Application.DoEvents();

        Rectangle r = new Rectangle(f.Location.X, f.Location.Y, f.Width, f.Height);
        using (Bitmap bmp = new Bitmap(r.Width, r.Height, PixelFormat.Format32bppArgb))
        {
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.CopyFromScreen(r.Left, r.Top, 0, 0, new Size(r.Width, r.Height));
            }
            bmp.Save(path, ImageFormat.Png);
        }
        f.Hide();
        Console.WriteLine("saved " + path + " " + r.Width + "x" + r.Height);
    }

    private static void WidgetStatesShot(string path)
    {
        InputPolish.WidgetState[] states = new InputPolish.WidgetState[]
        {
            InputPolish.WidgetState.Idle,
            InputPolish.WidgetState.Busy,
            InputPolish.WidgetState.Success,
            InputPolish.WidgetState.Error,
            InputPolish.WidgetState.Paused
        };
        string[] labels = new string[] { "Idle", "Busy", "Success", "Error", "Paused" };

        InputPolish.FloatWidget w = new InputPolish.FloatWidget();
        w.CreateControl();
        IntPtr h = w.Handle;
        w.ApplyScale();
        int cw = w.Width;
        int ch = w.Height;
        MethodInfo apply = typeof(InputPolish.FloatWidget).GetMethod("ApplyState",
            BindingFlags.NonPublic | BindingFlags.Instance);

        int cell = cw + 30;
        using (Bitmap bmp = new Bitmap(cell * states.Length, ch + 46, PixelFormat.Format32bppArgb))
        {
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (LinearGradientBrush bg = new LinearGradientBrush(
                    new Rectangle(0, 0, bmp.Width, bmp.Height),
                    Color.FromArgb(247, 248, 252), Color.FromArgb(226, 231, 242), 90f))
                {
                    g.FillRectangle(bg, 0, 0, bmp.Width, bmp.Height);
                }
                using (Font lf = new Font("Segoe UI", 11f, GraphicsUnit.Pixel))
                using (SolidBrush lb = new SolidBrush(Color.FromArgb(90, 96, 112)))
                {
                    for (int i = 0; i < states.Length; i++)
                    {
                        apply.Invoke(w, new object[] { states[i], "" });
                        using (Bitmap one = new Bitmap(cw, ch, PixelFormat.Format32bppArgb))
                        {
                            w.DrawToBitmap(one, new Rectangle(0, 0, cw, ch));
                            g.DrawImage(one, 15 + i * cell, 10);
                        }
                        StringFormat sf = new StringFormat();
                        sf.Alignment = StringAlignment.Center;
                        g.DrawString(labels[i], lf, lb, new RectangleF(i * cell, ch + 18, cell, 18), sf);
                        sf.Dispose();
                    }
                }
            }
            bmp.Save(path, ImageFormat.Png);
        }
        w.Dispose();
        Console.WriteLine("saved " + path);
    }
}

