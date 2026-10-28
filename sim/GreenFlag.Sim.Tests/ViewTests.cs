using System;
using System.Collections.Generic;
using System.Linq;
using GreenFlag.Sim;
using GreenFlag.View;
using Xunit;

namespace GreenFlag.Sim.Tests
{
    /// <summary>Step 29: the race view's engine-free half (layout, motion, driver, pylon, cameras, looks).</summary>
    public class ViewTests
    {
        public static IEnumerable<object[]> AllTracks() => Tracks.All.Select(t => new object[] { t.Id });

        [Theory, MemberData(nameof(AllTracks))]
        public void Layout_is_a_closed_smooth_loop(string id)
        {
            var l = TrackLayout.Of(Tracks.ById(id));
            foreach (double lane in new[] { TrackLayout.InnerLane, 0, TrackLayout.OuterLane })
            {
                var a = l.At(0, lane).P; var b = l.At(1 - 1e-9, lane).P;
                Assert.True((a - b).Length < 1e-4, $"{id} lane {lane}: loop does not close ({a} vs {b})");
                var ring = l.Ring(lane, 2000);
                double mean = 0, max = 0;
                for (int i = 1; i < ring.Length; i++) { double d = (ring[i] - ring[i - 1]).Length; mean += d; max = Math.Max(max, d); }
                mean /= ring.Length - 1;
                Assert.True(max < mean * 3.5, $"{id} lane {lane}: a jump in the outline (max step {max:F4}, mean {mean:F4})");
            }
            // the centre line's length is the sim's perimeter
            var c = l.Ring(0, 4000); double len = 0;
            for (int i = 1; i < c.Length; i++) len += (c[i] - c[i - 1]).Length;
            Assert.InRange(len, l.Perimeter * 0.999, l.Perimeter * 1.001);
        }

        [Theory, MemberData(nameof(AllTracks))]
        public void Tangent_points_along_travel_and_normal_points_outside(string id)
        {
            var l = TrackLayout.Of(Tracks.ById(id));
            for (int i = 0; i < 200; i++)
            {
                double u = i / 200.0;
                var q = l.At(u, 0);
                Assert.InRange(q.T.Length, 0.999, 1.001);
                Assert.InRange(q.N.Length, 0.999, 1.001);
                Assert.True(Math.Abs(q.T.X * q.N.X + q.T.Z * q.N.Z) < 1e-9);
                var ahead = l.At(u + 1e-5, 0).P - q.P;
                Assert.True(ahead.X * q.T.X + ahead.Z * q.T.Z > 0, $"{id} u {u}: tangent points backwards");
                var outside = l.At(u, 1).P - q.P;
                Assert.InRange(outside.X * q.N.X + outside.Z * q.N.Z, TrackLayout.LaneW * 0.999, TrackLayout.LaneW * 1.001);
            }
        }

        [Fact]
        public void Ovals_run_counter_clockwise_seen_from_above_with_the_wall_outside()
        {
            foreach (var t in Tracks.All.Where(t => !t.IsPath))
            {
                var l = TrackLayout.Of(t);
                // signed area of the centre line: positive = counter-clockwise with X right and Z up
                var r = l.Ring(0, 800); double area = 0;
                for (int i = 1; i < r.Length; i++) area += r[i - 1].X * r[i].Z - r[i].X * r[i - 1].Z;
                Assert.True(area > 0, t.Id + " runs clockwise: left turns would draw as right turns");
                for (int i = 0; i < 50; i++)
                {
                    double u = i / 50.0;
                    Assert.True(l.At(u, TrackLayout.OuterLane).P.Length > l.At(u, TrackLayout.InnerLane).P.Length, t.Id + ": wall is on the inside");
                }
            }
        }

        [Fact]
        public void Start_finish_line_is_where_race_distance_zero_is()
        {
            foreach (var t in Tracks.All)
            {
                var l = TrackLayout.Of(t);
                Assert.True(Math.Abs(SimMath.Frac(l.Shape.UOf(0) - l.StartFinishU + 0.5) - 0.5) < 1e-6, t.Id);
            }
        }

        [Theory, MemberData(nameof(AllTracks))]
        public void Drawing_a_race_does_not_change_it(string id)
        {
            var t = Tracks.ById(id);
            var plain = new Race(RaceSetup.Quick(t, 61));
            plain.RunToEnd();

            var drawn = new Race(RaceSetup.Quick(t, 61));
            var d = new RaceDriver(drawn) { SpeedIndex = 3 };
            var frame = new SimRng(7);
            var rig = new CameraRig();
            int frames = 0;
            while (!drawn.Done && frames < 200_000)
            {
                // uneven frames (30 to 144 fps, with the odd hitch) at 8x
                double dt = frame.NextDouble() < 0.01 ? 0.3 : 1.0 / (30 + frame.NextDouble() * 114);
                d.Advance(dt);
                frames++;
                var me = d.Poses.First(p => p.IsMe);
                rig.Update(d.Layout, me, drawn.Phase, 16 / 9.0, dt);
                Assert.True(rig.Eye.IsFinite && rig.LookAt.IsFinite);
                foreach (var p in d.Poses)
                {
                    Assert.False(double.IsNaN(p.P.X) || double.IsNaN(p.P.Z) || double.IsNaN(p.Yaw), $"{id}: NaN pose at t={drawn.T:F2}");
                    if (!p.IsPace) Assert.True(p.Lane >= TrackLayout.StallLane - 0.01 && p.Lane <= TrackLayout.WallLane + 1e-9, $"{id}: car {p.Id} lane {p.Lane:F3} at t={drawn.T:F2} pit={p.InPit} dnf={p.Dnf}");
                }
            }
            Assert.True(drawn.Done, id + ": race did not finish on screen");
            Assert.Equal(plain.Fingerprint(), drawn.Fingerprint());
            Assert.Equal(plain.Cars.Count, d.Poses.Count(p => !p.IsPace));
        }

        [Fact]
        public void Interpolation_moves_cars_smoothly_between_ticks()
        {
            var race = new Race(RaceSetup.Quick(Tracks.Carver, 62));
            var d = new RaceDriver(race);
            // run into green-flag racing
            while (race.Phase != RacePhase.Green) d.Advance(1 / 60.0);
            for (int i = 0; i < 600; i++) d.Advance(1 / 60.0);
            var me = race.Me;
            double Vis() => d.Motion.VisDist(me, d.DistOf(me));
            double last = Vis(), maxStep = 0;
            for (int i = 0; i < 240; i++)
            {
                d.Advance(1 / 60.0);
                double now = Vis();
                if (me.Pit == null && race.Phase == RacePhase.Green) maxStep = Math.Max(maxStep, now - last);
                Assert.True(now >= last - 1e-4, $"car went backwards on screen by {last - now:G3}");
                last = now;
            }
            // one 60 fps frame at full speed is a fraction of a percent of a lap; a tick-sized jump would be 3x that
            Assert.InRange(maxStep, 1e-6, 1.0 / race.Track.LapTime / 60 * 1.6);
        }

        [Fact]
        public void Caution_line_up_slides_cars_instead_of_teleporting()
        {
            // find a seed with a caution at Millbrook, then watch the screen positions across the line-up
            for (uint seed = 1; seed < 60; seed++)
            {
                var race = new Race(RaceSetup.Quick(Tracks.Millbrook, seed));
                var d = new RaceDriver(race);
                var last = new Dictionary<int, Vec2>();
                double worst = 0;
                bool sawCaution = false;
                while (!race.Done && race.T < 400)
                {
                    d.Advance(1 / 60.0);
                    if (race.Phase == RacePhase.Caution) sawCaution = true;
                    foreach (var p in d.Poses)
                    {
                        if (p.IsPace || p.Dnf || p.InPit) { last.Remove(p.Id); continue; }
                        if (last.TryGetValue(p.Id, out var q)) worst = Math.Max(worst, (p.P - q).Length);
                        last[p.Id] = p.P;
                    }
                }
                if (!sawCaution) continue;
                // the fastest car covers about 0.012 track units a frame at Millbrook; allow the slide on top of that
                Assert.True(worst < 0.05, $"seed {seed}: a car jumped {worst:F3} units in one frame");
                return;
            }
            Assert.Fail("no Millbrook caution in 60 seeds");
        }

        [Fact]
        public void Pylon_lists_the_top_rows_and_always_shows_you()
        {
            var race = new Race(RaceSetup.Quick(Tracks.Carver, 61));
            var d = new RaceDriver(race);
            var rows = new List<PylonRow>();
            for (int i = 0; i < 4000 && !race.Done; i++)
            {
                d.Advance(1 / 30.0);
                Pylon.Rows(race, 5, rows);
                Assert.Contains(rows, r => r.IsMe);
                Assert.Equal(race.Order[0].Id, rows[0].CarId);
                Assert.True(rows[0].Gap == "LEADER" || rows[0].Gap == "PIT" || rows[0].Gap == "OUT");
                int mePos = race.Position(race.Me);
                Assert.Equal(mePos <= 5 ? 5 : 7, rows.Count);
                Assert.Equal(mePos, rows.First(r => r.IsMe).Pos);
            }
            var (track, right, _) = Pylon.Header(race);
            Assert.Equal("CARVER", track);
            Assert.EndsWith("TO GO", right);
        }

        [Fact]
        public void Track_short_names_match_the_slice()
        {
            var want = new Dictionary<string, string> { ["mill"] = "Millbrook", ["carver"] = "Carver", ["talon"] = "Talon Ridge", ["pine"] = "Pinewood", ["ridge"] = "Ridgeback", ["hollow"] = "Hollow Creek", ["cedar"] = "Cedar Bowl", ["cross"] = "Crossroads" };
            foreach (var kv in want) Assert.Equal(kv.Value, Pylon.ShortName(Tracks.ById(kv.Key)));
        }

        [Theory, MemberData(nameof(AllTracks))]
        public void Wide_shot_fits_the_whole_track(string id)
        {
            var l = TrackLayout.Of(Tracks.ById(id));
            var rig = new CameraRig();
            foreach (double aspect in new[] { 16 / 9.0, 4 / 3.0, 21 / 9.0 })
            {
                rig.Shot(l, default, CameraMode.Wide, aspect, out var eye, out var look);
                Assert.True(eye.Y > 0.5);
                // camera basis
                var f = Norm(look - eye); var right = Norm(Cross(f, new Vec3(0, 1, 0))); var up = Cross(right, f);
                double tv = Math.Tan(rig.FovY * Math.PI / 360), th = tv * aspect;
                foreach (double lane in new[] { TrackLayout.OuterLane, TrackLayout.PitPadOut })
                    foreach (var p in l.Ring(lane, 300))
                    {
                        var v = new Vec3(p.X, 0, p.Z) - eye;
                        double z = Dot(v, f);
                        Assert.True(z > 0);
                        Assert.InRange(Dot(v, right) / z, -th, th);
                        Assert.InRange(Dot(v, up) / z, -tv, tv);
                    }
            }
        }

        [Fact]
        public void Follow_shot_sits_behind_your_car_and_looks_ahead()
        {
            var race = new Race(RaceSetup.Quick(Tracks.Talon, 5));
            var d = new RaceDriver(race);
            while (race.Phase != RacePhase.Green) d.Advance(1 / 60.0);
            for (int i = 0; i < 300; i++) d.Advance(1 / 60.0);
            var me = d.Poses.First(p => p.IsMe);
            var rig = new CameraRig { Mode = CameraMode.Follow };
            rig.Update(d.Layout, me, race.Phase, 16 / 9.0, 0);
            var road = d.Layout.At(me.U, me.Lane);
            var toCar = new Vec2(me.P.X - rig.Eye.X, me.P.Z - rig.Eye.Z);
            Assert.True(toCar.X * road.T.X + toCar.Z * road.T.Z > 0.5, "camera is not behind the car");
            Assert.True(rig.Eye.Y > 0.1);
        }

        [Fact]
        public void Camera_auto_is_wide_at_the_start_and_under_caution()
        {
            var rig = new CameraRig();
            Assert.Equal(CameraMode.Wide, rig.Resolve(RacePhase.Start));
            Assert.Equal(CameraMode.Wide, rig.Resolve(RacePhase.Caution));
            Assert.Equal(CameraMode.Follow, rig.Resolve(RacePhase.Green));
            rig.Mode = CameraMode.Wide;
            Assert.Equal(CameraMode.Wide, rig.Resolve(RacePhase.Green));
            Assert.Equal(CameraMode.Follow, CameraRig.Next(CameraMode.Wide));
            Assert.Equal(CameraMode.Auto, CameraRig.Next(CameraMode.Follow));
        }

        [Fact]
        public void Looks_default_to_clean_and_cycle_through_all_four()
        {
            Assert.Same(Looks.Clean, Looks.Default);
            Assert.Equal(1, Looks.Clean.PixelSize);
            var seen = new HashSet<string>();
            var l = Looks.Default;
            for (int i = 0; i < 4; i++) { seen.Add(l.Key); l = Looks.Next(l); }
            Assert.Equal(new[] { "clean", "pixel", "poly", "tv" }.ToHashSet(), seen);
            Assert.Same(Looks.Pixel, Looks.ByKey("pixel"));
            Assert.Same(Looks.Default, Looks.ByKey("nope"));
            Assert.Equal(10, Looks.Teams.Count);
            Assert.Equal("#2f6fe0", Looks.Team(1).Body.ToString());
        }

        [Fact]
        public void Booth_and_banners_follow_the_flags()
        {
            var race = new Race(RaceSetup.Quick(Tracks.Millbrook, 3));
            var d = new RaceDriver(race) { SpeedIndex = 3 };
            var banners = new HashSet<string>();
            while (!race.Done) { d.Advance(1 / 30.0); if (d.Banner is Banner b) banners.Add(b.Text); }
            Assert.Contains("GREEN FLAG", banners);
            Assert.Contains("CHECKERED FLAG", banners);
            Assert.InRange(d.Booth.Count, 1, 4);
            if (race.Cautions > 0) Assert.Contains("CAUTION", banners);
        }

        static Vec3 Cross(Vec3 a, Vec3 b) => new Vec3(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
        static double Dot(Vec3 a, Vec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
        static Vec3 Norm(Vec3 a) { double n = Math.Sqrt(Dot(a, a)); return a * (1 / n); }
    }
}
