using GreenFlag.View;
using UnityEngine;
#if GF_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace GreenFlag.Presentation
{
    /// <summary>
    /// Reads the controller each frame (roadmap step 32) with Unity's Input System and hands PadInput what changed: the
    /// d-pad and left stick as directions (held ones repeat), the face buttons through PadMap (a Nintendo pad confirms with
    /// A on the right, others with the bottom button), the shoulders, the triggers (held, for hold to play), Menu/+ and
    /// View/−. Whatever pad was used last sets the prompts' glyphs. Works for Xbox and PlayStation pads, the Switch Pro
    /// Controller and Joy-Con on PC, and the Switch's own pads once the Switch platform package is in (the same Gamepad
    /// controls). Without the Input System package the race still plays with the mouse and keyboard.
    /// </summary>
    public sealed class PadReader
    {
        readonly PadRepeat repeat = new PadRepeat();
        readonly bool[] was = new bool[14], now = new bool[14];
        public PadStyle LastStyle = PadStyle.Xbox;
        /// <summary>A pad is plugged in (the prompts can say so even before it is touched).</summary>
        public bool Connected { get; private set; }
        /// <summary>Confirm or Menu went down this frame (the finish screen starts a new race on it).</summary>
        public bool ConfirmOrStart { get; private set; }
#if GF_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
        Gamepad pad0;
        PadStyle style0;
        UnityEngine.InputSystem.Controls.ButtonControl[] controls;
#endif

        public void Read(PadInput pad, float dt)
        {
            ConfirmOrStart = false;
#if GF_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
            var g = Gamepad.current;
            Connected = g != null;
            if (g == null) return;
            // the controls and the style are looked up once per pad, so a frame makes no garbage (it matters on Switch)
            if (g != pad0)
            {
                pad0 = g;
                style0 = PadMap.StyleOf(g.layout, g.description.product, g.description.manufacturer, Application.platform == RuntimePlatform.Switch);
                controls = new[] { g.dpad.up, g.dpad.down, g.dpad.left, g.dpad.right, g.buttonSouth, g.buttonEast, g.buttonWest, g.buttonNorth,
                                   g.leftShoulder, g.rightShoulder, g.leftTrigger, g.rightTrigger, g.startButton, g.selectButton };
            }
            var style = style0;
            for (int i = 0; i < controls.Length; i++) now[i] = controls[i].isPressed;
            bool any = false;
            for (int i = 4; i < now.Length; i++)
            {
                if (now[i] == was[i]) continue;
                was[i] = now[i];
                any = true;
                if (PadMap.Of(style, (PadPhysical)i) is PadButton b)
                {
                    pad.Style = style;
                    pad.Press(b, now[i]);
                    if (now[i] && (b == PadButton.Confirm || b == PadButton.Pause)) ConfirmOrStart = true;
                }
            }

            // directions: the d-pad wins over the stick
            PadButton? dir = null;
            for (int i = 0; i < 4; i++) if (now[i]) { dir = PadMap.Of(style, (PadPhysical)i); break; }
            if (dir == null) { var v = g.leftStick.ReadValue(); dir = PadRepeat.Dir(v.x, v.y); }
            if (repeat.Step(dir, dt) is PadButton d) { pad.Style = style; pad.Press(d, true); any = true; }
            if (any) LastStyle = style;
#else
            Connected = false;
#endif
        }

        /// <summary>The window lost focus: let go of the triggers so a held aim doesn't stay held.</summary>
        public void Release(PadInput pad)
        {
            if (pad == null) return;
            if (was[(int)PadPhysical.RightTrigger]) pad.Press(PadButton.Aim, false);
            if (was[(int)PadPhysical.LeftTrigger]) pad.Press(PadButton.AimCancel, false);
            for (int i = 0; i < was.Length; i++) was[i] = false;
        }
    }
}
