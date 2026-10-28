using System;
using System.Collections.Generic;
using System.Globalization;

namespace GreenFlag.Sim
{
    /// <summary>A save file's contents: the career, which screen it was on, and (Standard stakes) the career as it was before
    /// the race on the reward screen, so Retry this race still works after a reload.</summary>
    public sealed class SaveGame
    {
        public const string Garage = "garage", Reward = "reward", Over = "over", Event = "event";

        public Career Career;
        /// <summary>Where the career picks up: Garage, Reward, Over or Event (tonight's off-track event, step 38). A save made
        /// at Go racing is a Garage save, so quitting mid-race restarts that race on every stakes setting (board Q48).</summary>
        public string Screen = Garage;
        public Career Retry;
        public uint RetrySeed;
        public DateTime SavedAt;
        /// <summary>The build that wrote it (shown on the slot, and kept for bug reports).</summary>
        public string Build;
        /// <summary>The format version the file was written in, before any migration.</summary>
        public int LoadedFrom = CareerSave.Version;
    }

    /// <summary>One step that brings a save from version From to From + 1. Up edits the whole file (the career and the retry
    /// career, see CareerSave.Careers) and returns it.</summary>
    public sealed class SaveMigration
    {
        public int From;
        public string What;
        public Func<JsonValue, JsonValue> Up;
    }

    /// <summary>The file was written by a newer build than this one: never load it (and never overwrite it by accident).</summary>
    public sealed class SaveTooNewException : FormatException
    {
        public readonly int FileVersion;
        public SaveTooNewException(int v) : base($"This save was made by a newer version of the game (save format {v}, this build reads up to {CareerSave.Version}).") { FileVersion = v; }
    }

    /// <summary>
    /// The career save format (roadmap step 35): versioned JSON with a migration chain, so a save from any older build loads in
    /// a newer one. CI loads every committed sample save from sim/GreenFlag.Sim.Tests/Saves (one or more per format version)
    /// and plays on from it. How to change the format: docs/saves.md.
    /// </summary>
    public static class CareerSave
    {
        public const string Format = "green-flag-deck-career";
        /// <summary>The format this build writes. Bump it whenever the JSON layout changes, and add a migration.</summary>
        public const int Version = 4;

        /// <summary>Version n to n + 1, in order.</summary>
        public static readonly IReadOnlyList<SaveMigration> Migrations = new SaveMigration[]
        {
            new SaveMigration
            {
                From = 1, What = "Step 37: the season (field, points, sponsor, contract, qualifying)",
                Up = root => { foreach (var c in Careers(root)) Career.MigrateV1(c); return root; },
            },
            new SaveMigration
            {
                From = 2, What = "Step 38: the driver sheet and off-track events",
                Up = root => { foreach (var c in Careers(root)) Career.MigrateV2(c); return root; },
            },
            new SaveMigration
            {
                From = 3, What = "Step 39: Crew HQ (the crew as people, candidates, rivals' offers)",
                Up = root => { foreach (var c in Careers(root)) Career.MigrateV3(c); return root; },
            },
        };

        static readonly CultureInfo inv = CultureInfo.InvariantCulture;

        public static string Write(SaveGame g)
        {
            if (g?.Career == null) throw new ArgumentException("Nothing to save");
            var c = g.Career;
            var root = JsonValue.Obj()
                .Set("format", Format).Set("v", Version).Set("build", g.Build ?? "dev")
                .Set("savedAt", g.SavedAt.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", inv))
                .Set("screen", g.Screen ?? SaveGame.Garage)
                .Set("summary", JsonValue.Obj().Set("name", c.Name).Set("persona", c.Persona).Set("series", c.Series.ToString()).Set("season", c.Season)
                    .Set("race", c.RaceIdx + 1).Set("of", c.SeasonLength).Set("track", c.NextTrack.Name).Set("money", c.Money).Set("races", c.Results.Count)
                    .Set("diff", c.Diff.ToString()).Set("stakes", c.Stakes.ToString()).Set("over", c.Over))
                .Set("career", c.ToJson())
                .Set("retry", g.Retry == null ? JsonValue.Null : JsonValue.Obj().Set("seed", (long)g.RetrySeed).Set("career", g.Retry.ToJson()));
            return root.Write();
        }

        /// <summary>Reads a save of any version up to this build's, migrating it first. Throws FormatException (or
        /// SaveTooNewException) when it can't.</summary>
        public static SaveGame Read(string text) => Read(text, Migrations, Version);

        /// <summary>Read with a given migration chain (tests use a made-up chain to check the machinery).</summary>
        public static SaveGame Read(string text, IReadOnlyList<SaveMigration> steps, int target)
        {
            var root = JsonValue.Parse(text);
            if (root.Kind != JsonKind.Object || root["format"].Str() != Format) throw new FormatException("Not a Green Flag Deck career save");
            int from = root["v"].Int();
            root = Migrate(root, steps, target);
            if (target != Version) return new SaveGame { LoadedFrom = from };
            var g = new SaveGame
            {
                LoadedFrom = from,
                Build = root["build"].Str(),
                Screen = root["screen"].Str(SaveGame.Garage),
                Career = Career.FromJson(root["career"]),
            };
            if (g.Screen != SaveGame.Garage && g.Screen != SaveGame.Reward && g.Screen != SaveGame.Over && g.Screen != SaveGame.Event) throw new FormatException($"Save file: unknown screen {g.Screen}");
            if (g.Screen == SaveGame.Reward && g.Career.Reward == null) g.Screen = SaveGame.Garage;
            if (g.Screen == SaveGame.Event && (g.Career.Event == null || g.Career.Event.Done)) g.Screen = SaveGame.Garage;
            if (DateTime.TryParse(root["savedAt"].Str(""), inv, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var at)) g.SavedAt = at;
            var r = root["retry"];
            if (!r.IsNull) { g.Retry = Career.FromJson(r["career"]); g.RetrySeed = r["seed"].UInt(); }
            return g;
        }

        /// <summary>Runs the chain from the file's version up to target.</summary>
        public static JsonValue Migrate(JsonValue root, IReadOnlyList<SaveMigration> steps, int target)
        {
            int v = root["v"].Int();
            if (v < 1) throw new FormatException($"Save file: bad format version {v}");
            if (v > target) throw new SaveTooNewException(v);
            while (v < target)
            {
                SaveMigration step = null;
                foreach (var s in steps) if (s.From == v) step = s;
                if (step == null) throw new FormatException($"No migration from save format {v}");
                root = step.Up(root) ?? throw new FormatException($"The migration from save format {v} returned nothing");
                v++;
                root.Set("v", v);
            }
            return root;
        }

        /// <summary>For migrations: the career objects in a file (the career, and the retry career when there is one).</summary>
        public static IEnumerable<JsonValue> Careers(JsonValue root)
        {
            yield return root["career"];
            var r = root["retry"];
            if (!r.IsNull) yield return r["career"];
        }
    }
}
