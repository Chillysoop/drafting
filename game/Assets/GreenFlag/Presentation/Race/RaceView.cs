using System.Collections.Generic;
using GreenFlag.Sim;
using GreenFlag.View;
using UnityEngine;

namespace GreenFlag.Presentation
{
    /// <summary>
    /// The race scene's one script (roadmap step 29): plays a race of the C# sim on screen. It builds the track from the sim's
    /// layout, a car per entrant, drives the race in real time through RaceDriver and points the camera with CameraRig.
    /// Everything that decides where things are lives in the engine-free GreenFlag.View package (tested in CI); this class only
    /// turns it into GameObjects (step 30 adds body lean, damage, glow and streaks on the cars, and sparks, smoke and skid marks
    /// in FxView). Step 31 hands you the car: PlayDesk runs the hand and beats rules and HandHud draws the hand, the console,
    /// the dash, the gauge, the beat clock and the pit stop hand (see HandHud for the keys). Step 32 adds the controller:
    /// PadReader reads the pad, PadInput moves a focus over the HUD, and the arrow keys and Enter do the same without a mouse.
    /// Keys: C camera, [ and ] speed, R new race, T / Shift+T track, L look, F fast-forward to the flag, A autoplay. Step 34 runs
    /// it inside a career (CareerFlow sets RaceSource and Finished): R and T are off and Enter at the flag goes to the rewards.
    /// </summary>
    public sealed class RaceView : MonoBehaviour
    {
        [Tooltip("Track id from GreenFlag.Sim.Tracks (mill, carver, talon, pine, ridge, pinek, hollow, cedar, milll, cross).")]
        public string trackId = "carver";
        public int seed = 61;
        [Tooltip("clean, pixel, poly or tv. Empty = Looks.Default (Clean until board Q37 is answered).")]
        public string lookKey = "";
        [Tooltip("Your persona's deck (drafter, bully, whisperer, restart, gambler; the kart cousin on kart tracks).")]
        public string persona = "drafter";
        [Tooltip("Step 29's hands-off race: no cards, the crew chief runs your car.")]
        public bool handsOff;

        /// <summary>The hand and beats rules for this race (null when hands-off).</summary>
        public PlayDesk Desk { get; private set; }
        public HudReadouts Readouts { get; } = new HudReadouts();
        public HudLayout Layout { get; } = new HudLayout();
        public HudInput HudInput { get; private set; }
        /// <summary>Controller and keyboard-only play (step 32): the focus over the HUD and what pad buttons do.</summary>
        public PadInput Pad { get; private set; }
        readonly PadReader padReader = new PadReader();
        /// <summary>The mouse player drives the HUD (A): a demo, and a quick way to watch a whole race played with cards.</summary>
        public bool Autoplay;
        MousePlayer bot;

        /// <summary>Career mode (step 34): where the race comes from, and who takes it back at the flag. While these are set the
        /// track and new-race keys are off and Enter or A at the finish goes to the reward screen.</summary>
        public System.Func<Race> RaceSource;
        public System.Action<Race> Finished;
        public bool InCareer => RaceSource != null;

        public RaceDriver Driver { get; private set; }
        public CameraRig Rig { get; } = new CameraRig();
        public LookDef Look { get; private set; }
        public Camera Cam { get; private set; }
        public RenderTexture LowRes { get; private set; }

        GameObject track, carsRoot;
        readonly Dictionary<int, CarRig> rigs = new Dictionary<int, CarRig>();
        RaceHud hud;
        HandHud hand;
        FxView fx;

        void Start()
        {
            Look = string.IsNullOrEmpty(lookKey) ? Looks.Default : Looks.ByKey(lookKey);
            Cam = Camera.main;
            if (Cam == null) Cam = new GameObject("Main Camera", typeof(Camera)) { tag = "MainCamera" }.GetComponent<Camera>();
            Cam.nearClipPlane = 0.5f; Cam.farClipPlane = 6000f;
            Cam.fieldOfView = (float)Rig.FovY;
            var sun = FindFirstObjectByType<Light>();
            if (sun == null) sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.transform.rotation = Quaternion.Euler(52, -35, 0);
            sun.intensity = 1.15f;
            sun.shadows = LightShadows.Soft;
            hud = new RaceHud(this);
            hand = new HandHud(this);
            NewRace();
        }

        public void NewRace()
        {
            var t = Tracks.ById(trackId);
            if (handsOff) { Driver = new RaceDriver(new Race(RaceSetup.Quick(t, (uint)seed))); Desk = null; HudInput = null; Pad = null; bot = null; }
            else
            {
                int speed = Driver?.SpeedIndex ?? 0;
                Driver = new RaceDriver(InCareer ? RaceSource() : PlayDesk.NewRace(t, (uint)seed, persona)) { SpeedIndex = speed };
                Desk = new PlayDesk(Driver);
                HudInput = new HudInput(Desk, Layout);
                Pad = new PadInput(Desk, Layout, HudInput) { Style = padReader.LastStyle, Active = Pad?.Active ?? false };
                bot = new MousePlayer(Desk, Layout, Readouts, HudInput);
            }
            Rebuild();
            Rig.Cut();
        }

        /// <summary>Rebuilds the track and cars for the current race and look (a new race or a look change).</summary>
        void Rebuild()
        {
            if (track != null) Destroy(track);
            if (carsRoot != null) Destroy(carsRoot);
            rigs.Clear();
            Kit.ClearMaterials();
            track = TrackBuilder.Build(Driver.Layout, Look);
            carsRoot = new GameObject("Cars");
            fx = new FxView(carsRoot.transform);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.52f, 0.54f, 0.58f);
            Cam.clearFlags = CameraClearFlags.SolidColor;
            Cam.backgroundColor = Kit.C(Look.Sky);
            ApplyPixelSize();
        }

        void ApplyPixelSize()
        {
            int px = Mathf.Max(1, Look.PixelSize);
            if (LowRes != null) { Cam.targetTexture = null; LowRes.Release(); Destroy(LowRes); LowRes = null; }
            if (px > 1)
            {
                LowRes = new RenderTexture(Mathf.Max(64, Screen.width / px), Mathf.Max(36, Screen.height / px), 24) { filterMode = FilterMode.Point, name = "Low res " + Look.Key };
                Cam.targetTexture = LowRes;
            }
        }

        public void NextLook()
        {
            Look = Looks.Next(Look);
            Rebuild();
        }

        public void NextTrack(int dir)
        {
            if (InCareer) return;
            var all = Tracks.All;
            int i = 0;
            for (int k = 0; k < all.Count; k++) if (all[k].Id == trackId) i = k;
            trackId = all[((i + dir) % all.Count + all.Count) % all.Count].Id;
            NewRace();
        }

        /// <summary>At the flag: back to the career's reward screen, or a new race outside a career.</summary>
        public void FinishOrRestart()
        {
            if (InCareer) { if (Driver != null && Driver.Race.Done) Finished?.Invoke(Driver.Race); }
            else Restart(true);
        }

        public void Restart(bool newSeed)
        {
            if (InCareer) return;
            if (newSeed) seed = Random.Range(1, 100000);
            NewRace();
        }

        void Update()
        {
            if (Driver == null) return;
            if (LowRes != null && (LowRes.width != Mathf.Max(64, Screen.width / Look.PixelSize) || LowRes.height != Mathf.Max(36, Screen.height / Look.PixelSize))) ApplyPixelSize();
            float dt = Time.unscaledDeltaTime;
            if (Desk != null)
            {
                Desk.Frame(dt);
                Readouts.Update(Desk);
                Layout.Update(Screen.width, Screen.height, Desk, Readouts);
                if (Autoplay) bot.Frame();
                else padReader.Read(Pad, dt);
                Pad.Frame();
                if (Desk.Race.Done && padReader.ConfirmOrStart) { FinishOrRestart(); return; }
            }
            else Driver.Advance(dt);
            double now = Driver.ViewT;
            CarPose me = default;
            foreach (var p in Driver.Poses)
            {
                if (!rigs.TryGetValue(p.Id, out var rig)) rigs[p.Id] = rig = new CarRig(carsRoot.transform, p);
                rig.Set(p, now);
                if (p.IsMe) me = p;
            }
            // the pace car's rig goes when the sim drops the pace car
            if (rigs.TryGetValue(-1, out var pace) && Driver.Race.PaceCar == null) { pace.Destroy(); rigs.Remove(-1); }

            Rig.Update(Driver.Layout, me, Driver.Race.Phase, Cam.aspect, dt);
            Cam.transform.position = Kit.V(Rig.Eye);
            Cam.transform.LookAt(Kit.V(Rig.LookAt));
            fx.Draw(Driver.Motion.Fx, Driver.Motion.T, Cam);
        }

        void OnGUI()
        {
            hud?.Draw();    // the look's low-res frame, pylon, banners, booth, controls
            hand?.Draw();   // the hand and beats HUD on top
        }

        void OnApplicationFocus(bool focused) { if (!focused) { hand?.LostFocus(); padReader.Release(Pad); } }

        void OnDestroy()
        {
            if (LowRes != null) { if (Cam != null) Cam.targetTexture = null; LowRes.Release(); Destroy(LowRes); }
            // a career race (step 34) is torn down for the reward screen: the track and cars go with it
            if (track != null) Destroy(track);
            if (carsRoot != null) Destroy(carsRoot);
            Kit.ClearMaterials();
        }
    }
}
