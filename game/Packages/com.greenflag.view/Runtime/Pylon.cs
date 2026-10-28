using System;
using System.Collections.Generic;
using GreenFlag.Sim;

namespace GreenFlag.View
{
    public enum PylonFlag { None, Green, Caution, Final }

    public struct PylonRow
    {
        /// <summary>Running position, 1 = leader. 0 marks the gap row before your car when you are outside the top rows.</summary>
        public int Pos;
        public int CarId, Team, Number;
        public string Name, Gap;
        public bool IsMe, Dnf, InPit;
    }

    /// <summary>The scoring pylon: the slice's renderPylon as data, so any HUD can draw it.</summary>
    public static class Pylon
    {
        /// <summary>Header text (the track) and the right-hand side (laps to go or FINAL), with the flag colour.</summary>
        public static (string track, string right, PylonFlag flag) Header(Race r)
        {
            var lead = r.Order[0];
            int toGo = Math.Max(0, r.Track.Laps - (int)Math.Floor(lead.Dist));
            var flag = r.Phase == RacePhase.Caution ? PylonFlag.Caution : r.Phase == RacePhase.Green || r.Phase == RacePhase.Restart ? PylonFlag.Green : r.Phase == RacePhase.Finished ? PylonFlag.Final : PylonFlag.None;
            return (ShortName(r.Track).ToUpperInvariant(), r.Phase == RacePhase.Finished ? "FINAL" : toGo + " TO GO", flag);
        }

        /// <summary>The top n cars, plus a gap row and your car if you are not in them.</summary>
        public static void Rows(Race r, int n, List<PylonRow> into)
        {
            into.Clear();
            var order = r.Phase == RacePhase.Finished && r.FinalOrder != null ? r.FinalOrder : r.Order;
            var lead = order[0];
            bool meShown = false;
            for (int i = 0; i < order.Count && i < n; i++) { into.Add(Row(r, order, i, lead)); meShown |= order[i].IsMe; }
            if (!meShown)
            {
                int mi = order.IndexOf(r.Me);
                if (mi >= 0) { into.Add(new PylonRow { Pos = 0, CarId = -1 }); into.Add(Row(r, order, mi, lead)); }
            }
        }

        static PylonRow Row(Race r, List<RaceCar> order, int i, RaceCar lead)
        {
            var c = order[i];
            double g = lead.Dist - c.Dist;
            string gap = c.Dnf ? "OUT" : c.Pit != null ? "PIT" : i == 0 ? "LEADER" : g >= 1 ? "-" + Math.Floor(g) + " LAP" : "+" + (g * r.Track.LapTime).ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
            return new PylonRow { Pos = i + 1, CarId = c.Id, Team = c.Team, Number = c.Ref.Number, Name = ShortName(c), Gap = gap, IsMe = c.IsMe, Dnf = c.Dnf, InPit = c.Pit != null };
        }

        /// <summary>The slice's shortName: YOU, or the driver's last name in capitals.</summary>
        public static string ShortName(RaceCar c)
        {
            if (c.IsMe) return "YOU";
            var parts = c.Ref.Name.Split(' ');
            return parts[parts.Length - 1].ToUpperInvariant();
        }

        static readonly string[] Suffixes = { " Motor Speedway", " Superspeedway", " Speedway", " Kart Park", " Kart Club", " Kartway", " Road Course", " Figure-8" };

        /// <summary>The track's short name (the slice's TRACKS[].short): "Carver Motor Speedway" is "Carver".</summary>
        public static string ShortName(TrackDef t)
        {
            foreach (var s in Suffixes) if (t.Name.EndsWith(s, StringComparison.Ordinal)) return t.Name.Substring(0, t.Name.Length - s.Length);
            return t.Name;
        }
    }
}
