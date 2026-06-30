# Changelog

All notable changes to the Ember ECS Framework.

## [0.10.10-preview] — 热路径性能修复

### Fixed
- **Tick 性能回归**：`ValidateConsistency()` 从 `Tick()` 热路径中移除（改为按需调用），消除每 Tick 的 O(N) 遍历和 `HashSet` 分配。

---

## [0.10.9-preview] — 资源边界保护

### Added
- **实体上限**：`World.MaxEntities`（默认 1,000,000），`CreateEntity` 触达上限时抛出 `InvalidOperationException`，避免无限制创建导致 OOM。
- **Chunk 上限**：`World.MaxTotalChunks`（默认 20,000），新 Chunk 分配时检查，保护 Native 内存不被耗尽。
- **活跃实体计数**：`World.AliveEntityCount` 实时追踪当前 alive 实体数量。

---

## [0.10.8-preview] — ECB 健壮性

### Fixed
- **ECB Dispose 安全**：6 个公共方法添加 `ThrowIfDisposed()` 守卫，Dispose 后调用不再访问已释放 NativeList，改为 `ObjectDisposedException`。
- **ECB SetComponent 实体校验**：回放 `SetComponent` 命令前检查实体存活状态，避免对已销毁实体的 hard crash。
- **ECB 临时实体跨缓冲区分辨**：未解析的临时 ID 在 DEBUG 下 `LogWarning`，帮助诊断 ECB 间实体引用错误。

---

## [0.10.7-preview] — 结构变更异常安全

### Fixed
- **批处理预分配**：`AddComponentBatch`/`RemoveComponentBatch` 迁移实体前先确保所有目标 Chunk slot 存在，避免中途分配失败导致部分实体已迁移的不一致状态。

### Added
- **一致性反向校验**：`ValidateConsistency` 增加 Chunk→Record 回指验证，检测双重放置和过时 Record 的 EntityRecord→Archetype→Chunk 三方不一致。
- **结构变更测试**：8 个单元测试覆盖一致性验证、批处理边界、守卫顺序。

---

## [0.10.6-preview] — 崩溃防护补充

### Fixed
- **遗漏的 ThrowIfDisposed 守卫**：`World.Exists`、5 个 BufferElement 方法、4 个 UNITY_EDITOR Buffer 内省方法添加守卫。
- **Dispose 顺序**：`m_Disposed = true` 移至 finally 块，确保异常路径也正确标记。`DisposeEcsCore` 同样移入 finally。
- **SystemTicker 入口保护**：`Tick()` 和 `TickSerialFlat()` 增加 `world.IsDisposed` 早期返回。

### Changed
- **DestroyEntity 内部拆分**：`DestroyEntity` 公开方法调用 `ThrowIfDisposed` 后委托给 `DestroyEntityInternal`，World.Dispose 中直接走内部路径避免重复检查。

---

## [0.10.5-preview] — 崩溃防护加固

### Fixed
- **World Dispose 后操作崩溃**：32 个公共 API 入口添加 `ThrowIfDisposed()` 守卫，Dispose 后调用不再抛 `NullReferenceException`，改为明确的 `ObjectDisposedException`。
- **Dispose 幂等**：重复调用 `World.Dispose()` 不再抛异常。

### Added
- **`World.IsDisposed`**：查询 World 是否已释放。
- **内部一致性自检**：`World.ValidateConsistency()` 在 DEBUG 模式下自动校验 EntityRecord → Archetype → Chunk 三方一致性，提前发现内部状态损坏。
- **崩溃场景测试**：24 个单元测试覆盖 Dispose 后 API 调用、无效 Entity、Batch null 参数等边界条件。

---

## [0.10.4-preview] — 安装文档

### Added
- **Unity 安装说明**：README 新增安装章节

---

## [0.10.3-preview] — Chunk 池化 + GC 热路径消除

### Fixed
- **TypeIdEnumerator off-by-one**：`MoveNext()` 未在移出 set bit 后递增 `m_BitPos`，导致同一 word 内首个 bit 之后的所有 bit 偏移 -1，Archetype 创建丢失组件类型。
- **ECB Allocator.Temp→Persistent**：ECB 池化后 `Clear()` 跨帧复用，但 `Allocator.Temp` 每帧释放，导致 use-after-free。修复：默认 `Allocator.Persistent` + `DisposeECB()` 正确清理。
- **Tag GetComponent 错误**：`Chunk.GetComponent<T>()` 对 Tag 组件抛出 "cannot get data for tag" 异常。修复：合并到 `offset<0` 分支，Editor 和 Release 均正确处理。
- **Tag SetComponent 兼容**：业务代码对 Tag 调用 `SetComponent<T>()` 导致异常。修复：检测 `ComponentKind.Tag`，已有 Tag → no-op，缺少 Tag → `AddComponentAt` 自动添加。

### Added
- **archetype_layout_report MCP 命令**：布局分析、浪费检测、组件拆分分析。返回 topByEntities、topByWaste、emptyChunks、componentSplitAnalysis、summary。
- **Profiling 运行时开关**：`EmberProfiler.Enabled` 静态开关，MCP 窗口一键开启/关闭 ProfilerMarker，无需重新编译。
- **EMBER_ENABLE_PROFILING 条件编译**：csproj 默认 `EMBER_ENABLE_PROFILING` 启用，可通过 `-p:EmberEnableProfiling=false` 关闭。单元测试默认关闭。

### Perf
- **4 项 CPU 吞吐优化**：扩展内联索引 512 槽（减少 Dictionary 查找）、GetChunks 缓存命中跳过 O(N) 重验证、De Bruijn TZ 表替代 BitOperations、Tag check guard 跳过无数据路径。
- **ECB 池化**：`SystemContext` 中 ECB 的 `Clear()` 替代 `Dispose()+null`，消除每帧 ECB 分配。
- **CreateArchetype List 消除**：`CreateArchetype` 和 `GetOrBuildCopyPlan` 中用两遍计数+预分配数组替代 `List<T>`；修复 `ComponentMask.ExtraWords` 共享修改 bug（struct 复制后共享引用）。
- **EmberEditorGuard**：Debug 窗口关闭时跳过所有 Editor 追踪（结构变更计数、访问验证 BitSet），`DebugWindowRefCount` 引用计数。
- **Editor CPU 优化**：`Texture2D` 创建替换为 `GUI.backgroundColor`（零分配）、`MethodInfo` 缓存避免每帧反射、SystemsWindow cached views 300ms 刷新、Repaint 节流 50ms。
- **4 项 GC 修复**：enum-based `ThrowIfEntityIndexNotAlive`（避免字符串插值）、`TryGetComponentRef<T>` 零分配 TryGet、`EntityQueryCache` 预创建常用查询、`GetSetTypes()` 零分配枚举器。
- **per-system result cache**：`GetOrCopyResult` 为每个 system 独立缓存验证结果，消除 system 间互相 invalidate 导致的频繁分配。
- **ValidateAccess 零分配**：`List<string>` 替换为预分配 `string[]` 缓冲区，声明正确时零分配。
- **Chunk 池化**：空 Chunk 不 Dispose 而是进入池（上限 8），`FindOrCreateChunkSlot` 优先从池取，消除 ECB 创建/销毁循环中的 ~4.5KB GC 抖动。
- **CompiledQueryCore List 预分配**：`m_Chunks` 初始容量 0→64，消除 0→4→8→16→32→64 级联扩容中的 ~1KB GC。
- **GetColumnFast 快速路径**：内部热路径（`ComponentPackColumnCache`、`ApplyDeferredComponentWrites`）使用无 Profiling 标记的 `GetColumnFast<T>()`，减少 96B 微分配。

### Perf
- **组件类型名缓存**：`ValidateAccess` 循环中每帧调用 `Type.Name` 产生 ~12.5KB GC。修复：`Init()` 时一次性缓存到 `m_CachedTypeNames[]`，热路径直接索引访问（零分配）。

## [0.10.1-preview] — ValidateAccess GC 分配修复

### Perf
- **ValidateAccess 零分配**：4 个 `new List<string>()` + 4 个 `.ToArray()` 替换为复用缓存 + `Array.Empty<string>()`。访问声明正确时每帧零分配，消除 ~56 次/帧的 GC 压力。

## [0.10.0-preview] — Access 验证 + 实体命名 + 编辑器代码隔离

### Added
- **Access 运行时验证**：`SystemContext` 自动追踪 Get/Set/Add/Remove 的实际组件访问，tick 结束后对比 DeclareAccess 声明。漏声明（红色）→ Console Warning；过度声明（黄色）→ 限制并行度提示。全部 `#if UNITY_EDITOR` 零运行时开销。
- **Systems Window 验证着色**：Graph 节点按访问一致性染色——🔴 红色 = 漏声明、🟡 黄色 = 过度声明、⚪ 默认 = 一致。鼠标悬停 Tooltip 列出具体差异。选中节点详情面板显示完整验证报告。
- **实体命名**：新增 `EntityName : IDataComponent`（FixedString64Bytes，零 GC）。有名称的实体在 Entities Window / Inspector 中优先显示名称，无则回退 `E(Index, vVersion)`。
- **原型自动命名**：Archetypes Window 显示可读名称（如 `Position+Velocity+…(+3)`），替代 `Arch #0`。

### Changed
- **Editor 代码隔离**：所有运行时中为 Editor 调试增加的代码统一包裹 `#if UNITY_EDITOR`（15 文件，csproj + 14 源文件）。`DefineConstants` 新增 `UNITY_EDITOR`。

### Fixed
- **Systems Window 点击检测**：节点点击改为使用完整节点 Rect，修复"点不中"问题。
- **Marquee 动画卡顿**：OnGUI 末尾添加 Repaint()，滚动不再依赖鼠标移动。
- **节点信息精简**：组件显示改为 `R:3 W:1` + `Entities: 42`（计数替代完整列表），详情面板保留完整信息。

### Other
- **ember-perf-optimize skill**：新增 §2b Access Validation 优化章节——着色规则、Tooltip、Console 警告、优化流程、代码示例。

### Added
- **Graph 独显**：Systems Window 移除 Table 视图，Graph 为唯一展示模式。简化 Toolbar，仅保留 Ticker 下拉。
- **性能数据显示**：Graph 节点上直接展示 Avg ms，超过 1ms 红色高亮。点击节点，详情面板展示 Last Tick / Average / Max / Tick Count 四项性能指标。
- **跑马灯自动滚动**：系统名称超出节点宽度时自动左右往复滚动（40px/s），两端各暂停 1 秒以便阅读。

### Changed
- **Systems Window 精简**：移除 Table 视图、Graph/Table 切换按钮、Lifecycle/Layers 勾选框。净减 83 行。

## [0.8.0-preview] — 依赖图可视化 + MCP 稳定性修复

### Added
- **依赖图视图**：Systems Window 新增 Graph/Table 切换按钮，Graph 视图以分层节点图展示依赖关系。并行层绿色背景、串行层灰色背景，系统节点显示类型和 Read/Write 组件访问列表。箭头标识依赖方向，选中节点底部详情面板展示 Hook、组件访问、层信息。

### Fixed
- **MCP 连接稳定性**：修复 `s_Client.Connected` 在 macOS 不可靠导致的响应静默丢弃、`ListenLoop` 异常未 break 导致 TCP 连接被 finally 关闭、`Execute()` 抛异常而非返回 error JSON 等多个连接重置问题。
- **MCP 安装流程**：修复 `--port` 写死、路径依赖绝对路径、缺少 `runtimeconfig.json` 导致 `dotnet exec` 崩溃、JSON 合并时多出逗号等问题。新增一键 Install/Uninstall、自动路径更新。

---

## [0.7.5-preview] — Skills 一键安装 + 数据流视图优化

### Added
- **Skills 一键安装**：MCP 窗口新增 Skills foldout，一键安装 `ember-perf-optimize`（性能优化）和 `ember-architecture`（架构指南）到项目 `.claude/skills/`，Claude Code 和 Codex 自动发现。`EmberSkillManager` 内嵌完整 SKILL.md 内容为编译时常量，`IsInstalled()` 运行时文件存在性检查。
- **ember-perf-optimize skill**：覆盖 System 类型选择、DeclareAccess 精确度、Job 结构体设计、ChunkJobMeta 访问、Structural Change 优化、NativeArray 生命周期等 8 个优化域。MCP 诊断工作流：world_info → system_status → get_dependency_graph → get_archetypes。
- **ember-architecture skill**：覆盖存储模型（Entity/Component/Archetype/Chunk/SOA）、System 类型体系（SystemBase/DeclaredSystemBase/JobSystem/SystemGroup）、查询管线（ctx.QueryChunks/QueryBuilder/Aspect）、ECB/Buffer/Singleton、DependencyGraph 并行调度、MCP 调试命令速查表。

### Changed
- **数据流视图**：按执行层排序展示（refine: data flow view — display by execution layer order）

---

## [0.7.1-preview] — Editor 窗口列对齐修复 + Entities 界面简化

### Fixed
- **4 个 Editor 窗口列对齐**：`SystemsWindow`/`EntitiesWindow`/`ArchetypesWindow`/`ComponentTypesWindow` 表头与数据行宽度错位。根因：表头在 `ScrollView` 外用 `EditorStyles.toolbar`，数据行在内用 `EditorStyles.helpBox`，滚动条 + 内边距差异导致对齐偏移。修复：表头统一移入 `BeginScrollView`，用 `EditorGUI.DrawRect` + 手动 `Rect` 定位，列宽常量化。

### Changed
- **EntitiesWindow 简化**：移除内联 Components 列，点击 Entity 自动打开 `EntityInspectorWindow` 独立查看组件详情。工具栏 Filter 改用 `EditorStyles.toolbarSearchField`。

---

## [0.7.0-preview] — 开发 Agent 体系 + Editor 工具增强

### Added
- **开发 Agent 体系**：`ember-dev` 主入口 + `ember-code-review`/`ember-perf-check`/`ember-unit-test`/`ember-release` 子 Agent，覆盖代码审查、性能检查、单元测试、发布流程。
- **ComponentInspector 可编辑**：Play Mode 下 int/float/double/bool/long/string 字段直接编辑，`World.SetComponent<T>` 通过反射自动回写。
- **SystemsWindow 计时列**：LastTick(ms)、Avg、Max、TickCount、LastError 列，>1ms 红色高亮。
- **SystemsWindow 并行层视图**：Toggle `DrawParallelLayers` 显示每层系统列表，并行层绿色标识，串行层默认色。
- **Bridge 自动重连**：`EditorApplication.playModeStateChanged` 回调，退出 PlayMode 优雅断连，进入 PlayMode 自动重启。
- **SystemBase 公开接口统一**：`DeclaredSystemBase` 替代 `SystemBase` 作为用户继承入口，`SystemBase` 改为 internal。

### Fixed
- **8 个 Write Command 修复**：struct 副本修复（`AddComponentToMask` 传 ref）、NRE 守卫、`FindWorldMethod` 消除 Entity/int 重载歧义、batch 验证、`remove_component` 加 HasComponent 检查。
- **3 个 JSON/Parsing 修复**：`SimpleJson.IsNumeric()` 全量校验消除 `"10_ECSVsOOP"` 误判、stale ops 清理、`advance_frame deltaTime` GetFloat。
- **4 个 MCP Bridge 稳定性修复**：初始连接失败不退出、渐进重连延迟 70s、Heartbeat BOM 修复、并行层 EndTick 执行顺序。

### Changed
- **EmberBridgeCommands.cs 拆分**：2872 行 monolith → 主文件 2788 行 + `EmberBridgeCommands.Helpers.cs`（93 行），8 个 helper 方法提取为 partial class。
- **CLAUDE.md 更新**：补充并行调度、MCP bridge、开发 workflow 文档。

### Perf
- **并行层零分配计时**：`GetTimestamp()` 替代 `Stopwatch.StartNew()`，消除 per-system tick 分配。

---

## [0.6.0-preview] — 真并行调度 + MCP 同步

### Added
- **ComponentInspector 可编辑**：Play Mode 下 int/float/double/bool/long/string 字段直接编辑，`World.SetComponent<T>` 自动回写。
- **SystemsWindow 计时列**：LastTick(ms)、Avg、Max、TickCount、LastError，>1ms 红色高亮。
- **SystemsWindow 并行层视图**：Toggle 显示每层系统列表，并行层绿色标识，串行层默认色。
- **Agent 体系**：`ember-dev` 主入口 + `ember-code-review`/`ember-perf-check`/`ember-unit-test`/`ember-release`。

### Fixed
- **PlayMode 自动重连**：`EditorApplication.playModeStateChanged` 回调 — 退出 PlayMode/域重载时优雅断连，进入 PlayMode 时自动重启 bridge。不再需要手动 `EmberBridge.Stop(); EmberBridge.Start();`。
- **SimpleJson.BuildJson 数字误判**：`char.IsDigit(v[0])` 导致 `"10_ECSVsOOP"` 输出为非法 JSON `10_ECSVsOOP`。改为 `IsNumeric()` 全量校验。
- **McpTools 命令列表过期**：描述改为分类引用，不再硬编码 26 个旧命令。
- **advance_frame deltaTime**：`GetInt` → `GetFloat`，正确解析 `0.0167`。
- **MCP Server 初始连接失败不退出**：改为重试而非直接 exit。
- **MCP Server domain reload 超时不足**：渐进延迟 70s（5×2s + 12×5s）。
- **并行层 EndSystemExecution 顺序**：移到 `EndParallelLayer` 之前执行。

### Added
- **WorldSafety 并行层**：`BeginParallelJobLayer()`/`EndParallelJobLayer()` 允许多系统同层并发。
- **JobHandle 依赖链**：`JobSystem<TJob>.ScheduleJob` 返回 `JobHandle`，Ticker 收集→`CombineDependencies`→`Complete`。
- **注册顺序定向依赖**：`DependsOn(a,b)` 改为 `hasConflict && a > b`，后注册依赖先注册。
- **QueryMask 拆分**：`GetQueryMask()` 虚拟属性，默认 = AllMask，可重写缩小查询范围。
- **MCP `get_system_info`**：按 tickerIndex+systemName 查系统类型/计时/hooks。
- **MCP `capabilities` 更新**：`supportsParallelism`/`supportsJobSystem`/`supportsDependencyGraph`。
- **MCP `get_dependency_graph` 增强**：每层输出含系统类型（`{name, type}`）。

### Perf
- **systemIndices 字段复用**：`List<int>` 类字段，跨层复用。
- **移除 LINQ**：`layer.Systems.All()` → 手动 for 循环，消除 delegate 分配。
- **移除 `System.Linq`**：`SystemTicker` + `DependencyGraph` 无 LINQ 依赖。

### Changed
- `JobSystemBase.ScheduleJob` 返回值 `void` → `JobHandle`。
- `ChunkJobScheduler.Schedule<T>` 保持兼容（同步 Complete），`ScheduleAsync<T>` 供框架内部。

## [0.5.2-preview] — 性能 + 稳健性扫除

### Perf
- **ComponentMask >256 COW 消除**：`EnsureExtraCapacityForWrite` 容量足够时不再 `CloneExtraForWrite`。
- **DeferredDestroy O(1)**：`List` 线性扫描 → `Dictionary` O(1) 查找。
- **GetDebugView 缓存**：数组只在系统数量变化时重建。

### Fixed
- **12 处裸 `catch {}`** 改为日志：异常不再静默吞。
- **Chunk disposed guard**：`AllocRow`/`RemoveAtSwapBack` 加 `ObjectDisposedException`。
- **Entity version overflow**：`int.MaxValue` wrap 正确重置到 1。
- **FreeEntityIndices cap**：上限 65536，防止无界增长。
- **Error message context**：`ComponentTypeRegistry` 异常加 `max registered` 范围。
- **BufferElement 类型安全**：`Dictionary<Type, object>` → `IBufferElementStore` 泛型接口。

## [0.5.1-preview] — 混合层语义修复 + 零分配加固

### Fixed
- **SystemTicker 混合层**：混入 SystemBase/DeclaredSystemBase 的层整层串行执行，不再跳过非 JobSystem。
- **NativeArray 泄漏**：`ScheduleAsync` + `SystemTicker` 加 try-finally/catch 保护，异常路径不泄漏。

### Added
- **`DeclaredSystemBase`**：串行 System 声明读写访问后参与依赖图，不被当作全局 barrier。
- **`Slot<TComponent>()`**：`JobSystemBase` 提供组件槽位索引查询，用户不再手算 Comp0-3。
- **`MovementChunkMeta.Wrap()`**：Source Generator 编译期生成类型安全 wrapper。
- **ChunkJobMeta overflow**：≤4 固定槽位零开销，>4 透明溢出缓冲（内部指针，用户无感知）。

### Perf
- **预计算 AllComponentTypes[]**：`BuildAccess` 时排序一次，调度时不再每帧 `new List` + registry 扫描。
- **Warning 一次**：overflow warning 改为 static bool，每次 session 只打一次。
- **systemIndices 复用**：`List<int>(8)` 跨层复用。
- **`GetTimestamp()`**：替代 `Stopwatch.StartNew()`，零分配计时。

## [0.5.0-preview] — IJobParallelFor 默认路径 + 零分配调度

### Changed — Breaking
- **`IEmberChunkJob.Execute` 签名变更**：`Execute(Chunk chunk, int)` → `Execute(ChunkJobMeta meta, int)`。ChunkJobMeta 含 BufferPtr、EntityCount、Comp0-3 Offset/Stride，通过 unsafe 指针直接访问组件数据。
- **`JobSystem<TJob>` 替代 `JobSystemBase`**：并行 System 继承 `JobSystem<MoveJob>`（泛型），`CompileJob` 返回具体 struct 类型，`Schedule<T>` 自动走 IJobParallelFor（零 delegate/closure/装箱）。
- **移除 `Parallel.ForEach` 路径**：默认调度改为 `ChunkJobWrapper<T> : IJobParallelFor`，消除托管调度分配。
- **移除 `ExecuteUnsafe`**：不再需要双路径，IJobParallelFor 是唯一默认路径。

## [0.4.1-preview] — GC 零分配 + IJobParallelFor 接入 + MCP 多实例修复

### Fixed — GC
- **Stopwatch.StartNew() → GetTimestamp()**：每 system tick 零分配计时。
- **IEmberChunkJob struct 装箱**：新增 `Schedule<T>()` 泛型重载，struct job 直传不装箱。
- **No-op 双调度**：新增 `HandlesOwnScheduling` 属性，系统自行调度时跳过默认路径。

### Added — 并行
- **IJobParallelFor 接入**：`ScheduleUnsafe<T>` 自动从 `ArchetypeLayout` 提取组件 offset/stride 填入 `ChunkJobMeta`，`ChunkJobWrapper<T> : IJobParallelFor` 包装调度。
- **SystemTicker JobHandle 依赖链**：并行层 `CombineDependencies + Complete` 层边界同步。

### Fixed — MCP
- **多 Unity 实例端口发现**：`SimpleJson.GetString` 同时解析字符串值和整数值；`ScanPorts` 不再空吞异常。
- **Heartbeat BOM**：`WriteHeartbeat` 改为 `new UTF8Encoding(false)`，不再写 BOM。
- **Handshake 容错**：`System.Text.Json` 失败时用 `ParseHandshakeManually` 逐字段提取。
- **TOML RemoveEmberFromConfig**：修复 `\n[` 误匹配 array bracket 导致裸 JSON 残留。
- **activeScene null guard**：handshake 和 ProcessQueue 加空串兜底。

---

## [0.4.0-preview] — 并行化 + MCP v0.4.0

### Added — 并行化
- **EcsSystem 抽象基类**：提取 OnCreate/OnDestroy/生命周期钩子/ECB，SystemBase 和 JobSystemBase 兄弟类共享。
- **JobSystemBase**：声明读写访问 + CompileJob → IEmberChunkJob，框架自动依赖图 + 并行调度。
- **AccessBuilder**：`access.Read<T>().Write<T>()` fluent 声明，框架据此推导依赖图。
- **DependencyGraph**：拓扑分层 + 保守掩码互斥（未声明 = 与所有系统串行）。同层无冲突系统并行。
- **SystemTicker 层调度**：`Tick()` 按 Layer 执行，并行层走 ChunkJobScheduler，串行层走 TickSystemAt。
- **ChunkJobScheduler**：双路径 — `Schedule()`（Parallel.ForEach，默认）+ `ScheduleUnsafe<T>()`（IJobParallelFor + Burst-ready）。
- **SystemTicker 计时环**：per-system 20-tick 环 avg/max，3-entry 错误环，tickCount。

### Added — MCP v0.4.0
- **统一 `ember_execute` 协议**：12 分散工具 → 40 命令入口，一次 TCP 往返批量执行。
- **BufferHandle 内省**：`get_buffer`（entity+fieldPath / singleton+fieldPath）、BufferStore 分布统计。
- **统一 envelope**：`{ok, world, data/error, warnings, truncated, nextCursor}` + 结构化错误码。
- **新增命令**：`capabilities`、`list_worlds`、`component_schema`（isBufferHandle/isEntity/isWritable）、`validate_component_payload`（fieldPath 级错误）、`query_entities_v2`（all/any/none）、`get_entity_full`（BufferHandle 摘要）、`get_singletons`（namesOnly）、`system_status`（avg/max/tickCount/error 环）、`world_snapshot`（snapshotId + 分布）、`snapshot_diff`、`trace_entity`、`query_archetypes`、`safe_write_batch`（默认 dryRun）。
- **World introspection API**：`EnumerateBufferStores()`、`GetBufferInfo()`、`ReadBufferSample()`、`FindBufferOwners()`。

### Fixed
- MCP Server 断线后自动重连（10 次 × 2s），不再致命退出。
- 重连后 warmup ping 防首包超时。
- `ECSManager.Start()` 自动设置 `Active = this`，Dispose 自动清理。
- `get_ecs_status` null manager 守卫。
- `safe_write_batch` ECB create_entity tempIndex 修复 → 直接执行。
- `AccessBuilder` struct 副本修复。

### Changed
- `SystemBase` 移除了 `OnCreate`/`OnDestroy`/钩子 → 迁移到 `EcsSystem`（向后兼容，零改动）。
- `SystemTicker.Register<T>()` 约束从 `SystemBase` 改为 `EcsSystem`。
- `SystemGroup : SystemBase : EcsSystem` 继承链不变。

---

## [0.3.0] — Editor 可视化调试工具

### Added
- **Systems 窗口** (`Window > Ember > Systems`)：Ticker 下拉切换，系统执行顺序列表，生命周期钩子标识（EC/ED/CA/CR），SystemGroup 高亮显示。
- **Entities 窗口** (`Window > Ember > Entities`)：组件名过滤 + 分页（200条/页），实体列表含 Archetype/Components/Placed 列，选中实体下方展开组件值面板。
- **Archetypes 窗口** (`Window > Ember > Archetypes`)：全局填充率条，原型分布列表，Chunk Fill Histogram 直方图，选中原型展开各 Chunk 详情。
- **Component Types 窗口** (`Window > Ember > Component Types`)：组件注册表全览，TypeId/Kind/Size/Alignment 列，搜索过滤，选中显示完整类型信息。
- **Component Inspector**：反射读取 Chunk 原始字节渲染组件 struct 字段，支持 IDataComponent/ITagComponent/IBufferElement 区分显示。
- **DebugView 系统**：`WorldDebugView`、`EntityDebugView`、`ArchetypeDebugView`、`ChunkDebugView`、`SystemTickerDebugView`、`SystemInfoDebugView` — 只读快照 struct，仅在 Editor 窗口打开时构造，关闭 = 零开销。
- **ECSManager.Active**：静态引用，Editor 窗口通过此属性获取运行时的 Manager 实例。
- **ECSManager.GetDebugViews()** / **World.Debug** / **World.GetComponentBoxed()** / **World.GetEntityComponentTypes()**：Debug API 层。

### Changed
- **ECSManager**：新增 `Active` 静态属性和 `GetDebugViews()` 公共方法。
- **SystemTicker**：新增 `GetDebugView(int)` 内部方法。
- **World.Listeners**：`IsOverridden` 从 private 改为 internal static，供 SystemTicker 复用。
- **csproj**：新增 `InternalsVisibleTo("Ember.Editor")`，排除 `Editor/**` 编译。
- **包结构**：新增 `Editor/Ember.Editor.dll` (~20KB) + `Editor/Ember.Editor.asmdef` + `.meta` 文件。

---

## [0.2.1] — API 精简 / 性能优化 / 单元测试

### Added
- **EntityQuery 静态工厂**：`EntityQuery.With<Health, Position>().None<DeadTag>()` 链式构造，0GC struct builder。
- **SystemContext 快捷方法**：`ctx.Get<T>(e)` / `ctx.Set<T>(e,v)` / `ctx.Has<T>(e)` 直接变组件，零开销 wrapper。
- **6 个新 ProfilerMarker**：CreateEntity、DestroyEntity、ChunkColumn.Access、ECB.Playback、FlushDeferredCreates、QueryMatches。
- **NUnit 单元测试项目**：`tests/Unit/`，205 tests，12 个纯 C# 类型全覆盖，`dotnet test` 一键运行。

### Changed
- **移除 ColumnAccessor&lt;T&gt;**：消除每个 column×chunk 的堆分配和冗余 Dictionary 查找，`SystemChunk.Get<T>()` 直通 `Chunk.GetColumn<T>()`（O(1) 数组索引）。
- **ChunkColumn.At() + AggressiveInlining**：保证 JIT 内联指针运算。
- **EMBER003 诊断从 Warning 升级为 Error**。
- **README**：Buffer/BufferElement 区分为两章，EntityQuery 示例更新，ComponentMask With vs WithAdded 警告。

### Fixed
- **ECB 异常时静默丢弃**：系统 OnTick 抛异常后 ECB 始终回放。

---

## [0.2.0] — 生命周期钩子 / DeferredDestroy / ComponentPack Generator / PairQuery

### Added
- **迭代中创建实体**：`SystemContext.CreateEntity()` 允许在 query 遍历中创建实体并立即获取真实 `Entity` 引用，chunk placement 自动延迟到 `foreach` 结束时执行。解决了 ECB 临时 ID 无法存入组件数据的限制。
- **EntityRecord.Placed 标志**：区分已分配但尚未放入 chunk 的实体，所有组件访问方法对 pending 实体返回明确错误。
- **ComponentPackAdapterGenerator**：编译期自动生成 `IComponentPackAdapter<T>.Describe()`，通过 Roslyn SemanticModel 分析 Read/Write 方法体中的 `ctx.Read<T>()` / `ctx.Write<T>()` 调用，消除手写 Describe() 与代码不一致的风险。与 `ComponentRegistrationGenerator` 同 DLL，支持 HybridCLR。
- **Entity/Component 生命周期钩子**：`SystemBase` 新增 4 个 `protected virtual` 钩子——`OnEntityCreated`、`OnEntityDestroyed`、`OnComponentAdded`、`OnComponentRemoved`。按需 override，不 override = 零开销。World 维护独立监听者列表，精确投递。
- **DeferredDestroy 自动 flush**：每个系统 Tick 结束后（ECB playback 之前）自动执行延迟销毁实体的真正销毁。之前标记了延迟销毁的实体只在 World.Dispose 全量销毁时才清理。
- **World.cs 拆分**：1615 行拆为 10 个 partial 文件，按 #region 职责分文件，零逻辑改动。
- **结构变更错误信息升级**：修复 `AddComponent<T>` 缺失的 `Placed` 守卫；数字 typeId 替换为组件类型名；Chunk 内部错误加上下文和 "this is a framework bug" 指引。

### Removed
- **Aspect 子系统**：`IAspect`、`AspectRegistry`、`AspectQueryBuilder` 等全部移除。该功能从未被实际使用，查询统一通过 `SystemContext.QueryChunks()` / `EcsAPI.Query()` 完成。
- **SystemBase.CreateQuery / GetQuery / GetChunks**：旧查询声明模式移除。系统内查询统一使用 `SystemContext.QueryChunks()`。
- **SystemTicker.runOnce**：`Register<T>(runOnce: true)` 参数移除。一次性初始化逻辑改用 `OnCreate()` 或系统内标志位。
- **QueryBuilder.Any<T> / None<T> / GetEnumerator()**：`QueryBuilder` 上的 `Any`/`None` 链式方法和隐式 `foreach` 支持移除，简化 API 表面积。
- **文件删除**：`SparseSet<T>`、`ListPool<T>`、`IBufferStore`、`ReadOnlyChunkView` 等从未集成或已被替代的文件移除。
- **World 死代码**：`GetReadOnlyChunkView()`、`TryGetEntityLocation()`、`CollectAliveIndices()`、`EnsureEntityCapacity()` 等无调用者的方法移除。
- **Chunk(NativeArray<int>) 构造函数**：有缺陷且从未使用的重载移除。

### Changed
- **IComponent 不再继承 IDisposable**：组件 struct 不再需要实现 `Dispose()` 方法。
- **SystemBase 精简**：`OnTick(World, float)` 重载移除，唯一入口为 `OnTick(SystemContext ctx)`。通过 `ctx.World` / `ctx.DeltaTime` 访问。
- **IBufferStore → IDisposable**：`BufferStore<T>` 的内部接口简化，`IBufferStore` 移除，直接实现 `IDisposable`。

### Fixed
- **Native 内存泄漏**：修复 `World.Dispose()` 在异常场景下跳过 `DisposeBufferStores()` 和 `DisposeEcsCore()` 导致 Native 容器泄漏的问题。三层防御：`SystemTicker.Dispose` 中每个系统的 `OnDestroy` 独立 try-catch，`ECSManager.Dispose` 以 try-finally 保证 World 必定销毁，`World.Dispose` 以 try-finally 保证 BufferStore 和 Archetype/Chunk 的 Native 内存必定释放。
- **README 章节编号**：多次重构后的编号偏移修复，所有子节编号与父章节对齐。
- **Benchmark 编译**：`StructuralChangeBenchmarks` 中对已删除的 `QueryBuilder.GetEnumerator()` 的 `foreach` 调用修复为 `.AsRows()`。
- **ECB 异常时静默丢弃**：修复系统 `OnTick` 抛异常时 ECB 命令被静默丢弃的问题。ECB（和 DeferredDestroy flush）现在始终在 `EndTick` 中执行，无论系统是否成功完成。

---

## [0.1.0] — 架构统一

### Added
- **ECSManager**：顶层的系统管理器，持有多个整数索引的 `SystemTicker`，由用户决定在 Unity 哪个生命周期驱动（`FixedUpdate` / `Update` / `LateUpdate`）。
- **SystemTicker**：零分配 Tick 循环，按注册顺序保守执行系统，自动回放 ECB。
- **SystemGroup**：系统组合模式，`Configure(SystemTicker)` 展开子系统和嵌套组，注册顺序即执行顺序。
- **SystemContext**：统一的系统查询入口。提供 `QueryChunks<T>()`、`Lookup<T>()`、`Pack<T>()`、`ECB` 等复用缓存。
- **ComponentPack**：将查询结果打包为连续数组进行高频计算，通过 `IComponentPackAdapter<T>` 描述读写映射。
- **IBufferElement**：实体级动态数组组件，实体内可携带多个同类型元素。
- **Source Generator**：编译期自动扫描 `IComponent` 实现生成注册代码（`src/Generator/`）。
- **ArchetypeIndex**：基于 `BitSet` 的组件类型到 archetype 位图索引，加速查询匹配。
- **ComponentTypeRegistry.Seal()**：注册完成后锁定，防止运行时意外注册。
- **EmberProfiler**：Unity ProfilerMarker 插桩支持，通过 `EmberEnableProfiling=true` 编译开关启用。
- **DeferredDestroySet**：O(1) 去重的延迟销毁集合。

### Changed
- **`ECSManager` 替代 `ECSServer`**：系统生命周期管理从单体 Server 改为多 Ticker 架构。
- **`SystemBase` 统一**：`TickSystem`、`ChunkSystem`、`EntitySystem` 三种系统子类型合并为单一 `SystemBase`，全部通过 `OnTick(SystemContext)` 表达。
- **手动注册替代特性标注**：移除所有执行顺序特性，系统通过 `ticker.Register<T>()` 显式注册。

### Removed
- `ECSServer`（由 `ECSManager` 替代）
- `TickSystem` / `ChunkSystem` / `EntitySystem`（统一为 `SystemBase`）
- 系统执行顺序特性及反射调用
- `InitSystem`（由 `SystemBase.OnCreate()` 替代）
- `ChunkReadLease` / `ReadOnlyChunkView`（不再使用的旧线程安全模型）
