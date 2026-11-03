using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

// 一次性截图：用真的 QuickPanel + ChipStrip 渲染 Ctrl+` 快捷面板（README 图）。
// 只截图，不改源码、不碰 dist。
//   csc /nologo /target:exe /main:QuickPanelShot /out:tests\out\quickpanelshot.exe
//       /r:System.Core.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll
//       /r:System.Web.Extensions.dll /r:<WPF>\UIAutomationClient.dll /r:<WPF>\UIAutomationTypes.dll
//       /r:<WPF>\WindowsBase.dll  src\*.cs  tests\quickpanelshot.cs
public static class QuickPanelShot
{
    [STAThread]
    public static void Main(string[] args)
    {
        string outPng = args.Length > 0 ? args[0] : "quick-panel.png";
        Application.EnableVisualStyles();
        try
        {
            InputPolish.QuickPanel p = new InputPolish.QuickPanel();
            p.StartPosition = FormStartPosition.Manual;
            p.Location = new Point(280, 220);

            InputPolish.ChipStrip chips = (InputPolish.ChipStrip)Field(p, "_chips");
            InputPolish.PromptItem[] items = new InputPolish.PromptItem[]
            {
                Item("outline", "结构大纲", "把这一页整理成三级结构大纲，标出每节的核心结论。"),
                Item("tldr", "一句话", "用一句话概括这一页最重要的结论，不超过 40 字。"),
                Item("deep", "深度解读", "逐段解释这一页的关键判断，指出作者的论证链条和隐藏前提。"),
                Item("data", "关键数据", "列出这一页出现的所有数字与结论，标注它们的来源和口径。"),
                Item("action", "行动清单A", "请把下面这篇网页转成可执行的行动清单：用「1.」编号，每条以动词开头，给出负责人和验收标准。")
            };
            chips.SetItems(items, "action");
            Invoke(p, "RefreshPreview");

            p.Show();
            Application.DoEvents();
            p.SetInstructionCue("上次：用三句话概括这一页，并标出作者最担心的风险。");
            FocusButton(p, "执行");
            Application.DoEvents();
            Thread.Sleep(400);
            Application.DoEvents();

            Point origin = p.PointToScreen(Point.Empty);
            Size cs = p.ClientSize;
            using (Bitmap bmp = new Bitmap(cs.Width, cs.Height, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.CopyFromScreen(origin.X, origin.Y, 0, 0, cs);
                }
                bmp.Save(outPng, ImageFormat.Png);
            }
            p.Hide();
            p.Dispose();
            Console.WriteLine("saved " + outPng + " " + cs.Width + "x" + cs.Height);
        }
        catch (Exception ex)
        {
            Console.WriteLine("ERR " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static InputPolish.PromptItem Item(string id, string label, string prompt)
    {
        return new InputPolish.PromptItem(id, label, "", 1200, prompt, false);
    }

    private static object Field(object target, string name)
    {
        FieldInfo f = target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
        if (f == null) throw new InvalidOperationException("field not found: " + name);
        return f.GetValue(target);
    }

    private static void Invoke(object target, string name)
    {
        MethodInfo m = target.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance);
        if (m != null) m.Invoke(target, null);
    }

    private static void FocusButton(Control host, string text)
    {
        foreach (Control c in host.Controls)
        {
            Button b = c as Button;
            if (b != null && b.Text == text)
            {
                b.Focus();
                return;
            }
        }
    }
}
