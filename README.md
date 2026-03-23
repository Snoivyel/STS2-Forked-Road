# Slay the Spire 2 Forked Road

[GitHub Repository](https://github.com/Snoivyel/STS2-Forked-Road) | [MIT License](LICENSE) | [中文说明](README_ZH.md)

![Version](https://img.shields.io/badge/version-1.0.0-2ea043)
![Game](https://img.shields.io/badge/Game-Slay%20the%20Spire%202-b54131)
![License](https://img.shields.io/badge/License-MIT-1f6feb)
![Type](https://img.shields.io/badge/Type-Gameplay%20Mod-8250df)

`Forked Road` is a multiplayer gameplay mod for Slay the Spire 2. When players vote for different combat route nodes on the map, the mod resolves those votes as separate branches instead of forcing everyone onto a single path.

The mod keeps track of each player's route, runs room logic and combat scaling based on the players who actually entered that branch, and then returns the party to the shared map flow after the full split batch has been resolved.

## ✨ Features

- Branch-based route resolution: when multiplayer players vote for different map nodes, the mod groups players by destination and resolves each branch in sequence instead of overriding minority votes.
- Per-branch combat scaling: each branch only uses the players who actually entered that branch when resolving multiplayer combat logic.
- Waiting and spectating flow: players who are not part of the currently active branch are moved into waiting or spectator flow instead of interfering with the active room.
- Support for shops, treasure rooms, and shared events: split-route handling includes synchronization work for shops, treasure, and some shared-event transitions.
- Automatic merge after branch wipes: if a support branch is fully defeated, the mod attempts to merge those players into another still-active branch.
- Return to shared map flow: once all branches in the current split batch are resolved, map progression and voting return to the normal shared multiplayer flow.

## 📦 Installation

### Windows

1. Get the mod files and make sure the final folder contains at least `ForkedRoad.dll`, `ForkedRoad.json`, and `LICENSE`.
2. Copy the entire `ForkedRoad` folder into `<Slay the Spire 2>/mods/`.
3. Launch the game. The mod will load automatically.

### Multiplayer Notes

1. This mod directly changes multiplayer flow, so all players in the lobby should use the same version.
2. If only some players install it, or if versions do not match, map voting, room synchronization, or branch resolution may break.
3. The current version does not require any extra config files.

## ⚙️ Build From Source

1. Update `Sts2Dir` in `ForkedRoad.csproj` so it matches your local game installation path.
2. Run `dotnet build` in the project root.
3. After the build finishes, the distributable mod files will be available in `package/ForkedRoad/`.
4. Copy the entire `package/ForkedRoad/` directory into `<Slay the Spire 2>/mods/`.

## 🧭 How It Works

- This mod is mainly designed for multiplayer map votes that target different destination nodes.
- Once a split begins, players inside the active branch enter the room normally, while the others wait or spectate until their own branch is processed or the split batch is finished.
- Some branches trigger merge behavior at the end, and the mod synchronizes player location, room state, and the next map-vote flow accordingly.
- The current project is focused on split combat-route handling and is best used with a consistent multiplayer group running the same version.

## ⚠️ Known Issues

1. `SL` is not supported yet. After using `SL`, all player branches may collapse back into the route of the current scene.
2. In co-op events such as `物理机互肘` and `历史书任务`, if combat starts after the route has split, the battle may get stuck and fail to end the turn properly. Right now, `SL` is the only unreliable workaround.

## 📁 Project Structure

- `src/ForkedRoadPatches.cs`: main Harmony patches and room-flow adaptations.
- `src/ForkedRoadManager.cs`: branch state, player position tracking, and network-message coordination.
- `src/ForkedRoadWaitingRoom.cs`: waiting-room UI shown during branch resolution.
- `package/ForkedRoad/`: built mod package ready for distribution.
