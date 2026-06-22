# Changelog

All notable changes to the Ember ECS Framework.

## [0.1.2] — 迭代内创建实体 / API 清理

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
