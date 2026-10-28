using System;
using System.Collections.Generic;
using System.Globalization;

namespace GreenFlag.View
{
    /// <summary>A colour as 0-1 floats, so the engine can turn it into its own colour type without parsing.</summary>
    public readonly struct Rgb
    {
        public readonly float R, G, B, A;
        public Rgb(float r, float g, float b, float a = 1) { R = r; G = g; B = b; A = a; }

        /// <summary>"#rrggbb", with an optional alpha.</summary>
        public static Rgb Hex(string hex, float a = 1)
        {
            int n = int.Parse(hex.TrimStart('#'), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return new Rgb(((n >> 16) & 255) / 255f, ((n >> 8) & 255) / 255f, (n & 255) / 255f, a);
        }

        /// <summary>The slice's shade(): f below 0 darkens toward black, above 0 lightens toward white.</summary>
        public Rgb Shade(float f)
        {
            float m = f < 0 ? 0 : 1, k = Math.Abs(f);
            return new Rgb(R + (m - R) * k, G + (m - G) * k, B + (m - B) * k, A);
        }

        public Rgb WithAlpha(float a) => new Rgb(R, G, B, a);
        public override string ToString() => $"#{(int)Math.Round(R * 255):x2}{(int)Math.Round(G * 255):x2}{(int)Math.Round(B * 255):x2}";
    }

    /// <summary>A team's paint: body and stripe (the slice's TEAMS c and c2).</summary>
    public readonly struct Livery
    {
        public readonly Rgb Body, Stripe;
        public Livery(Rgb body, Rgb stripe) { Body = body; Stripe = stripe; }
    }

    /// <summary>
    /// One look for the race view: the palette plus how the frame is drawn. The step 11 look is still benched, so the build
    /// defaults to Clean and the other three exist as palettes and a pixel size only. Step 45 (retro art) fills in their models,
    /// dither and scanlines; nothing outside this file and the engine's look hook needs to change when the pick comes in.
    /// </summary>
    public sealed class LookDef
    {
        public string Key, Name, Blurb;
        public Rgb Sky, Grounds, Stand, Asphalt, Apron, Infield, Pit, LaneLine, Wall, Kerb;
        /// <summary>Render at 1/PixelSize of the screen and scale up with no filtering (the slice's `lo`). 1 = full resolution.</summary>
        public int PixelSize = 1;
        /// <summary>True only for looks that are fully built. The others draw with their palette and pixel size.</summary>
        public bool Finished;
    }

    public static class Looks
    {
        // PAL0 in the slice is the Clean look; the others override some of it
        public static readonly LookDef Clean = new LookDef
        {
            Key = "clean", Name = "Clean", Blurb = "Today's slice: smooth 2.5D, flat colours, the 90s dash.", Finished = true,
            Sky = Rgb.Hex("#15202b"), Grounds = Rgb.Hex("#1f3326"), Stand = Rgb.Hex("#2a3245"), Asphalt = Rgb.Hex("#3a4150"), Apron = Rgb.Hex("#4a5160"),
            Infield = Rgb.Hex("#2d5a3a"), Pit = Rgb.Hex("#3d4454"), LaneLine = new Rgb(1, 1, 1, 0.18f), Wall = Rgb.Hex("#d9dde6"), Kerb = Rgb.Hex("#e5484d"),
        };

        public static readonly LookDef Pixel = Derive(Clean, "pixel", "16-bit pixel", "SNES-era sprites: chunky pixels, a fixed palette, outlined cars.", 3,
            sky: "#1e4a22", grounds: "#3c8c3a", stand: "#5b4f86", asphalt: "#5a5b70", apron: "#9a9aa8", infield: "#4aa83e", pit: "#6c6d80", wall: "#f2f2f2", lane: 0.55f);

        public static readonly LookDef Poly = Derive(Clean, "poly", "32-bit low-poly", "PS1-era 3D: faceted track, flat-shaded cars, dithered colour.", 2,
            sky: "#24361f", grounds: "#4d7038", stand: "#4a4f60", asphalt: "#4a4c57", apron: "#6d6f79", infield: "#5b8240", pit: "#55575f", wall: "#c9cdd5", lane: 0.4f);

        public static readonly LookDef Tv = Derive(Clean, "tv", "90s TV broadcast", "Today's track as a 90s network feed: punchy colour, glossy graphics.", 1,
            sky: "#0e2a14", grounds: "#1d5a2a", stand: "#2a3a6a", asphalt: "#3c4256", apron: "#545b70", infield: "#2f8a3e", pit: "#454c62", wall: "#f4f6fa", lane: 0.18f);

        public static readonly IReadOnlyList<LookDef> All = new[] { Clean, Pixel, Poly, Tv };

        /// <summary>The look the build starts in. Change this one line when board Q37 is answered.</summary>
        public static LookDef Default => Clean;

        public static LookDef ByKey(string key)
        {
            foreach (var l in All) if (l.Key == key) return l;
            return Default;
        }

        public static LookDef Next(LookDef l)
        {
            for (int i = 0; i < All.Count; i++) if (All[i] == l) return All[(i + 1) % All.Count];
            return Default;
        }

        static LookDef Derive(LookDef b, string key, string name, string blurb, int px, string sky, string grounds, string stand, string asphalt, string apron, string infield, string pit, string wall, float lane)
            => new LookDef
            {
                Key = key, Name = name, Blurb = blurb, PixelSize = px, Kerb = b.Kerb,
                Sky = Rgb.Hex(sky), Grounds = Rgb.Hex(grounds), Stand = Rgb.Hex(stand), Asphalt = Rgb.Hex(asphalt), Apron = Rgb.Hex(apron),
                Infield = Rgb.Hex(infield), Pit = Rgb.Hex(pit), Wall = Rgb.Hex(wall), LaneLine = new Rgb(1, 1, 1, lane),
            };

        /// <summary>The ten team liveries in team order (the same colours in every series, as in the slice).</summary>
        public static readonly IReadOnlyList<Livery> Teams = new[]
        {
            L("#e0453a", "#ffffff"), L("#2f6fe0", "#f2c94c"), L("#f08a24", "#111827"), L("#2fb36a", "#ffffff"), L("#b87333", "#1b1b1b"),
            L("#29b6d6", "#0b2540"), L("#8b5cf6", "#f4f5f8"), L("#8b95a7", "#e5484d"), L("#c9a227", "#3a2a10"), L("#f4f5f8", "#14b8a6"),
        };

        public static readonly Livery PaceCar = L("#eef1f6", "#e9b52f");
        public static readonly Rgb Wrecked = Rgb.Hex("#555c6b");
        public static readonly Rgb You = Rgb.Hex("#e9b52f");

        public static Livery Team(int team) => Teams[((team % Teams.Count) + Teams.Count) % Teams.Count];

        static Livery L(string a, string b) => new Livery(Rgb.Hex(a), Rgb.Hex(b));
    }
}
