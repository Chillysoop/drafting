# Unity setup (roadmap step 25)

The Unity project is in `game/`. It is Unity 6.3 LTS (`6000.3.27f1`) with URP. The repo holds only what Unity needs to rebuild everything else: the package manifest, the editor version, our scripts and the sim package. On first open, `Green Flag/Set Up Project` runs by itself and creates the URP asset, the player settings and the empty `Boot` scene. CI runs the same setup before every build, so a bare checkout always builds.

## What is in the repo

| Path | What it is |
|---|---|
| `game/ProjectSettings/ProjectVersion.txt` | The editor version. Unity Hub and CI both read it. |
| `game/Packages/manifest.json` | URP 17.3, Test Framework, uGUI. |
| `game/Packages/com.greenflag.sim/` | The race sim as a local package. `noEngineReferences: true`, so it can never touch UnityEngine. |
| `game/Assets/GreenFlag/Presentation/` | Scripts that draw what the sim says. For now only `BootInfo`, which shows the build and a sim roll on screen. |
| `game/Assets/GreenFlag/Editor/` | `ProjectBootstrap` (URP, player settings, boot scene) and `BuildScript` (command-line Windows build). |
| `sim/` | .NET 8 solution that compiles the same sim files, plus xUnit tests. Runs in CI without Unity. |
| `.github/workflows/sim.yml` | `dotnet test` on every PR that touches the sim. |
| `.github/workflows/nightly.yml` | Windows player build every night at 08:00 UTC, on demand, and on PRs that touch `game/`. |
| `tools/setup-git.ps1`, `tools/setup-git.sh` | One-time Git LFS and Unity Smart Merge setup. |

## What Conner does (about 30 minutes, once)

1. **Install Unity Hub** from unity.com/download and sign in with a Unity account (Personal is free and fine for now).
2. **Install the editor.** In Hub, Installs → Install Editor → Archive, pick **6000.3.27f1**, and tick **Windows Build Support (Mono)**. If 6000.3.27f1 is not offered, install the newest 6000.3 LTS and tell Claude the number; Claude updates `ProjectVersion.txt` to match.
3. **Open the project.** Clone the repo, then in Hub choose Add → Add project from disk → the `game` folder. The first open takes a few minutes while Unity imports. When it finishes, the console says `[GreenFlag] Project setup checked`, and pressing Play shows "Green Flag Deck · build … · sim 0.1.0 · seed 61 roll 0.548171" in the middle of a dark screen. If the console shows red errors instead, copy them to Claude.
4. **Run the git setup once** from the repo root: `powershell -ExecutionPolicy Bypass -File tools/setup-git.ps1`.
5. **Give CI a licence.** GitHub Actions needs three repository secrets (repo Settings → Secrets and variables → Actions → New repository secret):
   - `UNITY_EMAIL`: your Unity account email.
   - `UNITY_PASSWORD`: your Unity account password. (A separate Unity account just for CI is a good idea; Personal is free.)
   - `UNITY_LICENSE`: the full text of your licence file. After Hub has activated a Personal licence, it is at `C:\ProgramData\Unity\Unity_lic.ulf` on Windows (`/Library/Application Support/Unity/Unity_lic.ulf` on macOS). Open it in Notepad and paste everything.
   GameCI's guide for this is at game.ci/docs/github/activation.
6. **Start a build.** Actions → nightly → Run workflow. A green run has an artifact named `GreenFlagDeck-Windows-<version>`. Download it, unzip, run `GreenFlagDeck.exe`. That closes step 25.

Until step 5 is done, the nightly run is green but its build job is skipped, with a notice saying the secrets are missing.

## Building without CI

In the editor: `Green Flag → Build Windows` writes `game/Builds/Windows/GreenFlagDeck.exe`. From a terminal:

```
"C:\Program Files\Unity\Hub\Editor\6000.3.27f1\Editor\Unity.exe" -batchmode -quit -projectPath game -executeMethod GreenFlag.EditorTools.BuildScript.BuildWindows -logFile -
```

## Sim tests without Unity

```
dotnet test sim/GreenFlag.Sim.sln
```

`SimRng` is the slice harness's mulberry32 generator, ported bit for bit, and the tests check its first rolls against the browser for three seeds. Step 26 builds the race sim on top of it.

## Known gaps

- The Unity-side scripts (`ProjectBootstrap`, `BuildScript`, `BootInfo`) were written without a Unity editor to compile them, because the editor needs a licence. The first open or the first licensed CI run is their first compile. If either shows an error, it is a quick fix.
- The nightly builds Mono on Linux runners. IL2CPP needs a Windows runner and comes later, when performance matters (around step 30).
- Switch builds need Nintendo's SDK and are not part of this pipeline (step 14 was skipped).
