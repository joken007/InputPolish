using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Windows.Automation;

namespace InputPolish
{
    public enum IoLayer
    {
        None = 0,
        Value = 1,
        Text = 2,
        Clipboard = 3,
        Win32 = 4
    }

    public class ReadResult
    {
        public bool Ok;
        public string Error = "";
        public string Text = "";
        public IoLayer Layer = IoLayer.None;
        public AutomationElement Element;
        public string ProcessName = "";
        public string ControlTypeName = "";
        public string ClassName = "";
    }

    public class WriteResult
    {
        public bool Changed;
        public bool Verified;
        public string Error = "";
        public string LayerName = "";
    }

    public static class UiaIo
    {
        private static bool FocusByHandle(AutomationElement el)
        {
            try
            {
                int h = el.Current.NativeWindowHandle;
                if (h == 0) return false;
                return Native.ForceControlFocus(new IntPtr(h));
            }
            catch
            {
                return false;
            }
        }

        public static bool SameTarget(AutomationElement el, AutomationElement other)
        {
            if (el == null || other == null) return false;
            try
            {
                if (Automation.Compare(el, other)) return true;
            }
            catch
            {
            }

            int elHandle = 0;
            int otherHandle = 0;
            try { elHandle = el.Current.NativeWindowHandle; } catch { elHandle = 0; }
            try { otherHandle = other.Current.NativeWindowHandle; } catch { otherHandle = 0; }
            if (elHandle != 0 && elHandle == otherHandle) return true;

            if (elHandle != 0)
            {
                IntPtr fgFocus = Native.ForegroundFocusHwnd();
                if (fgFocus != IntPtr.Zero && fgFocus.ToInt64() == elHandle) return true;
            }
            return false;
        }

        public static string DescribeElement(AutomationElement el)
        {
            if (el == null) return "(null)";
            try
            {
                return el.Current.ControlType.ProgrammaticName.Replace("ControlType.", "") +
                       "/" + el.Current.ClassName + "/" + el.Current.NativeWindowHandle;
            }
            catch
            {
                return "(unreadable)";
            }
        }

        public static string SafeName(AutomationElement el)
        {
            try { return el.Current.Name; }
            catch { return ""; }
        }

        public static string ForegroundInfo()
        {
            try
            {
                IntPtr h = Native.GetForegroundWindow();
                if (h == IntPtr.Zero) return "(none)";
                uint pid;
                Native.GetWindowThreadProcessId(h, out pid);
                string proc = "";
                try { proc = System.Diagnostics.Process.GetProcessById((int)pid).ProcessName; }
                catch { }
                return "hwnd=" + h.ToInt64() + " proc=" + proc + " title=" + Native.WindowTitle(h);
            }
            catch (Exception ex)
            {
                return "EX " + ex.Message;
            }
        }

        public static ReadResult Read(Config cfg)
        {
            ReadResult r = new ReadResult();

            AutomationElement el = null;
            try
            {
                el = AutomationElement.FocusedElement;
            }
            catch (Exception ex)
            {
                r.Error = "无法获取焦点控件：" + ex.Message;
                return r;
            }

            if (el == null)
            {
                r.Error = "当前没有焦点输入框";
                return r;
            }

            r.Element = el;

            try
            {
                r.ControlTypeName = el.Current.ControlType.ProgrammaticName.Replace("ControlType.", "");
                r.ClassName = el.Current.ClassName == null ? "" : el.Current.ClassName;
            }
            catch (Exception ex)
            {
                r.Error = "无法读取焦点控件属性：" + ex.Message;
                return r;
            }

            try
            {
                int pid = el.Current.ProcessId;
                try
                {
                    r.ProcessName = Process.GetProcessById(pid).ProcessName;
                }
                catch
                {
                    r.ProcessName = "";
                }
            }
            catch
            {
            }

            if (cfg.excludeProcesses != null && r.ProcessName.Length > 0)
            {
                foreach (string p in cfg.excludeProcesses)
                {
                    if (p == null) continue;
                    string t = p.Trim();
                    if (t.Length == 0) continue;
                    if (string.Equals(t, r.ProcessName, StringComparison.OrdinalIgnoreCase))
                    {
                        r.Error = "该程序在排除名单中：" + r.ProcessName;
                        return r;
                    }
                }
            }

            bool isPassword = false;
            try
            {
                isPassword = el.Current.IsPassword;
            }
            catch
            {
            }
            if (isPassword)
            {
                r.Error = "检测到密码输入框，已拒绝处理";
                return r;
            }

            bool enabled = true;
            try
            {
                enabled = el.Current.IsEnabled;
            }
            catch
            {
            }
            if (!enabled)
            {
                r.Error = "焦点控件不可用";
                return r;
            }

            object vpObj = null;
            bool hasValue = false;
            try
            {
                hasValue = el.TryGetCurrentPattern(ValuePattern.Pattern, out vpObj);
            }
            catch
            {
                hasValue = false;
            }

            object tpObj = null;
            bool hasText = false;
            try
            {
                hasText = el.TryGetCurrentPattern(TextPattern.Pattern, out tpObj);
            }
            catch
            {
                hasText = false;
            }

            int nativeHandle = 0;
            try
            {
                nativeHandle = el.Current.NativeWindowHandle;
            }
            catch
            {
                nativeHandle = 0;
            }

            string ct = r.ControlTypeName;
            bool typeOk = ct == "Edit" || ct == "Document" || ct == "Text";
            bool classLooksEditable = r.ClassName.IndexOf("edit", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!typeOk && !classLooksEditable && !hasValue && !hasText)
            {
                r.Error = "焦点位置不是可编辑输入框（" + ct + "，" + r.ProcessName + " / " + r.ClassName + "）";
                Log.Write("focus reject: type=" + ct + " proc=" + r.ProcessName + " class=" + r.ClassName +
                          " name=" + SafeName(el) + " value=" + hasValue + " text=" + hasText +
                          " hwnd=" + nativeHandle + " fg=" + ForegroundInfo());
                return r;
            }

            if (hasValue)
            {
                try
                {
                    ValuePattern vp = (ValuePattern)vpObj;
                    bool readOnly = vp.Current.IsReadOnly;
                    if (!readOnly)
                    {
                        string v = vp.Current.Value;
                        r.Text = v == null ? "" : v;
                        r.Layer = IoLayer.Value;
                        r.Ok = true;
                        return r;
                    }
                    r.Error = "当前焦点是只读区域，无法写回修改（" + ct + " / " + r.ProcessName + "），请点进可编辑的输入框再试";
                    Log.Write("focus reject: read-only value pattern type=" + ct + " proc=" + r.ProcessName +
                              " class=" + r.ClassName + " len=" + (vp.Current.Value == null ? 0 : vp.Current.Value.Length));
                    return r;
                }
                catch (Exception ex)
                {
                    Log.Write("value pattern read failed: " + ex.Message);
                }
            }

            if (hasText)
            {
                try
                {
                    TextPattern tp = (TextPattern)tpObj;
                    var range = tp.DocumentRange;
                    string t = range.GetText(-1);
                    r.Text = t == null ? "" : t;
                    r.Layer = IoLayer.Text;
                    r.Ok = true;
                    return r;
                }
                catch (Exception ex)
                {
                    Log.Write("text pattern read failed: " + ex.Message);
                }
            }

            if (nativeHandle != 0 && (classLooksEditable || typeOk))
            {
                string wt = Native.GetControlText(new IntPtr(nativeHandle));
                if (wt != null)
                {
                    r.Text = wt;
                    r.Layer = IoLayer.Win32;
                    r.Ok = true;
                    return r;
                }
                Log.Write("win32 gettext returned null hwnd=" + nativeHandle + " class=" + r.ClassName);
            }

            if (cfg.clipboardFallback)
            {
                string text;
                string err;
                if (ReadViaClipboard(out text, out err))
                {
                    r.Text = text;
                    r.Layer = IoLayer.Clipboard;
                    r.Ok = true;
                    return r;
                }
                r.Error = err;
                return r;
            }

            r.Error = "该输入框不支持 UIA 读取，且剪贴板兜底已关闭";
            return r;
        }

        private static bool ReadViaClipboard(out string text, out string error)
        {
            text = "";
            error = "";

            string before = ClipUtil.GetText();
            string sentinel = "\u2063IP_SENTINEL_" + Guid.NewGuid().ToString("N") + "\u2063";
            try
            {
                if (!ClipUtil.SetText(sentinel))
                {
                    error = "无法占用剪贴板以读取内容";
                    return false;
                }

                Sender.SelectAll();
                Sender.Copy();
                Thread.Sleep(140);

                string got = ClipUtil.GetText();
                if (got == null || got == sentinel)
                {
                    error = "无法从该输入框读取内容（复制未生效）";
                    return false;
                }
                text = got;
                return true;
            }
            finally
            {
                Thread.Sleep(60);
                ClipUtil.SetText(before == null ? "" : before);
            }
        }

        public static WriteResult Write(Config cfg, AutomationElement el, IoLayer layer, string text, bool allowRefocus)
        {
            return Write(cfg, el, layer, text, allowRefocus, 0);
        }

        public static AutomationElement ElementFromHandle(int handle)
        {
            if (handle == 0) return null;
            try
            {
                return AutomationElement.FromHandle(new IntPtr(handle));
            }
            catch
            {
                return null;
            }
        }

        public static bool SameTarget(AutomationElement el, AutomationElement other, int knownHandle)
        {
            if (el != null && other != null)
            {
                try
                {
                    if (Automation.Compare(el, other)) return true;
                }
                catch
                {
                }
            }

            int otherHandle = 0;
            try { if (other != null) otherHandle = other.Current.NativeWindowHandle; } catch { otherHandle = 0; }

            if (knownHandle != 0 && otherHandle == knownHandle) return true;
            if (knownHandle != 0)
            {
                IntPtr fgFocus = Native.ForegroundFocusHwnd();
                if (fgFocus != IntPtr.Zero && fgFocus.ToInt64() == knownHandle) return true;
            }
            if (el == null) return false;
            return SameTarget(el, other);
        }

        public static WriteResult Write(Config cfg, AutomationElement el, IoLayer layer, string text, bool allowRefocus, int knownHandle)
        {
            WriteResult res = new WriteResult();
            Log.Write("write start layer=" + layer + " hwnd=" + knownHandle + " text=[" + Log.Preview(text) + "]");

            int elHandle = knownHandle;
            if (el != null)
            {
                try { elHandle = el.Current.NativeWindowHandle; }
                catch { }
            }

            if (elHandle == 0 && el == null)
            {
                res.Error = "目标输入框已失效";
                return res;
            }
            if (elHandle != 0 && !Native.IsWindow(new IntPtr(elHandle)))
            {
                res.Error = "目标输入框已关闭";
                return res;
            }

            // 第一优先：真正的原生编辑控件直接 WM_SETTEXT —— 不需要焦点，撤销最可靠
            if (elHandle != 0 && Native.LooksLikeEditControl(new IntPtr(elHandle)))
            {
                Log.Write("native settext try hwnd=" + elHandle + " class=" + Native.ClassNameOf(new IntPtr(elHandle)));
                long lr0;
                if (Native.SetControlText(new IntPtr(elHandle), text, out lr0))
                {
                    bool sawAnyText = false;
                    for (int i = 0; i < 20; i++)
                    {
                        Thread.Sleep(100);
                        string back0 = Native.GetControlText(new IntPtr(elHandle));
                        if (back0 != null) sawAnyText = true;
                        if (back0 != null && Normalize(back0) == Normalize(text))
                        {
                            res.Changed = true;
                            res.Verified = true;
                            res.LayerName = "Win32 WM_SETTEXT";
                            return res;
                        }
                    }
                    // 回读全部超时（目标线程忙 / 不处理消息）：WM_SETTEXT 本身返回成功，按已写入处理，
                    // 不要落到「重新聚焦」分支去报错，也不要再用键盘层重复写入。
                    // WM_SETTEXT 返回 TRUE 就表示控件已接受文本；回读失败（目标线程忙、控件正被销毁）
                    // 不能当作写入失败，否则会误报「撤销失败」并落到重新聚焦分支。
                    if (lr0 != 0)
                    {
                        Log.Write("native settext ok, readback unavailable hwnd=" + elHandle + " lresult=" + lr0 +
                                  " sawText=" + sawAnyText + " back=[" + Log.Preview(Native.GetControlText(new IntPtr(elHandle))) + "]");
                        res.Changed = true;
                        res.Verified = false;
                        res.LayerName = "Win32 WM_SETTEXT";
                        return res;
                    }
                    Log.Write("native settext readback mismatch hwnd=" + elHandle + " back=[" + Log.Preview(Native.GetControlText(new IntPtr(elHandle))) + "]");
                }
                else
                {
                    Log.Write("native settext failed hwnd=" + elHandle);
                }
            }

            if (allowRefocus)
            {
                AutomationElement cur = null;
                try
                {
                    cur = AutomationElement.FocusedElement;
                }
                catch
                {
                }
                if (!SameTarget(el, cur, elHandle))
                {
                    Log.Write("refocus needed: cur=" + DescribeElement(cur) + " el=" + DescribeElement(el) +
                              " hwnd=" + elHandle +
                              " fg=" + Native.GetForegroundWindow().ToInt64() + " fgFocus=" + Native.ForegroundFocusHwnd().ToInt64());
                    bool focused = false;
                    if (elHandle != 0)
                    {
                        focused = Native.ForceControlFocus(new IntPtr(elHandle));
                    }
                    if (!focused && el != null)
                    {
                        try
                        {
                            el.SetFocus();
                            Thread.Sleep(150);
                            focused = true;
                        }
                        catch (Exception ex)
                        {
                            Log.Write("element SetFocus failed: " + ex.Message);
                            focused = FocusByHandle(el);
                        }
                    }
                    if (!focused)
                    {
                        res.Error = "无法重新聚焦目标输入框";
                        return res;
                    }
                    if (el == null)
                    {
                        el = ElementFromHandle(elHandle);
                    }
                }
            }

            AutomationElement now = null;
            try
            {
                now = AutomationElement.FocusedElement;
            }
            catch
            {
            }
            if (!SameTarget(el, now, elHandle))
            {
                Log.Write("focus mismatch before write, fgFocus=" + Native.ForegroundFocusHwnd().ToInt64() +
                          " elHwnd=" + elHandle + " now=" + (now == null ? "(null)" : DescribeElement(now)));
                res.Error = "焦点已改变，已放弃写入以免误改其它窗口";
                return res;
            }

            if (layer == IoLayer.Win32)
            {
                int h = elHandle;
                Log.Write("win32 write try hwnd=" + h + " focusHwnd=" + Native.GetForegroundWindow().ToInt64());
                if (h != 0 && Native.SetControlText(new IntPtr(h), text))
                {
                    for (int i = 0; i < 8; i++)
                    {
                        Thread.Sleep(60);
                        string back = Native.GetControlText(new IntPtr(h));
                        if (back != null && Normalize(back) == Normalize(text))
                        {
                            res.Changed = true;
                            res.Verified = true;
                            res.LayerName = "Win32 WM_SETTEXT";
                            return res;
                        }
                    }
                    res.Changed = true;
                    string latest = Native.GetControlText(new IntPtr(h));
                    Log.Write("win32 settext readback mismatch, back=[" + Log.Preview(latest) + "]");
                }
                else
                {
                    Log.Write("win32 settext failed hwnd=" + h);
                }
            }

            if ((layer == IoLayer.Value || layer == IoLayer.Text) && el != null)
            {
                object vpObj = null;
                bool hasValue = false;
                try
                {
                    hasValue = el.TryGetCurrentPattern(ValuePattern.Pattern, out vpObj);
                }
                catch
                {
                    hasValue = false;
                }

                if (hasValue)
                {
                    try
                    {
                        ValuePattern vp = (ValuePattern)vpObj;
                        if (!vp.Current.IsReadOnly)
                        {
                            vp.SetValue(text);
                            for (int i = 0; i < 10; i++)
                            {
                                Thread.Sleep(60);
                                string back = vp.Current.Value;
                                if (Normalize(back) == Normalize(text))
                                {
                                    res.Changed = true;
                                    res.Verified = true;
                                    res.LayerName = "ValuePattern";
                                    return res;
                                }
                            }
                            Log.Write("setvalue readback mismatch, falling back to keyboard");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Write("setvalue failed: " + ex.Message);
                    }
                }
            }

            if (!cfg.clipboardFallback)
            {
                res.Error = "UIA 写入失败，且剪贴板兜底已关闭";
                return res;
            }

            string saved = ClipUtil.GetText();
            try
            {
                if (!ClipUtil.SetText(text))
                {
                    res.Error = "无法写入剪贴板";
                    return res;
                }

                Thread.Sleep(80);
                IntPtr focusNow = Native.ForegroundFocusHwnd();
                if (elHandle != 0 && focusNow.ToInt64() != elHandle)
                {
                    bool refocused = false;
                    for (int i = 0; i < 3 && !refocused; i++)
                    {
                        refocused = Native.ForceControlFocus(new IntPtr(elHandle));
                        if (!refocused) Thread.Sleep(120);
                    }
                    if (!refocused)
                    {
                        res.Error = "目标输入框已失去焦点，为避免误改其它窗口已取消";
                        Log.Write("keyboard layer aborted: focus moved to " + focusNow.ToInt64() + " target=" + elHandle);
                        return res;
                    }
                }
                bool selected = el != null && SelectAllViaTextPattern(el);
                if (!selected)
                {
                    Sender.SelectAll();
                    Thread.Sleep(120);
                }
                Log.Write("selectall viaTextPattern=" + selected + " selection=[" + Log.Preview(SelectionText(el)) + "] fg=" + Native.GetForegroundWindow().ToInt64() +
                          " focusHwnd=" + Native.ForegroundFocusHwnd().ToInt64());
                Sender.Paste();
                res.Changed = true;
                res.LayerName = "Keyboard";
                Thread.Sleep(380);
                for (int i = 0; i < 10; i++)
                {
                    string back = ReadCurrentText(el);
                    if (back == null && elHandle != 0) back = Native.GetControlText(new IntPtr(elHandle));
                    if (back != null && Normalize(back) == Normalize(text))
                    {
                        res.Verified = true;
                        break;
                    }
                    Thread.Sleep(80);
                }
                int kbHandle = elHandle;
                if (kbHandle != 0)
                {
                    Log.Write("after paste win32 back=[" + Log.Preview(Native.GetControlText(new IntPtr(kbHandle))) + "]");
                }

                if (!res.Verified)
                {
                    res.Error = "内容已粘贴，但未能回读确认（可能仍已生效）";
                }
                return res;
            }
            finally
            {
                Thread.Sleep(320);
                ClipUtil.SetText(saved == null ? "" : saved);
            }
        }

        public static string ReadCurrentText(AutomationElement el)
        {
            if (el == null) return null;
            try { if (el.Current.NativeWindowHandle == 0) return null; } catch { return null; }
            try
            {
                object vpObj;
                if (el.TryGetCurrentPattern(ValuePattern.Pattern, out vpObj))
                {
                    try
                    {
                        ValuePattern vp = (ValuePattern)vpObj;
                        if (!vp.Current.IsReadOnly) return vp.Current.Value;
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }

            try
            {
                object tpObj;
                if (el.TryGetCurrentPattern(TextPattern.Pattern, out tpObj))
                {
                    try
                    {
                        TextPattern tp = (TextPattern)tpObj;
                        return tp.DocumentRange.GetText(-1);
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }

            try
            {
                int h = el.Current.NativeWindowHandle;
                if (h != 0)
                {
                    string wt = Native.GetControlText(new IntPtr(h));
                    if (wt != null) return wt;
                }
            }
            catch
            {
            }
            return null;
        }

        private static bool SelectAllViaTextPattern(AutomationElement el)
        {
            try
            {
                object tpObj;
                if (!el.TryGetCurrentPattern(TextPattern.Pattern, out tpObj)) return false;
                TextPattern tp = (TextPattern)tpObj;
                tp.DocumentRange.Select();
                Thread.Sleep(80);
                System.Windows.Automation.Text.TextPatternRange[] sel = tp.GetSelection();
                if (sel == null || sel.Length == 0) return false;
                string t = sel[0].GetText(-1);
                if (t == null || t.Length == 0) return false;
                Log.Write("textpattern select ok len=" + t.Length);
                return true;
            }
            catch (Exception ex)
            {
                Log.Write("textpattern select failed: " + ex.Message);
                return false;
            }
        }

        private static string SelectionText(AutomationElement el)
        {
            try
            {
                object tpObj;
                if (!el.TryGetCurrentPattern(TextPattern.Pattern, out tpObj)) return "(no text pattern)";
                TextPattern tp = (TextPattern)tpObj;
                System.Windows.Automation.Text.TextPatternRange[] sel = tp.GetSelection();
                if (sel == null || sel.Length == 0) return "(no selection)";
                return sel[0].GetText(-1);
            }
            catch (Exception ex)
            {
                return "(EX " + ex.Message + ")";
            }
        }

        public static string Normalize(string s)
        {
            if (s == null) return "";
            s = s.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\u00a0", " ");
            while (s.IndexOf("\n\n\n") >= 0) s = s.Replace("\n\n\n", "\n\n");
            return s.Trim();
        }

        public static string DescribeFocused()
        {
            try
            {
                AutomationElement el = AutomationElement.FocusedElement;
                if (el == null) return "FOCUS=NULL";
                string name = "";
                string cls = "";
                string aid = "";
                int pid = 0;
                try { name = el.Current.Name; } catch { }
                try { cls = el.Current.ClassName; } catch { }
                try { aid = el.Current.AutomationId; } catch { }
                try { pid = el.Current.ProcessId; } catch { }
                string proc = "";
                try { proc = Process.GetProcessById(pid).ProcessName; } catch { }
                object vp;
                bool hv = el.TryGetCurrentPattern(ValuePattern.Pattern, out vp);
                object tp;
                bool ht = el.TryGetCurrentPattern(TextPattern.Pattern, out tp);
                return string.Format("proc={0} type={1} cls={2} id={3} name=[{4}] value={5} text={6}",
                    proc, el.Current.ControlType.ProgrammaticName.Replace("ControlType.", ""), cls, aid, name, hv, ht);
            }
            catch (Exception ex)
            {
                return "FOCUS EX " + ex.Message;
            }
        }
    }
}
