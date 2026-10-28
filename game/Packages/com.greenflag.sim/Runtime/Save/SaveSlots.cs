using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace GreenFlag.Sim
{
    /// <summary>
    /// Where save files live. Write must replace a file in one step and keep the one it replaced as a backup, so a crash or a
    /// pulled plug mid-write can cost at most the newest save. PC uses FileSaveStore; the Switch needs its own store on the
    /// NintendoSDK save-data mount (mount, write, commit), which waits for the Switch developer account (roadmap step 14).
    /// </summary>
    public interface ISaveStore
    {
        /// <summary>The file's text, or null when there is none.</summary>
        string Read(string name);
        /// <summary>The text the last Write replaced, or null.</summary>
        string ReadBackup(string name);
        void Write(string name, string text);
        /// <summary>Deletes the file and its backup.</summary>
        void Delete(string name);
    }

    /// <summary>Save files in a folder (Unity passes Application.persistentDataPath). Writes go to name.tmp first and then
    /// replace the file, which moves to name.bak.</summary>
    public sealed class FileSaveStore : ISaveStore
    {
        public readonly string Dir;
        static readonly UTF8Encoding utf8 = new UTF8Encoding(false);

        public FileSaveStore(string dir) { Dir = dir; }

        string P(string name) => Path.Combine(Dir, name);

        public string Read(string name) => File.Exists(P(name)) ? File.ReadAllText(P(name), utf8) : null;
        public string ReadBackup(string name) => File.Exists(P(name) + ".bak") ? File.ReadAllText(P(name) + ".bak", utf8) : null;

        public void Write(string name, string text)
        {
            Directory.CreateDirectory(Dir);
            string path = P(name), tmp = path + ".tmp", bak = path + ".bak";
            var bytes = utf8.GetBytes(text);
            using (var f = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                f.Write(bytes, 0, bytes.Length);
                f.Flush(true);
            }
            if (!File.Exists(path)) { File.Move(tmp, path); return; }
            try { File.Replace(tmp, path, bak, true); }
            catch (PlatformNotSupportedException)
            {
                // no atomic replace here: keep the old file as the backup first, so one good copy always exists
                File.Copy(path, bak, true);
                File.Delete(path);
                File.Move(tmp, path);
            }
        }

        public void Delete(string name)
        {
            foreach (var p in new[] { P(name), P(name) + ".bak", P(name) + ".tmp" }) if (File.Exists(p)) File.Delete(p);
        }
    }

    /// <summary>Saves in memory, for tests and the harness. Behaves like FileSaveStore, backup included.</summary>
    public sealed class MemorySaveStore : ISaveStore
    {
        public readonly Dictionary<string, string> Files = new Dictionary<string, string>();
        public int Writes;
        public string Read(string name) => Files.TryGetValue(name, out var t) ? t : null;
        public string ReadBackup(string name) => Files.TryGetValue(name + ".bak", out var t) ? t : null;
        public void Write(string name, string text)
        {
            Writes++;
            if (Files.TryGetValue(name, out var old)) Files[name + ".bak"] = old;
            Files[name] = text;
        }
        public void Delete(string name) { Files.Remove(name); Files.Remove(name + ".bak"); }
    }

    public enum SlotState { Empty, Ok, Broken, TooNew }

    /// <summary>What the title screen shows for a slot.</summary>
    public sealed class SlotInfo
    {
        public int Slot;
        public SlotState State;
        /// <summary>The save (Ok only).</summary>
        public SaveGame Game;
        /// <summary>The newest file was broken and this is the one before it.</summary>
        public bool FromBackup;
        /// <summary>Why it won't load (Broken, TooNew).</summary>
        public string Error;
        public bool Used => State != SlotState.Empty;
    }

    /// <summary>
    /// The three career slots (roadmap step 35). Each slot is one file, career-1.json to career-3.json. Loading falls back to
    /// the backup when the newest file is broken; a save from a newer build is shown but never loaded or overwritten by an
    /// autosave.
    /// </summary>
    public sealed class SaveSlots
    {
        public const int Count = 3;
        public readonly ISaveStore Store;
        /// <summary>The build name written into each save.</summary>
        public string Build = "dev";
        /// <summary>The clock for SavedAt (tests pin it so saves compare byte for byte).</summary>
        public Func<DateTime> Now = () => DateTime.UtcNow;
        public int Saves;
        public string LastError;

        public SaveSlots(ISaveStore store) { Store = store; }

        public static string FileName(int slot) => $"career-{slot}.json";
        static void Check(int slot) { if (slot < 1 || slot > Count) throw new ArgumentOutOfRangeException(nameof(slot), "Slots are 1 to " + Count); }

        public SlotInfo Info(int slot)
        {
            Check(slot);
            var info = new SlotInfo { Slot = slot };
            string text, err = null;
            try { text = Store.Read(FileName(slot)); }
            catch (Exception e) { text = null; err = e.Message; }
            if (text != null)
            {
                try { info.Game = CareerSave.Read(text); info.State = SlotState.Ok; return info; }
                catch (SaveTooNewException e) { info.State = SlotState.TooNew; info.Error = e.Message; return info; }
                catch (Exception e) when (e is FormatException || e is ArgumentException || e is InvalidOperationException || e is IndexOutOfRangeException || e is ArgumentOutOfRangeException) { err = e.Message; }
            }
            string bak = null;
            try { bak = Store.ReadBackup(FileName(slot)); } catch (Exception) { }
            if (bak != null)
            {
                try { info.Game = CareerSave.Read(bak); info.State = SlotState.Ok; info.FromBackup = true; return info; }
                catch (Exception e) when (e is FormatException || e is ArgumentException || e is InvalidOperationException || e is IndexOutOfRangeException || e is ArgumentOutOfRangeException) { err = err ?? e.Message; }
            }
            if (text == null && err == null) { info.State = SlotState.Empty; return info; }
            info.State = SlotState.Broken;
            info.Error = err ?? "The save file can't be read";
            return info;
        }

        public List<SlotInfo> All()
        {
            var o = new List<SlotInfo>();
            for (int s = 1; s <= Count; s++) o.Add(Info(s));
            return o;
        }

        /// <summary>The slot saved most recently that loads, for Continue (0 when none).</summary>
        public int MostRecent()
        {
            int best = 0; DateTime at = DateTime.MinValue;
            foreach (var i in All()) if (i.State == SlotState.Ok && (best == 0 || i.Game.SavedAt > at)) { best = i.Slot; at = i.Game.SavedAt; }
            return best;
        }

        public SaveGame Load(int slot)
        {
            var i = Info(slot);
            if (i.State != SlotState.Ok) throw new InvalidOperationException(i.State == SlotState.Empty ? $"Slot {slot} is empty" : i.Error);
            return i.Game;
        }

        /// <summary>Writes a save. Refuses to overwrite a newer build's file. Returns false (LastError says why) when the
        /// write fails, so a full disk never crashes the game.</summary>
        public bool Save(int slot, SaveGame g)
        {
            Check(slot);
            LastError = null;
            if (Info(slot).State == SlotState.TooNew) { LastError = $"Slot {slot} holds a save from a newer version of the game"; return false; }
            g.SavedAt = Now();
            g.Build = Build;
            try { Store.Write(FileName(slot), CareerSave.Write(g)); Saves++; return true; }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { LastError = e.Message; return false; }
        }

        public void Delete(int slot) { Check(slot); Store.Delete(FileName(slot)); }
    }
}
