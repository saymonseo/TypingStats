using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using TypingStats.Core;
using TypingStats.Storage;
using ApplicationIdentity=TypingStats.Core.ApplicationIdentity;

namespace TypingStats.App.Windows;

internal sealed class ApplicationResolver(StatsStore store) : IDisposable
{
    private sealed record Entry(Process Process, ApplicationIdentity Identity, DateTimeOffset? Started);
    private readonly Dictionary<uint,Entry> cache=new();
    public string Resolve(uint pid,DateTimeOffset captured)
    {
        if(cache.TryGetValue(pid,out var entry))
        {
            try { if(!entry.Process.HasExited && (entry.Started==null || entry.Started<=captured)) return entry.Identity.Id; }
            catch(Exception e) when(e is InvalidOperationException or System.ComponentModel.Win32Exception){}
            entry.Process.Dispose();cache.Remove(pid);
        }
        Process? process=null;
        try
        {
            process=Process.GetProcessById((int)pid);DateTimeOffset? started=null;
            try { started=new DateTimeOffset(process.StartTime.ToUniversalTime()); } catch(System.ComponentModel.Win32Exception){}
            if(started>captured){process.Dispose();return "unknown";} // Recycled PID must not reattribute a queued event.
            var exe=process.ProcessName.ToLowerInvariant()+".exe";var name=exe;var fingerprint="";
            var handle=Native.OpenProcess(0x1000,false,pid);
            if(handle!=0)
            {
                try
                {
                    var path=new StringBuilder(32768);uint length=(uint)path.Capacity;
                    if(Native.QueryFullProcessImageName(handle,0,path,ref length))
                    {
                        var image=path.ToString();exe=Path.GetFileName(image).ToLowerInvariant();
                        fingerprint=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(image).ToLowerInvariant())));
                        try { var info=FileVersionInfo.GetVersionInfo(image);name=string.IsNullOrWhiteSpace(info.FileDescription)?info.ProductName??exe:info.FileDescription; }
                        catch(Exception e) when(e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception){name=exe;}
                    }
                }
                finally{Native.CloseHandle(handle);}
            }
            name=string.Join(" ",name.Split(['\r','\n','\t'],StringSplitOptions.RemoveEmptyEntries)).Trim();if(name.Length==0)name=exe;if(name.Length>120)name=name[..120];
            var identity=store.RegisterApplication(exe,name,fingerprint);
            if(cache.Count>=256){foreach(var old in cache.Values)old.Process.Dispose();cache.Clear();}
            cache[pid]=new Entry(process,identity,started);return identity.Id;
        }
        catch(Exception e) when(e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException)
        {process?.Dispose();return "unknown";}
    }
    public void Dispose(){foreach(var e in cache.Values)e.Process.Dispose();cache.Clear();}
}
