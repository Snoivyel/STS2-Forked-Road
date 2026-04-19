# Slay the Spire 2 Forked Road

Language: **English** | [中文](README_ZH.md)

[GitHub Repository](https://github.com/Snoivyel/STS2-Forked-Road) | [MIT License](LICENSE)

![Version](https://img.shields.io/badge/version-1.0.8-2ea043)
![Game](https://img.shields.io/badge/Game-Slay%20the%20Spire%202-b54131)
![License](https://img.shields.io/badge/License-MIT-1f6feb)

`Forked Road` is a multiplayer gameplay mod for Slay the Spire 2.
It allows multiplayer map votes to resolve into separate route branches instead of forcing the entire lobby onto the same combat path.

The mod tracks which branch each player is currently in, scopes room and combat logic to the players who actually entered that branch, and sends everyone back to the shared map flow only after the whole split batch is resolved.

## Core Features

- Branch-based route splitting: when players vote for different map nodes, the mod groups them by destination and resolves the run as a split batch instead of overriding minority votes.
- Parallel branch flow: each branch enters and resolves its own room context while preserving synchronized multiplayer progression.
- Read-only spectator system: players who finish early, are inactive in the current branch, or are eliminated can spectate other active branches without interacting with them.
- Branch-local combat scaling: combat scaling and participant logic are based on the players who actually entered the branch, instead of the full lobby.
- Shared flow recovery: after all branches in the current batch are completed, the party returns to the shared map selection flow and can naturally reconverge on the next vote.
- Save/load support for split runs: the mod stores branch runtime state and restores split multiplayer runs more safely after loading.
- Extended room support: split handling and spectator support now cover combat, event-like rooms, treasure rooms, merchants, and rest sites.

## Current Gameplay Flow

1. Players vote on the shared map as usual.
2. If votes diverge, the mod locks a split batch and groups players by destination.
3. Each branch enters its own room flow with its own active participants.
4. Players who finish early move into waiting or spectator mode instead of advancing the run alone.
5. When every branch in the batch is resolved, everyone returns to the shared map flow for the next vote.

## Important Behavior Notes

- This mod directly changes multiplayer gameplay flow. Every player in the lobby should use the same mod version.
- Players who are eliminated in a branch no longer need to be force-merged into another active branch to keep the run moving.
- Spectator views are intended to be read-only. They are for observing branch state, not for controlling remote rooms.
- The current repository contains two source trees:
  - `beta/**` is the active latest-game implementation.
  - `src/**` is the legacy old-game port kept for compatibility and reference.

## Player Installation

### Windows

1. Get the mod files and make sure the final folder contains at least `ForkedRoad.dll` and `ForkedRoad.json`.
2. Copy the entire `ForkedRoad` folder into `<Slay the Spire 2>/mods/`.
3. Launch the game. The mod loads automatically.

## Multiplayer Compatibility Notes

1. Everyone in the same multiplayer lobby should use the same build of the mod.
2. Mixed versions can break map voting, room synchronization, branch progression, spectator state, or save/load restore behavior.
3. No extra configuration file is required for normal use.

## Repository Notes

- The packaged mod metadata currently reports version `1.0.8`.
- Version `1.0.8` includes substantial refactors around branch runtime management, spectator views, and save/restore handling.
- If you are building from source for the latest game branch, use `ForkedRoad.csproj`.
- If you are working on the legacy old-game port, use `ForkedRoad.Legacy.csproj`.
