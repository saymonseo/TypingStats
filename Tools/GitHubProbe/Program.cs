using TypingStats.Core;

if(args.Length==3)
{
    UpdatePackage.VerifyHash(args[1],args[2]);var files=UpdatePackage.Extract(args[1],args[0]);Console.WriteLine("PASS packaged Windows update SHA256 and safe extraction: "+files.Count+" files");return;
}
if(args.Length!=1)throw new ArgumentException("new output directory required");
Directory.CreateDirectory(args[0]);using var http=new HttpClient();http.DefaultRequestHeaders.UserAgent.ParseAdd("TypingStats-UpdateProbe/0.3.0");http.DefaultRequestHeaders.Add("X-GitHub-Api-Version","2022-11-28");
var json=await http.GetStringAsync("https://api.github.com/repos/"+UpdatePackage.Repository+"/releases?per_page=30");
var update=UpdatePackage.SelectRelease(json,new Version(0,0,0))??throw new Exception("No eligible Windows release");
var file=Path.Combine(args[0],"github-package.zip");var bytes=await http.GetByteArrayAsync(update.Download);if(bytes.LongLength!=update.Size)throw new Exception("Asset size mismatch");
await File.WriteAllBytesAsync(file,bytes);UpdatePackage.VerifyHash(file,update.Sha256);
Console.WriteLine("PASS anonymous GitHub API -> version selection -> Windows ZIP download -> matching SHA256: "+update.Tag);
