# Changelog

All notable changes to the Ember ECS Framework.

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
