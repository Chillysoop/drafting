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
    /// in FxView). Your car runs hands-off until the hand UI arrives in step 31.
    /// Keys: C camera, 1-4 speed, Space pause, R new race, T / Shift+T track, L look, F fast-forward to the flag.
    /// </summary>
    public sealed class RaceView : MonoBehaviour
    {
        [Tooltip("Track id from GreenFlag.Sim.Tracks (mill, carver, talon, pine, ridge, pinek, hollow, cedar, milll, cross).")]
        public string trackId = "carver";
        public int seed = 61;
        [Tooltip("clean, pixel, poly or tv. Empty = Looks.Default (Clean until board Q37 is answered).")]
        public string lookKey = "";

        public RaceDriver Driver { get; private set; }
        public CameraRig Rig { get; } = new CameraRig();
        public LookDef Look { get; private set; }
        public Camera Cam { get; private set; }
        public RenderTexture LowRes { get; private set; }

        GameObject track, carsRoot;
        readonly Dictionary<int, CarRig> rigs = new Dictionary<int, CarRig>();
        RaceHud hud;
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
            NewRace();
        }

        public void NewRace()
        {
            var t = Tracks.ById(trackId);
            Driver = new RaceDriver(new Race(RaceSetup.Quick(t, (uint)seed)));
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
            var all = Tracks.All;
            int i = 0;
            for (int k = 0; k < all.Count; k++) if (all[k].Id == trackId) i = k;
            trackId = all[((i + dir) % all.Count + all.Count) % all.Count].Id;
            NewRace();
        }

        public void Restart(bool newSeed)
        {
            if (newSeed) seed = Random.Range(1, 100000);
            NewRace();
        }

        void Update()
        {
            if (Driver == null) return;
            if (LowRes != null && (LowRes.width != Mathf.Max(64, Screen.width / Look.PixelSize) || LowRes.height != Mathf.Max(36, Screen.height / Look.PixelSize))) ApplyPixelSize();
            float dt = Time.unscaledDeltaTime;
            Driver.Advance(dt);
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

        void OnGUI() => hud?.Draw();

        void OnDestroy()
        {
            if (LowRes != null) { LowRes.Release(); Destroy(LowRes); }
            Kit.ClearMaterials();
        }
    }
}
