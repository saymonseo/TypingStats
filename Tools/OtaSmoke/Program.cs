using System.Diagnostics;
using System.Text.Json;
using TypingStats.Core;
using TypingStats.Storage;

if(args.Length<2)throw new ArgumentException("publish-dir and new-test-dir required");
var rollback=args.Contains("--rollback");
var published=Path.GetFullPath(args[0]);var root=Path.GetFullPath(args[1]);
if(Directory.Exists(root))throw new IOException("Use a new test directory.");
var source=Path.Combine(root,"program");var target=Path.Combine(root,"installed");var helper=Path.Combine(root,"helper");
Copy(published,source);Copy(published,target);Copy(published,helper);
if(rollback){File.WriteAllText(Path.Combine(source,"blocked-update.dll"),"fixture");Directory.CreateDirectory(Path.Combine(target,"blocked-update.dll"));}
var data=Path.Combine(target,"data");Directory.CreateDirectory(data);File.WriteAllText(Path.Combine(target,"portable.flag"),"");
var preferences=Path.Combine(data,"settings.json");var preferencesJson="{\"Theme\":2,\"AutomaticUpdates\":false,\"IncludePreviewUpdates\":true,\"TrackMouse\":true}";File.WriteAllText(preferences,preferencesJson);
var file=Path.Combine(data,"stats.sqlite");var backup=Path.Combine(data,"backups","before-test.sqlite");Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
using(var store=new StatsStore(file))
{
    var engine=new TypingEngine();var ctx=new InputContext("fixture-game.exe","test","test");var now=DateTimeOffset.UtcNow;
    for(var i=0;i<70;i++)engine.KeyPress(ctx,now,1000+i,"sc:0011","W / Ц");engine.Stop(now,1100,"fixture");store.Save(engine.Drain());store.Backup(backup);
    foreach (var button in Enum.GetValues<MouseButton>()) for (var i = 0; i < 3; i++) engine.MousePress(ctx.App, now, button);
    store.Save(engine.Drain()); store.Backup(backup);
}
var job=new UpdateJob(Guid.NewGuid().ToString("N"),int.MaxValue,source,target,file,backup,Path.Combine(root,"result.json"),["--smoke","--data-dir",data]);
var path=Path.Combine(root,"job.json");File.WriteAllText(path,JsonSerializer.Serialize(job));
var info=new ProcessStartInfo(Path.Combine(helper,"TypingStats.Updater.exe")){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,WorkingDirectory=helper};info.ArgumentList.Add(path);
using var process=Process.Start(info)??throw new IOException("Helper did not start");
if(!process.WaitForExit(45000))throw new TimeoutException("Helper timeout");
var result=JsonSerializer.Deserialize<UpdateResult>(File.ReadAllText(job.ResultFile))!;
if(!rollback&&(!result.Success||process.ExitCode!=0))throw new Exception(result.Message);
if(rollback&&result.Success)throw new Exception("Expected rollback did not happen");
// Wait for the restarted smoke app to close its DB before checking the fixture.
for(var i=0;i<100&&!File.Exists(Path.Combine(data,"smoke-report.json"));i++)Thread.Sleep(100);
Thread.Sleep(300);
if(File.Exists(Path.Combine(data,"smoke-error.txt")))throw new Exception(File.ReadAllText(Path.Combine(data,"smoke-error.txt")));
if(!File.Exists(Path.Combine(data,"smoke-report.json")))throw new Exception("Restarted UI did not complete its smoke check");
using(var store=new StatsStore(file))
{
    var keys=store.ReadKeys("2000-01-01","9999-12-31").Where(k=>k.Key.App=="fixture-game.exe").Sum(k=>k.Presses);
    if(keys!=70)throw new Exception("History changed during update");
    var clicks = store.ReadMouse("2000-01-01", "9999-12-31").Where(m => m.App == "fixture-game.exe").Sum(m => m.Presses);
    if (clicks != 15) throw new Exception("Mouse history changed during update");
}
if(!File.Exists(Path.Combine(target,"portable.flag")))throw new Exception("Portable flag lost");
if(File.ReadAllText(preferences)!=preferencesJson)throw new Exception("User preferences changed during update");
Console.WriteLine(rollback?"PASS failed replacement -> rollback -> old app restart; 70 key presses, 15 mouse presses and portable mode preserved":"PASS actual updater -> file replacement -> ready handshake -> restart; 70 key presses, 15 mouse presses and portable mode preserved");
Console.WriteLine(root);

static void Copy(string source,string target)
{
    Directory.CreateDirectory(target);
    foreach(var file in Directory.GetFiles(source,"*",SearchOption.AllDirectories))
    {var dest=Path.Combine(target,Path.GetRelativePath(source,file));Directory.CreateDirectory(Path.GetDirectoryName(dest)!);File.Copy(file,dest);}
}
