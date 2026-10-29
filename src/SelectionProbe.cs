using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Automation;
using System.Windows.Automation.Text;

namespace InputPolish
{
    /// <summary>一次「轻量选区探测」的结果：既有文字，也有选区在屏幕上的位置。</summary>
    public class SelectionHit
    {
        public bool Ok;
        public string Error;
        public string Text;
        public string ProcessName;
        public string ControlTypeName;
        /// <summary>选区（或焦点元素）的屏幕矩形；W/H 为 0 表示没拿到。</summary>
        public int X;
        public int Y;
        public int W;
        public int H;

        public bool HasRect
        {
            get { return W > 0 && H > 0; }
        }
    }

    /// <summary>
    /// 只读的选区探测：不使用剪贴板、不注入按键，因此可以随时调用（鼠标划完就探测）。
    /// 热键触发的正式读取仍走 SelectionReader.Read（带剪贴板兜底）。
    /// </summary>
    internal static class SelectionProbe
    {
        /// <summary>探测当前选区。fallbackX/Y 用于选区拿不到矩形时（鼠标位置）兜底定位浮标。</summary>
        public static SelectionHit Probe(int fallbackX, int fallbackY)
        {
            SelectionHit hit = new SelectionHit();
            hit.X = fallbackX;
            hit.Y = fallbackY;
            try
            {
                AutomationElement el = AutomationElement.FocusedElement;
                if (el == null)
                {
                    hit.Error = "没有拿到焦点元素";
                    return hit;
                }

                AutomationElement cur = el;
                for (int depth = 0; depth < 5 && cur != null; depth++)
                {
                    string text;
                    SelRect rect;
                    if (TryReadRange(cur, out text, out rect))
                    {
                        hit.Ok = true;
                        hit.Text = text;
                        if (rect.W > 0 && rect.H > 0)
                        {
                            hit.X = rect.X;
                            hit.Y = rect.Y;
                            hit.W = rect.W;
                            hit.H = rect.H;
                        }
                        hit.ProcessName = SafeProc(cur);
                        hit.ControlTypeName = SafeType(cur);
                        return hit;
                    }
                    try { cur = TreeWalker.ControlViewWalker.GetParent(cur); }
                    catch { cur = null; }
                }

                hit.Error = "焦点处没有选中文字";
                return hit;
            }
            catch (Exception ex)
            {
                hit.Error = ex.Message;
                return hit;
            }
        }

        private static bool TryReadRange(AutomationElement el, out string text, out SelRect rect)
        {
            text = null;
            rect = new SelRect();
            if (el == null) return false;
            try
            {
                object pattern;
                if (!el.TryGetCurrentPattern(TextPattern.Pattern, out pattern)) return false;
                TextPattern tp = pattern as TextPattern;
                if (tp == null) return false;

                TextPatternRange[] ranges = tp.GetSelection();
                if (ranges == null || ranges.Length == 0) return false;

                string all = "";
                double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
                bool anyRect = false;

                for (int i = 0; i < ranges.Length; i++)
                {
                    string part = null;
                    try { part = ranges[i].GetText(-1); }
                    catch { part = null; }
                    if (part != null && part.Trim().Length > 0)
                    {
                        if (all.Length > 0) all += "\n";
                        all += part;
                    }

                    try
                    {
                        System.Windows.Rect[] rects = ranges[i].GetBoundingRectangles();
                        if (rects != null)
                        {
                            for (int k = 0; k < rects.Length; k++)
                            {
                                double x0 = rects[k].Left;
                                double y0 = rects[k].Top;
                                double w = rects[k].Width;
                                double h = rects[k].Height;
                                if (w <= 0 || h <= 0) continue;
                                if (x0 < minX) minX = x0;
                                if (y0 < minY) minY = y0;
                                if (x0 + w > maxX) maxX = x0 + w;
                                if (y0 + h > maxY) maxY = y0 + h;
                                anyRect = true;
                            }
                        }
                    }
                    catch
                    {
                    }
                }

                if (all.Trim().Length == 0) return false;

                text = all.Trim();
                if (anyRect)
                {
                    rect = new SelRect((int)minX, (int)minY, (int)(maxX - minX), (int)(maxY - minY));
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string SafeProc(AutomationElement el)
        {
            try
            {
                int pid = el.Current.ProcessId;
                System.Diagnostics.Process p = System.Diagnostics.Process.GetProcessById(pid);
                return p.ProcessName;
            }
            catch
            {
                return "";
            }
        }

        private static string SafeType(AutomationElement el)
        {
            try { return el.Current.ControlType.ProgrammaticName; }
            catch { return ""; }
        }

        private struct SelRect
        {
            public int X;
            public int Y;
            public int W;
            public int H;

            public SelRect(int x, int y, int w, int h)
            {
                X = x;
                Y = y;
                W = w;
                H = h;
            }
        }
    }
}
