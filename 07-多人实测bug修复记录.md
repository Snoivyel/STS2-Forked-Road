# 07 — ForkedRoad 1.0.11：多人实测两个 bug 的定位与修复

日期：2026-08-24。分支 `port-v0.111.0`（基于 88c78bd）。版本号保持 1.0.11 不变。

## 背景

用户双人实测（godot.log 9.2MB，2026-08-24 01:24–01:52 会话）暴露两个 bug。
两个 bug 均为**原版代码潜伏缺陷**（src/ 树逻辑逐字相同），非 0.111.0 移植引入。

## Bug 1：QuickSL（DirectConnectIP 模组 QuickSlCore）重开后分叉崩坏

**症状**：两人房间怪变化 + 无法出牌（PlayCardAction NRE）。

**机制链**：
1. 分叉锁定时模组把批次状态存入 sidecar（`current_run_mp.forkedroad.json`）
2. QuickSL 走游戏原生 `NGame.LoadRun` 重开；模组设计上支持"读档续玩分叉"
   （`TryHandleSavedSplitLoad` → `LoadIntoLatestMapCoordWithRestoreAsync`，
   含按分支坐标+已解析遭遇重新进入分支房间的完整路径）
3. **缺陷**：`TryGetSavedRestoreLocalCoord` 用 `selectionCoord != sharedCurrentCoord`
   判断"是否需要分支恢复"，而 `sharedCurrentCoord` 快照时取的是主机实时 run 状态
   坐标 = 主机分支坐标 → 主机端恒判"无需恢复"走 vanilla 回退；客户端（坐标不同）
   走分支恢复 → 两端两套重进流程 → CombatStateSynchronizer 同步饿死 →
   两端各自生成战斗（怪不同）→ 远端出牌在主机找不到目标生物 → NRE
4. 佐证：整个会话 `restoring local player to saved branch coord` 出现 0 次

**修复**（`beta/Flow/ForkedRoadManager.SaveRestore.cs` 的 `TryGetSavedRestoreLocalCoord`）：
快照 `hasActiveBatch` 时必走分支恢复——恢复坐标取本人 `selectionCoord`，
缺失则回退到本人所在分支的 `targetCoord`；原比较逻辑仅保留给"无批次但位置分歧"场景。
**不动** `CreateSaveRestoreSnapshot` 的字段语义：`sharedCurrentCoord` 同时被
`DoesSaveRestoreSnapshotMatchSaveLocation` 用作与游戏存档的配对校验，改了会误杀快照。

## Bug 2：共享房间双人同时死亡 → 复活保 1 血 + 误判胜利

**症状**：两人同房间同时死亡，一人复活 1 血、战斗判胜；日志尾部同一角色死亡
被记 3 次（3→5 losses）、随后又记败北、进 game over——胜/负两条路径并行执行。

**机制链**：
1. `CombatManager.HandlePlayerDeath` 补丁 → `OnCombatPlayerDied` →
   `ShouldTriggerDeathClear` 只判"本战斗全员死亡"（`Players.All(IsDead)`），
   不要求活跃批次、不区分分支/共享房间
2. 事故时批次早已结束（日志 71301 行快照已删、ActiveBatch=null）——
   纯共享战斗团灭被当成"分支全灭死亡清除"
3. `ClearCombatAfterPlayerDeathAsync` 把活敌全部 `CreatureCmd.Escape` 再
   `CheckWinCondition()` → 敌全灭判 WIN → 原生 `ReviveBeforeCombatEnd` 复活到 1 血
4. 被挂起的 vanilla 伤害链继续结算第二个死亡 → 撞上已结束的战斗 →
   `killed outside of combat in multiplayer` 报错 + 死亡重复计数

**修复**（`beta/ForkedRoadManager.cs` 的 `ShouldTriggerDeathClear`）：
增加门槛 `IsSplitBatchInProgress && branch != null`——死亡清除仅限活跃批次中
本人所在分支的战斗；共享房间团灭交还 vanilla 判负流程（单条路径、死亡记一次）。
`OnCombatPlayerDied` 与 StartTurn 钩子两个调用点同被此门槛覆盖；
`ShouldSkipReviveBeforeCombatEnd` 依赖的抑制标记只由 `MarkDeathClearTriggered`
设置，随新门槛自然限定在分支场景。

## 移植审查（排查 0.111.0 适配引入的 bug）

- 已知 0.111.0 破坏 API（`MaxPlayers`/`versionInfo`/`EndPlayerTurnPhaseOne*`/
  `StartRunLobbyPlayer/LoadRunLobbyPlayer`）在 beta/ **零引用**——影响面不存在
- `EndCombatInternal` 挂点：挂带 `CombatTurnState` 的生产重载；反编译确认
  `CheckWinCondition` 有 `PendingLoss` 优先 + `IsCombatEnding` 防重入守卫，
  补丁不会重复触发
- `EventCombatSynchronizer.InitializeForEvent` 字段 swap：Prefix/Finalizer 成对，
  异常路径也恢复；`BeginEvent` 传参逐字镜像游戏内部调用
- 静态门槛复验：build 0 错 0 警、verify-api OK=129 FAIL=0、dump-patch 78 全解析
- 结论：**未发现移植引入的 bug**；两个实测 bug 均为原版潜伏缺陷

## 部署

- 版本号不变（1.0.11），`mods\ForkedRoad-1.0.11\` 四件套 + 分发 zip 同步刷新
- **队友必须同步更新到本构建**（zip）——QuickSL 分叉恢复是两端协同流程，
  一端旧版仍走 vanilla 回退就会复现分歧

## 回归测试清单（两人局）

1. 分叉进行中 QuickSL：双方各自回到分支房间、遭遇不重摇、能出牌
2. 无分叉普通局 QuickSL：行为与 vanilla 一致（不重摇怪）
3. 共享房间双人同时死亡：直接判负进 game over，无复活、无重复死亡计数
4. 分支战斗单人死亡：死亡清除照常（分支收尾、另一分支继续）——确认新门槛没误伤
5. 事件战斗（EventLayoutType.Combat）进出正常（0.111.0 新流程 + 字段 swap 路径）
