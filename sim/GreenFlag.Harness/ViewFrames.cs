using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using GreenFlag.Sim;
using GreenFlag.View;

namespace GreenFlag.Harness
{
    /// <summary>
    /// `frames [track] [seed] [out.html] [n]`: plays a race through the race view's driver and motion (the same code the Unity
    /// view runs) and writes n top-down SVG snapshots with the pylon beside each. A way to look at the view without the editor.
    /// </summary>
    public static class ViewFrames
    {
        public static int Run(string track, uint seed, string outPath, int n)
        {
            var t = Tracks.ById(track);
            var race = new Race(RaceSetup.Quick(t, seed));
            var d = new RaceDriver(race);
            var look = Looks.Default;
            // snapshots spread over the race, plus the first caution if there is one
            var plain = new Race(RaceSetup.Quick(t, seed));
            plain.RunToEnd();
            double len = plain.FinishedAt;
            var times = Enumerable.Range(0, n).Select(i => 4 + (len - 4) * i / Math.Max(1, n - 1)).ToList();
            var sb = new StringBuilder();
            sb.Append("<!doctype html><meta charset=utf-8><title>Race view frames</title><style>body{background:#0b0f18;color:#dfe6f3;font:13px system-ui;margin:16px}figure{display:inline-block;margin:0 12px 16px 0;vertical-align:top}figcaption{margin:4px 0}svg{background:")
              .Append(look.Sky).Append(";border-radius:6px}</style>");
            sb.Append($"<h2>{t.Name}, seed {seed}</h2>");
            int k = 0;
            bool cautionShot = false;
            while (!race.Done || k < times.Count)
            {
                if (race.Done) { times[k] = race.T; }
                bool shoot = race.T >= times[k] || (!cautionShot && race.Phase == RacePhase.Caution && race.PhaseT > 6);
                if (shoot)
                {
                    if (race.Phase == RacePhase.Caution && race.T < times[k]) cautionShot = true; else k++;
                    sb.Append(Frame(d, look));
                    if (k >= times.Count) break;
                }
                d.Advance(1 / 60.0);
            }
            File.WriteAllText(outPath, sb.ToString());
            Console.WriteLine($"wrote {outPath}");
            return 0;
        }

        static string F(double v) => v.ToString("F4", CultureInfo.InvariantCulture);

        static string Frame(RaceDriver d, LookDef look)
        {
            var l = d.Layout; var r = d.Race;
            double pad = 0.6, x0 = l.MinX - pad, x1 = l.MaxX + pad, z0 = l.MinZ - pad, z1 = l.MaxZ + pad;
            double W = 560, H = W * (z1 - z0) / (x1 - x0);
            var sb = new StringBuilder();
            sb.Append($"<figure><figcaption>t {r.T:F1} s · {r.Phase} · lap {r.LapOf(r.Leader)}/{r.Track.Laps}</figcaption>");
            sb.Append($"<svg width='{W:F0}' height='{H:F0}' viewBox='{F(x0)} {F(-z1)} {F(x1 - x0)} {F(z1 - z0)}'>");
            string Pts(IEnumerable<Vec2> ps) => string.Join(" ", ps.Select(p => F(p.X) + "," + F(-p.Z)));
            string Band(double a, double b) { var A = l.Ring(a); var B = l.Ring(b); return Pts(A.Concat(B.Reverse())); }
            sb.Append($"<rect x='{F(x0)}' y='{F(-z1)}' width='{F(x1 - x0)}' height='{F(z1 - z0)}' fill='{look.Grounds}'/>");
            if (!l.Track.IsPath) sb.Append($"<polygon points='{Pts(l.Ring(TrackLayout.ApronLane))}' fill='{look.Infield}'/>");
            sb.Append($"<polygon points='{Band(TrackLayout.OuterLane, TrackLayout.InnerLane)}' fill='{look.Asphalt}' fill-rule='evenodd'/>");
            sb.Append($"<polygon points='{Band(TrackLayout.InnerLane, TrackLayout.ApronLane)}' fill='{look.Apron}' fill-rule='evenodd'/>");
            // pit road surface along the front straight and the pit lane line
            var pad1 = new List<Vec2>(); var pad2 = new List<Vec2>();
            for (int i = 0; i <= 20; i++) { double u = l.FrontU(0.1 + (l.FrontStraight - 0.2) * i / 20); pad1.Add(l.At(u, TrackLayout.PitPadIn).P); pad2.Add(l.At(u, TrackLayout.PitPadOut).P); }
            pad2.Reverse();
            sb.Append($"<polygon points='{Pts(pad1.Concat(pad2))}' fill='{look.Pit}'/>");
            var g = l.Shape.Pit; var lane = new List<Vec2>();
            for (int i = 0; i <= 48; i++) { double s = g.E0 + (g.X1 - g.E0) * i / 48; lane.Add(l.AtDist(s, l.PitRoadLane(s)).P); }
            sb.Append($"<polyline points='{Pts(lane)}' fill='none' stroke='{look.Pit.Shade(0.15f)}' stroke-width='0.06' stroke-linejoin='round'/>");
            sb.Append($"<polyline points='{Pts(l.Ring(TrackLayout.OuterLane))}' fill='none' stroke='{look.Wall}' stroke-width='0.02'/>");
            sb.Append($"<polyline points='{Pts(l.Ring(TrackLayout.LaneLine))}' fill='none' stroke='#ffffff' stroke-opacity='{look.LaneLine.A}' stroke-width='0.006'/>");
            var sa = l.At(l.StartFinishU, TrackLayout.InnerLane).P; var sbp = l.At(l.StartFinishU, TrackLayout.OuterLane).P;
            sb.Append($"<line x1='{F(sa.X)}' y1='{F(-sa.Z)}' x2='{F(sbp.X)}' y2='{F(-sbp.Z)}' stroke='#eee' stroke-width='0.02' stroke-dasharray='0.02 0.02'/>");
            foreach (var p in d.Poses)
            {
                var liv = p.IsPace ? Looks.PaceCar : Looks.Team(p.Team);
                string col = p.Dnf ? Looks.Wrecked.ToString() : liv.Body.ToString();
                double deg = p.Yaw * 180 / Math.PI;   // world yaw is clockwise from +Z; in the SVG (y = -Z) +Z is up, so rotate from "up"
                sb.Append($"<g transform='translate({F(p.P.X)},{F(-p.P.Z)}) rotate({deg.ToString("F1", CultureInfo.InvariantCulture)})' opacity='{F(p.Alpha)}'>");
                sb.Append($"<rect x='{F(-TrackLayout.CarW / 2)}' y='{F(-TrackLayout.CarL / 2)}' width='{F(TrackLayout.CarW)}' height='{F(TrackLayout.CarL)}' rx='0.008' fill='{col}' stroke='#000' stroke-opacity='.5' stroke-width='0.004'/>");
                sb.Append($"<rect x='{F(-TrackLayout.CarW * 0.1)}' y='{F(-TrackLayout.CarL / 2)}' width='{F(TrackLayout.CarW * 0.2)}' height='{F(TrackLayout.CarL)}' fill='{liv.Stripe}'/>");
                if (p.Braking) sb.Append($"<rect x='{F(-TrackLayout.CarW / 2)}' y='{F(TrackLayout.CarL / 2 - 0.006)}' width='{F(TrackLayout.CarW)}' height='0.006' fill='#ff3b3b'/>");
                sb.Append("</g>");
                if (p.IsMe) sb.Append($"<text x='{F(p.P.X)}' y='{F(-p.P.Z - 0.09)}' font-size='0.08' fill='{Looks.You}' text-anchor='middle' font-weight='800'>YOU</text>");
            }
            sb.Append("</svg>");
            var rows = new List<PylonRow>();
            Pylon.Rows(r, 10, rows);
            var (trk, right, flag) = Pylon.Header(r);
            sb.Append($"<div style='font:12px monospace;white-space:pre;margin-top:4px'>{trk} {right} [{flag}]\n");
            foreach (var row in rows) sb.Append(row.Pos == 0 ? "  ...\n" : $"{row.Pos,3} #{row.Number,-3}{row.Name,-12}{row.Gap}\n");
            if (d.Booth.Count > 0) sb.Append("booth: " + System.Net.WebUtility.HtmlEncode(d.Booth[d.Booth.Count - 1]));
            sb.Append("</div></figure>");
            return sb.ToString();
        }
    }
}
