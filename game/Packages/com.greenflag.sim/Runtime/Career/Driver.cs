using System;
using System.Collections.Generic;
using System.Linq;

namespace GreenFlag.Sim
{
    /// <summary>The six driver attributes (the slice's DATTR), scored 8 to 20.</summary>
    public enum DAttr { Presence, Wits, Composure, Savvy, Rapport, Grit }

    /// <summary>The eleven off-track skills (the slice's DSKILL). Each one rolls with one attribute's modifier.</summary>
    public enum DSkill { Media, Pitch, Read, Deflect, Cool, Negotiate, Contract, Crew, Peace, Endure, Fans }

    /// <summary>How a d20 check came out, best first: natural 20, made it, missed by 1 to 4, missed by 5 or more, natural 1.</summary>
    public enum CheckTier { Crit, Success, Mixed, Fail, Fumble }

    /// <summary>One die, or two dice keeping the higher (advantage) or the lower (disadvantage).</summary>
    public enum RollMode { Flat, Adv, Dis }

    /// <summary>One labeled number on a check: what it adds to the die (or to the DC) and where it comes from.
    /// K is the source kind: attr, prof, image, trait, sit (the situation) or hype.</summary>
    public sealed class ModLine
    {
        public int V;
        public string Src, K;
        public ModLine() { }
        public ModLine(int v, string src, string k = "sit") { V = v; Src = src; K = k; }
    }

    /// <summary>The race an off-track event follows (the slice's ev.ctx): traits, flaws and the event's own situation read it.</summary>
    public sealed class CheckCtx
    {
        public string Ev, Track, TrackId, Rival;
        public int Fin, Start, Exp, Dmg;
        public bool Dnf, Top3, Big, BeatRival;
        public CheckCtx Clone() => (CheckCtx)MemberwiseClone();
    }

    /// <summary>What a background's trait or flaw does to one check: a number on the die, or a reason for advantage or disadvantage.</summary>
    public sealed class TraitFx
    {
        public int V;
        public string Src, Adv, Dis;
    }

    public sealed class DriverTrait
    {
        public string Name, Text;
        /// <summary>The trait's effect on a check with this skill after this race (ctx is null on the sheet), or null.</summary>
        public Func<DSkill, CheckCtx, TraitFx> Fx;
    }

    /// <summary>A driver background (the slice's BACKGROUNDS): a starting spread, two proficient skills, a trait and a flaw.</summary>
    public sealed class Background
    {
        public string Id, Name, Blurb;
        /// <summary>Presence, Wits, Composure, Savvy, Rapport, Grit.</summary>
        public int[] Attr;
        public DSkill[] Prof;
        public int Fans = 35;
        public DriverTrait Trait, Flaw;
    }

    /// <summary>An image on the map (the slice's IMAGES): x is Humble (-) to Cocky (+), y is Professional (-) to Showman (+).</summary>
    public sealed class DriverImage
    {
        public string Id, Name, Color, Text;
        public int X, Y;
        public DSkill[] Fav;
    }

    /// <summary>One check on the driver sheet's "Recent checks".</summary>
    public sealed class CheckLog
    {
        public string Ev, Opt, Txt;
        public DSkill Skill;
        public CheckTier Tier;
        public int Season, Race;
        public CheckLog Clone() => (CheckLog)MemberwiseClone();
    }

    /// <summary>
    /// Your driver off track (the slice's G.drv, step 18): attributes, proficiencies, a background, level and XP, Hype earned
    /// on track, fans and garage respect, the image map and its trail, the last checks, and Momentum promised for the next
    /// green flag. Grit and Wits also reach the race (Career.Loadout copies them in).
    /// </summary>
    public sealed class Driver
    {
        public string Bg = "hero";
        /// <summary>Scores by DAttr.</summary>
        public int[] Attr = new int[6];
        public List<DSkill> Prof = new List<DSkill>();
        public int Lvl = 1, Xp, Pts, Hype, Fans = 35, Respect = 35;
        public int ImgX, ImgY;
        /// <summary>The last 12 spots on the image map, oldest first (x, y).</summary>
        public List<int[]> Trail = new List<int[]>();
        /// <summary>The last 8 checks, newest first.</summary>
        public List<CheckLog> Log = new List<CheckLog>();
        /// <summary>Momentum at the next race's green flag (an answer promised it), at most 3.</summary>
        public int NextMom;

        public int Score(DAttr a) => Attr[(int)a];
        public int ModOf(DAttr a) => DriverRules.Mod(Score(a));
        public Background Background => DriverRules.Bg(Bg);
        /// <summary>Your image's id, or null while you're a fresh face.</summary>
        public string Image => DriverRules.ImageOf(ImgX, ImgY);
        public bool LeansIn => Math.Abs(ImgX) >= DriverRules.ImgLean && Math.Abs(ImgY) >= DriverRules.ImgLean;
        public int XpNeed => DriverRules.XpNeed(Lvl);

        public static Driver New(string bg)
        {
            var B = DriverRules.Bg(bg);
            return new Driver { Bg = B.Id, Attr = (int[])B.Attr.Clone(), Prof = B.Prof.ToList(), Fans = B.Fans };
        }

        /// <summary>Adds XP and returns the levels reached (each level gives one attribute point).</summary>
        public List<int> AddXp(int n)
        {
            var up = new List<int>();
            Xp += n;
            while (Xp >= DriverRules.XpNeed(Lvl)) { Xp -= DriverRules.XpNeed(Lvl); Lvl++; Pts++; up.Add(Lvl); }
            return up;
        }

        /// <summary>Spends a level-up point on an attribute. Returns why not, or null.</summary>
        public string RaiseAttr(DAttr a)
        {
            if (Pts <= 0) return "No points to spend";
            if (Score(a) >= DriverRules.AttrMax) return "Maxed at 20";
            Pts--; Attr[(int)a]++;
            return null;
        }

        /// <summary>An answer's tone moves the image one step per axis (and remembers the spot on the trail).</summary>
        public void MoveImage(int tx, int ty)
        {
            if (tx == 0 && ty == 0) return;
            ImgX = Math.Max(-DriverRules.ImgMax, Math.Min(DriverRules.ImgMax, ImgX + Math.Sign(tx)));
            ImgY = Math.Max(-DriverRules.ImgMax, Math.Min(DriverRules.ImgMax, ImgY + Math.Sign(ty)));
            Trail.Add(new[] { ImgX, ImgY });
            if (Trail.Count > 12) Trail.RemoveAt(0);
        }

        /// <summary>The skill's total bonus on the sheet: attribute modifier, proficiency, and any flat trait or flaw.</summary>
        public int SkillTotal(DSkill s)
        {
            int v = ModOf(DriverRules.AttrOf(s)) + (Prof.Contains(s) ? DriverRules.Prof : 0);
            var B = Background;
            foreach (var t in new[] { B.Trait, B.Flaw }) { var f = t.Fx(s, null); if (f != null) v += f.V; }
            return v;
        }

        public Driver Clone()
        {
            var d = (Driver)MemberwiseClone();
            d.Attr = (int[])Attr.Clone();
            d.Prof = new List<DSkill>(Prof);
            d.Trail = Trail.Select(p => (int[])p.Clone()).ToList();
            d.Log = Log.Select(l => l.Clone()).ToList();
            return d;
        }
    }

    /// <summary>A check with every number on it, before the roll (the slice's chkBuild).</summary>
    public sealed class CheckBuild
    {
        public DSkill Skill;
        /// <summary>What adds to the die, attribute first. Mod is their sum.</summary>
        public List<ModLine> L = new List<ModLine>();
        /// <summary>What moves the DC from the answer's base DC.</summary>
        public List<ModLine> Dcl = new List<ModLine>();
        public List<string> Adv = new List<string>(), Dis = new List<string>();
        public int BaseDc, Mod, Dc;
        public RollMode Mode;
        /// <summary>Chance of each CheckTier, in tier order; they add up to 1.</summary>
        public double[] Odds;
        /// <summary>The die you need for a success (2 to 20; a 1 never makes it, a 20 always does).</summary>
        public int Need;
        public double Pass => Odds[(int)CheckTier.Crit] + Odds[(int)CheckTier.Success];
        public double GetsIt => Pass + Odds[(int)CheckTier.Mixed];
    }

    /// <summary>A rolled check. Two dice are always rolled: with one die the other only shows what advantage would have done.</summary>
    public sealed class CheckRoll
    {
        public int A, B, Kept, Other, Total;
        public CheckTier Tier;
        public bool Made => Tier == CheckTier.Crit || Tier == CheckTier.Success;
    }

    /// <summary>
    /// The driver sheet's rules (roadmap step 38, ported from slice v41's driver-js): attributes and skills, the five
    /// backgrounds, the image map, XP, and the d20 check. A check is d20 + attribute modifier + proficiency (if proficient)
    /// + labeled extras against a DC. Natural 20 is a critical, natural 1 a fumble; made it is a success, missed by 1 to 4 is
    /// mixed (you get it, with a cost), 5 or more is a failure. Advantage and disadvantage roll two dice and keep the higher
    /// or lower; both at once cancel. Every number shows its source before the roll, with the exact five-tier odds.
    /// </summary>
    public static class DriverRules
    {
        public const int Prof = 2, AttrMin = 8, AttrMax = 20, ImgAt = 2, ImgLean = 5, ImgMax = 10, HypeMax = 3, MomMax = 3;
        public const int XpRace = 25, XpMade = 30, XpMissed = 20;
        public static int XpNeed(int lvl) => 100 * lvl;
        /// <summary>The attribute modifier: (score - 10) / 2, rounded down.</summary>
        public static int Mod(int score) => (int)Math.Floor((score - 10) / 2.0);
        public static string Sgn(int v) => (v < 0 ? "−" : "+") + Math.Abs(v);

        public static readonly string[] AttrNames = { "Presence", "Wits", "Composure", "Savvy", "Rapport", "Grit" };
        public static readonly string[] AttrAbbr = { "PRE", "WIT", "COM", "SAV", "RAP", "GRT" };
        public static readonly string[] AttrText = { "Camera charm, star power", "Thinking on your feet", "Handling pressure and criticism", "Business sense", "Relationships with people", "Fitness and work ethic" };
        public static readonly string[] SkillNames = { "Media", "Sponsor Pitch", "Read the Room", "Deflect", "Keep Cool", "Negotiate", "Read Contract", "Lead the Crew", "Make Peace", "Endurance", "Fan Work" };
        static readonly DAttr[] skillAttr = { DAttr.Presence, DAttr.Presence, DAttr.Wits, DAttr.Wits, DAttr.Composure, DAttr.Savvy, DAttr.Savvy, DAttr.Rapport, DAttr.Rapport, DAttr.Grit, DAttr.Grit };
        public static DAttr AttrOf(DSkill s) => skillAttr[(int)s];
        public static string Name(DSkill s) => SkillNames[(int)s];
        public static string Name(DAttr a) => AttrNames[(int)a];
        public static IEnumerable<DSkill> SkillsOf(DAttr a) => Enum.GetValues(typeof(DSkill)).Cast<DSkill>().Where(s => AttrOf(s) == a);
        public static readonly string[] TierNames = { "Critical", "Success", "Mixed", "Failure", "Fumble" };
        public static string Name(CheckTier t) => TierNames[(int)t];

        static bool ShortTrack(CheckCtx c) => c != null && (c.TrackId == "mill" || c.TrackId == "pine");
        static DriverTrait T(string n, string txt, Func<DSkill, CheckCtx, TraitFx> fx) => new DriverTrait { Name = n, Text = txt, Fx = fx };

        public static readonly IReadOnlyList<Background> Backgrounds = new[]
        {
            new Background { Id = "hero", Name = "Short-Track Hero", Blurb = "Grew up winning Saturday nights at the local bullring.", Attr = new[] { 14, 12, 9, 10, 13, 15 }, Prof = new[] { DSkill.Media, DSkill.Endure },
                Trait = T("Hometown Favorite", "Advantage on Media checks after a short-track race (Millbrook, Pinewood).", (s, c) => s == DSkill.Media && ShortTrack(c) ? new TraitFx { Adv = "Hometown Favorite: short-track crowd" } : null),
                Flaw = T("Short Fuse", "Disadvantage on Keep Cool after a DNF or a race you finished with heavy damage.", (s, c) => s == DSkill.Cool && c != null && (c.Dnf || c.Dmg >= 40) ? new TraitFx { Dis = "Short Fuse: still hot from the race" } : null) },
            new Background { Id = "rich", Name = "Rich Kid", Blurb = "Family money bought the first car. Now you have to prove it.", Attr = new[] { 15, 13, 12, 14, 10, 9 }, Prof = new[] { DSkill.Pitch, DSkill.Negotiate }, Fans = 20,
                Trait = T("Old Money", "+2 on Sponsor Pitch. You know how these dinners go.", (s, c) => s == DSkill.Pitch ? new TraitFx { V = 2, Src = "Old Money (trait)" } : null),
                Flaw = T("Silver Spoon", "−1 on Media and Fan Work. Fans don't buy the story. Starts with fewer fans.", (s, c) => s == DSkill.Media || s == DSkill.Fans ? new TraitFx { V = -1, Src = "Silver Spoon (flaw)" } : null) },
            new Background { Id = "dirt", Name = "Dirt Grinder", Blurb = "Ran dirt ovals in a truck you fixed yourself.", Attr = new[] { 10, 12, 14, 9, 13, 15 }, Prof = new[] { DSkill.Endure, DSkill.Fans },
                Trait = T("Seen Worse", "+2 on Keep Cool after a DNF.", (s, c) => s == DSkill.Cool && c != null && c.Dnf ? new TraitFx { V = 2, Src = "Seen Worse (trait)" } : null),
                Flaw = T("Rough Edges", "−1 on Sponsor Pitch and Negotiate.", (s, c) => s == DSkill.Pitch || s == DSkill.Negotiate ? new TraitFx { V = -1, Src = "Rough Edges (flaw)" } : null) },
            new Background { Id = "crew", Name = "Former Crew Member", Blurb = "Changed tires for six seasons before anyone let you drive.", Attr = new[] { 9, 14, 13, 10, 15, 12 }, Prof = new[] { DSkill.Crew, DSkill.Read },
                Trait = T("Shop Talk", "Advantage on Lead the Crew checks.", (s, c) => s == DSkill.Crew ? new TraitFx { Adv = "Shop Talk: you speak their language" } : null),
                Flaw = T("Camera Shy", "−1 on Media.", (s, c) => s == DSkill.Media ? new TraitFx { V = -1, Src = "Camera Shy (flaw)" } : null) },
            new Background { Id = "sim", Name = "Sim Racer", Blurb = "Ten thousand online races, three real ones.", Attr = new[] { 9, 15, 13, 14, 10, 12 }, Prof = new[] { DSkill.Read, DSkill.Deflect },
                Trait = T("Data Brain", "+1 on Read the Room and Read Contract.", (s, c) => s == DSkill.Read || s == DSkill.Contract ? new TraitFx { V = 1, Src = "Data Brain (trait)" } : null),
                Flaw = T("Keyboard Warrior", "Disadvantage on Keep Cool at press scrums.", (s, c) => s == DSkill.Cool && c != null && c.Ev == "press" ? new TraitFx { Dis = "Keyboard Warrior: the comments get to you" } : null) },
        };

        public static Background Bg(string id)
        {
            foreach (var b in Backgrounds) if (b.Id == id) return b;
            throw new KeyNotFoundException("No background " + id);
        }

        public static readonly IReadOnlyList<DriverImage> Images = new[]
        {
            new DriverImage { Id = "rookie", Name = "Cocky Rookie", X = 1, Y = 1, Fav = new[] { DSkill.Media, DSkill.Deflect }, Color = "#ff6b5e", Text = "Big fan swings. Youth brands love you; veterans don't." },
            new DriverImage { Id = "champ", Name = "Humble Champion", X = -1, Y = -1, Fav = new[] { DSkill.Cool, DSkill.Crew }, Color = "#2fb3a0", Text = "Blue-chip sponsors and top teams trust you." },
            new DriverImage { Id = "fan", Name = "Fan Favorite", X = -1, Y = 1, Fav = new[] { DSkill.Fans, DSkill.Pitch }, Color = "#f2c94c", Text = "Fans and grassroots sponsors. Media goes easy on you." },
            new DriverImage { Id = "assassin", Name = "Cold Assassin", X = 1, Y = -1, Fav = new[] { DSkill.Read, DSkill.Negotiate }, Color = "#a99bff", Text = "Media is cold, the garage respects you, contracts are pure business." },
        };

        public static DriverImage Image(string id)
        {
            foreach (var i in Images) if (i.Id == id) return i;
            throw new KeyNotFoundException("No image " + id);
        }

        /// <summary>An image needs 2 or more on both axes; the quadrant names it.</summary>
        public static string ImageOf(int x, int y)
        {
            if (Math.Abs(x) < ImgAt || Math.Abs(y) < ImgAt) return null;
            foreach (var i in Images) if (i.X == Math.Sign(x) && i.Y == Math.Sign(y)) return i.Id;
            return null;
        }

        /// <summary>An answer is in character when its tone doesn't pull against the image on either axis.</summary>
        public static bool InChar(int tx, int ty, string image)
        {
            if (image == null || (tx == 0 && ty == 0)) return false;
            var I = Image(image);
            return (tx == 0 || Math.Sign(tx) == I.X) && (ty == 0 || Math.Sign(ty) == I.Y);
        }

        public static string ToneName(int tx, int ty)
        {
            var o = new List<string>();
            if (tx != 0) o.Add(tx < 0 ? "Humble" : "Cocky");
            if (ty != 0) o.Add(ty < 0 ? "Professional" : "Showman");
            return string.Join(" · ", o);
        }

        /// <summary>The persona's default background (the slice's BG_FOR).</summary>
        public static string BgFor(string persona) => Personas.Get(persona).Background ?? "hero";

        // ------------------------------------------------------------------ the check

        public static CheckTier Tier(int die, int mod, int dc)
        {
            if (die == 20) return CheckTier.Crit;
            if (die == 1) return CheckTier.Fumble;
            int t = die + mod;
            return t >= dc ? CheckTier.Success : t >= dc - 4 ? CheckTier.Mixed : CheckTier.Fail;
        }

        /// <summary>The exact chance of each tier: each face's chance of being the die you keep, times its tier.</summary>
        public static double[] Odds(int mod, int dc, RollMode mode)
        {
            var o = new double[5];
            for (int k = 1; k <= 20; k++)
            {
                double p = mode == RollMode.Adv ? (k * k - (k - 1) * (k - 1)) / 400.0 : mode == RollMode.Dis ? ((21 - k) * (21 - k) - (20 - k) * (20 - k)) / 400.0 : 1 / 20.0;
                o[(int)Tier(k, mod, dc)] += p;
            }
            return o;
        }

        /// <summary>Two dice from the check's own generator; the mode picks which one counts.</summary>
        public static CheckRoll Roll(CheckBuild b, SimRng rng)
        {
            int a = D20(rng), z = D20(rng);
            int kept = b.Mode == RollMode.Adv ? Math.Max(a, z) : b.Mode == RollMode.Dis ? Math.Min(a, z) : a;
            return new CheckRoll { A = a, B = z, Kept = kept, Other = kept == a ? z : a, Total = kept + b.Mod, Tier = Tier(kept, b.Mod, b.Dc) };
        }

        public static int D20(SimRng rng) => 1 + (int)Math.Floor(rng.NextDouble() * 20);

        /// <summary>Sums the lines into Mod and Dc, sets the mode (advantage and disadvantage cancel), the odds and the roll needed.</summary>
        public static CheckBuild Finish(CheckBuild b)
        {
            b.Mod = b.L.Sum(l => l.V);
            b.Dc = b.BaseDc + b.Dcl.Sum(l => l.V);
            b.Mode = b.Adv.Count > 0 && b.Dis.Count == 0 ? RollMode.Adv : b.Dis.Count > 0 && b.Adv.Count == 0 ? RollMode.Dis : RollMode.Flat;
            b.Odds = Odds(b.Mod, b.Dc, b.Mode);
            b.Need = Math.Max(2, Math.Min(20, b.Dc - b.Mod));
            return b;
        }

        /// <summary>The lines every check starts with: the attribute, proficiency, the image's in-character bonus and the
        /// signature lean, and the background's trait and flaw.</summary>
        public static CheckBuild Start(Driver d, DSkill skill, int dc, CheckCtx ctx, int tx = 0, int ty = 0, string sig = null)
        {
            var a = AttrOf(skill);
            var b = new CheckBuild { Skill = skill, BaseDc = dc };
            b.L.Add(new ModLine(Mod(d.Score(a)), $"{Name(a)} {d.Score(a)}", "attr"));
            if (d.Prof.Contains(skill)) b.L.Add(new ModLine(Prof, "Proficiency", "prof"));
            var im = d.Image;
            if (im != null && Image(im).Fav.Contains(skill) && InChar(tx, ty, im)) b.L.Add(new ModLine(1, $"{Image(im).Name}, in character", "image"));
            if (sig != null && im == sig && d.LeansIn) b.L.Add(new ModLine(1, "Leaning all the way in", "image"));
            var B = d.Background;
            foreach (var t in new[] { B.Trait, B.Flaw })
            {
                var f = t.Fx(skill, ctx);
                if (f == null) continue;
                if (f.V != 0) b.L.Add(new ModLine(f.V, f.Src, "trait"));
                if (f.Adv != null) b.Adv.Add(f.Adv);
                if (f.Dis != null) b.Dis.Add(f.Dis);
            }
            return b;
        }

        /// <summary>"What would have helped" after a roll (the slice's chkHelp): the attribute or proficiency that would have
        /// turned it, answering in character, advantage (your other die), the other answers on the same die, and the best
        /// bet this round. others are the round's other answers' builds, by name.</summary>
        public static List<string> Help(Driver d, CheckBuild b, CheckRoll R, string ownName, DSkill skill, IReadOnlyList<(string name, CheckBuild build)> others, int tx = 0, int ty = 0)
        {
            var A = AttrOf(skill); int sc = d.Score(A);
            var o = new List<string>();
            if (R.Tier == CheckTier.Crit) { o.Add("Natural 20. Nothing could beat that."); return o; }
            if (R.Tier == CheckTier.Success)
            {
                int m = R.Total - b.Dc;
                o.Add($"Made it by {m}. {(m >= 4 ? "Plenty of room." : $"One bad roll from a mixed result: every +1 on {Name(skill)} is +5% here.")}");
                if (b.Mode == RollMode.Flat && R.Other == 1) o.Add("Your second die was a 1. Rolling with disadvantage would have fumbled.");
                return o;
            }
            string hypeTip = d.Hype > 0 ? "You have Hype to spend on advantage." : "Earn Hype on track with a ×4 combo or a big finish.";
            if (R.Tier == CheckTier.Fumble)
            {
                o.Add("A natural 1 always fumbles, whatever the modifiers.");
                if (b.Mode != RollMode.Adv) o.Add($"With advantage you'd have kept your other die, a {R.Other}: {Name(Tier(R.Other, b.Mod, b.Dc)).ToLowerInvariant()}. {hypeTip}");
                return o;
            }
            int gap = b.Dc - R.Total;
            string goal = R.Tier == CheckTier.Mixed ? "a success" : "a mixed result";
            int gapM = R.Tier == CheckTier.Mixed ? gap : gap - 4;
            o.Add($"Rolled {R.Kept} {Sgn(b.Mod)} = {R.Total} vs DC {b.Dc}. Missed by {gap}.");
            int s = sc;
            while (s < AttrMax && Mod(s) < Mod(sc) + gapM) s++;
            if (Mod(s) >= Mod(sc) + gapM) o.Add($"{Name(A)} {sc} → {s} ({Sgn(Mod(s))}) would have made it {goal}.");
            if (!d.Prof.Contains(skill) && gapM <= Prof) o.Add($"Proficiency in {Name(skill)} (+{Prof}) would have made it {goal}.");
            var im = d.Image;
            var fav = Images.FirstOrDefault(i => i.Fav.Contains(skill));
            if (gapM <= 1 && fav != null && !(im == fav.Id && InChar(tx, ty, im))) o.Add($"Answering in character as a {fav.Name} (+1) would have made it {goal}.");
            if (b.Mode != RollMode.Adv && R.Other > R.Kept)
            {
                var t2 = Tier(R.Other, b.Mod, b.Dc);
                if (t2 < R.Tier) o.Add($"Advantage would have kept your other die, a {R.Other}: {Name(t2).ToLowerInvariant()}. {(d.Hype > 0 ? "Spend Hype for advantage." : "Earn Hype on track with a ×4 combo or a big finish.")}");
            }
            foreach (var (n, b2) in others)
            {
                var t2 = Tier(R.Kept, b2.Mod, b2.Dc);
                if (t2 == CheckTier.Success || t2 == CheckTier.Crit) o.Add($"{n} ({Name(b2.Skill)}, needed {b2.Need}+) would have passed on this roll.");
            }
            double mine = b.Pass;
            var best = others.Where(x => x.build.Pass > mine + 0.001).OrderByDescending(x => x.build.Pass).FirstOrDefault();
            if (best.build != null) o.Add($"Best odds this round: {best.name} ({Name(best.build.Skill)}) passed {Pct(best.build.Pass)} of the time ({best.build.Need}+), vs {Pct(mine)} for this answer.");
            else o.Add($"This was already your best answer here ({Pct(mine)} to pass). Long term, every +1 on {Name(skill)} adds 5%: raise {Name(A)}{(d.Prof.Contains(skill) ? "" : " or pick up the proficiency")}.");
            return o;
        }

        public static string Pct(double p) => p > 0 && p < 0.005 ? "<1%" : Math.Round(p * 100).ToString(System.Globalization.CultureInfo.InvariantCulture) + "%";

        /// <summary>Hype from a race (the slice's raceHype): a ×4 combo, and a big finish (top 3, or 5+ spots over expected).</summary>
        public static List<string> RaceHype(RaceReport r)
        {
            var why = new List<string>();
            if (r.BestChain >= 4) why.Add("×4 combo");
            if (!r.Dnf && (r.Finish <= 3 || r.Expected - r.Finish >= 5)) why.Add(r.Finish <= 3 ? $"P{r.Finish} finish" : $"{r.Expected - r.Finish} spots over expected");
            return why;
        }
    }
}
