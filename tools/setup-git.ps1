# One-time git setup for the Unity project (Windows). Run from the repo root in PowerShell:
#   powershell -ExecutionPolicy Bypass -File tools/setup-git.ps1
# It turns on Git LFS and tells git to merge scenes and prefabs with Unity's Smart Merge.
$ErrorActionPreference = 'Stop'
git lfs install
$version = (Get-Content game/ProjectSettings/ProjectVersion.txt | Select-String 'm_EditorVersion: (.+)').Matches[0].Groups[1].Value.Trim()
$merge = "C:/Program Files/Unity/Hub/Editor/$version/Editor/Data/Tools/UnityYAMLMerge.exe"
if (-not (Test-Path $merge)) { Write-Warning "UnityYAMLMerge not found at $merge. Install Unity $version through Unity Hub first, then run this again." }
git config merge.unityyamlmerge.name "Unity SmartMerge"
git config merge.unityyamlmerge.driver "`"$merge`" merge -p %O %B %A %A"
git config merge.unityyamlmerge.recursive binary
Write-Host "Git LFS on, Unity Smart Merge set to $merge"
