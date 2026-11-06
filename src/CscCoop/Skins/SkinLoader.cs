using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace CscCoop.Skins;

// Every person in the game, found once by scanning the scene files (PersonReader.Catalog, about a second, in the
// background) and kept in BepInEx/config/CscCoop-skins.tsv with the scene files' sizes, so it's scanned again only
// if the game's files change.
public static class SkinCatalog
{
    public static IReadOnlyList<Person> People { get; private set; }
    public static bool Ready => People != null;
    static Task scan;
    static string CachePath => Path.Combine(BepInEx.Paths.ConfigPath, "CscCoop-skins.tsv");

    // Starts loading (from the file, or scanning in the background); People is set once done.
    public static void Load()
    {
        if (Ready || scan != null) return;
        string dir = SkinFiles.DataDir;
        string stamp = Stamp(dir);
        try
        {
            if (File.Exists(CachePath))
            {
                var lines = File.ReadAllLines(CachePath);
                if (lines.Length > 0 && lines[0] == stamp)
                {
                    People = lines.Skip(1).Select(l => l.Split('\t')).Where(a => a.Length >= 5)
                        .Select(a => new Person { File = a[0], Root = long.Parse(a[1]), Name = a[2], Meshes = int.Parse(a[3]), MeshKey = a[4] }).ToList();
                    Plugin.L.LogInfo($"COOP skins: {People.Count} people from {CachePath}");
                    return;
                }
            }
        }
        catch (Exception e) { Plugin.L.LogWarning($"COOP skins: cache unreadable ({e.Message}), scanning"); }
        scan = Task.Run(() =>
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var people = PersonReader.Catalog(dir);
            try { File.WriteAllLines(CachePath, new[] { stamp }.Concat(people.Select(p => $"{p.File}\t{p.Root}\t{p.Name}\t{p.Meshes}\t{p.MeshKey}"))); } catch { }
            People = people;
            Plugin.L.LogInfo($"COOP skins: scanned {people.Count} people in {sw.ElapsedMilliseconds} ms");
        });
    }

    static string Stamp(string dir)
    {
        long h = 17;
        for (int n = 0; n < 200; n++)
        {
            var p = Path.Combine(dir, "level" + n);
            if (File.Exists(p)) h = h * 31 + new FileInfo(p).Length;
        }
        return "v1 " + h;
    }

    public static Person Find(string key) => People?.FirstOrDefault(p => p.Key == key);
}

// Loads people one at a time: the files are read and decoded on a worker thread (PersonReader: no Unity), the Unity
// objects made on the main thread in Tick. After each person the owner is asked which one it wants next (Next):
// the skins window answers with the first cell on screen that has none yet, so what is in view loads first. Stop
// ends it: the person being read is dropped, nothing more is started.
public sealed class SkinLoader
{
    public readonly int MipSkip;
    readonly Func<Person> next;
    readonly Action<Person, GameObject, Transform[]> loaded;
    Task<PersonData> reading;
    Person current;
    volatile bool stopped;
    public bool Busy => reading != null;
    public int Loaded, Failed;

    public SkinLoader(int mipSkip, Func<Person> next, Action<Person, GameObject, Transform[]> loaded)
    { MipSkip = mipSkip; this.next = next; this.loaded = loaded; }

    public void Stop() { stopped = true; }

    // Main thread, every frame.
    public void Tick()
    {
        if (reading != null)
        {
            if (!reading.IsCompleted) return;
            var t = reading; var p = current;
            reading = null; current = null;
            if (stopped) return;
            if (t.IsFaulted || t.Result == null)
            {
                Failed++;
                Plugin.L.LogWarning($"COOP skin '{p?.Display}' not read: {(t.IsFaulted ? t.Exception?.GetBaseException().Message : "no meshes")}");
                loaded(p, null, null);
            }
            else
            {
                GameObject go = null; Transform[] hum = null; string why = "";
                try { go = SkinFiles.Create(t.Result, SkinModel.Shelf, out hum, out why); }
                catch (Exception e) { why = e.Message; }
                if (go == null) { Failed++; Plugin.L.LogWarning($"COOP skin '{p.Display}' not made: {why}"); }
                else Loaded++;
                loaded(p, go, hum);
            }
            return;   // one person a frame: making one takes a few ms on this thread
        }
        if (stopped) return;
        var want = next();
        if (want == null) return;
        current = want;
        int mip = MipSkip;
        reading = Task.Run(() =>
        {
            var set = new AssetSet(SkinFiles.DataDir);
            try { return PersonReader.Read(set, want, mip, () => stopped); }
            finally { set.Close(); }
        });
    }
}
