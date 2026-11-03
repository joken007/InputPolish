using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

// 一次性截图：用真的 PopupCard 类离屏渲染一张划词翻译卡片（README 图）。
// 只截图，不改源码、不碰 dist。
//   csc /nologo /target:exe /main:PopupShot /out:tests\out\popupshot.exe
//       /r:System.Core.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll
//       /r:System.Web.Extensions.dll /r:<WPF>\UIAutomationClient.dll /r:<WPF>\UIAutomationTypes.dll
//       /r:<WPF>\WindowsBase.dll  src\*.cs  tests\popupshot.cs
public static class PopupShot
{
    private const string Meta = "划词翻译 → 简体中文";
    private const string Translation =
        "部署流水线还需要两天，因为数据库迁移存在风险。在回滚脚本于预发布环境完成测试之前，我们应该冻结功能开发工作。";
    private const string Explanation =
        "- pipeline 译为「流水线」是 CI/CD 语境下的通行译法，「管道」也可，但工程团队更常用前者。\n" +
        "- staging 指与生产环境隔离的预发布/暂存环境，这里译作「预发布环境」；若团队内部习惯说「staging 环境」，保留原词也无妨。\n" +
        "- freeze 译为「冻结」，保留了原文中暂停、不再推进的强硬意味；不宜弱化为「暂缓」。\n" +
        "- risky 译为「存在风险」而非「很危险」，与原文技术评估的客观语气保持一致。";

    [STAThread]
    public static void Main(string[] args)
    {
        string outPng = args.Length > 0 ? args[0] : "popup-translate.png";
        Application.EnableVisualStyles();
        try
        {
            Type t = typeof(InputPolish.PopupCard);
            object card = Activator.CreateInstance(t, BindingFlags.Instance | BindingFlags.NonPublic, null,
                new object[] { Meta, Translation, Explanation }, null);
            using (Form f = (Form)card)
            {
                f.StartPosition = FormStartPosition.Manual;
                f.Location = new Point(320, 320);
                f.Show();
                Application.DoEvents();
                Thread.Sleep(450);
                Application.DoEvents();

                Rectangle r = new Rectangle(f.Location.X, f.Location.Y, f.Width, f.Height);
                using (Bitmap shot = new Bitmap(r.Width, r.Height, PixelFormat.Format32bppArgb))
                {
                    using (Graphics gs = Graphics.FromImage(shot))
                    {
                        gs.CopyFromScreen(r.Left, r.Top, 0, 0, new Size(r.Width, r.Height));
                    }
                    f.Hide();
                    Composite(shot, outPng, 24);
                }
                Console.WriteLine("saved " + outPng);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("ERR " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    /// <summary>抓到的卡片贴到浅色底上：去掉圆角外的桌面像素，加一点点投影。</summary>
    private static void Composite(Bitmap shot, string path, int margin)
    {
        int w = shot.Width;
        int h = shot.Height;
        using (Bitmap canvas = new Bitmap(w + margin * 2, h + margin * 2, PixelFormat.Format32bppArgb))
        {
            using (Graphics g = Graphics.FromImage(canvas))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                using (LinearGradientBrush bg = new LinearGradientBrush(
                    new Rectangle(0, 0, canvas.Width, canvas.Height),
                    Color.FromArgb(247, 248, 252), Color.FromArgb(229, 233, 242), 90f))
                {
                    g.FillRectangle(bg, 0, 0, canvas.Width, canvas.Height);
                }

                // 卡片 Region 是 12px 圆角；往里收 1px 去掉抓图边缘的桌面像素
                RectangleF body = new RectangleF(margin + 1, margin + 1, w - 2, h - 2);
                using (GraphicsPath cardPath = RoundRect(body, 11f))
                using (SolidBrush shadow = new SolidBrush(Color.FromArgb(30, 26, 32, 48)))
                using (GraphicsPath shadowPath = RoundRect(new RectangleF(body.X, body.Y + 5f, body.Width, body.Height), 11f))
                {
                    g.FillPath(shadow, shadowPath);
                    Region old = g.Clip;
                    g.SetClip(cardPath, CombineMode.Intersect);
                    g.DrawImage(shot, new Rectangle(margin, margin, w, h));
                    g.Clip = old;
                }
            }
            canvas.Save(path, ImageFormat.Png);
        }
    }

    private static GraphicsPath RoundRect(RectangleF r, float radius)
    {
        GraphicsPath p = new GraphicsPath();
        float d = Math.Min(radius * 2f, Math.Min(r.Width, r.Height));
        if (d <= 0.1f)
        {
            p.AddRectangle(r);
            return p;
        }
        p.AddArc(r.X, r.Y, d, d, 180f, 90f);
        p.AddArc(r.Right - d, r.Y, d, d, 270f, 90f);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0f, 90f);
        p.AddArc(r.X, r.Bottom - d, d, d, 90f, 90f);
        p.CloseFigure();
        return p;
    }
}
