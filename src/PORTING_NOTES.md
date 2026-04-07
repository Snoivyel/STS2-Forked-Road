# Legacy game port notes

This `src/` tree is the legacy-game port of the latest `beta/` implementation.

Porting basis:

- Mod source used for transplant: `forked-road/beta/**`
- Old game source checked against: `d:\sts2\qilu\game\src\Core\**`
- Newer game source checked against: `d:\sts2\qilu\game-beta\src\Core\**`

Key compatibility checks completed before the transplant:

- `RunManager`
  - `SetUpSavedMultiPlayer`
  - `LoadIntoLatestMapCoord`
  - `EnterMapCoordDebug`
  - `DebugOnlyGetState`
  - `RoomEntered`
- `SaveManager`
  - `SaveRun`
  - `DeleteCurrentMultiplayerRun`
  - `LoadAndCanonicalizeMultiplayerRunSave`
- Multiplayer synchronizers
  - `RunLocationTargetedMessageBuffer`
  - `MapSelectionSynchronizer`
  - `ActionQueueSynchronizer`
  - `ActionQueueSet`
  - `PlayerChoiceSynchronizer`
  - `EventSynchronizer`
  - `RestSiteSynchronizer`
  - `TreasureRoomRelicSynchronizer`
  - `OneOffSynchronizer`
  - `CombatStateSynchronizer`
  - `ChecksumTracker`
- UI / room nodes
  - `NMapScreen`
  - `MapSplitVoteAnimation`
  - `NMultiplayerPlayerState`
  - `NMerchantRoom`
  - `NRestSiteRoom`
  - `NTreasureRoom`
- Reflection field names used by the mod were also checked in old-game source and are still present.

Result:

- The old game keeps the same core APIs and reflection targets required by the current mod logic.
- Therefore the legacy port in `src/` is a direct source transplant of the latest `beta/` implementation.
- `beta/` and game source trees were intentionally left unchanged.

Note:

- The repository's active build project still compiles `beta/**` only.
- `src/**` exists as the legacy-game source port and can later be wired to a separate legacy build project if needed.
