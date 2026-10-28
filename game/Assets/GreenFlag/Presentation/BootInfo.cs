using GreenFlag.Sim;
using UnityEngine;

namespace GreenFlag.Presentation
{
    /// <summary>
    /// The empty boot scene's only script: proves the sim package compiles into the player, runs one headless race
    /// on it and shows the fingerprint, which must match `dotnet run --project sim/GreenFlag.Harness -- race carver 61`.
    /// </summary>
    public sealed class BootInfo : MonoBehaviour
    {
        string line;
        GUIStyle style;

        void Awake()
        {
            // Seed 61 is the harness's reference seed; its first roll is 0.548170919530094 in the slice too.
            double roll = new SimRng(61).NextDouble();
            var race = new Race(RaceSetup.Quick(Tracks.Carver, 61));
            var res = race.RunToEnd();
            line = $"Green Flag Deck  ·  build {Application.version}  ·  sim {SimInfo.Version}  ·  seed 61 roll {roll:F6}\n" +
                   $"Carver seed 61: you P{res.MyStart} → P{res.MyFinish}, {res.Cautions} caution(s)  ·  fingerprint {race.Fingerprint()}";
        }

        void OnGUI()
        {
            style ??= new GUIStyle(GUI.skin.label) { fontSize = 22, alignment = TextAnchor.MiddleCenter };
            GUI.Label(new Rect(0, 0, Screen.width, Screen.height), line, style);
        }
    }
}
