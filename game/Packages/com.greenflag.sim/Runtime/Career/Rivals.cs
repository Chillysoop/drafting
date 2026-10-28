using System;
using System.Collections.Generic;
using System.Linq;

namespace GreenFlag.Sim
{
    /// <summary>
    /// One named driver of "the class" (roadmap step 43): who they are, how they hold a Grudge toward you, and how their career
    /// moves. The Grudge numbers are the slice's (step 20 rivals-js): Mul scales every Grudge gain, Cool is how much it cools
    /// after a race, Pay how likely payback is once Hot, Block how hard they block you and cut your passing odds, Settle how
    /// much a payback clears. Proud drivers heat up when you beat them by 1 to 3 places; Press drivers make every reply
    /// public (fan swings double); RespectK makes clean racing near them earn Respect faster.
    /// Gen is where their career starts against yours: 0 = your generation (your starting series), 1 = one rung ahead,
    /// 2 = the veterans (two rungs ahead, never past stock cars). Skill (1 to 10) is the driver's racecraft, defense,
    /// consistency and tire care; it grows by Grow a season up to Peak. Cap is the highest series they reach. Retire is
    /// the season count they retire after (0 = never). Money: a family team always finds them a top ride.
    /// </summary>
    public sealed class RivalDef
    {
        public string Id, Name, Nick, Text, Color;
        public Trait Trait;
        public double Mul = 1, Cool = 1, Pay = 1, Block = 1, Settle = 3, RespectK = 1;
        public bool Proud, Press, Money;
        public int Gen, Number, Retire;
        public double Skill, Peak, Grow;
        public Series Cap = Series.Stock;
        public string Last => Name.Substring(Name.LastIndexOf(' ') + 1);
    }

    /// <summary>An incident between you and a class driver, kept for their card on the Rivals page.</summary>
    public sealed class RivalHist
    {
        public int Season, Race, Lap;
        /// <summary>wreck (you wrecked them), hit (they wrecked you), payback, pass (a hard pass you landed on them).</summary>
        public string Kind, Track;
        public RivalHist Clone() => (RivalHist)MemberwiseClone();
    }

    /// <summary>
    /// A class driver's career between races and seasons: series and team, skill, the Grudge toward you (−3 Respect to 10
    /// Feud), head to head (races you finished ahead of them and behind them), their record and the line each season wrote
    /// in their path. While they race in your series they also have a seat in the career's field (Entrant.Rival = Id).
    /// </summary>
    public sealed class ClassDriver
    {
        public string Id;
        public Series Series;
        public int Team;
        public double Skill, G;
        public int Ahead, Behind, Starts, Wins, Titles, Seasons, LastRank;
        public bool Retired;
        /// <summary>The season they last shared a track with you (0 = never yet).</summary>
        public int Met;
        public List<RivalHist> Hist = new List<RivalHist>();
        public List<string> Path = new List<string>();
        public RivalDef Def => RivalBook.Get(Id);
        public ClassDriver Clone() { var c = (ClassDriver)MemberwiseClone(); c.Hist = Hist.Select(h => h.Clone()).ToList(); c.Path = new List<string>(Path); return c; }
    }

    /// <summary>A message from a class driver in your garage inbox, and your reply once you've sent one.</summary>
    public sealed class RivalMsg
    {
        public string Id, Rid, Kind, Track, Subj, Body;
        public int Season, Race;
        public double G;
        /// <summary>The answer you sent (null = not answered), how it went and what it did.</summary>
        public string Reply, ReplyText;
        public CheckTier? Tier;
        public string Roll;
        public RivalMsg Clone() => (RivalMsg)MemberwiseClone();
    }

    /// <summary>A rival's Grudge before and after one race, and why it moved: the reward screen's rival rows.</summary>
    public sealed class RivalLine
    {
        public string Rid, Name;
        public double G0, G1;
        public bool Week;
        public int Fin;
        public List<(string why, double d)> Why = new List<(string, double)>();
    }

    /// <summary>One reply to a rival's message: what you say, the d20 check (skill and DC), its tone on the image map, and what
    /// a success (Win) and a miss (Lose) do. GWin and GLose move the Grudge.</summary>
    public sealed class RivalReply
    {
        public string Id, Name, Say, Hit, Miss;
        public DSkill Skill;
        public int Dc, TX, TY;
        public OffFx Win = new OffFx(), Lose = new OffFx();
        public double GWin, GLose;
    }

    /// <summary>The twelve drivers of the class, their Grudge rules (the slice's rivals-js) and their messages and replies.</summary>
    public static class RivalBook
    {
        public const double GMin = -3, GMax = 10;
        public const int InboxMax = 12;

        static RivalDef D(string id, string name, string nick, Trait t, int gen, double skill, double peak, double grow, int num, string color, string text) =>
            new RivalDef { Id = id, Name = name, Nick = nick, Trait = t, Gen = gen, Skill = skill, Peak = peak, Grow = grow, Number = num, Color = color, Text = text };

        public static readonly IReadOnlyList<RivalDef> All = Build();

        static List<RivalDef> Build()
        {
            var o = new List<RivalDef>();
            // your generation: they start where you start
            var r = D("prodigy", "Nina Valdez", "The Prodigy", Trait.Smooth, 0, 6, 9, 1, 7, "#5ad1ff", "Quickest kid at every track on the calendar, and everybody already knows it. Climbs fast. Hates losing a close one, but forgets a hit quickly.");
            r.Mul = 0.8; r.Cool = 1.5; r.Pay = 0.6; r.Settle = 3; r.Proud = true; o.Add(r);
            r = D("hothead", "Jet Alvarez", "The Hothead", Trait.Aggressive, 0, 5, 8, 0.75, 51, "#ff8a3d", "Short fuse, fast hands. Gets hot fast and cools fast. Hard to talk down while it is hot, and takes a close loss personally.");
            r.Mul = 1.6; r.Cool = 1.5; r.Pay = 1.2; r.Block = 0.8; r.Settle = 3; r.Proud = true; o.Add(r);
            r = D("talker", "Skye Rennick", "The Media Darling", Trait.Drafter, 0, 5, 8, 0.75, 88, "#f2c94c", "Fights on camera more than on track. Every message goes public, so the fan swings on your replies are doubled. Hates losing a close finish.");
            r.Pay = 0.7; r.Settle = 3; r.Press = true; r.Proud = true; o.Add(r);
            r = D("heir", "Brock Hollister", "The Heir", Trait.Rookie, 0, 3, 6, 0.75, 10, "#d4a5ff", "Family money buys the best ride in every series, whether the results earn it or not. Wild in traffic, proud, and never short of a seat.");
            r.Mul = 1.2; r.Settle = 3; r.Proud = true; r.Money = true; o.Add(r);
            r = D("grinder", "Tommy Kessler", "The Grinder", Trait.Veteran, 0, 4, 7, 0.5, 42, "#7fd17f", "Not the quickest, never the one who throws it away. Races you the way you race them, and clean racing earns Respect twice as fast.");
            r.Mul = 0.7; r.Cool = 2; r.Pay = 0.6; r.Block = 1.1; r.Settle = 4; r.RespectK = 2; o.Add(r);
            // one rung ahead of you
            r = D("enforcer", "Cole Mercer", "The Enforcer", Trait.Restarter, 1, 6, 7, 0.5, 3, "#e5484d", "Every hit gets paid back, and then it is even. Payback is likely once Mercer is Hot, and it clears most of the Grudge.");
            r.Pay = 1.5; r.Block = 1.2; r.Settle = 5; o.Add(r);
            r = D("grudge", "Vance Pruitt", "The Grudge", Trait.Closer, 1, 6, 7, 0.5, 13, "#8b95a7", "Keeps a list. The Grudge barely cools, even seasons apart, payback barely settles it, apologies are a hard check, and every close finish you win goes on the list.");
            r.Mul = 1.1; r.Cool = 0.25; r.Settle = 1; r.Proud = true; o.Add(r);
            r = D("local", "Dakota Lane", "The Local Hero", Trait.Closer, 1, 5, 7, 0.5, 24, "#ff6fa8", "Grandstand favorite who saves it all for the last ten laps. The fans hear every word, so fan swings on your replies are doubled.");
            r.Pay = 0.9; r.Settle = 3; r.Press = true; o.Add(r);
            r = D("journey", "Rusty Calhoun", "The Journeyman", Trait.Veteran, 1, 5, 6, 0.25, 57, "#c08a5a", "A new team every winter and never quite the ride to move up. Blocks like the seat depends on it, because it does.");
            r.Mul = 0.8; r.Cool = 1.5; r.Pay = 0.8; r.Block = 1.3; r.Settle = 3; r.Cap = Series.Late; o.Add(r);
            // the veterans, waiting at the top
            r = D("pro", "Hank Doyle", "The Old Pro", Trait.Veteran, 2, 8, 8, -0.25, 6, "#2fb3a0", "Twenty years in the top series. Races you the way you race him and forgets fast. Clean racing near Doyle earns Respect twice as fast. Retires after six more seasons.");
            r.Mul = 0.6; r.Cool = 2; r.Pay = 0.5; r.Block = 0.8; r.Settle = 4; r.RespectK = 2; r.Retire = 6; o.Add(r);
            r = D("wall", "Marty Krause", "The Wall", Trait.Wall, 2, 7, 7, 0, 31, "#a98bff", "Rarely wrecks anyone. Never lets you by: the hotter the Grudge, the harder Krause blocks and the worse your passing odds.");
            r.Mul = 0.9; r.Pay = 0.6; r.Block = 2; r.Settle = 2; o.Add(r);
            r = D("ice", "Mika Sorensen", "The Ice", Trait.Smooth, 2, 8, 8, 0, 9, "#9ad8e8", "The champion everybody chases. Barely reacts to anything, on track or off, and blocks you a little harder every time you make it personal.");
            r.Mul = 0.5; r.Cool = 2; r.Pay = 0.4; r.Block = 1.2; r.Settle = 2; o.Add(r);
            return o;
        }

        public static RivalDef Get(string id)
        {
            foreach (var d in All) if (d.Id == id) return d;
            throw new KeyNotFoundException("No class driver " + id);
        }

        public static bool Has(string id) => All.Any(d => d.Id == id);

        /// <summary>The Grudge tiers: Respect (−2 or less), Cool, Warm (3+), Hot (6+), Feud (9+).</summary>
        public static string Tier(double g) => g <= -2 ? "Respect" : g < 3 ? "Cool" : g < 6 ? "Warm" : g < 9 ? "Hot" : "Feud";
        public static string TierColor(double g) => g <= -2 ? "#2fb36a" : g < 3 ? "#98a2b8" : g < 6 ? "#e9b52f" : g < 9 ? "#ff8a3d" : "#e5484d";
        public static double Q(double g) => Math.Round(SimMath.Clamp(g, GMin, GMax) * 4) / 4;
        public static string Num(double g) => g.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

        // ------------------------------------------------------------------ on track (the slice's numbers)

        /// <summary>Chance a Hot rival on your bumper turns an attack into payback.</summary>
        public static double PaybackChance(RivalDef d, double g) => d == null || g < 6 ? 0 : Math.Min(0.6, 0.12 * (g - 5) * d.Pay);
        /// <summary>The extra read a rival gets when attacking you (Warm and up), or the room they give you (Respect).</summary>
        public static double Lean(RivalDef d, double g) => d == null ? 0 : g >= 3 ? Math.Round(g * 1.5 * d.Pay) : g <= -2 ? -8 : 0;
        /// <summary>How much more often they block you.</summary>
        public static double BlockK(RivalDef d, double g) => d == null ? 1 : g <= -2 ? 0.5 : 1 + Math.Max(0, g) * 0.25 * d.Block;

        // ------------------------------------------------------------------ messages

        static readonly Dictionary<string, Dictionary<string, (string subj, string body)>> Msg = new Dictionary<string, Dictionary<string, (string, string)>>
        {
            ["wrecked"] = new Dictionary<string, (string, string)>
            {
                ["pro"] = ("Lap {lap}", "\"Twenty years I've done this. That was not racing. I'll be watching for you.\""),
                ["enforcer"] = ("You owe me", "\"You know how this works. You put me in the fence at {track}, I put you in it. Then we're square.\""),
                ["talker"] = ("Posted to 40k followers", "\"Watch the replay, everybody. {me} has no business in this series. #wrecked\""),
                ["hothead"] = ("YOU", "\"YOU DUMPED ME. Next week. Your bumper. Mine.\""),
                ["wall"] = ("Noted", "\"Fine. You won't get by me again this year.\""),
                ["grudge"] = ("I keep a list", "\"Your name is on it now. In pen.\""),
                ["prodigy"] = ("Cute", "\"I'll be gone to the next series before you learn to brake. Stay out of my way till then.\""),
                ["heir"] = ("My dad saw that", "\"Do you know what that car cost? My family's lawyers will be in touch.\""),
                ["local"] = ("Ask the grandstand", "\"Thirty thousand people saw you do that. They'll let you know next week.\""),
                ["_"] = ("Lap {lap}", "\"You wrecked me at {track}. I won't forget it.\""),
            },
            ["payback"] = new Dictionary<string, (string, string)>
            {
                ["pro"] = ("That's the one", "\"That's the only tap you get from me. Clean from here, if you are.\""),
                ["enforcer"] = ("Square", "\"Now we're even. Race me clean and I race you clean.\""),
                ["talker"] = ("Karma clip", "\"Funny how karma works. Clip's already up.\""),
                ["hothead"] = ("How'd that feel", "\"How'd that feel? Bet you think twice next time.\""),
                ["wall"] = ("Didn't want to", "\"You left me no choice. Don't make me do it again.\""),
                ["grudge"] = ("One down", "\"That's one. The list is long.\""),
                ["_"] = ("Even", "\"That's for {track}. We're even now.\""),
            },
            ["hit"] = new Dictionary<string, (string, string)>
            {
                ["pro"] = ("My fault", "\"Car got loose under me at {track}. That one's on me.\""),
                ["grinder"] = ("On me", "\"Got in too deep at {track}. That's on me. Won't happen again.\""),
                ["_"] = ("Rubbin's racing", "\"Rubbin's racing. Don't take it personal.\""),
            },
            ["respect"] = new Dictionary<string, (string, string)>
            {
                ["pro"] = ("Good racing", "\"You gave me room when it counted at {track}. I won't forget it.\""),
                ["_"] = ("Good racing", "\"Hard and clean all day at {track}. I'll give you the same.\""),
            },
            ["gloat"] = new Dictionary<string, (string, string)>
            {
                ["talker"] = ("P{them} and smiling", "\"P{them}, ahead of {me} again. Some of us just have it.\""),
                ["hothead"] = ("Mirror", "\"Saw you in my mirror all day. Get used to the view.\""),
                ["prodigy"] = ("Again", "\"Ahead of you again. Starting to look like a pattern.\""),
                ["heir"] = ("Better car", "\"Some of us have the budget. Don't feel bad.\""),
                ["_"] = ("Good run", "\"Good run. Not good enough.\""),
            },
            ["beat"] = new Dictionary<string, (string, string)>
            {
                ["grudge"] = ("Enjoy it", "\"Enjoy it while it lasts.\""),
                ["hothead"] = ("Lucky", "\"Lucky day. Won't happen twice.\""),
                ["_"] = ("Nice run", "\"Nice run. See you next week.\""),
            },
            // the class arcs (step 43): an old face arrives in your series, or signs as your teammate
            ["arrive"] = new Dictionary<string, (string, string)>
            {
                ["prodigy"] = ("Told you", "\"Told you I'd get here. Now we find out who's actually quick.\""),
                ["hothead"] = ("Remember me?", "\"Remember {track}? I do. New series, same bumper.\""),
                ["grudge"] = ("Still on the list", "\"Different series. Same list. Your name's still on it.\""),
                ["heir"] = ("Moving up", "\"Dad bought a team up here. See you on track.\""),
                ["_"] = ("Moving up", "\"Made it up here too. Same as the old days: may the best one win.\""),
            },
            ["mate"] = new Dictionary<string, (string, string)>
            {
                ["grudge"] = ("Teammates", "\"Same shop now. Don't think that crosses you off the list.\""),
                ["hothead"] = ("Teammates?!", "\"Teammates. Great. Stay off my bumper and we'll get along.\""),
                ["prodigy"] = ("Same team", "\"Same team. Same data. No excuses for either of us now.\""),
                ["_"] = ("Same team", "\"Looks like we share a shop next season. Let's make it work.\""),
            },
        };

        public static (string subj, string body) Message(string kind, string id)
        {
            var m = Msg[kind];
            return m.TryGetValue(id, out var x) ? x : m["_"];
        }

        public static readonly string[] Kinds = { "wrecked", "payback", "hit", "respect", "gloat", "beat", "arrive", "mate" };

        static OffFx F(int fans = 0, int respect = 0, int hype = 0, int money = 0) => new OffFx { Fans = fans, Respect = respect, Hype = hype, Money = money };

        static RivalReply O(string id, string name, string say, DSkill sk, int dc, int tx, int ty, OffFx win, double gw, OffFx lose, double gl, string hit, string miss) =>
            new RivalReply { Id = id, Name = name, Say = say, Skill = sk, Dc = dc, TX = tx, TY = ty, Win = win, GWin = gw, Lose = lose, GLose = gl, Hit = hit, Miss = miss };

        /// <summary>The replies to each kind of message (the slice's RV_OPTS; money at stock car pay).</summary>
        static readonly Dictionary<string, RivalReply[]> Replies = new Dictionary<string, RivalReply[]>
        {
            ["wrecked"] = new[]
            {
                O("sorry", "Apologize", "\"That one's on me. I got in too deep.\"", DSkill.Cool, 12, -1, -1, F(respect: 2), -3, F(), 1, "They take the apology. The Grudge cools.", "It sounds forced. They hang up on you."),
                O("racing", "Call it racing", "\"Two cars, one groove. That's racing.\"", DSkill.Read, 13, 1, -1, F(respect: 1), -2, F(respect: -1), 1, "They grumble, but they get it.", "They hear an excuse."),
                O("fire", "Fire back", "\"Maybe hold your line next time.\"", DSkill.Media, 11, 1, 1, F(fans: 5), 2, F(fans: -2, respect: -2), 2, "The fans love it. So does the Grudge.", "It comes off petty, and they still want you."),
            },
            ["payback"] = new[]
            {
                O("even", "Call it even", "\"Fine. We're square. Clean from here.\"", DSkill.Cool, 12, -1, -1, F(respect: 2), -3, F(), 1, "A nod across the garage. It's over.", "Your voice says otherwise."),
                O("warn", "Warn them off", "\"Do that again and you'll see me in your mirror all year.\"", DSkill.Media, 12, 1, 1, F(fans: 5), 1, F(fans: -2), 2, "The clip plays all week.", "It sounds like a threat you can't back up."),
                O("report", "Report it to the series", "Take the in-car video to the series hauler.", DSkill.Contract, 14, 0, -1, F(respect: 2, money: 2000), 1, F(respect: -3), 1, "The series fines them and sends you money for the repairs.", "\"Racing incident.\" Now the garage thinks you tattle."),
            },
            ["hit"] = new[]
            {
                O("letgo", "Let it go", "\"Happens. We're good.\"", DSkill.Cool, 10, -1, -1, F(respect: 2), -1, F(respect: -1), 0, "Class. The garage notices.", "You meant it, but it came out sarcastic."),
                O("bill", "Send them the repair bill", "\"$2,500 for the right side. I take checks.\"", DSkill.Negotiate, 14, 1, -1, F(money: 2500), 1, F(respect: -2), 2, "Their team pays it to keep things quiet.", "They laugh, and now it's personal."),
            },
            ["respect"] = new[]
            {
                O("thanks", "Return the respect", "\"Same. I'll race you clean every lap.\"", DSkill.Read, 10, -1, -1, F(respect: 3), -1, F(), 0, "That's how it should work.", "It reads stiff, but it's fine."),
                O("joke", "Joke about it on air", "\"Tell them I let them have that corner.\"", DSkill.Media, 12, 1, 1, F(fans: 4), 0, F(fans: -1), 1, "Big laugh in the booth.", "Nobody laughs, least of all them."),
            },
            ["gloat"] = new[]
            {
                O("laugh", "Laugh it off", "\"See you next week.\"", DSkill.Deflect, 11, -1, 1, F(fans: 3), -1, F(fans: -1), 0, "Hard to fight someone who won't take the bait.", "You sound rattled."),
                O("trash", "Talk trash back", "\"Check your mirror. I'm in it.\"", DSkill.Media, 13, 1, 1, F(fans: 6, hype: 1), 1, F(fans: -3), 2, "Now it's a storyline.", "It lands flat after the result."),
            },
            ["arrive"] = new[]
            {
                O("welcome", "Welcome them up", "\"Good to see you made it. Race you clean.\"", DSkill.Read, 10, -1, -1, F(respect: 1), -1, F(), 0, "An old rivalry, a fresh start.", "They read it as you looking down on them."),
                O("needle", "Remind them who got here first", "\"Took you long enough.\"", DSkill.Media, 12, 1, 1, F(fans: 3), 1, F(fans: -1), 1.5, "The booth runs the old clips all week.", "It sounds small, and they remember it."),
            },
            ["mate"] = new[]
            {
                O("team", "Team first", "\"Same shop, same goal. We push each other.\"", DSkill.Read, 11, -1, -1, F(respect: 2), -1, F(), 0.5, "The crew notices. So do they.", "It sounds rehearsed."),
                O("number1", "Tell them who's No. 1", "\"Just remember whose car the sponsor bought.\"", DSkill.Media, 13, 1, 1, F(fans: 4, hype: 1), 1.5, F(fans: -2, respect: -2), 2, "Tension sells. The fans eat it up.", "The whole shop heard it."),
            },
        };
        static RivalBook() { Replies["beat"] = Replies["gloat"]; }

        public static RivalReply[] RepliesFor(string kind) => Replies.TryGetValue(kind, out var r) ? r : Replies["gloat"];

        /// <summary>Harder answers: an apology to The Grudge is DC +3, and talking down a Hot hothead is DC +2.</summary>
        public static int DcFor(RivalReply o, RivalDef d, double g) => o.Dc + (d.Id == "grudge" && o.GWin < 0 ? 3 : 0) + (d.Id == "hothead" && g >= 6 && o.GWin < 0 ? 2 : 0);
    }
}
