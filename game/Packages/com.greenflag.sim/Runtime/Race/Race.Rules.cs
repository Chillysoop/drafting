using System;

namespace GreenFlag.Sim
{
    public sealed partial class Race
    {
        /// <summary>Step 42: the premier stock series' track rules are on for this race (a stock car track, SimBalance.StockRules).</summary>
        public bool RulesOn => Bal.StockRules && Track.Kind != TrackKind.None;

        /// <summary>
        /// Step 42, what each kind of stock car track does to a card's read, so each one wants a different race deck:
        ///   Superspeedway: the pack runs single file and only the draft passes. Drafting cards read better with a car behind
        ///     you to push, passing cards read worse.
        ///   Intermediate: clean air and tire fall-off. Passing or drafting a car that's right ahead of you is dirty air unless
        ///     your tires are fresher than his; tire care and long-run cards read better.
        ///   Short track: beating and banging. Bump and Run and Block read better; drafting reads worse,
        ///     and so does a Dive Bomb (everyone protects the bottom).
        ///   Road course: braking zones. The tow only counts on the long straight; passing cards read better into any corner,
        ///     Bump and Run only works in a hairpin, and a Block can't cover both lines into a corner.
        /// </summary>
        void RulesOdds(CardDef cd, CardCtx c, OddsBuilder R)
        {
            if (!RulesOn) return;
            var f = cd.Family; var id = cd.Id; var s = c.Seg;
            switch (Track.Kind)
            {
                case TrackKind.Superspeedway:
                    if (f == CardFamily.Draft && c.Behind != null && c.GapB < Track.DraftRange) R.A(6, "Pushed by the pack", "rules");
                    if (f == CardFamily.Pass) R.A(-10, "Single file: pass with the draft", "rules");
                    break;
                case TrackKind.Intermediate:
                    if ((f == CardFamily.Pass || f == CardFamily.Draft) && c.Ahead != null && c.GapA < 0.5)
                    {
                        if (c.Tires >= c.Ahead.Tires + 10) R.A(6, "Fresher tires through his dirty air", "rules");
                        else R.A(-10, "Dirty air", "rules");
                    }
                    if (id == "save_tires" || id == "hot_laps" || id == "high_line") R.A(8, "Long green runs", "rules");
                    // Clear Air reads +6 at Carver on its own (the slice's card); give the other intermediates the same
                    if (id == "clear_air" && Track.Id != "carver") R.A(6, "Intermediate", "rules");
                    break;
                case TrackKind.ShortTrack:
                    if (id == "bump_run") R.A(10, "Short track rules", "rules");
                    if (id == "block") R.A(8, "Hard to pass here", "rules");
                    if (id == "dive_bomb") R.A(-12, "He's protecting the bottom", "rules");
                    if (f == CardFamily.Draft) R.A(-8, "No draft to speak of", "rules");
                    if (Tune != null && Tune.Hand > 0 && (f == CardFamily.Pass || f == CardFamily.Risk)) R.A(-4 * Tune.Hand, "Loose off flat corners", "rules");
                    break;
                case TrackKind.RoadCourse:
                    if (f == CardFamily.Draft && !(s.Straight && s.Long)) R.A(-14, "The tow only counts on the long straight", "rules");
                    if (f == CardFamily.Pass && s.Approach) R.A(8, "Braking zone", "rules");
                    if (id == "bump_run" && s.Kind != TurnKind.Hairpin) R.A(-8, "Too fast to move him here", "rules");
                    if (id == "block") R.A(-6, "Two lines into every braking zone", "rules");
                    break;
            }
        }

        /// <summary>Step 42, pace from the track rules: tires fall off harder on an intermediate (everyone), and your setup's aero
        /// matters on a road course (downforce is worth speed, low drag costs it).</summary>
        double RulesPace(RaceCar car)
        {
            if (!RulesOn) return 1;
            double m = 1;
            if (Track.Kind == TrackKind.Intermediate) m *= 1 - (1 - car.Tires / 100) * Bal.IntFalloff;
            if (Track.Kind == TrackKind.RoadCourse && car.IsMe && Tune != null) m *= 1 - Bal.RoadAero * Tune.Aero;
            return m;
        }
    }
}
