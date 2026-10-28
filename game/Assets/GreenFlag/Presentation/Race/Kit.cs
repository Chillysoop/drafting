using System.Collections.Generic;
using GreenFlag.View;
using UnityEngine;

namespace GreenFlag.Presentation
{
    /// <summary>Shared helpers for the race view: track units to metres, colours, materials and simple meshes.</summary>
    public static class Kit
    {
        /// <summary>Metres per track unit. A stock car is 0.085 units, so about 5.1 m.</summary>
        public const float S = 60f;

        public static Vector3 V(Vec2 p, double y = 0) => new Vector3((float)(p.X * S), (float)(y * S), (float)(p.Z * S));
        public static Vector3 V(Vec3 p) => new Vector3((float)(p.X * S), (float)(p.Y * S), (float)(p.Z * S));
        public static Color C(Rgb c) => new Color(c.R, c.G, c.B, c.A);
        public static Quaternion Yaw(double radians) => Quaternion.Euler(0, (float)(radians * Mathf.Rad2Deg), 0);

        /// <summary>A colour with alpha flattened onto the colour under it (the slice's translucent lane line on asphalt).</summary>
        public static Rgb Over(Rgb top, Rgb under)
        {
            float a = top.A;
            return new Rgb(under.R + (top.R - under.R) * a, under.G + (top.G - under.G) * a, under.B + (top.B - under.B) * a);
        }

        static Material template;
        static readonly Dictionary<string, Material> Mats = new Dictionary<string, Material>();

        /// <summary>
        /// One opaque, double-sided material per colour. The template is the URP Lit material the project bootstrap saves under
        /// Resources (so the shader is always in the build); without it, the URP shaders are looked up by name.
        /// </summary>
        public static Material Mat(Rgb c)
        {
            string key = c.ToString();
            if (Mats.TryGetValue(key, out var m) && m != null) return m;
            if (template == null)
            {
                template = Resources.Load<Material>("GreenFlag/LookBase");
                if (template == null)
                {
                    var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Standard");
                    template = new Material(sh);
                }
            }
            m = new Material(template) { name = "GF " + key };
            var col = C(c); col.a = 1;
            m.color = col;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", col);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.15f);
            if (m.HasProperty("_Cull")) m.SetFloat("_Cull", 0f);
            Mats[key] = m;
            return m;
        }

        /// <summary>Forget cached materials (a look change rebuilds everything).</summary>
        public static void ClearMaterials()
        {
            foreach (var m in Mats.Values) if (m != null) Object.Destroy(m);
            Mats.Clear();
        }

        public static GameObject MeshObject(string name, Transform parent, Mesh mesh, Rgb color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = Mat(color);
            return go;
        }

        /// <summary>A box with no collider.</summary>
        public static GameObject Box(string name, Transform parent, Vector3 localPos, Vector3 size, Rgb color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = Mat(color);
            return go;
        }

        /// <summary>A flat strip between two lanes of the track at height y (track units), from u0 to u1.</summary>
        public static Mesh Ribbon(TrackLayout l, double laneA, double laneB, double y, double u0 = 0, double u1 = 1, int n = 0)
        {
            if (n <= 0) n = Mathf.Max(8, (int)((u1 - u0) * (l.Track.IsPath ? 560 : 200)));
            var a = new Vec2[n + 1]; var b = new Vec2[n + 1];
            for (int i = 0; i <= n; i++) { double u = u0 + (u1 - u0) * i / n; a[i] = l.At(u, laneA).P; b[i] = l.At(u, laneB).P; }
            return Strip(a, b, y);
        }

        /// <summary>A flat strip between two matching polylines.</summary>
        public static Mesh Strip(IList<Vec2> a, IList<Vec2> b, double y)
        {
            int n = a.Count;
            var v = new Vector3[n * 2]; var nm = new Vector3[n * 2]; var t = new int[(n - 1) * 6];
            for (int i = 0; i < n; i++) { v[i * 2] = V(a[i], y); v[i * 2 + 1] = V(b[i], y); nm[i * 2] = nm[i * 2 + 1] = Vector3.up; }
            for (int i = 0; i < n - 1; i++)
            {
                int k = i * 6, p = i * 2;
                t[k] = p; t[k + 1] = p + 2; t[k + 2] = p + 1;
                t[k + 3] = p + 1; t[k + 4] = p + 2; t[k + 5] = p + 3;
            }
            return Build(v, nm, t);
        }

        /// <summary>A strip of half-width w (track units) along a polyline.</summary>
        public static Mesh Line(IList<Vec2> pts, double w, double y)
        {
            int n = pts.Count;
            var a = new Vec2[n]; var b = new Vec2[n];
            for (int i = 0; i < n; i++)
            {
                var d = pts[System.Math.Min(n - 1, i + 1)] - pts[System.Math.Max(0, i - 1)];
                double len = d.Length; if (len < 1e-9) len = 1;
                var nn = new Vec2(-d.Z / len, d.X / len);
                a[i] = pts[i] + nn * w; b[i] = pts[i] - nn * w;
            }
            return Strip(a, b, y);
        }

        /// <summary>A filled polygon as a fan around its centroid (fine for the oval infields, which are convex).</summary>
        public static Mesh Fan(IList<Vec2> ring, double y)
        {
            int n = ring.Count;
            double cx = 0, cz = 0; foreach (var p in ring) { cx += p.X; cz += p.Z; }
            var v = new Vector3[n + 1]; var nm = new Vector3[n + 1]; var t = new int[n * 3];
            v[n] = V(new Vec2(cx / n, cz / n), y); nm[n] = Vector3.up;
            for (int i = 0; i < n; i++) { v[i] = V(ring[i], y); nm[i] = Vector3.up; t[i * 3] = n; t[i * 3 + 1] = i; t[i * 3 + 2] = (i + 1) % n; }
            return Build(v, nm, t);
        }

        /// <summary>Vertical wall panels along polyline runs, h track units high.</summary>
        public static Mesh Wall(List<List<Vec2>> runs, double h)
        {
            var v = new List<Vector3>(); var nm = new List<Vector3>(); var t = new List<int>();
            foreach (var run in runs)
            {
                for (int i = 0; i + 1 < run.Count; i++)
                {
                    int b = v.Count;
                    var d = run[i + 1] - run[i];
                    var n = new Vector3((float)-d.Z, 0, (float)d.X).normalized;
                    v.Add(V(run[i], 0)); v.Add(V(run[i], h)); v.Add(V(run[i + 1], 0)); v.Add(V(run[i + 1], h));
                    nm.Add(n); nm.Add(n); nm.Add(n); nm.Add(n);
                    t.Add(b); t.Add(b + 1); t.Add(b + 2); t.Add(b + 2); t.Add(b + 1); t.Add(b + 3);
                }
            }
            return Build(v.ToArray(), nm.ToArray(), t.ToArray());
        }

        static Mesh Build(Vector3[] v, Vector3[] nm, int[] t)
        {
            var m = new Mesh();
            if (v.Length > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.vertices = v; m.normals = nm; m.triangles = t;
            m.RecalculateBounds();
            return m;
        }
    }
}
