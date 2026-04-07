using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Managers;

namespace ForkedRoad;

internal static partial class ForkedRoadManager
{
    private const string SaveRestoreSidecarFileName = "current_run_mp.forkedroad.json";

    private const int SaveRestoreSnapshotVersion = 1;

    private static readonly JsonSerializerOptions SaveRestoreJsonOptions = new()
    {
        IncludeFields = true
    };

    private static readonly System.Reflection.MethodInfo RunManagerEnterMapCoordInternalMethod =
        AccessTools.Method(typeof(RunManager), "EnterMapCoordInternal");

    private static readonly System.Reflection.MethodInfo RunManagerEnterRoomInternalMethod =
        AccessTools.Method(typeof(RunManager), "EnterRoomInternal");

    private static bool _isLoadingSavedMultiplayerRun;

    private static bool _savedRestoreAvailabilityKnown;

    private static bool _expectedRemoteRestoreSnapshot;

    private static bool _hasReceivedRemoteRestoreSnapshot;

    private static bool _hasAppliedActiveSavedRestoreSnapshot;

    private static bool _shouldBroadcastSavedRestoreState;

    private static bool _hasBroadcastSavedRestoreState;

    private static TaskCompletionSource? _savedRestoreReadySource;

    private static ForkedRoadSavedRunSnapshot? _loadedSavedRestoreSnapshot;

    private static ForkedRoadSavedRunSnapshot? _activeSavedRestoreSnapshot;

    private static INetGameService? _earlySavedRestoreNetService;

    private static bool _earlySavedRestoreHandlersRegistered;

    private static bool _earlySavedRestoreAvailabilityKnown;

    private static bool _earlyExpectedRemoteRestoreSnapshot;

    private static ForkedRoadSavedRunSnapshot? _earlyReceivedRestoreSnapshot;

    internal static void RegisterEarlySavedRestoreHandlers(INetGameService netService)
    {
        if (netService.Type != NetGameType.Client)
        {
            return;
        }

        if (_earlySavedRestoreHandlersRegistered && ReferenceEquals(_earlySavedRestoreNetService, netService))
        {
            return;
        }

        UnregisterEarlySavedRestoreHandlers();
        ResetEarlySavedRestoreMessageBuffer();
        netService.RegisterMessageHandler<ForkedRoadSaveRestoreAvailabilityMessage>(HandleEarlySaveRestoreAvailabilityMessage);
        netService.RegisterMessageHandler<ForkedRoadSaveRestoreStateMessage>(HandleEarlySaveRestoreStateMessage);
        _earlySavedRestoreNetService = netService;
        _earlySavedRestoreHandlersRegistered = true;
        Log.Debug("ForkedRoad registered early save-restore handlers for loaded-lobby join.");
    }

    internal static void UnregisterEarlySavedRestoreHandlers(INetGameService? netService = null)
    {
        INetGameService? service = netService ?? _earlySavedRestoreNetService;
        if (!_earlySavedRestoreHandlersRegistered || service == null)
        {
            return;
        }

        service.UnregisterMessageHandler<ForkedRoadSaveRestoreAvailabilityMessage>(HandleEarlySaveRestoreAvailabilityMessage);
        service.UnregisterMessageHandler<ForkedRoadSaveRestoreStateMessage>(HandleEarlySaveRestoreStateMessage);
        if (ReferenceEquals(_earlySavedRestoreNetService, service))
        {
            _earlySavedRestoreNetService = null;
        }
        _earlySavedRestoreHandlersRegistered = false;
    }

    internal static void ResetEarlySavedRestoreMessageBuffer()
    {
        _earlySavedRestoreAvailabilityKnown = false;
        _earlyExpectedRemoteRestoreSnapshot = false;
        _earlyReceivedRestoreSnapshot = null;
    }

    private static void HandleEarlySaveRestoreAvailabilityMessage(ForkedRoadSaveRestoreAvailabilityMessage message, ulong senderId)
    {
        _earlySavedRestoreAvailabilityKnown = true;
        _earlyExpectedRemoteRestoreSnapshot = message.hasRestoreState;
        if (!message.hasRestoreState)
        {
            _earlyReceivedRestoreSnapshot = null;
        }

        Log.Info($"ForkedRoad buffered early save-restore availability from host {senderId}: hasRestoreState={message.hasRestoreState}");
        ApplyBufferedEarlySaveRestoreMessagesIfNeeded();
    }

    private static void HandleEarlySaveRestoreStateMessage(ForkedRoadSaveRestoreStateMessage message, ulong senderId)
    {
        _earlySavedRestoreAvailabilityKnown = true;
        _earlyExpectedRemoteRestoreSnapshot = true;
        _earlyReceivedRestoreSnapshot = message.snapshot;
        Log.Info($"ForkedRoad buffered early saved split restore snapshot from host {senderId} during loaded-lobby join.");
        ApplyBufferedEarlySaveRestoreMessagesIfNeeded();
    }

    private static void ApplyBufferedEarlySaveRestoreMessagesIfNeeded()
    {
        if (!_isLoadingSavedMultiplayerRun || !_earlySavedRestoreAvailabilityKnown)
        {
            return;
        }

        NetGameType? loadNetType = _netService?.Type ?? _earlySavedRestoreNetService?.Type;
        if (loadNetType != NetGameType.Client)
        {
            return;
        }

        _savedRestoreAvailabilityKnown = true;
        _expectedRemoteRestoreSnapshot = _earlyExpectedRemoteRestoreSnapshot;
        if (_earlyReceivedRestoreSnapshot.HasValue)
        {
            _hasReceivedRemoteRestoreSnapshot = true;
            _activeSavedRestoreSnapshot = _earlyReceivedRestoreSnapshot;
            _hasAppliedActiveSavedRestoreSnapshot = false;
            TryApplyActiveSavedRestoreSnapshot();
        }
        else if (!_earlyExpectedRemoteRestoreSnapshot)
        {
            _activeSavedRestoreSnapshot = null;
        }

        TryFinalizeSavedRestoreReadiness();
    }

    internal static void CaptureSaveRestoreSnapshotForCurrentRun()
    {
        if (_netService?.Type != NetGameType.Host || _runState == null)
        {
            return;
        }

        try
        {
            if (!ShouldPersistSaveRestoreSnapshot())
            {
                DeleteSaveRestoreSnapshotFile();
                return;
            }

            ForkedRoadSavedRunSnapshot snapshot = CreateSaveRestoreSnapshot();
            string path = GetSaveRestoreSnapshotPath();
            string? directoryPath = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }

            File.WriteAllText(path, JsonSerializer.Serialize(snapshot, SaveRestoreJsonOptions));
            Log.Info($"ForkedRoad saved split restore snapshot to {path}.");
        }
        catch (System.Exception ex)
        {
            Log.Warn($"ForkedRoad failed to save split restore snapshot: {ex.Message}");
        }
    }

    internal static void DeleteSaveRestoreSnapshotFile()
    {
        try
        {
            string path = GetSaveRestoreSnapshotPath();
            if (File.Exists(path))
            {
                File.Delete(path);
                Log.Info($"ForkedRoad deleted split restore snapshot at {path}.");
            }
        }
        catch (System.Exception ex)
        {
            Log.Warn($"ForkedRoad failed to delete split restore snapshot: {ex.Message}");
        }
    }

    internal static void LoadSaveRestoreSnapshotFromDisk(SerializableRun? save)
    {
        _loadedSavedRestoreSnapshot = null;
        _activeSavedRestoreSnapshot = null;
        _hasAppliedActiveSavedRestoreSnapshot = false;

        if (save == null)
        {
            return;
        }

        try
        {
            string path = GetSaveRestoreSnapshotPath();
            if (!File.Exists(path))
            {
                return;
            }

            ForkedRoadSavedRunSnapshot? snapshot = JsonSerializer.Deserialize<ForkedRoadSavedRunSnapshot>(File.ReadAllText(path), SaveRestoreJsonOptions);
            if (!snapshot.HasValue || snapshot.Value.version != SaveRestoreSnapshotVersion)
            {
                Log.Warn($"ForkedRoad ignored split restore snapshot at {path} due to incompatible version.");
                return;
            }

            snapshot = NormalizeStaleActTransitionSnapshot(snapshot.Value);

            if (snapshot.Value.currentActIndex != save.CurrentActIndex)
            {
                Log.Warn($"ForkedRoad ignored split restore snapshot at {path} because act index {snapshot.Value.currentActIndex} did not match save act index {save.CurrentActIndex}.");
                return;
            }

            if (!DoesSaveRestoreSnapshotMatchSaveLocation(snapshot.Value, save))
            {
                Log.Warn($"ForkedRoad ignored split restore snapshot at {path} because its shared location did not match the save's current map coord.");
                return;
            }

            HashSet<ulong> savePlayerIds = save.Players.Select(static player => player.NetId).ToHashSet();
            if (!TrySanitizeSaveRestoreSnapshot(snapshot.Value, savePlayerIds, out ForkedRoadSavedRunSnapshot sanitizedSnapshot))
            {
                Log.Warn($"ForkedRoad ignored split restore snapshot at {path} because it had no compatible player/branch state for the loaded save.");
                return;
            }

            _loadedSavedRestoreSnapshot = sanitizedSnapshot;
            Log.Info($"ForkedRoad loaded split restore snapshot from {path}.");
        }
        catch (System.Exception ex)
        {
            Log.Warn($"ForkedRoad failed to load split restore snapshot: {ex.Message}");
        }
    }

    internal static void PrepareForSavedMultiplayerLoad(SerializableRun run, NetGameType netGameType, INetGameService netService)
    {
        _isLoadingSavedMultiplayerRun = true;
        _savedRestoreAvailabilityKnown = netGameType == NetGameType.Host;
        _expectedRemoteRestoreSnapshot = false;
        _hasReceivedRemoteRestoreSnapshot = false;
        _hasAppliedActiveSavedRestoreSnapshot = false;
        _shouldBroadcastSavedRestoreState = netGameType == NetGameType.Host;
        _hasBroadcastSavedRestoreState = false;
        _savedRestoreReadySource = new TaskCompletionSource();
        _activeSavedRestoreSnapshot = netGameType == NetGameType.Host ? _loadedSavedRestoreSnapshot : null;

        if (_activeSavedRestoreSnapshot.HasValue)
        {
            _expectedRemoteRestoreSnapshot = true;
        }

        if (netGameType == NetGameType.Client)
        {
            RegisterEarlySavedRestoreHandlers(netService);
            ApplyBufferedEarlySaveRestoreMessagesIfNeeded();
        }

        TryFinalizeSavedRestoreReadiness();
    }

    internal static void TryInitializeSavedRestoreState()
    {
        if (!_isLoadingSavedMultiplayerRun)
        {
            return;
        }

        TryApplyActiveSavedRestoreSnapshot();
        if (_netService?.Type == NetGameType.Host && _shouldBroadcastSavedRestoreState && !_hasBroadcastSavedRestoreState)
        {
            _hasBroadcastSavedRestoreState = true;
            _ = TaskHelper.RunSafely(BroadcastSavedRestoreStateAsync());
        }
    }

    internal static bool TryHandleSavedSplitLoad(RunManager runManager, AbstractRoom? preFinishedRoom, ref Task __result)
    {
        if (!_isLoadingSavedMultiplayerRun || _runState == null || _netService == null || _netService.Type == NetGameType.Singleplayer)
        {
            return false;
        }

        __result = LoadIntoLatestMapCoordWithRestoreAsync(runManager, preFinishedRoom);
        return true;
    }

    private static async Task LoadIntoLatestMapCoordWithRestoreAsync(RunManager runManager, AbstractRoom? preFinishedRoom)
    {
        try
        {
            await WaitForSavedRestoreReadinessAsync();
            TryApplyActiveSavedRestoreSnapshot();

            if (TryGetSavedRestoreLocalCoord(out MapCoord localCoord))
            {
                Log.Info($"ForkedRoad restoring local player to saved branch coord {localCoord} instead of shared save coord {_runState?.CurrentMapCoord?.ToString() ?? "none"}.");
                if (_runState != null)
                {
                    NormalizeRunLocationBuffer(new RunLocation(localCoord, _runState.CurrentActIndex));
                }

                BranchGroupRuntime? localBranch = GetLocalBranch();
                if (localBranch != null && TryGetResolvedRoomPlan(localBranch, out ResolvedBranchRoomPlan plan))
                {
                    await EnterResolvedRoomAsync(localBranch, plan);
                }
                else
                {
                    await InvokeEnterMapCoordInternal(runManager, localCoord, null, saveGame: false);
                }

                return;
            }

            await DefaultLoadIntoLatestMapCoordAsync(runManager, preFinishedRoom);
        }
        finally
        {
            CompleteSavedMultiplayerLoad();
        }
    }

    private static async Task DefaultLoadIntoLatestMapCoordAsync(RunManager runManager, AbstractRoom? preFinishedRoom)
    {
        if (_runState == null)
        {
            return;
        }

        if (_runState.VisitedMapCoords.Count > 0)
        {
            IReadOnlyList<MapCoord> visitedMapCoords = _runState.VisitedMapCoords;
            await InvokeEnterMapCoordInternal(runManager, visitedMapCoords[visitedMapCoords.Count - 1], preFinishedRoom, saveGame: false);
        }
        else
        {
            await InvokeEnterRoomInternal(runManager, new MapRoom(), isRestoringRoomStackBase: false);
        }
    }

    private static async Task InvokeEnterMapCoordInternal(RunManager runManager, MapCoord coord, AbstractRoom? preFinishedRoom, bool saveGame)
    {
        if (RunManagerEnterMapCoordInternalMethod.Invoke(runManager, new object?[] { coord, preFinishedRoom, saveGame }) is Task task)
        {
            await task;
        }
    }

    private static async Task InvokeEnterRoomInternal(RunManager runManager, AbstractRoom room, bool isRestoringRoomStackBase)
    {
        if (RunManagerEnterRoomInternalMethod.Invoke(runManager, new object?[] { room, isRestoringRoomStackBase }) is Task task)
        {
            await task;
        }
    }

    private static async Task WaitForSavedRestoreReadinessAsync(int timeoutMs = 2500)
    {
        if (_savedRestoreReadySource == null || _savedRestoreReadySource.Task.IsCompleted)
        {
            return;
        }

        int remaining = timeoutMs;
        while (!_savedRestoreReadySource.Task.IsCompleted && remaining > 0)
        {
            await Task.Delay(50);
            remaining -= 50;
        }

        if (!_savedRestoreReadySource.Task.IsCompleted)
        {
            Log.Warn("ForkedRoad timed out waiting for saved split restore snapshot; falling back to normal multiplayer load.");
            _savedRestoreAvailabilityKnown = true;
            _expectedRemoteRestoreSnapshot = false;
            _savedRestoreReadySource.TrySetResult();
        }
    }

    private static void CompleteSavedMultiplayerLoad()
    {
        _isLoadingSavedMultiplayerRun = false;
        _savedRestoreAvailabilityKnown = false;
        _expectedRemoteRestoreSnapshot = false;
        _hasReceivedRemoteRestoreSnapshot = false;
        _savedRestoreReadySource = null;
    }

    private static async Task BroadcastSavedRestoreStateAsync()
    {
        foreach (int delayMs in new[] { 150, 500, 1000, 2000 })
        {
            await Task.Delay(delayMs);
            await BroadcastSavedRestoreStateAttemptAsync();
        }

        _shouldBroadcastSavedRestoreState = false;
    }

    private static Task BroadcastSavedRestoreStateAttemptAsync()
    {
        if (_netService?.Type != NetGameType.Host)
        {
            return Task.CompletedTask;
        }

        ForkedRoadSavedRunSnapshot? snapshot = _activeSavedRestoreSnapshot;
        bool hasSnapshot = snapshot.HasValue;
        ForkedRoadSaveRestoreAvailabilityMessage availabilityMessage = new()
        {
            hasRestoreState = hasSnapshot
        };
        _netService.SendMessage(availabilityMessage);

        if (hasSnapshot)
        {
            ForkedRoadSavedRunSnapshot snapshotValue = snapshot.GetValueOrDefault();
            ForkedRoadSaveRestoreStateMessage stateMessage = new()
            {
                snapshot = snapshotValue
            };
            _netService.SendMessage(stateMessage);
        }

        return Task.CompletedTask;
    }

    private static void HandleSaveRestoreAvailabilityMessage(ForkedRoadSaveRestoreAvailabilityMessage message, ulong senderId)
    {
        if (_netService?.Type != NetGameType.Client || !_isLoadingSavedMultiplayerRun)
        {
            return;
        }

        _savedRestoreAvailabilityKnown = true;
        _expectedRemoteRestoreSnapshot = message.hasRestoreState;
        if (!message.hasRestoreState)
        {
            _activeSavedRestoreSnapshot = null;
        }

        Log.Info($"ForkedRoad received save-restore availability from host {senderId}: hasRestoreState={message.hasRestoreState}");
        TryFinalizeSavedRestoreReadiness();
    }

    private static void HandleSaveRestoreStateMessage(ForkedRoadSaveRestoreStateMessage message, ulong senderId)
    {
        if (_netService?.Type != NetGameType.Client || !_isLoadingSavedMultiplayerRun || _hasReceivedRemoteRestoreSnapshot)
        {
            return;
        }

        _savedRestoreAvailabilityKnown = true;
        _expectedRemoteRestoreSnapshot = true;
        _hasReceivedRemoteRestoreSnapshot = true;
        _activeSavedRestoreSnapshot = message.snapshot;
        _hasAppliedActiveSavedRestoreSnapshot = false;
        Log.Info($"ForkedRoad received saved split restore snapshot from host {senderId}.");
        TryApplyActiveSavedRestoreSnapshot();
        TryFinalizeSavedRestoreReadiness();
    }

    private static void TryFinalizeSavedRestoreReadiness()
    {
        if (_savedRestoreReadySource == null || _savedRestoreReadySource.Task.IsCompleted)
        {
            return;
        }

        if (!_savedRestoreAvailabilityKnown)
        {
            return;
        }

        if (_expectedRemoteRestoreSnapshot && !_activeSavedRestoreSnapshot.HasValue)
        {
            return;
        }

        _savedRestoreReadySource.TrySetResult();
    }

    private static void TryApplyActiveSavedRestoreSnapshot()
    {
        if (_hasAppliedActiveSavedRestoreSnapshot || !_activeSavedRestoreSnapshot.HasValue || _runState == null)
        {
            return;
        }

        ApplySavedRestoreSnapshot(_activeSavedRestoreSnapshot.Value);
        _hasAppliedActiveSavedRestoreSnapshot = true;
    }

    private static void ApplySavedRestoreSnapshot(ForkedRoadSavedRunSnapshot snapshot)
    {
        if (_runState == null)
        {
            return;
        }

        EnsureRuntimePlayers();
        Runtime.NextBatchId = System.Math.Max(snapshot.nextBatchId, 1);
        Runtime.Phase = snapshot.phase;
        Runtime.RequiresAuthoritativeRoomPlans = snapshot.requiresAuthoritativeRoomPlans;
        Runtime.ActiveBatch = null;
        Runtime.Spectators.Clear();
        ClearSeededSplitLocations();
        ClearSeededMapVoteLocations();

        foreach (PlayerBranchRuntime player in Runtime.Players.Values)
        {
            player.CurrentBranchId = null;
            player.SpectatingBranchId = null;
            player.MapVoteDestinationCoord = null;
            player.IsEliminated = false;
            player.Phase = RouteSplitPlayerPhase.ChoosingRoute;
        }

        foreach (ForkedRoadSavedPlayerSnapshot playerSnapshot in snapshot.players)
        {
            if (!Runtime.Players.TryGetValue(playerSnapshot.playerId, out PlayerBranchRuntime? player))
            {
                player = new PlayerBranchRuntime
                {
                    PlayerId = playerSnapshot.playerId
                };
                Runtime.Players[player.PlayerId] = player;
            }

            player.CurrentBranchId = playerSnapshot.hasCurrentBranchId ? playerSnapshot.currentBranchId : null;
            player.SelectionCoord = playerSnapshot.hasSelectionCoord ? playerSnapshot.selectionCoord : null;
            player.Phase = playerSnapshot.phase;
            player.SpectatingBranchId = playerSnapshot.hasSpectatingBranchId ? playerSnapshot.spectatingBranchId : null;
            player.MapVoteDestinationCoord = playerSnapshot.hasMapVoteDestinationCoord ? playerSnapshot.mapVoteDestinationCoord : null;
            player.IsEliminated = playerSnapshot.isEliminated;
        }

        if (snapshot.hasActiveBatch)
        {
            BranchBatchRuntime batch = new()
            {
                BatchId = snapshot.batchId,
                ActIndex = snapshot.batchActIndex,
                Phase = snapshot.phase
            };
            batch.SourceCoords.AddRange(snapshot.sourceCoords);
            foreach (ForkedRoadSavedBranchSnapshot branchSnapshot in snapshot.branches)
            {
                BranchGroupRuntime branch = new()
                {
                    BranchId = branchSnapshot.branchId,
                    TargetCoord = branchSnapshot.targetCoord,
                    AuthorityPlayerId = branchSnapshot.authorityPlayerId,
                    PlayerIds = branchSnapshot.playerIds.ToList(),
                    RoomType = branchSnapshot.hasRoomType ? branchSnapshot.roomType : null,
                    PointType = branchSnapshot.hasPointType ? branchSnapshot.pointType : null,
                    ResolvedModelId = branchSnapshot.hasResolvedModelId &&
                                      !string.IsNullOrEmpty(branchSnapshot.resolvedModelCategory) &&
                                      !string.IsNullOrEmpty(branchSnapshot.resolvedModelEntry)
                        ? new ModelId(branchSnapshot.resolvedModelCategory, branchSnapshot.resolvedModelEntry)
                        : null,
                    Phase = branchSnapshot.phase,
                    CompletionOrder = branchSnapshot.hasCompletionOrder ? branchSnapshot.completionOrder : null,
                    SuppressCombatRewards = branchSnapshot.suppressCombatRewards,
                    DeathClearTriggered = branchSnapshot.deathClearTriggered
                };
                foreach (ulong playerId in branchSnapshot.enteredPlayerIds)
                {
                    branch.EnteredPlayerIds.Add(playerId);
                }

                foreach (ulong playerId in branchSnapshot.readyPlayerIds)
                {
                    branch.ReadyPlayerIds.Add(playerId);
                }

                foreach (ulong playerId in branchSnapshot.eliminatedPlayerIds)
                {
                    branch.EliminatedPlayerIds.Add(playerId);
                }

                batch.BranchGroups.Add(branch);
            }

            Runtime.ActiveBatch = batch;
            SeedBranchRunLocations(batch);
            ApplyPlayerChoiceNamespacesForActiveBatch();
            ApplyHookActivationForLocalBranch();
        }
        else
        {
            Runtime.ActiveBatch = null;
            Runtime.RequiresAuthoritativeRoomPlans = false;
            SeedMapVoteLocationsIfNeeded();
            RestoreAllHookActivation();
        }

        NormalizeRunLocationBuffer(_runState.CurrentLocation);
        RefreshUiState();
    }

    private static bool TryGetSavedRestoreLocalCoord(out MapCoord coord)
    {
        coord = default;
        if (!_activeSavedRestoreSnapshot.HasValue || !LocalContext.NetId.HasValue)
        {
            return false;
        }

        foreach (ForkedRoadSavedPlayerSnapshot playerSnapshot in _activeSavedRestoreSnapshot.Value.players)
        {
            if (playerSnapshot.playerId != LocalContext.NetId.Value || !playerSnapshot.hasSelectionCoord)
            {
                continue;
            }

            coord = playerSnapshot.selectionCoord;
            return !_activeSavedRestoreSnapshot.Value.hasSharedCurrentCoord || coord != _activeSavedRestoreSnapshot.Value.sharedCurrentCoord;
        }

        return false;
    }

    private static bool ShouldPersistSaveRestoreSnapshot()
    {
        if (_runState == null || _netService?.Type != NetGameType.Host)
        {
            return false;
        }

        if (HasStaleActiveBatchForCurrentAct())
        {
            ResetSplitRuntimeToSharedMap(_runState.CurrentMapCoord, $"stale_batch_before_snapshot:act{_runState.CurrentActIndex}");
        }

        EnsureRuntimePlayers();
        return IsSplitBatchInProgress || HasDivergedPlayerLocations();
    }

    private static ForkedRoadSavedRunSnapshot CreateSaveRestoreSnapshot()
    {
        EnsureRuntimePlayers();
        HashSet<ulong> currentRunPlayerIds = _runState!.Players.Select(static player => player.NetId).ToHashSet();
        ForkedRoadSavedRunSnapshot snapshot = new()
        {
            version = SaveRestoreSnapshotVersion,
            currentActIndex = _runState.CurrentActIndex,
            hasSharedCurrentCoord = _runState.CurrentMapCoord.HasValue,
            sharedCurrentCoord = _runState.CurrentMapCoord ?? default,
            phase = Runtime.Phase,
            nextBatchId = Runtime.NextBatchId,
            requiresAuthoritativeRoomPlans = Runtime.RequiresAuthoritativeRoomPlans,
            hasActiveBatch = Runtime.ActiveBatch != null,
            batchId = Runtime.ActiveBatch?.BatchId ?? 0,
            batchActIndex = Runtime.ActiveBatch?.ActIndex ?? _runState.CurrentActIndex,
            sourceCoords = Runtime.ActiveBatch?.SourceCoords.ToList() ?? new List<MapCoord>(),
            players = Runtime.Players.Values
                .Where(player => currentRunPlayerIds.Contains(player.PlayerId))
                .Select(static player => new ForkedRoadSavedPlayerSnapshot
                {
                    playerId = player.PlayerId,
                    hasCurrentBranchId = player.CurrentBranchId.HasValue,
                    currentBranchId = player.CurrentBranchId ?? 0,
                    hasSelectionCoord = player.SelectionCoord.HasValue,
                    selectionCoord = player.SelectionCoord ?? default,
                    phase = player.Phase,
                    hasSpectatingBranchId = player.SpectatingBranchId.HasValue,
                    spectatingBranchId = player.SpectatingBranchId ?? 0,
                    hasMapVoteDestinationCoord = player.MapVoteDestinationCoord.HasValue,
                    mapVoteDestinationCoord = player.MapVoteDestinationCoord ?? default,
                    isEliminated = player.IsEliminated
                })
                .ToList(),
            branches = Runtime.ActiveBatch?.BranchGroups
                .Select(branch =>
                {
                    ModelId? resolvedModel = branch.ResolvedModelId ?? branch.EncounterId;
                    List<ulong> branchPlayerIds = branch.PlayerIds.Where(currentRunPlayerIds.Contains).ToList();
                    List<ulong> branchEnteredIds = branch.EnteredPlayerIds.Where(currentRunPlayerIds.Contains).ToList();
                    List<ulong> branchReadyIds = branch.ReadyPlayerIds.Where(currentRunPlayerIds.Contains).ToList();
                    List<ulong> branchEliminatedIds = branch.EliminatedPlayerIds.Where(currentRunPlayerIds.Contains).ToList();
                    if (branchPlayerIds.Count == 0)
                    {
                        return (ForkedRoadSavedBranchSnapshot?)null;
                    }

                    return new ForkedRoadSavedBranchSnapshot
                    {
                        branchId = branch.BranchId,
                        targetCoord = branch.TargetCoord,
                        authorityPlayerId = branch.AuthorityPlayerId,
                        playerIds = branchPlayerIds,
                        enteredPlayerIds = branchEnteredIds,
                        readyPlayerIds = branchReadyIds,
                        eliminatedPlayerIds = branchEliminatedIds,
                        hasRoomType = branch.RoomType.HasValue,
                        roomType = branch.RoomType ?? default,
                        hasPointType = branch.PointType.HasValue,
                        pointType = branch.PointType ?? default,
                        hasResolvedModelId = resolvedModel != null,
                        resolvedModelCategory = resolvedModel?.Category,
                        resolvedModelEntry = resolvedModel?.Entry,
                        phase = branch.Phase,
                        hasCompletionOrder = branch.CompletionOrder.HasValue,
                        completionOrder = branch.CompletionOrder ?? 0,
                        suppressCombatRewards = branch.SuppressCombatRewards,
                        deathClearTriggered = branch.DeathClearTriggered
                    };
                })
                .Where(static branch => branch.HasValue)
                .Select(static branch => branch!.Value)
                .ToList() ?? new List<ForkedRoadSavedBranchSnapshot>()
        };

        return snapshot;
    }

    private static bool TrySanitizeSaveRestoreSnapshot(ForkedRoadSavedRunSnapshot snapshot, HashSet<ulong> savePlayerIds, out ForkedRoadSavedRunSnapshot sanitizedSnapshot)
    {
        List<ForkedRoadSavedPlayerSnapshot> players = snapshot.players
            .Where(player => savePlayerIds.Contains(player.playerId))
            .ToList();
        if (players.Count == 0)
        {
            sanitizedSnapshot = default;
            return false;
        }

        List<ForkedRoadSavedBranchSnapshot> branches = snapshot.branches
            .Select(branch =>
            {
                List<ulong> playerIds = branch.playerIds.Where(savePlayerIds.Contains).ToList();
                if (playerIds.Count == 0)
                {
                    return (ForkedRoadSavedBranchSnapshot?)null;
                }

                branch.playerIds = playerIds;
                branch.enteredPlayerIds = branch.enteredPlayerIds.Where(savePlayerIds.Contains).ToList();
                branch.readyPlayerIds = branch.readyPlayerIds.Where(savePlayerIds.Contains).ToList();
                branch.eliminatedPlayerIds = branch.eliminatedPlayerIds.Where(savePlayerIds.Contains).ToList();
                return branch;
            })
            .Where(static branch => branch.HasValue)
            .Select(static branch => branch!.Value)
            .ToList();

        if (snapshot.hasActiveBatch && branches.Count == 0)
        {
            sanitizedSnapshot = default;
            return false;
        }

        snapshot.players = players;
        snapshot.branches = branches;
        sanitizedSnapshot = snapshot;
        return true;
    }

    private static ForkedRoadSavedRunSnapshot NormalizeStaleActTransitionSnapshot(ForkedRoadSavedRunSnapshot snapshot)
    {
        if (!snapshot.hasActiveBatch || snapshot.batchActIndex == snapshot.currentActIndex)
        {
            return snapshot;
        }

        Log.Warn($"ForkedRoad stripped stale split restore batch {snapshot.batchId} because batch act {snapshot.batchActIndex} did not match snapshot act {snapshot.currentActIndex}.");
        snapshot.phase = RouteSplitRunPhase.SharedMapSelection;
        snapshot.requiresAuthoritativeRoomPlans = false;
        snapshot.hasActiveBatch = false;
        snapshot.batchId = 0;
        snapshot.batchActIndex = snapshot.currentActIndex;
        snapshot.sourceCoords = new List<MapCoord>();
        snapshot.branches = new List<ForkedRoadSavedBranchSnapshot>();

        for (int i = 0; i < snapshot.players.Count; i++)
        {
            ForkedRoadSavedPlayerSnapshot player = snapshot.players[i];
            player.hasCurrentBranchId = false;
            player.currentBranchId = 0;
            player.hasSpectatingBranchId = false;
            player.spectatingBranchId = 0;
            player.hasMapVoteDestinationCoord = false;
            player.mapVoteDestinationCoord = default;
            player.isEliminated = false;
            player.phase = RouteSplitPlayerPhase.ChoosingRoute;
            if (snapshot.hasSharedCurrentCoord)
            {
                player.hasSelectionCoord = true;
                player.selectionCoord = snapshot.sharedCurrentCoord;
            }

            snapshot.players[i] = player;
        }

        return snapshot;
    }

    private static bool DoesSaveRestoreSnapshotMatchSaveLocation(ForkedRoadSavedRunSnapshot snapshot, SerializableRun save)
    {
        bool saveHasCurrentCoord = save.VisitedMapCoords.Count > 0;
        if (snapshot.hasSharedCurrentCoord != saveHasCurrentCoord)
        {
            return false;
        }

        if (!saveHasCurrentCoord)
        {
            return true;
        }

        MapCoord saveCoord = save.VisitedMapCoords[save.VisitedMapCoords.Count - 1];
        return snapshot.sharedCurrentCoord == saveCoord;
    }

    private static string GetSaveRestoreSnapshotPath()
    {
        string godotPath = UserDataPathProvider.GetProfileScopedPath(
            SaveManager.Instance.CurrentProfileId,
            Path.Combine("saves", SaveRestoreSidecarFileName));
        return ProjectSettings.GlobalizePath(godotPath);
    }
}
