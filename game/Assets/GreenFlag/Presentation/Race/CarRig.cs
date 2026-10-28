using GreenFlag.View;
using UnityEngine;

namespace GreenFlag.Presentation
{
    /// <summary>
    /// One car built from boxes in its team's paint: body, cabin, windshield, roof stripe, wheels, brake lights. The pace car
    /// gets a flashing light bar, your car a marker above it. Step 30: everything above the wheels sits on a sprung pivot that
    /// rolls, pitches and hops with the pose; damage crumples the nose and tail and scrapes the flanks; a card outcome glows
    /// under the car and a clean move trails speed streaks. Step 45 brings real models.
    /// </summary>
    public sealed class CarRig
    {
        public readonly GameObject Root;
        readonly Transform sprung;
        readonly MeshRenderer body, cabin;
        readonly MeshRenderer[] brake = new MeshRenderer[2], bar = new MeshRenderer[2];
        readonly Transform marker, noseDent, tailDent, glow;
        readonly Transform[] scrape = new Transform[2], streak = new Transform[2];
        readonly MeshRenderer glowR;
        readonly Livery livery;
        readonly bool isPace;
        readonly float L, W, H, bodyH, ride;
        bool wrecked, braking;
        string glowKey;

        static readonly Rgb Glass = Rgb.Hex("#141e2d"), Tyre = Rgb.Hex("#16181d"), BrakeOff = Rgb.Hex("#5a1214"), BrakeOn = Rgb.Hex("#ff3b3b"), Amber = Rgb.Hex("#ffb000"), Orange = Rgb.Hex("#ff6a00");
        static readonly Rgb Scrape = Rgb.Hex("#1a1a20"), Streak = Rgb.Hex("#e8eef8");

        public CarRig(Transform parent, CarPose pose)
        {
            isPace = pose.IsPace;
            livery = isPace ? Looks.PaceCar : Looks.Team(pose.Team);
            Root = new GameObject(isPace ? "Pace car" : "Car " + pose.Id);
            Root.transform.SetParent(parent, false);
            var t = Root.transform;
            L = (float)TrackLayout.CarL * Kit.S; W = (float)TrackLayout.CarW * Kit.S; H = (float)TrackLayout.CarH * Kit.S;
            bodyH = H * 0.55f; ride = H * 0.12f;
            // the wheels stay on the ground; the body rolls and pitches over them about a point at axle height
            foreach (float x in new[] { -1f, 1f })
                foreach (float z in new[] { -1f, 1f })
                    Kit.Box("Wheel", t, new Vector3(x * W * 0.47f, H * 0.2f, z * L * 0.32f), new Vector3(W * 0.16f, H * 0.4f, L * 0.18f), Tyre);
            sprung = new GameObject("Sprung").transform;
            sprung.SetParent(t, false);
            sprung.localPosition = new Vector3(0, H * 0.2f, 0);
            var s = sprung;
            float y0 = -H * 0.2f;   // child heights below are from the ground, as before
            body = Kit.Box("Body", s, new Vector3(0, y0 + ride + bodyH / 2, 0), new Vector3(W, bodyH, L), livery.Body).GetComponent<MeshRenderer>();
            cabin = Kit.Box("Cabin", s, new Vector3(0, y0 + ride + bodyH + H * 0.18f, -L * 0.06f), new Vector3(W * 0.8f, H * 0.36f, L * 0.4f), livery.Body.Shade(-0.15f)).GetComponent<MeshRenderer>();
            Kit.Box("Windshield", s, new Vector3(0, y0 + ride + bodyH + H * 0.16f, L * 0.15f), new Vector3(W * 0.76f, H * 0.3f, L * 0.06f), Glass);
            Kit.Box("Stripe", s, new Vector3(0, y0 + ride + bodyH + 0.01f, 0), new Vector3(W * 0.2f, 0.02f, L * 0.98f), livery.Stripe);
            Kit.Box("Stripe roof", s, new Vector3(0, y0 + ride + bodyH + H * 0.36f + 0.01f, -L * 0.06f), new Vector3(W * 0.2f, 0.02f, L * 0.4f), livery.Stripe);
            for (int i = 0; i < 2; i++)
                brake[i] = Kit.Box("Brake light", s, new Vector3((i * 2 - 1) * W * 0.32f, y0 + ride + bodyH * 0.7f, -L / 2 - 0.02f), new Vector3(W * 0.2f, bodyH * 0.25f, 0.05f), BrakeOff).GetComponent<MeshRenderer>();
            if (isPace)
                for (int i = 0; i < 2; i++)
                    bar[i] = Kit.Box("Light bar", s, new Vector3((i * 2 - 1) * W * 0.2f, y0 + ride + bodyH + H * 0.42f, -L * 0.06f), new Vector3(W * 0.35f, H * 0.12f, L * 0.08f), Amber).GetComponent<MeshRenderer>();
            else
            {
                // damage: dark crumple blocks over the ends and scrape lines down the flanks, scaled by the zone's damage
                var dent = livery.Body.Shade(-0.55f);
                noseDent = Kit.Box("Nose damage", s, Vector3.zero, Vector3.one, dent).transform;
                tailDent = Kit.Box("Tail damage", s, Vector3.zero, Vector3.one, dent).transform;
                for (int i = 0; i < 2; i++)
                {
                    scrape[i] = Kit.Box("Scrape", s, new Vector3((i * 2 - 1) * (W / 2 + 0.01f), y0 + ride + bodyH * 0.55f, 0), new Vector3(0.02f, bodyH * 0.18f, L * 0.5f), Scrape).transform;
                    streak[i] = Kit.Box("Streak", t, Vector3.zero, Vector3.one, Streak).transform;
                }
                noseDent.gameObject.SetActive(false); tailDent.gameObject.SetActive(false);
                foreach (var x in scrape) x.gameObject.SetActive(false);
                foreach (var x in streak) x.gameObject.SetActive(false);
                glow = Kit.Box("Glow", t, new Vector3(0, 0.03f, 0), new Vector3(W * 1.7f, 0.02f, L * 1.35f), CarMotion.GlowSuccess).transform;
                glowR = glow.GetComponent<MeshRenderer>();
                glow.gameObject.SetActive(false);
            }
            if (pose.IsMe)
            {
                // a gold diamond floating over your car
                marker = Kit.Box("You", t, new Vector3(0, H * 2.6f, 0), Vector3.one * W * 0.55f, Looks.You).transform;
                marker.localRotation = Quaternion.Euler(45, 0, 45);
            }
        }

        public void Set(CarPose p, double time)
        {
            var t = Root.transform;
            t.localPosition = Kit.V(p.P, p.Lift);
            t.localRotation = Kit.Yaw(p.Yaw);
            // + roll = right side down (a clockwise turn about the forward axis seen from behind); + pitch = nose down
            sprung.localRotation = Quaternion.Euler((float)(p.Pitch * Mathf.Rad2Deg), 0, (float)(-p.Roll * Mathf.Rad2Deg));
            if (isPace)
            {
                // no fade in the Clean look's opaque materials: the pace car simply leaves once it has faded half way
                Root.SetActive(p.Alpha > 0.5);
                bool on = ((int)(time * 5)) % 2 == 0;
                bar[0].sharedMaterial = Kit.Mat(on ? Amber : Orange);
                bar[1].sharedMaterial = Kit.Mat(on ? Orange : Amber);
            }
            else
            {
                Dent(noseDent, p.Nose, 1);
                Dent(tailDent, p.Tail, -1);
                bool scraped = p.Side >= 1;
                for (int i = 0; i < 2; i++)
                {
                    scrape[i].gameObject.SetActive(scraped);
                    if (scraped) scrape[i].localScale = new Vector3(0.02f, bodyH * 0.18f, L * Mathf.Clamp(0.25f + (float)p.Side * 0.015f, 0.25f, 0.8f));
                    bool on = p.Streak > 0.02 && !p.Dnf;
                    streak[i].gameObject.SetActive(on);
                    if (on)
                    {
                        float len = L * (0.6f + 1.4f * (float)p.Streak);
                        streak[i].localScale = new Vector3(0.05f, 0.05f, len);
                        streak[i].localPosition = new Vector3((i * 2 - 1) * W * 0.55f, H * 0.5f, -L / 2 - len / 2 - 0.2f);
                    }
                }
                bool g = p.GlowA > 0.02;
                glow.gameObject.SetActive(g);
                if (g)
                {
                    string key = p.Glow.ToString();
                    if (key != glowKey) { glowKey = key; glowR.sharedMaterial = Kit.Mat(p.Glow); }
                    float k = 0.8f + 0.2f * (float)p.GlowA;
                    glow.localScale = new Vector3(W * 1.7f * k, 0.02f, L * 1.35f * k);
                }
            }
            if (p.Dnf != wrecked)
            {
                wrecked = p.Dnf;
                body.sharedMaterial = Kit.Mat(wrecked ? Looks.Wrecked : livery.Body);
                cabin.sharedMaterial = Kit.Mat(wrecked ? Looks.Wrecked.Shade(-0.15f) : livery.Body.Shade(-0.15f));
            }
            if (p.Braking != braking)
            {
                braking = p.Braking;
                foreach (var b in brake) b.sharedMaterial = Kit.Mat(braking ? BrakeOn : BrakeOff);
            }
            if (marker != null)
            {
                marker.gameObject.SetActive(!p.Dnf);
                marker.localRotation = Quaternion.Euler(45, (float)(time * 90), 45);
            }
        }

        /// <summary>A crumple block over one end (dir 1 = nose, -1 = tail): deeper and lower the more damage the zone has.</summary>
        void Dent(Transform d, double amt, int dir)
        {
            bool on = amt >= 1;
            d.gameObject.SetActive(on);
            if (!on) return;
            // the slice's crumple: 5% of the length plus 0.6% per point, up to 26%
            float f = Mathf.Clamp(0.05f + (float)amt * 0.006f, 0, 0.26f), depth = L * f;
            float h = bodyH * (1.04f - f * 0.8f), y0 = -H * 0.2f;
            d.localScale = new Vector3(W * 1.02f, h, depth);
            d.localPosition = new Vector3(0, y0 + ride + h / 2, dir * (L / 2 - depth / 2 + 0.01f));
        }

        public void Destroy() => Object.Destroy(Root);
    }
}
