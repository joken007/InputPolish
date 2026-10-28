using System;using System.Diagnostics;using System.Runtime.InteropServices;
internal static class X{
 [DllImport("advapi32.dll",SetLastError=true)] static extern bool OpenProcessToken(IntPtr p,uint a,out IntPtr t);
 [DllImport("advapi32.dll",SetLastError=true)] static extern bool GetTokenInformation(IntPtr t,int c,IntPtr i,int l,out int n);
 [DllImport("advapi32.dll",SetLastError=true)] static extern bool ConvertSidToStringSid(IntPtr s,out IntPtr str);
 [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint a,bool i,int pid);
 [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
 static string Il(int pid){IntPtr p=OpenProcess(0x1000,false,pid);if(p==IntPtr.Zero)return "open fail";IntPtr t;if(!OpenProcessToken(p,0x8,out t))return "tok fail";int n;IntPtr b=Marshal.AllocHGlobal(64);string r="?";if(GetTokenInformation(t,25,b,64,out n)){IntPtr s;if(ConvertSidToStringSid(Marshal.ReadIntPtr(b),out s))r=Marshal.PtrToStringAnsi(s);}Marshal.FreeHGlobal(b);CloseHandle(t);CloseHandle(p);return r;}
 static void Main(){foreach(Process p in Process.GetProcesses()){string n="";try{n=p.ProcessName;}catch{continue;}if(n=="explorer"||n=="e2e"||n=="InputPolish"||n=="pwsh"||n=="WindowsTerminal")Console.WriteLine(n+" pid="+p.Id+" IL="+Il(p.Id));}}
}
