using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace DragonCodexBoot
{
 internal sealed class WhiteDragonPlugin : IDisposable {
  internal const string PluginId = "white-dragon@white-dragon-local";
  private static readonly Regex Section = new Regex(@"^\[plugins\.(?:"""+PluginId+@"""|'"+PluginId+@"')\]\s*(?:#.*)?$", RegexOptions.CultureInvariant);
  private static readonly Regex Enabled = new Regex(@"^enabled\s*=\s*true\s*(?:#.*)?$", RegexOptions.CultureInvariant);
  private readonly string codexRoot;
  private readonly string log;
  private string loadedPath;
  private Assembly loadedModule;
  private IDisposable runtime;
  private DateTime nextCheck;

  internal WhiteDragonPlugin(string launcherRoot, string overrideCodexRoot = null) {
   codexRoot=overrideCodexRoot??Environment.GetEnvironmentVariable("CODEX_HOME");
   if(String.IsNullOrWhiteSpace(codexRoot))codexRoot=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".codex");
   log=Path.Combine(launcherRoot,"logs","monitor.log");
  }

  internal void Update(bool codexRunning) {
   if(!codexRunning) {
    Stop();
    nextCheck=DateTime.MinValue;
    return;
   }
   var now=DateTime.UtcNow;
   if(now<nextCheck)return;
   nextCheck=now.AddSeconds(1);
   try {
    var library=IsEnabled(Path.Combine(codexRoot,"config.toml"))?InstalledLibrary():null;
    if(runtime!=null && String.Equals(library,loadedPath,StringComparison.OrdinalIgnoreCase))return;
    Stop();
    if(library==null)return;
    if(!String.Equals(library,loadedPath,StringComparison.OrdinalIgnoreCase)||loadedModule==null) {
     loadedModule=Assembly.Load(File.ReadAllBytes(library));
     loadedPath=library;
    }
    string settingsFolder=Path.Combine(codexRoot,"plugins","data",PluginId);
    Directory.CreateDirectory(settingsFolder);
    var module=loadedModule.GetType("DragonQuota.CodexDesktopPlugin",true);
    runtime=(IDisposable)module.GetMethod("Start",new[]{typeof(string)}).Invoke(null,
     new object[]{Path.Combine(settingsFolder,"DragonQuota.settings.json")});
    Log("white-dragon-loaded version="+loadedModule.GetName().Version);
   } catch(Exception error) {
    Log("white-dragon-load-failed "+error.GetBaseException().Message);
    nextCheck=now.AddSeconds(10);
   }
  }

  private string InstalledLibrary() {
   string parent=Path.Combine(codexRoot,"plugins","cache","white-dragon-local","white-dragon");
   if(!Directory.Exists(parent))return null;
   return Directory.EnumerateDirectories(parent)
    .Select(folder=>new { Folder=folder, Version=ReadVersion(Path.GetFileName(folder)) })
    .Where(item=>item.Version!=null)
    .OrderByDescending(item=>item.Version)
    .Select(item=>Path.Combine(item.Folder,"runtime","DragonQuota.Plugin.dll"))
    .FirstOrDefault(File.Exists);
  }

  private static Version ReadVersion(string text) {
   Version version;
   return Version.TryParse(text,out version)?version:null;
  }

  internal static bool IsEnabled(string path) {
   if(!File.Exists(path))return false;
   bool matched=false;
   foreach(string raw in File.ReadLines(path)) {
    string line=raw.Trim();
    if(line.StartsWith("[",StringComparison.Ordinal))matched=Section.IsMatch(line);
    else if(matched&&Enabled.IsMatch(line))return true;
   }
   return false;
  }

  private void Stop() {
   if(runtime==null)return;
   var previous=runtime;
   runtime=null;
   previous.Dispose();
   Log("white-dragon-unloaded");
  }

  private void Log(string message) {
   try { File.AppendAllText(log,DateTime.Now.ToString("s")+" "+message+Environment.NewLine); }
   catch(IOException) {}
   catch(UnauthorizedAccessException) {}
  }

  public void Dispose() { Stop(); }
 }
}
