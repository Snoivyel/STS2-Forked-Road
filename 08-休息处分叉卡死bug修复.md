# 08 — Bug 3：从休息处分叉（精英/未知房）双方卡死无法进房

日期：2026-08-24。分支 `port-v0.111.0`（基于 6ec30ec）。版本号保持 1.0.11。

## 症状

休息处两人分别选精英房（(1,7) BYRDONIS_ELITE）与未知房（(2,7)→Treasure），
分叉成功锁定（batch 6）但双方都无法进入房间，停留休息处界面；
可以继续在地图上投票选下一步房间但永远无法进入。

## 机制链（godot.log 78710–78937 行实证）

1. 休息处（2,6）两人 SMITH → 投票分歧（(1,7)/(2,7)）→ `locked split batch 6`
2. 主机 `entering assigned branch 600`，房间计划正常解析/广播（Elite/Treasure）
3. **vanilla 休息处收尾流程的 `OnProceedButtonReleased` 此时才触发** →
   补丁无条件调 `NotifyLocalBranchCompleted("rest_site")` →
   branch 600 在**从未被进入**的情况下被标记完成
   （佐证：无 `ForkedRoadBranchRoomEnteredMessage`、位置永远停在 (2,6)、
   无战斗 setup 快照）
4. 旁观者状态劫持 UI → `BranchRoomCompletedMessage` → 批次/位置状态错位 → 死锁；
   后续投票全部在陈旧位置 (2,6) 上入队，永远无法移动

根因：`NotifyLocalBranchCompleted` 缺"本人已实际进入分支房间"守卫。
分叉从休息处发起时，"离开共享休息处"的 proceed 事件与"完成分支休息处"
共用同一个游戏事件源，补丁无法区分。

## 修复（`beta/Flow/ForkedRoadManager.Progress.cs` 的 `NotifyLocalBranchCompleted`）

统一守卫：`branch.EnteredPlayerIds.Contains(localPlayerId)`——
`EnteredPlayerIds` 只由 `HandleLocalRoomEntered`（房间真正打开的钩子，
Batch.cs:280，同时置 `player.Phase=InOwnBranchRoom`、`branch.Phase=InProgress`、
广播 EnteredMessage）添加。未进入则忽略完成通知并记日志。

对 6 个完成源全部成立（combat_terminal/event/rest_site/merchant/treasure/
death_clear 语义上都是"在分支房间内做完事"）；从普通房间分叉（batch 1 场景）
的 proceed 事件发生在投票前（批次未锁 → 原有 ActiveBatch 守卫已拦截），
不受影响。

## 验证与部署

- build 0 错 0 警、verify-api OK=129 FAIL=0、dump-patch 78 全解析
- 游戏运行中，自动部署监视器已挂（退出即部署四件套 + 刷新 zip + 验哈希）
- **队友需再次更新 zip**（此修复影响两端）

## 回归要点

1. 休息处分叉（不同房间类型组合，含精英/未知/商店/宝箱）→ 双方正常进入各自分支
2. 分支内的休息处正常完成（休息处分支房间里的 proceed 照常收尾）——
   守卫不误伤：那时 EnteredPlayerIds 已包含本人
3. 普通房间分叉照常（batch 1 场景）
