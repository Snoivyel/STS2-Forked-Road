# Slay the Spire 2 Forked Road

Language: **English** | [中文](README_ZH.md)

[GitHub Repository](https://github.com/Snoivyel/STS2-Forked-Road) | [MIT License](LICENSE)

![Version](https://img.shields.io/badge/version-1.0.0-2ea043)
![Game](https://img.shields.io/badge/Game-Slay%20the%20Spire%202-b54131)
![License](https://img.shields.io/badge/License-MIT-1f6feb)
![Type](https://img.shields.io/badge/Type-Gameplay%20Mod-8250df)

`Forked Road` is a multiplayer gameplay mod for Slay the Spire 2. It allows multiplayer map votes that target different combat routes to be resolved as separate branches, instead of forcing everyone onto the same route.

The mod records the route each player is currently on, and handles room logic, combat scaling, and follow-up synchronization based on how many players entered that branch. After all branches in the current split batch are resolved, the party returns to the shared map flow and continues forward.

## ✨ Core Features

- Split route resolution: when multiplayer players vote for different map nodes, the mod groups them by target node and resolves each combat branch in sequence instead of directly overriding minority votes.
- Per-branch combat scaling: each branch only resolves multiplayer combat logic using the players who actually entered that branch, avoiding counting players who did not participate.
- Spectating or waiting for inactive players: players who are not in the current branch enter a waiting or spectator flow and will not incorrectly interfere with room resolution in other branches.
- Support for shops, treasure, and shared events: the split flow handles synchronization and perspective switching for shops, treasure rooms, and some shared events to keep the multiplayer experience as consistent as possible.
- Automatic merging after support-branch wipes: if all players in a support branch go down, the mod attempts to merge that branch into another branch that is still in progress.
- Restore shared map flow after all branches finish: once all branches in the current split batch are resolved, the multiplayer map and vote state return to the normal shared mode.

## 📦 Player Installation Instructions

### Windows

1. Get the mod files and make sure the final folder contains at least `ForkedRoad.dll` and `ForkedRoad.json`.
2. Copy the entire `ForkedRoad` folder into `<Slay the Spire 2>/mods/`.
3. Launch the game and the mod will load automatically.

### Multiplayer Notes

1. This is a gameplay mod that directly affects multiplayer flow, so it is recommended that every player in the lobby use the same version.
2. If only some players install it, or if different players use different versions, map voting, room synchronization, or branch resolution may behave incorrectly.
3. The current version does not include any extra configuration files and can be used immediately after installation.

## 🧭 Mechanism Description

- This mod mainly handles situations where multiplayer map voting results in different target nodes.
- After entering a branch, players in the current branch enter the room normally, while the remaining players are restricted to spectator flow until it is their turn or until all branches in the current split batch have finished resolving.
- Some branches trigger regrouping logic at the end, and the mod synchronizes player positions, room state, and the follow-up map voting flow.
- The current project mainly focuses on split combat-route scenarios. Since it directly affects multiplayer gameplay, it is recommended for use with a fixed multiplayer group that stays on the same version.

## ⚠️ Known Issues

1. `SL` is not supported for now. After using `SL`, all player branch states will be merged back into the route corresponding to the current scene.
2. In co-op events such as `物理机互肘` and `历史书任务`, if combat starts after the route has split, the current version may fail to end turns properly. For now, the only rough workaround is using `SL`.
