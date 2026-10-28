using GreenFlag.Sim;
using GreenFlag.View;
using UnityEngine;

namespace GreenFlag.Presentation
{
    /// <summary>
    /// The career scene's one script (roadmap step 34): the menus between races and the race itself, in a loop. It draws the
    /// engine-free CareerMenu pages (title, garage with the shop, parts and tune, the race deck builder, the reward screen) with
    /// MenuHud. Go racing builds a RaceView for the career's next race; at the flag Enter or A brings the race back here for
    /// the payout and the reward screen, and the race view is torn down. Mouse, keyboard (arrows, Enter, Esc) and controller
    /// (d-pad or stick, A, B) all work in the menus.
    /// </summary>
    public sealed class CareerFlow : MonoBehaviour
    {
        [Tooltip("0 = a new random career seed each run.")]
        public int seed;

        public CareerMenu Menu { get; } = new CareerMenu();
        public MenuFocus Focus { get; } = new MenuFocus();
        /// <summary>The controller or the arrow keys moved last (draw the focus ring); the mouse hides it again.</summary>
        public bool PadActive;
        public PadStyle PadStyle = PadStyle.Xbox;

        RaceView view;
        MenuHud hud;
        readonly PadReader padReader = new PadReader();
        System.Action<PadStyle, PadButton, bool> onPad;
        Camera cam;

        void Start()
        {
            Menu.NewSeed = seed != 0 ? (uint)seed : (uint)Random.Range(1, int.MaxValue);
            hud = new MenuHud(this);
            onPad = OnPad;
            cam = Camera.main;
            if (cam == null) cam = new GameObject("Main Camera", typeof(Camera)) { tag = "MainCamera" }.GetComponent<Camera>();
            MenuCamera();
        }

        void MenuCamera()
        {
            cam.targetTexture = null;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.035f, 0.05f, 0.094f);
        }

        void Update()
        {
            if (view != null) return; // the race view runs the race and reads the pad itself
            var setup = Menu.TakeRace();
            if (setup != null) { StartRace(setup); return; }
            padReader.Read(onPad, Time.unscaledDeltaTime);
        }

        void StartRace(RaceSetup setup)
        {
            var race = new Race(setup);
            var go = new GameObject("RaceView");
            view = go.AddComponent<RaceView>();
            view.persona = Menu.Career.Persona;
            view.RaceSource = () => race;
            view.Finished = OnFinished;
        }

        void OnFinished(Race race)
        {
            if (view == null) return;
            Menu.RaceFinished(race);
            Destroy(view.gameObject);
            view = null;
            Focus.Id = null;
            MenuCamera();
        }

        /// <summary>A controller button or a menu key.</summary>
        public void OnPad(PadStyle style, PadButton b, bool down)
        {
            if (!down || view != null) return;
            PadStyle = style;
            PadActive = true;
            var page = Menu.Page();
            switch (b)
            {
                case PadButton.Up: case PadButton.Left: Focus.Move(page, -1); break;
                case PadButton.Down: case PadButton.Right: Focus.Move(page, 1); break;
                case PadButton.Confirm: Focus.Confirm(Menu); break;
                case PadButton.Back: Focus.Back(Menu); break;
            }
        }

        public void Click(string id)
        {
            PadActive = false;
            Focus.Id = id;
            Menu.Press(id);
        }

        void OnGUI()
        {
            if (view == null) hud?.Draw();
        }
    }
}
