using System.Text.Json;
using Microsoft.Win32;
using TypingStats.Core;

namespace TypingStats.App.Windows;

internal static class StartupVerification
{
    public static void Run(Settings original)
    {
        var folder = Path.Combine(original.DataDirectory, "startup-test"); Directory.CreateDirectory(folder);
        const string executable = @"C:\Program Files\Typing Stats\TypingStats.exe";
        var checks = 0;
        void Assert(bool condition, string label) { if(!condition)throw new InvalidOperationException("Startup verification failed: " + label); checks++; }
        var preferences = Settings.Load(folder); var store = new FakeStore();
        Assert(preferences.StartWithWindows, "new installation defaults on");
        Assert(preferences.InitializeAutoStart(store, executable) == null, "default registration succeeds");
        Assert(store.Command == StartupCommand.Build(executable, folder) && store.Writes == 1, "quoted executable, tray and correct data directory");
        Assert(Settings.Load(folder).StartWithWindows, "default preference saved");
        preferences.InitializeAutoStart(store, executable); Assert(store.Writes == 1, "unchanged Run entry is not rewritten on every login");
        preferences.SetAutoStart(false, store, executable); Assert(store.Command == null && store.Removes == 1, "disable removes owned entry");
        var disabled = Settings.Load(folder); Assert(!disabled.StartWithWindows, "disabled preference survives restart");
        disabled.InitializeAutoStart(store, executable); Assert(store.Writes == 1 && !disabled.StartWithWindows, "restart does not re-enable disabled choice");
        disabled.SetAutoStart(true, store, executable); Assert(store.Writes == 2 && Settings.Load(folder).StartWithWindows, "user can re-enable");
        disabled.InitializeAutoStart(store, @"D:\TypingStats\TypingStats.exe"); Assert(store.Command!.StartsWith("\"D:\\TypingStats\\TypingStats.exe\"", StringComparison.Ordinal), "moved executable repaired");
        var legacyFolder = Path.Combine(folder, "legacy"); Directory.CreateDirectory(legacyFolder);
        File.WriteAllText(Path.Combine(legacyFolder,"settings.json"), "{\"Mode\":1,\"DailyGoal\":321,\"Theme\":2}");
        var legacy = Settings.Load(legacyFolder); Assert(legacy.StartWithWindows && legacy.Mode==TrackingMode.Keys && legacy.DailyGoal==321 && legacy.Theme==AppTheme.Dark, "legacy default on without losing other preferences");
        var deniedFolder = Path.Combine(folder, "denied"); Directory.CreateDirectory(deniedFolder); var denied = new Settings(deniedFolder); var failing = new FakeStore { Fail=true };
        Assert(denied.InitializeAutoStart(failing,executable)!=null && denied.StartupError!=null, "registry error is reported, not a fatal startup exception");
        var old = disabled.StartWithWindows;
        try { disabled.SetAutoStart(false,new FakeStore{Command="previous",Fail=true},executable);throw new InvalidOperationException("Expected write failure"); }
        catch(UnauthorizedAccessException) { }
        Assert(disabled.StartWithWindows==old && Settings.Load(folder).StartWithWindows==old, "failed change restores saved preference");

        var production = new RegistryStartupStore().Read();
        var temporaryKey = @"Software\TypingStats\StartupVerification\" + Guid.NewGuid().ToString("N");
        try
        {
            var registry = new RegistryStartupStore(temporaryKey);
            using(var key=Registry.CurrentUser.CreateSubKey(temporaryKey))key.SetValue("OtherValue","untouched");
            WindowsStartup.Synchronize(true, executable, @"C:\Typing Stats data", registry);
            Assert(registry.Read()==StartupCommand.Build(executable,@"C:\Typing Stats data"), "real registry command roundtrip in non-startup test key");
            using(var key=Registry.CurrentUser.OpenSubKey(temporaryKey))Assert(key!.GetValueKind("TypingStats")==RegistryValueKind.String, "REG_SZ value type");
            WindowsStartup.Synchronize(false, "", "", registry); Assert(registry.Read()==null,"real registry disable removes only our value");
            using(var key=Registry.CurrentUser.OpenSubKey(temporaryKey))Assert((string?)key!.GetValue("OtherValue")=="untouched","other values preserved");
        }
        finally
        {
            using(var key=Registry.CurrentUser.OpenSubKey(temporaryKey,true)){key?.DeleteValue("TypingStats",false);key?.DeleteValue("OtherValue",false);}
            Registry.CurrentUser.DeleteSubKey(temporaryKey,false); // Exact, newly generated test leaf, never the Windows Run key.
        }
        Assert(new RegistryStartupStore().Read()==production,"actual Windows startup entry untouched by verification");
        File.WriteAllText(Path.Combine(original.DataDirectory,"startup-verification.json"),JsonSerializer.Serialize(new{Passed=true,Checks=checks,DefaultEnabled=true,DisabledChoicePreserved=true,RealStartupEntryUntouched=true,RebootTested=false}));
    }
    private sealed class FakeStore : IStartupStore
    {
        public string? Command;
        public int Writes,Removes;
        public bool Fail;
        public string? Read()=>Command;
        public void Write(string command){if(Fail)throw new UnauthorizedAccessException("fixture");Command=command;Writes++;}
        public void Remove(){if(Fail)throw new UnauthorizedAccessException("fixture");Command=null;Removes++;}
    }
}
