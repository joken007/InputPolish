using System;
using System.Drawing;
using System.Windows.Forms;

namespace InputPolish
{
    public class HotkeyWindow : Form
    {
        public event Action<int> Hotkey;

        public HotkeyWindow()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Bounds = new Rectangle(-30000, -30000, 1, 1);
            IntPtr h = Handle;
        }

        protected override void SetVisibleCore(bool value)
        {
            base.SetVisibleCore(false);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_HOTKEY)
            {
                Action<int> a = Hotkey;
                if (a != null)
                {
                    try { a((int)m.WParam.ToInt64()); }
                    catch (Exception ex) { Log.Write("hotkey handler error: " + ex.Message); }
                }
                return;
            }
            base.WndProc(ref m);
        }

        public static bool ParseHotkey(string spec, out int mods, out int vk)
        {
            mods = 0;
            vk = 0;
            if (spec == null) return false;
            string s = spec.Trim();
            if (s.Length == 0) return false;

            string[] parts = s.Split('+');
            foreach (string raw in parts)
            {
                string p = raw.Trim();
                if (p.Length == 0) continue;
                string up = p.ToUpperInvariant();
                if (up == "CTRL" || up == "CONTROL") mods |= Native.MOD_CONTROL;
                else if (up == "ALT") mods |= Native.MOD_ALT;
                else if (up == "SHIFT") mods |= Native.MOD_SHIFT;
                else if (up == "WIN" || up == "WINDOWS") mods |= Native.MOD_WIN;
                else if (up.Length == 1 && up[0] >= 'A' && up[0] <= 'Z') vk = up[0];
                else if (up.Length == 1 && up[0] >= '0' && up[0] <= '9') vk = up[0];
                else if (up.Length == 1 && (up[0] == '\u0020' || up[0] == ' ')) vk = Native.VK_SPACE;
                else if (up == "SPACE") vk = Native.VK_SPACE;
                else if (up.Length >= 2 && up[0] == 'F')
                {
                    int n;
                    if (int.TryParse(up.Substring(1), out n) && n >= 1 && n <= 24) vk = 0x70 + n - 1;
                    else return false;
                }
                else
                {
                    return false;
                }
            }
            return vk != 0 && mods != 0;
        }
    }
}
