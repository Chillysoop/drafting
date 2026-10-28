using System;
using System.Collections.Generic;
using System.Linq;
using GreenFlag.Sim;
using GreenFlag.View;
using Xunit;

namespace GreenFlag.Sim.Tests
{
    /// <summary>Step 30: card outcome moves, body roll and pitch, sparks, smoke, skids and wrecks played out as free bodies.</summary>
    public class MotionTests
    {
        const double Frame = 1 / 60.0;

        static RaceDriver Green(string track = "carver", uint seed = 61)
        {
            var r = Scenario.Green(track, null, seed);
            var d = new RaceDriver(r);
            for (int i = 0; i < 120; i++) d.Advance(Frame);
            return d;
        }

        static void Run(RaceDriver d, double secs) { for (int i = 0; i < secs * 60; i++) d.Advance(Frame); }

        /// <summary>Plays a card at a forced outcome and runs frames until it resolves.</summary>
        static void Play(RaceDriver d, string id, Tier t)
        {
            var r = d.Race;
            var h = r.GiveCard(id);
            r.ForceNextTier = t;
            Assert.Null(r.PlayCard(h.Uid));
            for (int g = 0; r.Attempt != null && g < 600; g++) d.Advance(Frame);
            Assert.Null(r.Attempt);
        }

        static CarPose Pose(RaceDriver d, RaceCar c) => d.Poses.First(p => p.Id == c.Id);

        /// <summary>Puts the car ahead of you just in front, close enough to touch.</summary>
        static RaceCar CloseAhead(RaceDriver d, double carLengths = 1.6)
        {
            var r = d.Race; var a = r.AheadOf(r.Me);
            if (a == null) { Scenario.CarBehind(r); a = r.BehindOf(r.Me); int i = r.Order.IndexOf(a); r.Order[i] = r.Me; r.Order[i - 1] = a; }
            a.Dist = r.Me.Dist + TrackLayout.CarL * carLengths / d.Layout.Perimeter;
            return a;
        }

        static double TrackYaw(RaceDriver d, CarPose p) => d.Layout.At(p.U, p.Lane).Yaw;
        static double Wrap(double a) => a - Math.PI * 2 * Math.Round(a / (Math.PI * 2));

        [Fact]
        public void Sim_reports_card_tiers_and_the_ai_moves_that_came_to_nothing()
        {
            var r = Scenario.Make("carver");
            r.RunToEnd();
            Assert.Contains(r.Log, e => e.Kind == RaceEventKind.AutoLift);
            Assert.Contains(r.Log, e => e.Kind == RaceEventKind.AutoAlongside);
            Assert.All(r.Log.Where(e => e.Kind != RaceEventKind.CardResolved), e => Assert.Equal(-1, e.Tier));

            var g = Scenario.Green("carver");
            var h = g.GiveCard("tuck_in"); g.ForceNextTier = Tier.Neutral; g.PlayCard(h.Uid);
            while (g.Attempt != null) g.Tick();
            var res = g.Log.Last(e => e.Kind == RaceEventKind.CardResolved);
            Assert.Equal((int)Tier.Neutral, res.Tier);
        }

        [Fact]
        public void A_clean_move_streaks_and_glows_green()
        {
            var d = Green();
            CloseAhead(d, 4);
            Play(d, "side_draft", Tier.Success);
            var p = Pose(d, d.Race.Me);
            Assert.True(p.Streak > 0, "no speed streaks after a clean draft move");
            Assert.True(p.GlowA > 0);
            Assert.Equal(CarMotion.GlowSuccess.ToString(), p.Glow.ToString());
        }

        [Fact]
        public void A_neutral_pass_stalls_side_by_side()
        {
            var d = Green();
            var a = CloseAhead(d, 1.5);
            Play(d, "dive_bomb", Tier.Neutral);
            Run(d, 1.5);
            var me = Pose(d, d.Race.Me); var him = Pose(d, a);
            Assert.True(Math.Abs(me.Lane - him.Lane) > 0.7, $"not alongside: lanes {me.Lane:F2} and {him.Lane:F2}");
            Assert.True(Math.Abs(SimMath.Frac(him.U - me.U + 0.5) - 0.5) * d.Layout.Perimeter < TrackLayout.CarL * 1.6, "not door to door");
        }

        [Fact]
        public void A_failed_move_wiggles_and_lays_rubber()
        {
            var d = Green();
            CloseAhead(d, 4);
            int marks = d.Motion.Fx.Skids.Count;
            Play(d, "push_limit", Tier.Fail);
            double maxSlip = 0, maxLane = -9;
            for (int i = 0; i < 90; i++)
            {
                d.Advance(Frame);
                var p = Pose(d, d.Race.Me);
                maxSlip = Math.Max(maxSlip, Math.Abs(Wrap(p.Yaw - TrackYaw(d, p))));
                maxLane = Math.Max(maxLane, p.Lane);
            }
            Assert.True(maxSlip > 0.12, $"no wiggle (max slip {maxSlip:F3} rad)");
            Assert.True(maxLane > 1.5, "Push to the Limit should brush the wall on a fail");
            Assert.True(d.Motion.Fx.Skids.Count > marks, "no skid marks");
            Assert.Contains(d.Motion.Fx.Puffs, x => x.Kind == PuffKind.Tire);
        }

        [Fact]
        public void A_bump_draft_taps_the_bumper_with_sparks()
        {
            var d = Green();
            var a = CloseAhead(d, 1.4);
            int sparks = d.Motion.Fx.SparksThrown;
            Play(d, "bump_draft", Tier.Success);
            Run(d, 1);
            Assert.True(d.Motion.Fx.SparksThrown > sparks, "no sparks off the bumper");
            Assert.True(Pose(d, a).SinceHit < 1.5);
        }

        [Fact]
        public void A_wreck_is_a_free_body_that_spins_slides_and_comes_back()
        {
            var d = Green("mill");
            var r = d.Race;
            var car = r.Order[3]; var other = r.Order[4];
            r.Wreck(car, other, "test");
            int frames = 0; double maxSpin = 0; bool wasFree = false;
            for (; frames < 60 * 4; frames++)
            {
                d.Advance(Frame);
                foreach (var p in d.Poses.Where(p => !p.IsPace))
                {
                    Assert.True(double.IsFinite(p.P.X) && double.IsFinite(p.P.Z) && double.IsFinite(p.Yaw) && double.IsFinite(p.Roll) && double.IsFinite(p.Lift), $"car {p.Id} pose is not finite");
                    Assert.InRange(p.Lane, TrackLayout.DnfLane - 0.01, TrackLayout.WallLane + 0.01);
                }
                var q = Pose(d, car);
                wasFree |= q.Wrecking;
                maxSpin = Math.Max(maxSpin, Math.Abs(Wrap(q.Yaw - TrackYaw(d, q))));
            }
            Assert.Equal(2, d.Motion.WreckBodies);
            Assert.True(wasFree);
            Assert.True(maxSpin > 1.2, $"the car hardly turned ({maxSpin:F2} rad)");
            Assert.NotEmpty(d.Motion.Fx.Skids);
            Assert.True(d.Motion.Fx.SparksThrown > 0);
            Assert.True(d.Motion.Fx.Sparks.Count <= Fx.MaxSparks && d.Motion.Fx.Puffs.Count <= Fx.MaxPuffs && d.Motion.Fx.Skids.Count <= Fx.MaxSkids);
            // the sim decided the outcome: once the spin is over the car is back where the sim has it, pointing down the track
            Run(d, 8);
            if (!car.Dnf && car.Pit == null)
            {
                var q = Pose(d, car);
                Assert.False(q.Wrecking);
                double gap = Math.Abs(d.Motion.VisDist(car, d.DistOf(car)) - d.DistOf(car)) * d.Layout.Perimeter;
                Assert.True(gap < TrackLayout.CarL * 0.5, $"still {gap / TrackLayout.CarL:F2} car lengths off its sim spot");
                Assert.True(Math.Abs(Wrap(q.Yaw - TrackYaw(d, q))) < 0.35, "still pointing the wrong way");
            }
        }

        [Fact]
        public void Bodies_lean_out_of_the_turns_and_dive_under_braking()
        {
            var d = Green("carver");
            double turn = 0, straight = 0, brake = 0, cruise = 0; int nt = 0, ns = 0, nb = 0, nc = 0;
            for (int i = 0; i < 60 * 40; i++)
            {
                d.Advance(Frame);
                if (d.Race.Phase != RacePhase.Green) continue;
                foreach (var p in d.Poses.Where(p => !p.IsPace && !p.Dnf && !p.InPit && !p.Wrecking && p.SinceHit > 2))
                {
                    double y0 = d.Layout.At(p.U - 0.004, 0).Yaw, y1 = d.Layout.At(p.U + 0.004, 0).Yaw;
                    bool inTurn = Math.Abs(Wrap(y1 - y0)) > 0.02;
                    if (inTurn) { turn += p.Roll; nt++; } else { straight += Math.Abs(p.Roll); ns++; }
                    if (d.Layout.BrakeZone(p.U)) { brake += p.Pitch; nb++; } else if (!inTurn) { cruise += p.Pitch; nc++; }
                }
            }
            turn /= nt; straight /= ns; brake /= nb; cruise /= nc;
            // the ovals turn left: the body leans right (out of the turn), a few degrees
            Assert.InRange(turn, 0.015, CarMotion.MaxDriveLean);
            Assert.True(straight < turn * 0.6, $"leaning as much on the straights ({straight:F3}) as in the turns ({turn:F3})");
            Assert.True(brake > cruise + 0.01, $"no nose dive under braking ({brake:F3} vs {cruise:F3})");
        }

        [Fact]
        public void The_view_is_deterministic_frame_for_frame()
        {
            string Run1()
            {
                var d = new RaceDriver(new Race(RaceSetup.Quick(Tracks.ById("mill"), 77)));
                var dts = new[] { 1 / 60.0, 1 / 30.0, 1 / 144.0, 0.1 };
                for (int i = 0; i < 4000; i++) { d.SpeedIndex = i / 1000; d.Advance(dts[i % 4]); }
                var p = d.Poses.Select(x => $"{x.Id}:{x.P}:{x.Yaw:F6}:{x.Roll:F6}:{x.Pitch:F6}");
                return string.Join("|", p) + $"#{d.Motion.Fx.SparksThrown}:{d.Motion.Fx.Skids.Count}:{d.Motion.Fx.Puffs.Count}:{d.Motion.WreckBodies}";
            }
            Assert.Equal(Run1(), Run1());
        }
    }
}
