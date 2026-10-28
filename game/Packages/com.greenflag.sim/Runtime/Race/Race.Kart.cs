using System;

namespace GreenFlag.Sim
{
    /// <summary>
    /// Kart card rules (step 21): Tow pips from sitting in a train, the fairing that a scruffy contact move pushes in (the third
    /// push is +5 s at the flag), the cutback after a Late Brake, and the partner who pushes you back.
    /// </summary>
    public sealed partial class Race
    {
        // KART: combo window -3 s and +3 per link (cap +6), Tow up to 3 pips (one per 4 s in a train, one fades after 8 s out of it)
        const double KartWin = 3, KartBonus = 3, KartTowEvery = 4, KartTowFade = 8, KartRepass = 0.55, KartFairPen = 5;
        const int KartTowMax = 3, KartFairMax = 3;

        public int Tow, Fairing;
        public bool FairingOut;
        /// <summary>Places the fairing penalty cost you at the flag.</summary>
        public int FairingLost;
        public RaceCar KAlong;
        public double KAlongAt;
        /// <summary>Around the Outside: the corner direction it went round, and when.</summary>
        public int KOutDir;
        public double KOutAt = double.NegativeInfinity;
        public RaceCar KRepass;
        public double KRepassAt;
        RaceCar kPartner;
        double kPartnerUntil, kPartnerP;
        bool kPartnerPassed;
        double towT, fadeT;

        public int FairingMax => KartFairMax + (int)(Loadout?.PartSum(p => p.FairMax) ?? 0);

        public void AddTow(int n)
        {
            Tow = (int)SimMath.Clamp(Tow + n, 0, KartTowMax);
        }

        public void AddFairing(int n)
        {
            if (!Track.Kart) return;
            int mx = FairingMax, was = Fairing;
            Fairing = Math.Min(mx, was + n);
            if (Fairing >= mx && was < mx) { FairingOut = true; Emit(RaceEventKind.FairingPushed, Me, null, $"Fairing pushed in: +{KartFairPen} s at the flag"); }
            else if (was < mx) Emit(RaceEventKind.FairingPushed, Me, null, $"Fairing {Fairing}/{mx}");
        }

        public bool Covering(RaceCar car) => car != null && car.CoverUntil > T;

        /// <summary>How many karts are nose to tail ahead of this one (up to 3).</summary>
        public int KartTrain(RaceCar car)
        {
            int n = 0; RaceCar a = AheadOf(car), b = car;
            while (a != null && n < 3 && !a.Dnf && GapSec(a, b) < Track.DraftRange) { n++; b = a; a = AheadOf(a); }
            return n;
        }

        public bool OutsideFresh => T - KOutAt < 9;
        internal void SetOutside(int dir) { KOutDir = dir; KOutAt = T; }
        internal void ClearOutside() { KOutAt = double.NegativeInfinity; }
        internal void SetRepass(RaceCar car) { KRepass = car; KRepassAt = T + 2; }

        internal void SetPartner(RaceCar car, double p)
        {
            if (car == null || car.Dnf) return;
            kPartner = car; kPartnerUntil = T + 20; kPartnerP = p; kPartnerPassed = false;
        }

        void KartTick(double dt)
        {
            if (Phase != RacePhase.Green) return;
            var m = Me;
            if (m.Dnf) return;
            var a = AheadOf(m);
            int tr = a != null && GapSec(a, m) < Track.DraftRange * 0.9 ? KartTrain(m) : 0;
            if (tr > 0)
            {
                towT += dt * (tr >= 2 ? 1 : 0.5); fadeT = 0;
                if (towT >= KartTowEvery) { towT = 0; if (Tow < KartTowMax) AddTow(1); }
            }
            else
            {
                fadeT += dt;
                if (fadeT >= KartTowFade && Tow > 0) { fadeT = 0; AddTow(-1); }
            }
            if (KRepass != null && T >= KRepassAt)
            {
                var rp = KRepass; KRepass = null;
                var b = BehindOf(m);
                if (b == rp && !b.Dnf && GapSec(m, b) < 0.8 && !m.HasEffect(EffectKind.Block) && CardRng.NextDouble() < KartRepass)
                {
                    StartOvertake(b, 1); CardStats.Repassed++;
                    Emit(RaceEventKind.Repassed, b, m, "Poor exit: crossed back");
                }
            }
            if (kPartner != null)
            {
                if (T > kPartnerUntil || kPartner.Dnf) kPartner = null;
                else
                {
                    var b = BehindOf(m);
                    if (!kPartnerPassed && IndexOf(kPartner) > IndexOf(m)) kPartnerPassed = true;
                    if (b == kPartner && GapSec(m, b) < 0.5 && SpotOf(m).Straight)
                    {
                        kPartner = null;
                        if (CardRng.NextDouble() < kPartnerP) { AddPace(m, 1.04, 5); CardStats.PushBack++; Emit(RaceEventKind.PartnerPush, b, m); }
                    }
                }
            }
        }

        /// <summary>The fairing penalty: +5 s at the flag drops you behind everyone who finished within 5 s of you.</summary>
        void KartFairingPenalty()
        {
            if (!Track.Kart || !FairingOut || FinalOrder == null) return;
            var fo = FinalOrder;
            int i = fo.IndexOf(Me);
            if (i < 0 || Me.Dnf) return;
            double cut = Me.Dist - KartFairPen / Track.LapTime;
            int j = i;
            while (j + 1 < fo.Count && !fo[j + 1].Dnf && fo[j + 1].Dist >= cut) j++;
            fo.RemoveAt(i); fo.Insert(j, Me);
            FairingLost = j - i;
            Emit(RaceEventKind.FairingPenalty, Me, null, $"+{KartFairPen} s: P{i + 1} to P{j + 1}");
        }
    }
}
