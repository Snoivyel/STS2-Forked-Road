# Forked Road Route Split Refactor

## 1. Goal

重构当前 `Forked Road` 的核心玩法流程，使多人联机在地图分路时可以稳定地：

- 玩家各自选择路线
- 所有玩家完成选择后，同时进入各自房间
- 各分支房间并行进行
- 先完成房间的玩家进入旁观模式
- 只有当当前批次中的所有分支都完成后，所有玩家才能进入下一次路线选择
- 当多名玩家再次选择同一路线时，重新汇合到同一个房间，并继续遵循同一套流程

## 2. Core Principle

这次重构最重要的设计原则是：

`不要再做“死亡后立即并线到其他正在进行中的分支”`

原因：

- 当前项目的大部分多人不同步都发生在“死亡后立即并入其他分支”的链路上
- 这会引入房间栈切换、战斗状态重建、玩家快照重写、checksum 编号漂移、延迟消息污染等复杂问题
- 从玩法上看，“死亡玩家改为旁观，等待本轮全部分支完成后再一起进入下一轮路线选择”已经能满足核心体验

所以新的重构方案中：

- 玩家死亡后，该玩家所在分支直接视为“该玩家已结束本分支”
- 该玩家不再插入其他正在进行的分支战斗
- 该玩家只进入旁观模式
- 下一轮路线选择开始前，再重新回到共享流程

## 3. Player Experience Flow

### 3.1 Shared Route Selection

1. 所有人停留在共享地图界面。
2. 每个玩家独立选择下一节点。
3. 所有人都确认后，系统锁定本轮路线结果。
4. 按目标节点对玩家分组，生成一个 `BranchBatch`。

### 3.2 Parallel Room Phase

1. 每个目标节点对应一个 `BranchGroup`。
2. 每个 `BranchGroup` 拥有自己的房间上下文。
3. 各组同时进入自己的房间。
4. 同一路线的玩家进入同一个房间共同战斗。
5. 不同路线的玩家进入不同房间并行战斗。

### 3.3 Branch Completion Phase

1. 某分支中的玩家先完成当前房间后，点击 `Proceed`。
2. 该玩家不立即进入下一地图节点。
3. 该玩家进入 `Spectator` 状态。
4. 画面左右显示箭头，可以切换旁观其他仍在进行中的分支房间。
5. 旁观时只能看，不能操作其他分支。

### 3.4 Batch Barrier

1. 当前 `BranchBatch` 中只要还有一个分支未完成，任何玩家都不能进入下一路线。
2. 当最后一个分支完成后，当前 `BranchBatch` 结束。
3. 所有玩家回到统一的“下一步路线选择”阶段。

### 3.5 Re-Convergence

1. 下一次选择路线时，如果多名玩家选择相同节点，则这些玩家组成同一个新 `BranchGroup`。
2. 这些玩家进入同一个房间。
3. 若所有玩家都选同一路线，则退化为普通共享房间流程。
4. 若再次分歧，则重复执行并行分支流程。

## 4. Simplified State Machine

建议把整个系统拆成下面几个明确状态：

### Run Level

- `SharedMapSelection`
- `BatchLocked`
- `ParallelRoomsRunning`
- `BatchResolved`

### Player Level

- `ChoosingRoute`
- `InOwnBranchRoom`
- `FinishedWaiting`
- `SpectatingOtherBranch`
- `ReadyForNextBatch`

### Branch Level

- `PendingEnter`
- `InProgress`
- `Completed`

## 5. Recommended Data Model

建议不要继续用“当前逻辑里大量全局变量 + 房间补丁联动”的方式承载所有状态，而是明确引入一套运行态模型。

### BranchBatch

- `batchId`
- `actIndex`
- `sourceCoords`
- `branchGroups`
- `status`

### BranchGroup

- `branchId`
- `targetCoord`
- `playerIds`
- `roomType`
- `status`
- `completionOrder`

### PlayerBranchState

- `playerId`
- `currentBranchId`
- `selectionCoord`
- `status`
- `spectatingBranchId`

### SpectatorState

- `availableBranchIds`
- `currentViewedBranchIndex`
- `canSwitchLeft`
- `canSwitchRight`

## 6. Network Responsibilities

建议网络消息只负责同步“明确状态变化”，不要再依赖大量隐式副作用。

### Required Messages

- `RouteChoiceSubmitted`
- `BranchBatchLocked`
- `BranchRoomEntered`
- `BranchRoomCompleted`
- `PlayerSpectateTargetChanged`
- `BatchAllCompleted`

### Optional Messages

- `SharedMapStateSync`
- `SpectatorUiStateSync`
- `BranchSummarySync`

## 7. Room Handling Rules

### Combat Room

- 同一 `BranchGroup` 内共享一个战斗房间
- 只同步该组玩家
- 房间结束后，该组玩家进入 `FinishedWaiting`

### Event / Question Room

- 若事件不进入战斗，则仍作为一个独立分支房间处理
- 若问号房进入怪物战，则该战斗仍属于当前 `BranchGroup`
- 事件内嵌战斗结束后，不恢复到“共享事件逻辑”，只恢复到当前分支自己的房间完成状态

### Treasure / Merchant / Rest Site

- 若多人同组进入，则共享同一个分支房间
- 若不同组分别进入，则各组独立处理
- 当前组完成后也进入 `FinishedWaiting`

## 8. Spectator UI

旁观界面应尽量简单：

- 中心显示当前正在旁观的分支房间
- 左右箭头切换到其他未完成分支
- 顶部显示：
  - `当前分支编号`
  - `剩余未完成分支数`
  - `当前正在旁观的玩家列表`
- 禁止任何会影响远端状态的输入

## 9. Convergence Rule

“汇合”不要再特殊处理成一套完全不同逻辑，而应当只是：

- 下一轮选择时，多名玩家的 `selectionCoord` 相同
- 系统按相同坐标自动分到同一个 `BranchGroup`
- 后续流程与普通分支完全一致

也就是说：

- `汇合 == 新一轮批次中多个玩家恰好分到了同一组`

## 10. Refactor Recommendation

建议按下面顺序重构，不要一次性重写所有补丁。

### Phase 1: Define Runtime Model

- 新建独立的 `RouteSplitRuntime` / `BranchBatchRuntime`
- 把“当前批次 / 分支 / 玩家状态 / 旁观状态”从零散静态字段中抽离

### Phase 2: Rebuild Route Selection

- 单独实现“提交路线 -> 锁定批次 -> 按坐标分组”
- 不在这一阶段处理死亡并线

### Phase 3: Rebuild Parallel Room Entry

- 每个分支独立进入房间
- 完成后不推进地图，只切换到 `FinishedWaiting`

### Phase 4: Add Spectator UI

- 增加左右箭头
- 增加旁观分支切换
- 保证只读

### Phase 5: Add Batch Barrier

- 全部分支完成后统一回到下一轮路线选择
- 这一步是整个系统的同步屏障

### Phase 6: Add Re-Convergence

- 允许下一轮按选择结果自然汇合
- 不再额外做“运行中并线”

## 11. What To Delete From Old Logic

这次重构建议直接废弃或弱化以下旧逻辑：

- `死亡后实时并入其他活跃分支`
- `战斗进行中跨分支复活并强制切房`
- `通过临时 checksum 放宽来兜底分支并线`
- `把旧分支所有坐标都塞进 visited/RunLocation buffer`
- `依赖大量 room-specific patch 临时补齐状态`

## 12. Minimal Acceptance Criteria

重构完成后，至少要满足这些最小验收目标：

1. 两名玩家第一次分路后，能进入不同房间并行战斗。
2. 先完成战斗的玩家只能旁观，不能提前进入下一节点。
3. 所有分支完成后，所有玩家才能进行下一次路线选择。
4. 下一次选择同一路线时，玩家能稳定汇合到同一房间。
5. 问号房进入战斗时，流程仍与普通分支房间一致。
6. 玩家死亡后不会再强制并入其他正在进行中的分支。
7. 地图绘画、指针、基础 UI 同步不依赖实时并线逻辑。

## 13. Final Recommendation

如果这次要做真正可维护的重构，建议把目标从：

`在旧逻辑上继续补丁修修补补`

改成：

`把“分路批次”当成一个独立玩法系统重做`

最关键的一句话是：

`把“死亡后并线”从系统中移除，把“所有分支完成后再统一进入下一轮路线选择”作为唯一同步屏障。`

这会让整体复杂度下降一个量级，也更符合你现在想做的玩法体验。
