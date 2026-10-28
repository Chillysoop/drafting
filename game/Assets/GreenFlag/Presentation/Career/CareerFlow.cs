using System.IO;
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
    /// Saves (step 35): three slots under persistentDataPath/saves. The menu writes a checkpoint when you go racing, at the
    /// results, on every reward-screen press and when you leave the deck builder; anything else is flushed when the game is
    /// paused, loses focus or quits. Quitting mid-race resumes in the garage before that race.
    /// The race weekend (step 37): Go racing opens qualifying; during the timed run this ticks the run every frame with brake
    /// held from Space or Enter, the controller's A or the BRAKE button (QualiHud draws it), and its grid screen starts the race.
    /// </summary>
    public sealed class CareerFlow : MonoBehaviour
    {
        [Tooltip("0 = a new random career seed each run.")]
        public int seed;

        public CareerMenu Menu { get; private set; }
        public MenuFocus Focus { get; } = new MenuFocus();
        /// <summary>The controller or the arrow keys moved last (draw the focus ring); the mouse hides it again.</summary>
        public bool PadActive;
        public PadStyle PadStyle = PadStyle.Xbox;

        RaceView view;
        MenuHud hud;
        QualiHud quali;
        bool padBrake;
        readonly PadReader padReader = new PadReader();
        System.Action<PadStyle, PadButton, bool> onPad;
        Camera cam;

        void Awake()
        {
            var store = SaveStore();
            Menu = store != null ? new CareerMenu(new SaveSlots(store) { Build = Application.version }) : new CareerMenu();
        }

        /// <summary>Where saves live on this platform, or null to play without saving.</summary>
        static ISaveStore SaveStore()
        {
#if UNITY_SWITCH && !UNITY_EDITOR
            // The Switch needs the NintendoSDK save-data mount and a commit after each write; that store comes with the
            // Switch developer account (roadmap step 14). Until then a Switch build plays without saving.
            Debug.LogWarning("Green Flag Deck: no Switch save store yet, playing without saves");
            return null;
#else
            return new FileSaveStore(Path.Combine(Application.persistentDataPath, "saves"));
#endif
        }

        void OnApplicationPause(bool paused) { if (paused) Menu?.Flush(); }
        void OnApplicationFocus(bool focused) { if (!focused) Menu?.Flush(); }
        void OnApplicationQuit() => Menu?.Flush();

        void Start()
        {
            Menu.NewSeed = seed != 0 ? (uint)seed : (uint)Random.Range(1, int.MaxValue);
            hud = new MenuHud(this);
            quali = new QualiHud(this);
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
            if (Menu.Screen == CareerScreen.QualiRun) Menu.QualiTick(Time.unscaledDeltaTime, quali.MouseHeld || quali.KeyHeld || padBrake);
            else { padBrake = false; quali.MouseHeld = quali.KeyHeld = false; }
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
            if (view != null) return;
            // qualifying: A is the brake, held and let go; B gives up
            if (Menu.Screen == CareerScreen.QualiRun)
            {
                PadStyle = style;
                if (b == PadButton.Confirm) { padBrake = down; PadActive = true; }
                else if (b == PadButton.Back && down) Click("quali.giveup");
                return;
            }
            if (!down) return;
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
            if (view != null) return;
            if (Menu.Screen == CareerScreen.QualiRun) quali?.Draw();
            else hud?.Draw();
        }
    }
}
