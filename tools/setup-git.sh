#!/usr/bin/env bash
# One-time git setup for the Unity project (macOS/Linux). Run from the repo root: bash tools/setup-git.sh
set -euo pipefail
git lfs install
version=$(sed -n 's/^m_EditorVersion: //p' game/ProjectSettings/ProjectVersion.txt | tr -d '\r')
case "$(uname)" in
  Darwin) merge="/Applications/Unity/Hub/Editor/$version/Unity.app/Contents/Tools/UnityYAMLMerge" ;;
  *)      merge="$HOME/Unity/Hub/Editor/$version/Editor/Data/Tools/UnityYAMLMerge" ;;
esac
[ -x "$merge" ] || echo "warning: UnityYAMLMerge not found at $merge (install Unity $version through Unity Hub first)"
git config merge.unityyamlmerge.name "Unity SmartMerge"
git config merge.unityyamlmerge.driver "\"$merge\" merge -p %O %B %A %A"
git config merge.unityyamlmerge.recursive binary
echo "Git LFS on, Unity Smart Merge set to $merge"
