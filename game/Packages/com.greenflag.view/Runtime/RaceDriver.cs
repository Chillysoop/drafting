using System;
using System.Collections.Generic;
using GreenFlag.Sim;

namespace GreenFlag.View
{
    /// <summary>A flag or message across the top of the screen.</summary>
    public readonly struct Banner
    {
        public readonly string Text;
        public readonly Rgb Bg, Fg;
        /// <summary>Sim time it was raised and how long it stays (sim seconds).</summary>
        public readonly double At, For;
        public Banner(string text, Rgb bg, Rgb fg, double at, double secs) { Text = text; Bg = bg; Fg = fg; At = at; For = secs; }
    }

    /// <summary>
    /// Plays a race in real time: runs the sim's fixed 20 Hz tick from the frame clock (times the playback speed), interpolates
    /// each car's distance between ticks, steps the car motion, and turns the sim's events into banners and booth lines.
    /// The engine calls Advance once a frame and draws Poses; nothing here touches the race except Race.Tick().
    /// </summary>
    public sealed class RaceDriver
    {
        public static readonly double[] Speeds = { 1, 2, 4, 8 };
        /// <summary>A frame never runs more than this many ticks (a long hitch slows the race instead of freezing the frame).</summary>
        public const int MaxTicksPerFrame = 40;

        public readonly Race Race;
        public readonly TrackLayout Layout;
        public readonly CarMotion Motion;
        public readonly List<CarPose> Poses = new List<CarPose>();
        /// <summary>Booth lines, newest last (the last few notable events).</summary>
        public readonly List<string> Booth = new List<string>();
        public Banner? Banner { get; private set; }
        public int SpeedIndex;
        public bool Paused;
        /// <summary>Ticks run so far.</summary>
        public int Ticks { get; private set; }
        /// <summary>Sim time drawn this frame: the last tick's time plus the interpolated part of the next.</summary>
        public double ViewT => Race.T + acc;

        readonly double dt = 1.0 / SimInfo.TickHz;
        readonly Dictionary<RaceCar, double> prev = new Dictionary<RaceCar, double>();
        readonly Dictionary<RaceCar, double> before = new Dictionary<RaceCar, double>();
        readonly Func<RaceCar, double> distFn;
        double acc;

        public RaceDriver(Race race)
        {
            Race = race;
            Layout = TrackLayout.Of(race.Track);
            Motion = new CarMotion(race, Layout);
            foreach (var c in race.Cars) prev[c] = c.Dist;
            distFn = DistOf;
            Motion.Poses(distFn, Poses);
        }

        public double Speed => Speeds[Math.Max(0, Math.Min(Speeds.Length - 1, SpeedIndex))];

        /// <summary>The car's race distance for this frame, between its last two ticks.</summary>
        public double DistOf(RaceCar c)
        {
            if (!prev.TryGetValue(c, out double p)) return c.Dist;
            double f = acc / dt;
            return p + (c.Dist - p) * f;
        }

        /// <summary>Moves the race on by a frame of realSeconds. Returns the number of sim ticks it ran.</summary>
        public int Advance(double realSeconds)
        {
            if (Paused || realSeconds <= 0) { Motion.Poses(distFn, Poses); return 0; }
            double simDt = Math.Min(realSeconds, 0.25) * Speed;
            int n = 0;
            if (Race.Done) acc = 0;
            else
            {
                acc += simDt;
                while (acc >= dt && n < MaxTicksPerFrame && !Race.Done) { acc -= dt; TickOnce(); n++; }
                if (acc >= dt) acc = dt * 0.999;   // dropped time: the race runs slower than the wall clock this frame
                if (Race.Done) acc = 0;
            }
            Motion.Step(simDt, distFn);
            Motion.Poses(distFn, Poses);
            if (Banner is Banner b && ViewT > b.At + b.For) Banner = null;
            return n;
        }

        /// <summary>Runs the race straight to the end with no drawing (tests and the skip button).</summary>
        public void Finish(int maxTicks = 20 * 60 * 60)
        {
            for (int i = 0; i < maxTicks && !Race.Done; i++) TickOnce();
            acc = 0;
            Motion.Poses(distFn, Poses);
        }

        void TickOnce()
        {
            before.Clear();
            foreach (var c in Race.Cars) before[c] = c.Dist;
            Race.Tick();
            Ticks++;
            foreach (var c in Race.Cars)
            {
                double b = before[c], step = c.Dist - b;
                // a normal tick moves a car well under a hundredth of a lap; anything else is a line-up, a stall snap or a lucky dog
                double typical = Math.Max(0, c.Speed * dt);
                if (step < -1e-9 || step > typical + 0.004) Motion.Jumped(c, step - typical, c.Pit != null);
                prev[c] = c.Dist - (step < -1e-9 || step > typical + 0.004 ? typical : step);
            }
            foreach (var e in Race.Events) OnEvent(e);
        }

        static readonly Rgb Green = Rgb.Hex("#2fb36a"), Yellow = Rgb.Hex("#e9b52f"), White = Rgb.Hex("#ffffff"), Black = Rgb.Hex("#000000"), Ink = Rgb.Hex("#0b0f18");

        void OnEvent(RaceEvent e)
        {
            switch (e.Kind)
            {
                case RaceEventKind.GreenFlag: Raise("GREEN FLAG", Green, White, 2.5); break;
                case RaceEventKind.Restart: Raise("GREEN FLAG", Green, White, 2.5); break;
                case RaceEventKind.Caution: Raise("CAUTION", Yellow, Ink, 3); break;
                case RaceEventKind.PitRoadOpen: Raise("PIT ROAD OPEN", Yellow, Ink, 2.5); break;
                case RaceEventKind.OneToGo: Raise("ONE TO GO", Yellow, Ink, 2.5); break;
                case RaceEventKind.Checkered: Raise("CHECKERED FLAG", White, Black, 4); break;
            }
            string line = BoothLine(e);
            if (line != null)
            {
                Booth.Add(line);
                if (Booth.Count > 4) Booth.RemoveAt(0);
            }
        }

        void Raise(string text, Rgb bg, Rgb fg, double secs) => Banner = new Banner(text, bg, fg, Race.T, secs);

        string BoothLine(RaceEvent e)
        {
            RaceCar car = Car(e.Car), other = Car(e.Other);
            bool mine = (car != null && car.IsMe) || (other != null && other.IsMe);
            switch (e.Kind)
            {
                case RaceEventKind.Caution: return "Caution is out" + (e.Text != null ? ": " + e.Text : "");
                case RaceEventKind.Wreck: return car != null ? Label(car) + (other != null ? " and " + Label(other) : "") + " wreck" : null;
                case RaceEventKind.Dnf: return car != null ? Label(car) + " is out of the race" : null;
                case RaceEventKind.Pass: return mine && car != null && other != null ? Label(car) + " gets by " + Label(other) : null;
                case RaceEventKind.LuckyDog: return car != null ? Label(car) + " gets the free pass" : null;
                case RaceEventKind.PitPenalty: return car != null ? Label(car) + ": pit road penalty" : null;
                case RaceEventKind.OutOfFuel: return car != null ? Label(car) + " is out of fuel" : null;
                case RaceEventKind.Checkered:
                    var lead = Race.Leader;
                    return lead.IsMe ? "You win it!" : Label(lead) + " takes the checkered flag";
            }
            return null;
        }

        RaceCar Car(int id)
        {
            if (id < 0) return null;
            foreach (var c in Race.Cars) if (c.Id == id) return c;
            return null;
        }

        static string Label(RaceCar c) => c.IsMe ? "You" : "#" + c.Ref.Number + " " + c.Ref.Name;
    }
}
