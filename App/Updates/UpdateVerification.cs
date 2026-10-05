using System.Text.Json;
using TypingStats.Core;

namespace TypingStats.App.Updates;

/// <summary>Deterministic offline orchestration checks; no real update is installed.</summary>
internal static class UpdateVerification
{
    public static async Task Run(Collector collector)
    {
        var settings = collector.Settings;
        var original = (settings.AutomaticUpdates, settings.IncludePreviewUpdates, settings.LastUpdateCheckUtc, settings.AutoUpdateAttemptVersion, settings.FailedAutoUpdateVersion);
        var now = DateTimeOffset.UtcNow; var checks = 0;
        var version = new Version(GitHubUpdates.Current.Major, GitHubUpdates.Current.Minor + 1, 0);
        var update = new ReleaseUpdate(version, "v" + version, "fixture", new Uri("https://github.com/fixture"), new string('a',64), 100, false);
        void Assert(bool value, string name) { if(!value)throw new InvalidOperationException("Automatic update verification failed: " + name);checks++; }
        void Reset() { settings.AutomaticUpdates=true;settings.IncludePreviewUpdates=true;settings.LastUpdateCheckUtc=null;settings.AutoUpdateAttemptVersion=null;settings.FailedAutoUpdateVersion=null; }
        try
        {
            Reset(); var transport = new FakeTransport(update); var launches = 0; var restarts = 0;
            using(var coordinator = new UpdateCoordinator(collector,transport,(_,_)=>{launches++;return Task.CompletedTask;},()=>now))
            {
                coordinator.RestartRequested += ()=>restarts++;
                await coordinator.Tick(()=>false); Assert(transport.Checks==0,"startup delay"); now=now.AddSeconds(31);
                await coordinator.Tick(()=>false); Assert(transport.Checks==1&&transport.Downloads==1&&launches==0,"download then defer for visible/active UI");
                await coordinator.Tick(()=>false); Assert(transport.Downloads==1,"do not re-download while waiting");
                settings.AutomaticUpdates=false;await coordinator.Tick(()=>true);Assert(launches==0,"disable cancels installation eligibility");settings.AutomaticUpdates=true;
                await coordinator.Tick(()=>true);Assert(launches==1&&restarts==1,"safe idle installs and restarts once");
                Assert(Settings.Load(settings.DataDirectory).AutoUpdateAttemptVersion==version.ToString(3),"attempt marker persisted before helper");
            }
            Reset();transport=new FakeTransport(update){Failure=new IOException("offline fixture")};
            using(var coordinator=new UpdateCoordinator(collector,transport,(_,_)=>Task.CompletedTask,()=>now))
            {now=now.AddSeconds(31);await coordinator.Tick(()=>true);now=now.AddMinutes(1);await coordinator.Tick(()=>true);Assert(transport.Checks==1,"network failure backs off");}
            Reset();transport=new FakeTransport(update);
            using(var coordinator=new UpdateCoordinator(collector,transport,(_,_)=>throw new IOException("install failure fixture"),()=>now))
            {now=now.AddSeconds(31);await coordinator.Tick(()=>true);Assert(settings.FailedAutoUpdateVersion==version.ToString(3),"failed release suppressed");}
            settings.LastUpdateCheckUtc=null;transport=new FakeTransport(update);
            using(var coordinator=new UpdateCoordinator(collector,transport,(_,_)=>Task.CompletedTask,()=>now))
            {now=now.AddSeconds(31);await coordinator.Tick(()=>true);Assert(transport.Downloads==0,"restart does not retry failed release");}
            Reset();settings.AutoUpdateAttemptVersion=version.ToString(3);transport=new FakeTransport(update);
            using(var coordinator=new UpdateCoordinator(collector,transport,(_,_)=>Task.CompletedTask,()=>now))
                Assert(settings.FailedAutoUpdateVersion==version.ToString(3)&&settings.AutoUpdateAttemptVersion==null,"rollback marker blocks update loop");
            Reset(); transport = new FakeTransport(update){DownloadWait=new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously)};
            using(var coordinator=new UpdateCoordinator(collector,transport,(_,_)=>Task.CompletedTask,()=>now))
            {
                now=now.AddSeconds(31);var automatic=coordinator.Tick(()=>false);await coordinator.Tick(()=>false);
                var manual=coordinator.Check(CancellationToken.None);Assert(transport.Checks==1&&!manual.IsCompleted,"manual and background share operation lock");
                transport.DownloadWait.SetResult("offline-fixture");await automatic;await manual;Assert(transport.Downloads==1&&transport.Checks==2,"manual resumes after background download");
            }
            var folder=Path.Combine(settings.DataDirectory,"preferences-test");var preference=new Settings(folder);Directory.CreateDirectory(folder);
            preference.Theme=AppTheme.Dark;preference.AutomaticUpdates=false;preference.IncludePreviewUpdates=false;preference.TrackMouse=false;preference.Save();
            var loaded=Settings.Load(folder);Assert(loaded.Theme==AppTheme.Dark&&!loaded.AutomaticUpdates&&!loaded.IncludePreviewUpdates&&!loaded.TrackMouse,"preferences survive reload");
            File.WriteAllText(Path.Combine(settings.DataDirectory,"updates-verification.json"),JsonSerializer.Serialize(new{Passed=true,Checks=checks,Offline=true,RealInstallerInvoked=false}));
        }
        finally
        {
            (settings.AutomaticUpdates, settings.IncludePreviewUpdates, settings.LastUpdateCheckUtc, settings.AutoUpdateAttemptVersion, settings.FailedAutoUpdateVersion)=original;settings.Save();
        }
    }
    private sealed class FakeTransport(ReleaseUpdate update) : IUpdateTransport
    {
        public int Checks,Downloads;
        public Exception? Failure;
        public TaskCompletionSource<string>? DownloadWait;
        public Task<ReleaseUpdate?> Check(bool previews,CancellationToken token){Checks++;return Failure==null?Task.FromResult<ReleaseUpdate?>(update):Task.FromException<ReleaseUpdate?>(Failure);}
        public Task<string> Download(ReleaseUpdate release,IProgress<int> progress,CancellationToken token,string? directory=null){Downloads++;return DownloadWait?.Task??Task.FromResult("offline-fixture");}
        public void Dispose() { }
    }
}
