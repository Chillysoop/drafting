using System.Collections.Generic;
using GreenFlag.View;
using UnityEngine;

namespace GreenFlag.Presentation
{
    /// <summary>
    /// Draws the view's particles (step 30) as three meshes rebuilt every frame: skid marks flat on the track, smoke puffs as
    /// quads facing the camera, sparks as short glowing streaks along their flight. No prefabs or particle systems: CarMotion
    /// already decided where every particle is, so this only turns the lists into triangles.
    /// </summary>
    public sealed class FxView
    {
        readonly GameObject root;
        readonly Mesh skids, smoke, sparks;
        readonly List<Vector3> v = new List<Vector3>();
        readonly List<Color32> c = new List<Color32>();
        readonly List<int> tri = new List<int>();

        static readonly Color Rubber = new Color(0.047f, 0.047f, 0.063f), Tire = new Color(0.78f, 0.80f, 0.84f), Engine = new Color(0.27f, 0.28f, 0.31f), Dust = new Color(0.60f, 0.48f, 0.33f);

        public FxView(Transform parent)
        {
            root = new GameObject("Fx");
            root.transform.SetParent(parent, false);
            var shader = Resources.Load<Shader>("GreenFlag/Fx") ?? Shader.Find("GreenFlag/Fx");
            skids = Layer("Skid marks", shader, false, 3000);
            smoke = Layer("Smoke", shader, false, 3002);
            sparks = Layer("Sparks", shader, true, 3001);
        }

        Mesh Layer(string name, Shader shader, bool additive, int queue)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root.transform, false);
            var m = new Mesh { name = name };
            m.MarkDynamic();
            go.AddComponent<MeshFilter>().sharedMesh = m;
            var mr = go.AddComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            if (shader != null)
            {
                var mat = new Material(shader) { name = "GF Fx " + name, renderQueue = queue };
                mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetFloat("_DstBlend", (float)(additive ? UnityEngine.Rendering.BlendMode.One : UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha));
                mr.sharedMaterial = mat;
            }
            return m;
        }

        public void Draw(Fx fx, double now, Camera cam)
        {
            var right = cam.transform.right; var up = cam.transform.up;

            // skid marks: thin dark quads just above the asphalt, fading over 30 s
            Begin();
            float hw = (float)(0.006 * Kit.S);
            foreach (var k in fx.Skids)
            {
                Vector3 a = Kit.V(k.A, 0.0012), b = Kit.V(k.B, 0.0012);
                var d = b - a; d.y = 0;
                if (d.sqrMagnitude < 1e-6f) continue;
                var n = new Vector3(-d.z, 0, d.x).normalized * hw;
                var col = Rubber; col.a = (float)Fx.SkidAlpha(k, now);
                Quad(a - n, a + n, b + n, b - n, col);
            }
            End(skids);

            // smoke: camera-facing quads that rise, grow and fade
            Begin();
            foreach (var p in fx.Puffs)
            {
                var col = p.Kind == PuffKind.Engine ? Engine : p.Kind == PuffKind.Dust ? Dust : Tire;
                col.a = (float)(0.5 * p.Alpha);
                var at = Kit.V(new Vec2(p.X, p.Z), p.Y + p.Radius);
                float r = (float)(p.Radius * Kit.S);
                Vector3 rx = right * r, uy = up * r;
                Quad(at - rx - uy, at - rx + uy, at + rx + uy, at + rx - uy, col);
            }
            End(smoke);

            // sparks: a streak from where the spark was 50 ms ago to where it is, white-hot to orange as it cools
            Begin();
            float sw = 0.05f;
            foreach (var k in fx.Sparks)
            {
                float f = (float)(1 - k.T / k.Life);
                var col = new Color(1, (150 + 90 * f) / 255f, 60 * f / 255f, f);
                Vector3 a = Kit.V(new Vec3(k.X, k.Y, k.Z)), b = Kit.V(new Vec3(k.X - k.Vx * 0.05, k.Y - k.Vy * 0.05, k.Z - k.Vz * 0.05));
                var w = Vector3.Cross(b - a, cam.transform.forward).normalized * sw;
                Quad(a - w, a + w, b + w, b - w, col);
            }
            End(sparks);
        }

        void Begin() { v.Clear(); c.Clear(); tri.Clear(); }

        void Quad(Vector3 a, Vector3 b, Vector3 d, Vector3 e, Color col)
        {
            int i = v.Count;
            v.Add(a); v.Add(b); v.Add(d); v.Add(e);
            Color32 k = col; c.Add(k); c.Add(k); c.Add(k); c.Add(k);
            tri.Add(i); tri.Add(i + 1); tri.Add(i + 2); tri.Add(i); tri.Add(i + 2); tri.Add(i + 3);
        }

        void End(Mesh m)
        {
            m.Clear();
            m.SetVertices(v);
            m.SetColors(c);
            m.SetTriangles(tri, 0);
            // world-space vertices under an object at the origin: give it huge bounds so it is never culled
            m.bounds = new Bounds(Vector3.zero, Vector3.one * 100000);
        }

        public void Destroy() => Object.Destroy(root);
    }
}
