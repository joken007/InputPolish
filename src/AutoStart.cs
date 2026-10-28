using System;
using Microsoft.Win32;

namespace InputPolish
{
    public static class AutoStart
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "InputPolish";

        public static bool IsEnabled()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, false))
                {
                    if (k == null) return false;
                    object v = k.GetValue(ValueName);
                    return v != null && Convert.ToString(v).Length > 0;
                }
            }
            catch
            {
                return false;
            }
        }

        public static bool Apply(bool enable)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (k == null) return false;
                    if (enable)
                    {
                        string exe = System.Windows.Forms.Application.ExecutablePath;
                        k.SetValue(ValueName, "\"" + exe + "\" --tray", RegistryValueKind.String);
                    }
                    else
                    {
                        k.DeleteValue(ValueName, false);
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                Log.Write("autostart apply failed: " + ex.Message);
                return false;
            }
        }
    }
}
