using System.Collections.Generic;
using GreenFlag.View;
using UnityEngine;

namespace GreenFlag.Presentation
{
    /// <summary>
    /// Builds a track from its layout in the current look: grounds, infield, apron, asphalt, lane line, outside wall, pit road,
    /// the start/finish line and a grandstand on the front stretch. Everything is generated meshes and boxes, so all ten tracks
    /// (and any added later) come straight from the sim's track data with no hand-made art. Step 45 swaps in real models.
    /// </summary>
    public static class TrackBuilder
    {
        public static GameObject Build(TrackLayout l, LookDef look)
        {
            var root = new GameObject("Track " + l.Track.Id).transform;

            // grounds: one big plane under everything
            double pad = 3;
            var g0 = new Vec2(l.MinX - pad, l.MinZ - pad); var g1 = new Vec2(l.MaxX + pad, l.MaxZ + pad);
            Kit.MeshObject("Grounds", root, Kit.Strip(new[] { g0, new Vec2(g1.X, g0.Z) }, new[] { new Vec2(g0.X, g1.Z), g1 }, -0.002), look.Grounds);

            // the oval infield is the inside of the apron; a figure-8 only gets a strip on the pit side; road courses sit on the grounds
            if (!l.Track.IsPath) Kit.MeshObject("Infield", root, Kit.Fan(l.Ring(TrackLayout.ApronLane), 0.0005), look.Infield);
            else if (l.Track.Fig8) Kit.MeshObject("Infield", root, Kit.Ribbon(l, -(TrackLayout.TrackHW + 0.07) / TrackLayout.LaneW, -(TrackLayout.TrackHW + 0.5) / TrackLayout.LaneW, 0.0005, l.FrontU(0.05), l.FrontU(l.FrontStraight - 0.05), 24), look.Infield);

            Kit.MeshObject("Apron", root, Kit.Ribbon(l, TrackLayout.InnerLane, TrackLayout.ApronLane, 0.001), look.Apron);
            Kit.MeshObject("Asphalt", root, Kit.Ribbon(l, TrackLayout.OuterLane, TrackLayout.InnerLane, 0.0015), look.Asphalt);
            Kit.MeshObject("Lane line", root, Kit.Ribbon(l, TrackLayout.LaneLine - 0.03, TrackLayout.LaneLine + 0.03, 0.0018), Kit.Over(look.LaneLine, look.Asphalt));

            // pit road: the surface along the front stretch, the lane cars drive, and a box per stall
            Kit.MeshObject("Pit road", root, Kit.Ribbon(l, TrackLayout.PitPadIn, TrackLayout.PitPadOut, 0.0012, l.FrontU(0.1), l.FrontU(l.FrontStraight - 0.1), 24), look.Pit);
            var g = l.Shape.Pit; var lane = new List<Vec2>();
            for (int i = 0; i <= 48; i++) { double s = g.E0 + (g.X1 - g.E0) * i / 48; lane.Add(l.AtDist(s, l.PitRoadLane(s)).P); }
            Kit.MeshObject("Pit lane", root, Kit.Line(lane, 0.05, 0.0013), look.Pit.Shade(0.08f));

            // outside wall, broken where a figure-8 crosses itself
            var cross = new List<Vec2>();
            foreach (double cu in l.CrossoverU()) cross.Add(l.At(cu, 0).P);
            var runs = new List<List<Vec2>>(); var run = new List<Vec2>();
            foreach (var p in l.Ring(TrackLayout.OuterLane + 0.05))
            {
                bool gap = false;
                foreach (var c in cross) if ((p - c).Length < 0.42) gap = true;
                if (gap) { if (run.Count > 1) runs.Add(run); run = new List<Vec2>(); }
                else run.Add(p);
            }
            if (run.Count > 1) runs.Add(run);
            Kit.MeshObject("Wall", root, Kit.Wall(runs, 0.025), look.Wall);

            // start/finish: a checkered band across the track
            var sf = new GameObject("Start finish").transform; sf.SetParent(root, false);
            var at = l.At(l.StartFinishU, 0);
            sf.localPosition = Kit.V(at.P, 0.002); sf.localRotation = Kit.Yaw(at.Yaw);
            int cells = 10; float cw = (float)(TrackLayout.TrackHW * 2 / cells * Kit.S), cl = (float)(0.012 * Kit.S);
            for (int i = 0; i < cells; i++)
                for (int j = 0; j < 2; j++)
                    Kit.Box("Check", sf, new Vector3(-TrackLayout.TrackHW.ToF() * Kit.S + cw * (i + 0.5f), 0.01f, cl * (j - 0.5f)), new Vector3(cw, 0.02f, cl), (i + j) % 2 == 0 ? Rgb.Hex("#eeeeee") : Rgb.Hex("#111111"));

            // a grandstand outside the front stretch, stepped
            var st = new GameObject("Grandstand").transform; st.SetParent(root, false);
            var mid = l.At(l.StartFinishU, TrackLayout.OuterLane);
            st.localPosition = Kit.V(mid.P + mid.N * 0.12); st.localRotation = Kit.Yaw(mid.Yaw);
            float len = (float)(l.FrontStraight * 0.8 * Kit.S);
            for (int k = 0; k < 4; k++)
            {
                float d = (float)((0.05 + k * 0.07) * Kit.S), h = (float)((0.02 + k * 0.025) * Kit.S);
                // in the stand's frame forward is the direction of travel, so the outside (N) is straight along local X
                Vector3 side = Quaternion.Inverse(st.localRotation) * new Vector3((float)mid.N.X, 0, (float)mid.N.Z);
                Kit.Box("Tier " + k, st, side * d + Vector3.up * h / 2, new Vector3(0.07f * Kit.S, h, len), look.Stand.Shade(k * 0.06f));
            }
            return root.gameObject;
        }

        static float ToF(this double d) => (float)d;
    }
}
