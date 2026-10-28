using GreenFlag.View;
using UnityEngine;

namespace GreenFlag.Presentation
{
    /// <summary>
    /// One car built from boxes in its team's paint: body, cabin, windshield, roof stripe, wheels, brake lights. The pace car
    /// gets a flashing light bar, your car a marker above it. Step 30 adds roll, pitch, smoke and sparks; step 45 real models.
    /// </summary>
    public sealed class CarRig
    {
        public readonly GameObject Root;
        readonly MeshRenderer body, cabin;
        readonly MeshRenderer[] brake = new MeshRenderer[2], bar = new MeshRenderer[2];
        readonly Transform marker;
        readonly Livery livery;
        readonly bool isPace;
        bool wrecked, braking;

        static readonly Rgb Glass = Rgb.Hex("#141e2d"), Tyre = Rgb.Hex("#16181d"), BrakeOff = Rgb.Hex("#5a1214"), BrakeOn = Rgb.Hex("#ff3b3b"), Amber = Rgb.Hex("#ffb000"), Orange = Rgb.Hex("#ff6a00");

        public CarRig(Transform parent, CarPose pose)
        {
            isPace = pose.IsPace;
            livery = isPace ? Looks.PaceCar : Looks.Team(pose.Team);
            Root = new GameObject(isPace ? "Pace car" : "Car " + pose.Id);
            Root.transform.SetParent(parent, false);
            var t = Root.transform;
            float L = (float)TrackLayout.CarL * Kit.S, W = (float)TrackLayout.CarW * Kit.S, H = (float)TrackLayout.CarH * Kit.S;
            float bodyH = H * 0.55f, ride = H * 0.12f;
            body = Kit.Box("Body", t, new Vector3(0, ride + bodyH / 2, 0), new Vector3(W, bodyH, L), livery.Body).GetComponent<MeshRenderer>();
            cabin = Kit.Box("Cabin", t, new Vector3(0, ride + bodyH + H * 0.18f, -L * 0.06f), new Vector3(W * 0.8f, H * 0.36f, L * 0.4f), livery.Body.Shade(-0.15f)).GetComponent<MeshRenderer>();
            Kit.Box("Windshield", t, new Vector3(0, ride + bodyH + H * 0.16f, L * 0.15f), new Vector3(W * 0.76f, H * 0.3f, L * 0.06f), Glass);
            Kit.Box("Stripe", t, new Vector3(0, ride + bodyH + 0.01f, 0), new Vector3(W * 0.2f, 0.02f, L * 0.98f), livery.Stripe);
            Kit.Box("Stripe roof", t, new Vector3(0, ride + bodyH + H * 0.36f + 0.01f, -L * 0.06f), new Vector3(W * 0.2f, 0.02f, L * 0.4f), livery.Stripe);
            foreach (float x in new[] { -1f, 1f })
                foreach (float z in new[] { -1f, 1f })
                    Kit.Box("Wheel", t, new Vector3(x * W * 0.47f, H * 0.2f, z * L * 0.32f), new Vector3(W * 0.16f, H * 0.4f, L * 0.18f), Tyre);
            for (int i = 0; i < 2; i++)
                brake[i] = Kit.Box("Brake light", t, new Vector3((i * 2 - 1) * W * 0.32f, ride + bodyH * 0.7f, -L / 2 - 0.02f), new Vector3(W * 0.2f, bodyH * 0.25f, 0.05f), BrakeOff).GetComponent<MeshRenderer>();
            if (isPace)
                for (int i = 0; i < 2; i++)
                    bar[i] = Kit.Box("Light bar", t, new Vector3((i * 2 - 1) * W * 0.2f, ride + bodyH + H * 0.42f, -L * 0.06f), new Vector3(W * 0.35f, H * 0.12f, L * 0.08f), Amber).GetComponent<MeshRenderer>();
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
            t.localPosition = Kit.V(p.P);
            t.localRotation = Kit.Yaw(p.Yaw);
            if (isPace)
            {
                // no fade in the Clean look's opaque materials: the pace car simply leaves once it has faded half way
                Root.SetActive(p.Alpha > 0.5);
                bool on = ((int)(time * 5)) % 2 == 0;
                bar[0].sharedMaterial = Kit.Mat(on ? Amber : Orange);
                bar[1].sharedMaterial = Kit.Mat(on ? Orange : Amber);
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

        public void Destroy() => Object.Destroy(Root);
    }
}
