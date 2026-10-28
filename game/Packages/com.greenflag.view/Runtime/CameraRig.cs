using System;

namespace GreenFlag.View
{
    public enum CameraMode { Auto, Wide, Follow }

    /// <summary>A 3D point in track units (Y up).</summary>
    public readonly struct Vec3
    {
        public readonly double X, Y, Z;
        public Vec3(double x, double y, double z) { X = x; Y = y; Z = z; }
        public static Vec3 operator +(Vec3 a, Vec3 b) => new Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Vec3 operator -(Vec3 a, Vec3 b) => new Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Vec3 operator *(Vec3 a, double k) => new Vec3(a.X * k, a.Y * k, a.Z * k);
        public static Vec3 Lerp(Vec3 a, Vec3 b, double f) => a + (b - a) * f;
        public bool IsFinite => !(double.IsNaN(X) || double.IsNaN(Y) || double.IsNaN(Z) || double.IsInfinity(X) || double.IsInfinity(Y) || double.IsInfinity(Z));
    }

    /// <summary>
    /// The two race cameras. Wide frames the whole track from the grandstand side, tilted like the slice's 2.5D view.
    /// Follow chases your car from behind and above. Auto is the slice's rule: wide at the start and under caution, follow
    /// otherwise. Both ease toward their target (the slice eases at 1.7 per second), so cuts between them are smooth.
    /// </summary>
    public sealed class CameraRig
    {
        public CameraMode Mode = CameraMode.Auto;
        /// <summary>Vertical field of view in degrees, which the engine camera must match.</summary>
        public double FovY = 40;
        /// <summary>Wide shot elevation (degrees above the horizon). The slice's TILT 0.5 is about 30; 3D reads better a bit higher.</summary>
        public double WideElevation = 52;
        /// <summary>Follow shot: distance behind, height above, and how far ahead of the car it looks (track units).</summary>
        public double FollowBack = 0.62, FollowUp = 0.26, FollowAhead = 0.35;
        public double Ease = 1.7;

        public Vec3 Eye { get; private set; }
        public Vec3 LookAt { get; private set; }
        bool primed;

        public static CameraMode Next(CameraMode m) => (CameraMode)(((int)m + 1) % 3);

        /// <summary>The mode actually in use this frame.</summary>
        public CameraMode Resolve(GreenFlag.Sim.RacePhase phase)
        {
            if (Mode != CameraMode.Auto) return Mode;
            return phase == GreenFlag.Sim.RacePhase.Caution || phase == GreenFlag.Sim.RacePhase.Start ? CameraMode.Wide : CameraMode.Follow;
        }

        /// <summary>
        /// Moves the camera toward this frame's shot. aspect = screen width / height, dt in real seconds. The follow shot is
        /// built from the car's position every frame (only its heading is smoothed), so it keeps up at 8x; switching between
        /// wide and follow blends the two shots.
        /// </summary>
        public void Update(TrackLayout layout, CarPose me, GreenFlag.Sim.RacePhase phase, double aspect, double dt)
        {
            double want = Resolve(phase) == CameraMode.Follow ? 1 : 0;
            double roadYaw = layout.At(me.U, me.Lane).Yaw;
            if (!primed || dt <= 0) { blend = want; yaw = roadYaw; primed = true; }
            else
            {
                blend += (want - blend) * Math.Min(1, dt * Ease);
                yaw += WrapPi(roadYaw - yaw) * Math.Min(1, dt * 5);
            }
            Shot(layout, me, CameraMode.Wide, aspect, out var we, out var wl);
            FollowShot(me, yaw, out var fe, out var fl);
            double f = TrackLayout.Smooth(blend);
            Eye = Vec3.Lerp(we, fe, f);
            LookAt = Vec3.Lerp(wl, fl, f);
        }

        /// <summary>Snap to the shot with no easing (a new race or a camera cut the player asked for).</summary>
        public void Cut() => primed = false;

        double blend, yaw;

        static double WrapPi(double a) => a - Math.PI * 2 * Math.Round(a / (Math.PI * 2));

        void FollowShot(CarPose me, double heading, out Vec3 eye, out Vec3 look)
        {
            double fx = Math.Sin(heading), fz = Math.Cos(heading);
            eye = new Vec3(me.P.X - fx * FollowBack, FollowUp, me.P.Z - fz * FollowBack);
            look = new Vec3(me.P.X + fx * FollowAhead, 0, me.P.Z + fz * FollowAhead);
        }

        public void Shot(TrackLayout layout, CarPose me, CameraMode mode, double aspect, out Vec3 eye, out Vec3 look)
        {
            if (mode == CameraMode.Follow)
            {
                // behind the car along the road's heading, not the car's: a spinning car should not whip the camera round
                FollowShot(me, layout.At(me.U, me.Lane).Yaw, out eye, out look);
                return;
            }
            // wide: fit the track's bounding box (plus the walls and pit road) in view, looking north from the south side
            double pad = 0.55;
            double w = layout.MaxX - layout.MinX + pad * 2, h = layout.MaxZ - layout.MinZ + pad * 2;
            double cx = (layout.MaxX + layout.MinX) / 2, cz = (layout.MaxZ + layout.MinZ) / 2;
            double el = WideElevation * Math.PI / 180, half = FovY * Math.PI / 360;
            // the box's depth shrinks by sin(elevation) on screen; fit whichever of width or depth is tighter
            double needV = h * Math.Sin(el) / 2, needH = w / 2 / Math.Max(0.1, aspect);
            double dist = Math.Max(needV, needH) / Math.Tan(half) * 1.05 + h * Math.Cos(el) * 0.15;
            look = new Vec3(cx, 0, cz);
            eye = new Vec3(cx, dist * Math.Sin(el), cz - dist * Math.Cos(el));
        }
    }
}
