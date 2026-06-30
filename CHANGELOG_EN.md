# Changelog

All notable changes to the Ember ECS Framework.

## [0.11.0-preview] — API Simplification

### Changed — Breaking
- **System rename**: `SystemBase` → `SimpleSystem`, `DeclaredSystemBase` → `DeclaredSystem`. Names now reflect usage: SimpleSystem = simple serial + global barrier, DeclaredSystem = declared access + dependency graph participant.

### Added
- **`Chunk.At<T>(index)`**: Direct typed component ref access without compIndex. `chunk.At<Position>(i)` replaces `chunk.Get<Position>(0).At(i)`.

---

## [0.10.11-preview] — MCP Performance Diagnostics

### Added
- **`perf_summary` MCP command**: One-click performance diagnostic. Samples N frames, auto-ranks slowest systems, returns frame-level timing breakdown with Top-N slow systems. Supports `tickerIndex`/`sampleFrames`/`topN` parameters.
- **Lightweight PerfCollect**: `EmberEditorGuard.ForcePerfCollect` flag. MCP commands can temporarily enable system timing collection without opening debug windows.
- **Perf skill update**: `ember-perf-optimize` diagnostic workflow now lists `perf_summary` as the primary entry point.

---

## [0.10.10-preview] — Hot-Path Performance Fix

### Fixed
- **Tick performance regression**: Removed `ValidateConsistency()` from hot `Tick()` path (now on-demand only), eliminating per-Tick O(N) traversal and `HashSet` allocation.

---

## [0.10.9-preview] — Resource Boundary Protection

### Added
- **Entity limit**: `World.MaxEntities` (default 1,000,000). `CreateEntity` throws `InvalidOperationException` when limit is reached, preventing OOM from unbounded creation.
- **Chunk limit**: `World.MaxTotalChunks` (default 20,000). Checked on new Chunk allocation, protecting Native memory from exhaustion.
- **Alive entity count**: `World.AliveEntityCount` tracks the current number of alive entities in real time.

---

## [0.10.8-preview] — ECB Robustness

### Fixed
- **ECB Dispose safety**: Added `ThrowIfDisposed()` guards to 6 public methods. Calls after Dispose throw `ObjectDisposedException` instead of accessing disposed NativeList.
- **ECB SetComponent entity validation**: Check entity alive status before replaying `SetComponent` commands, preventing hard crash on destroyed entities.
- **ECB temp entity cross-buffer detection**: Unresolved temp entity IDs log `LogWarning` in DEBUG mode, helping diagnose cross-buffer entity reference errors.

---

## [0.10.7-preview] — Structural Change Exception Safety

### Fixed
- **Batch pre-allocation**: `AddComponentBatch`/`RemoveComponentBatch` pre-allocate all target chunk slots before migrating any entity, preventing partial state if allocation fails mid-batch.

### Added
- **Reverse consistency validation**: `ValidateConsistency` adds Chunk→Record back-pointer verification, detecting dual-placement and stale records (EntityRecord→Archetype→Chunk 3-way inconsistency).
- **Structural change tests**: 8 unit tests covering consistency validation, batch boundaries, and guard ordering.

---

## [0.10.6-preview] — Crash Prevention Refinements

### Fixed
- **Missing ThrowIfDisposed guards**: Added guards to `World.Exists`, 5 BufferElement methods, and 4 UNITY_EDITOR Buffer introspection methods.
- **Dispose ordering**: Moved `m_Disposed = true` and `DisposeEcsCore()` into the finally block to ensure correct marking on exception paths.
- **SystemTicker entry guard**: Added `world.IsDisposed` early return in `Tick()` and `TickSerialFlat()`.

### Changed
- **DestroyEntity internal split**: Public `DestroyEntity` calls `ThrowIfDisposed` then delegates to `DestroyEntityInternal`. World.Dispose uses the internal path to avoid redundant checks.

---

## [0.10.5-preview] — Crash Prevention Hardening

### Fixed
- **World post-Dispose crash**: Added `ThrowIfDisposed()` guards to 32 public API entry points. Calls after Dispose throw `ObjectDisposedException` instead of `NullReferenceException`.
- **Idempotent Dispose**: Repeated calls to `World.Dispose()` no longer throw.

### Added
- **`World.IsDisposed`**: Property to query whether a World has been disposed.
- **Internal consistency self-check**: `World.ValidateConsistency()` automatically validates EntityRecord → Archetype → Chunk 3-way consistency in DEBUG mode, detecting internal state corruption early.
- **Crash scenario tests**: 24 unit tests covering post-Dispose API calls, invalid Entity handles, and batch null parameters.

---

## [0.10.4-preview] — Installation Documentation

### Added
- **Unity installation guide**: Added installation section to README.

---

## [0.10.3-preview] — Chunk Pooling + GC Hot-Path Elimination

### Fixed
- **TypeIdEnumerator off-by-one**: `MoveNext()` did not increment `m_BitPos` after removing a set bit, causing all bits after the first in the same word to be offset by -1, missing component types during Archetype creation.
- **ECB Allocator.Temp→Persistent**: Pooled ECB with `Clear()` reused across frames, but `Allocator.Temp` frees every frame, causing use-after-free. Fixed: default `Allocator.Persistent` + proper `DisposeECB()` cleanup.
- **Tag GetComponent error**: `Chunk.GetComponent<T>()` threw "cannot get data for tag" for Tag components. Fixed: merged into `offset<0` branch, working correctly in both Editor and Release.
- **Tag SetComponent compatibility**: User code calling `SetComponent<T>()` on Tags caused exceptions. Fixed: detect `ComponentKind.Tag`, existing Tag → no-op, missing Tag → auto-add via `AddComponentAt`.

### Added
- **archetype_layout_report MCP command**: Layout analysis, waste detection, component split analysis. Returns topByEntities, topByWaste, emptyChunks, componentSplitAnalysis, summary.
- **Runtime profiling toggle**: `EmberProfiler.Enabled` static switch; MCP window one-click enable/disable ProfilerMarker without recompilation.
- **EMBER_ENABLE_PROFILING conditional compilation**: csproj defaults to enabled, disabled via `-p:EmberEnableProfiling=false`. Unit tests disabled by default.

### Perf
- **4 CPU throughput optimizations**: Extended inline index to 512 slots (reduce Dictionary lookups), GetChunks cache-hit skip O(N) revalidation, De Bruijn TZ table replacing BitOperations, Tag check guard skipping data-free paths.
- **ECB pooling**: ECB `Clear()` replaces `Dispose()+null` in `SystemContext`, eliminating per-tick ECB allocation.
- **CreateArchetype List elimination**: Two-pass counting + pre-allocated arrays instead of `List<T>` in `CreateArchetype` and `GetOrBuildCopyPlan`; fixed `ComponentMask.ExtraWords` shared mutation bug (struct copy sharing reference).
- **EmberEditorGuard**: Skip all Editor tracking (structural change counts, access validation BitSet) when Debug windows are closed. `DebugWindowRefCount` reference counting.
- **Editor CPU optimization**: `Texture2D` creation replaced with `GUI.backgroundColor` (zero allocation), `MethodInfo` caching avoiding per-frame reflection, SystemsWindow cached views 300ms refresh, Repaint throttled to 50ms.
- **4 GC fixes**: enum-based `ThrowIfEntityIndexNotAlive` (avoid string interpolation), `TryGetComponentRef<T>` zero-allocation TryGet, `EntityQueryCache` pre-created common queries, `GetSetTypes()` zero-allocation enumerator.
- **Per-system result cache**: `GetOrCopyResult` caches validation results independently per system, eliminating cross-system invalidation allocations.
- **ValidateAccess zero allocation**: `List<string>` replaced with pre-allocated `string[]` buffers; zero allocation when declarations are correct.
- **Chunk pooling**: Empty Chunks are pooled (max 8) instead of disposed; `FindOrCreateChunkSlot` pulls from pool first, eliminating ~4.5KB GC churn in ECB create/destroy cycles.
- **CompiledQueryCore List pre-allocation**: `m_Chunks` initial capacity 0→64, eliminating ~1KB GC from cascading resizes.
- **GetColumnFast fast path**: Internal hot paths use profiling-free `GetColumnFast<T>()`, saving 96B micro-allocations.

### Perf
- **Component type name caching**: `Type.Name` per-frame calls in `ValidateAccess` loop produced ~12.5KB GC. Fixed: cached once at `Init()` into `m_CachedTypeNames[]`, hot path indexed directly (zero allocation).

## [0.10.1-preview] — ValidateAccess GC Allocation Fix

### Perf
- **ValidateAccess zero allocation**: 4 `new List<string>()` + 4 `.ToArray()` replaced with reusable caches + `Array.Empty<string>()`. Zero per-frame allocation when access declarations are correct, eliminating ~56 allocations/frame of GC pressure.

## [0.10.0-preview] — Access Validation + Entity Naming + Editor Code Isolation

### Added
- **Access runtime validation**: `SystemContext` automatically tracks actual Get/Set/Add/Remove component access, comparing against `DeclareAccess` declarations after tick. Missing declarations (red) → Console Warning; over-declarations (yellow) → parallelism restriction hint. All wrapped in `#if UNITY_EDITOR`, zero runtime overhead.
- **Systems Window validation coloring**: Graph nodes colored by access consistency — 🔴 red = missing declarations, 🟡 yellow = over-declarations, ⚪ default = consistent. Hover tooltip lists specific differences. Selected node detail panel shows full validation report.
- **Entity naming**: New `EntityName : IDataComponent` (FixedString64Bytes, zero GC). Named entities display names in Entities Window / Inspector, falling back to `E(Index, vVersion)`.
- **Archetype auto-naming**: Archetypes Window displays readable names (e.g., `Position+Velocity+…(+3)`) instead of `Arch #0`.

### Changed
- **Editor code isolation**: All runtime Editor-debug code wrapped in `#if UNITY_EDITOR` (15 files, csproj + 14 source files). `DefineConstants` now includes `UNITY_EDITOR`.

### Fixed
- **Systems Window click detection**: Node click now uses full node Rect, fixing "can't click" issue.
- **Marquee animation stutter**: Added Repaint() at end of OnGUI; scrolling no longer depends on mouse movement.
- **Node info compactness**: Component display changed to `R:3 W:1` + `Entities: 42` (counts replace full lists), detail panel retains complete info.

### Other
- **ember-perf-optimize skill**: Added §2b Access Validation optimization section — coloring rules, Tooltip, Console warnings, optimization workflow, code examples.

### Added
- **Graph-only mode**: Systems Window removes Table view, Graph is the sole display mode. Simplified Toolbar, Ticker dropdown only.
- **Performance data display**: Graph nodes show Avg ms directly, >1ms highlighted red. Click node for Last Tick / Average / Max / Tick Count performance metrics.
- **Marquee auto-scrolling**: System names exceeding node width auto-scroll left-right (40px/s), pausing 1 second at each end for reading.

### Changed
- **Systems Window simplification**: Removed Table view, Graph/Table toggle, Lifecycle/Layers toggles. Net reduction of 83 lines.

## [0.8.0-preview] — Dependency Graph Visualization + MCP Stability Fixes

### Added
- **Dependency graph view**: Systems Window adds Graph/Table toggle button. Graph view displays dependencies as layered nodes. Parallel layers green background, serial layers gray. System nodes show type and Read/Write component access lists. Arrows indicate dependency direction. Selected node bottom panel shows hooks, component access, layer info.

### Fixed
- **MCP connection stability**: Fixed unreliable `s_Client.Connected` on macOS causing silent response drops, `ListenLoop` exception not breaking causing TCP connection closure in finally, `Execute()` throwing exceptions instead of returning error JSON, among other connection reset issues.
- **MCP installation flow**: Fixed hard-coded `--port`, absolute path dependencies, missing `runtimeconfig.json` causing `dotnet exec` crash, extra commas in JSON merge. Added one-click Install/Uninstall with automatic path updates.

---

## [0.7.5-preview] — Skills One-Click Install + Data Flow View Optimization

### Added
- **Skills one-click install**: MCP window adds Skills foldout for one-click installation of `ember-perf-optimize` (performance optimization) and `ember-architecture` (architecture guide) to project `.claude/skills/`, auto-discovered by Claude Code and Codex. `EmberSkillManager` embeds full SKILL.md content as compile-time constants; `IsInstalled()` runtime file existence check.
- **ember-perf-optimize skill**: Covers 8 optimization domains: System type selection, DeclareAccess precision, Job struct design, ChunkJobMeta access, Structural Change optimization, NativeArray lifecycle, etc. MCP diagnostic workflow: world_info → system_status → get_dependency_graph → get_archetypes.
- **ember-architecture skill**: Covers storage model (Entity/Component/Archetype/Chunk/SOA), System type hierarchy (SystemBase/DeclaredSystemBase/JobSystem/SystemGroup), query pipeline (ctx.QueryChunks/QueryBuilder/Aspect), ECB/Buffer/Singleton, DependencyGraph parallel scheduling, MCP debug commands reference.

### Changed
- **Data flow view**: Display by execution layer order.

---

## [0.7.1-preview] — Editor Window Column Alignment Fix + Entities UI Simplification

### Fixed
- **4 Editor window column alignment**: `SystemsWindow`/`EntitiesWindow`/`ArchetypesWindow`/`ComponentTypesWindow` header-data row width mismatch. Root cause: headers outside `ScrollView` using `EditorStyles.toolbar`, data rows inside using `EditorStyles.helpBox`; scrollbar + padding differences caused alignment offset. Fix: headers moved into `BeginScrollView`, using `EditorGUI.DrawRect` + manual `Rect` positioning, column widths as constants.

### Changed
- **EntitiesWindow simplification**: Removed inline Components column; clicking Entity automatically opens `EntityInspectorWindow` for standalone component detail view. Toolbar Filter uses `EditorStyles.toolbarSearchField`.

---

## [0.7.0-preview] — Development Agent System + Editor Tool Enhancements

### Added
- **Development Agent System**: `ember-dev` main entry + `ember-code-review`/`ember-perf-check`/`ember-unit-test`/`ember-release` sub-agents covering code review, performance checking, unit testing, and release workflow.
- **ComponentInspector editable**: Play Mode int/float/double/bool/long/string fields directly editable, `World.SetComponent<T>` auto-writes back via reflection.
- **SystemsWindow timing columns**: LastTick(ms), Avg, Max, TickCount, LastError columns; >1ms highlighted red.
- **SystemsWindow parallel layer view**: Toggle `DrawParallelLayers` shows per-layer system lists; parallel layers green, serial layers default color.
- **Bridge auto-reconnect**: `EditorApplication.playModeStateChanged` callback — graceful disconnect on exiting PlayMode, auto-restart on entering PlayMode.
- **SystemBase public interface unification**: `DeclaredSystemBase` replaces `SystemBase` as user inheritance entry point; `SystemBase` made internal.

### Fixed
- **8 Write Command fixes**: struct copy fixes (`AddComponentToMask` pass ref), NRE guards, `FindWorldMethod` Entity/int overload disambiguation, batch validation, `remove_component` add HasComponent check.
- **3 JSON/Parsing fixes**: `SimpleJson.IsNumeric()` full validation eliminating `"10_ECSVsOOP"` false positive, stale ops cleanup, `advance_frame deltaTime` GetFloat.
- **4 MCP Bridge stability fixes**: Initial connection failure no longer exits, progressive retry delay 70s, Heartbeat BOM fix, parallel layer EndTick execution order.

### Changed
- **EmberBridgeCommands.cs split**: 2872-line monolith → main file 2788 lines + `EmberBridgeCommands.Helpers.cs` (93 lines), 8 helper methods extracted as partial class.
- **CLAUDE.md update**: Added parallel scheduling, MCP bridge, development workflow documentation.

### Perf
- **Parallel layer zero-allocation timing**: `GetTimestamp()` replaces `Stopwatch.StartNew()`, eliminating per-system tick allocation.

---

## [0.6.0-preview] — True Parallel Scheduling + MCP Synchronization

### Added
- **ComponentInspector editable**: Play Mode int/float/double/bool/long/string fields directly editable, `World.SetComponent<T>` auto-writes back via reflection.
- **SystemsWindow timing columns**: LastTick(ms), Avg, Max, TickCount, LastError; >1ms highlighted red.
- **SystemsWindow parallel layer view**: Toggle displays per-layer system lists; parallel layers green, serial layers default color.
- **Agent system**: `ember-dev` main entry + `ember-code-review`/`ember-perf-check`/`ember-unit-test`/`ember-release`.

### Fixed
- **PlayMode auto-reconnect**: `EditorApplication.playModeStateChanged` callback — graceful disconnect on PlayMode exit / domain reload, auto-restart on PlayMode enter. No more manual `EmberBridge.Stop(); EmberBridge.Start();`.
- **SimpleJson.BuildJson numeric misclassification**: `char.IsDigit(v[0])` causing `"10_ECSVsOOP"` to output as illegal JSON `10_ECSVsOOP`. Changed to `IsNumeric()` full validation.
- **McpTools command list outdated**: Description changed to categorized references, no longer hardcodes 26 old commands.
- **advance_frame deltaTime**: `GetInt` → `GetFloat`, correctly parsing `0.0167`.
- **MCP Server initial connection failure doesn't exit**: Changed to retry instead of direct exit.
- **MCP Server domain reload timeout insufficient**: Progressive delay 70s (5×2s + 12×5s).
- **Parallel layer EndSystemExecution order**: Moved before `EndParallelLayer`.

### Added
- **WorldSafety parallel layer**: `BeginParallelJobLayer()`/`EndParallelJobLayer()` allowing multiple systems to schedule concurrently in the same layer.
- **JobHandle dependency chain**: `JobSystem<TJob>.ScheduleJob` returns `JobHandle`, Ticker collects→`CombineDependencies`→`Complete`.
- **Registration-order directed dependency**: `DependsOn(a,b)` changed to `hasConflict && a > b`; later-registered depends on earlier.
- **QueryMask split**: `GetQueryMask()` virtual property, default = AllMask, overridable to narrow query scope.
- **MCP `get_system_info`**: Lookup system type/timing/hooks by tickerIndex+systemName.
- **MCP `capabilities` update**: `supportsParallelism`/`supportsJobSystem`/`supportsDependencyGraph`.
- **MCP `get_dependency_graph` enhanced**: Each layer output includes system type (`{name, type}`).

### Perf
- **systemIndices field reuse**: `List<int>` class field, reused across layers.
- **Removed LINQ**: `layer.Systems.All()` → manual for loop, eliminating delegate allocation.
- **Removed `System.Linq`**: `SystemTicker` + `DependencyGraph` now LINQ-free.

### Changed
- `JobSystemBase.ScheduleJob` return type `void` → `JobHandle`.
- `ChunkJobScheduler.Schedule<T>` stays compatible (synchronous Complete), `ScheduleAsync<T>` for framework internal use.

## [0.5.2-preview] — Performance + Robustness Sweep

### Perf
- **ComponentMask >256 COW elimination**: `EnsureExtraCapacityForWrite` no longer `CloneExtraForWrite` when capacity is sufficient.
- **DeferredDestroy O(1)**: `List` linear scan → `Dictionary` O(1) lookup.
- **GetDebugView caching**: Arrays rebuild only when system count changes.

### Fixed
- **12 bare `catch {}`** → logged: exceptions no longer silently swallowed.
- **Chunk disposed guard**: `AllocRow`/`RemoveAtSwapBack` now throw `ObjectDisposedException`.
- **Entity version overflow**: `int.MaxValue` wraps correctly to 1.
- **FreeEntityIndices cap**: Upper limit 65536 to prevent unbounded growth.
- **Error message context**: `ComponentTypeRegistry` exceptions include `max registered` range.
- **BufferElement type safety**: `Dictionary<Type, object>` → `IBufferElementStore` generic interface.

## [0.5.1-preview] — Mixed Layer Semantics Fix + Zero Allocation Hardening

### Fixed
- **SystemTicker mixed layers**: Layers containing SystemBase/DeclaredSystemBase now execute the entire layer serially, no longer skipping non-JobSystem members.
- **NativeArray leaks**: `ScheduleAsync` + `SystemTicker` now with try-finally/catch protection; exception paths do not leak.

### Added
- **`DeclaredSystemBase`**: Serial System declaring read/write access participates in dependency graph, not treated as global barrier.
- **`Slot<TComponent>()`**: `JobSystemBase` provides component slot index lookup; users no longer manually calculate Comp0-3.
- **`MovementChunkMeta.Wrap()`**: Source Generator produces compile-time type-safe wrappers.
- **ChunkJobMeta overflow**: ≤4 fixed slots zero overhead, >4 transparent overflow buffer (internal pointer, invisible to user).

### Perf
- **Precomputed AllComponentTypes[]**: Sorted once at `BuildAccess`; no per-frame `new List` + registry scanning.
- **Warning once**: Overflow warning changed to static bool, fires once per session.
- **systemIndices reuse**: `List<int>(8)` reused across layers.
- **`GetTimestamp()`**: Replaces `Stopwatch.StartNew()`, zero-allocation timing.

## [0.5.0-preview] — IJobParallelFor Default Path + Zero-Allocation Scheduling

### Changed — Breaking
- **`IEmberChunkJob.Execute` signature change**: `Execute(Chunk chunk, int)` → `Execute(ChunkJobMeta meta, int)`. ChunkJobMeta contains BufferPtr, EntityCount, Comp0-3 Offset/Stride, enabling direct component data access via unsafe pointers.
- **`JobSystem<TJob>` replaces `JobSystemBase`**: Parallel System inherits `JobSystem<MoveJob>` (generic), `CompileJob` returns concrete struct type, `Schedule<T>` automatically uses IJobParallelFor (zero delegate/closure/boxing).
- **Removed `Parallel.ForEach` path**: Default scheduling changed to `ChunkJobWrapper<T> : IJobParallelFor`, eliminating managed scheduling allocations.
- **Removed `ExecuteUnsafe`**: No longer need dual paths; IJobParallelFor is the sole default path.

## [0.4.1-preview] — GC Zero Allocation + IJobParallelFor Integration + MCP Multi-Instance Fix

### Fixed — GC
- **Stopwatch.StartNew() → GetTimestamp()**: Zero-allocation per-system tick timing.
- **IEmberChunkJob struct boxing**: New `Schedule<T>()` generic overload, struct jobs passed directly without boxing.
- **No-op double scheduling**: New `HandlesOwnScheduling` property; systems self-scheduling skip default path.

### Added — Parallel
- **IJobParallelFor integration**: `ScheduleUnsafe<T>` auto-extracts component offset/stride from `ArchetypeLayout` into `ChunkJobMeta`, `ChunkJobWrapper<T> : IJobParallelFor` wraps scheduling.
- **SystemTicker JobHandle dependency chain**: Parallel layers `CombineDependencies + Complete` layer boundary sync.

### Fixed — MCP
- **Multi-Unity-instance port discovery**: `SimpleJson.GetString` now parses both string and integer values; `ScanPorts` no longer swallows exceptions silently.
- **Heartbeat BOM**: `WriteHeartbeat` changed to `new UTF8Encoding(false)`, no longer writes BOM.
- **Handshake fault tolerance**: `System.Text.Json` failure falls back to `ParseHandshakeManually` field-by-field extraction.
- **TOML RemoveEmberFromConfig**: Fixed `\n[` false matching array bracket causing bare JSON leftovers.
- **activeScene null guard**: Handshake and ProcessQueue now with empty string fallback.

---

## [0.4.0-preview] — Parallelization + MCP v0.4.0

### Added — Parallelization
- **EcsSystem abstract base class**: Extracts OnCreate/OnDestroy/lifecycle hooks/ECB; SystemBase and JobSystemBase share as sibling classes.
- **JobSystemBase**: Declare read/write access + CompileJob → IEmberChunkJob; framework automatically builds dependency graph + parallel scheduling.
- **AccessBuilder**: `access.Read<T>().Write<T>()` fluent declaration; framework derives dependency graph from it.
- **DependencyGraph**: Topological layering + conservative mask mutual exclusion (undeclared = conflicts with all systems). Same-layer non-conflicting systems run in parallel.
- **SystemTicker layer scheduling**: `Tick()` executes by Layer; parallel layers via ChunkJobScheduler, serial layers via TickSystemAt.
- **ChunkJobScheduler**: Dual paths — `Schedule()` (Parallel.ForEach, default) + `ScheduleUnsafe<T>()` (IJobParallelFor + Burst-ready).
- **SystemTicker timing ring**: Per-system 20-tick ring avg/max, 3-entry error ring, tickCount.

### Added — MCP v0.4.0
- **Unified `ember_execute` protocol**: 12 scattered tools → 40 command entry points, single TCP round-trip for batch execution.
- **BufferHandle introspection**: `get_buffer` (entity+fieldPath / singleton+fieldPath), BufferStore distribution statistics.
- **Unified envelope**: `{ok, world, data/error, warnings, truncated, nextCursor}` + structured error codes.
- **New commands**: `capabilities`, `list_worlds`, `component_schema` (isBufferHandle/isEntity/isWritable), `validate_component_payload` (fieldPath-level errors), `query_entities_v2` (all/any/none), `get_entity_full` (BufferHandle summary), `get_singletons` (namesOnly), `system_status` (avg/max/tickCount/error ring), `world_snapshot` (snapshotId + distribution), `snapshot_diff`, `trace_entity`, `query_archetypes`, `safe_write_batch` (default dryRun).
- **World introspection API**: `EnumerateBufferStores()`, `GetBufferInfo()`, `ReadBufferSample()`, `FindBufferOwners()`.

### Fixed
- MCP Server auto-reconnects after disconnect (10×2s), no longer fatal-exits.
- Warmup ping after reconnection prevents first-packet timeout.
- `ECSManager.Start()` auto-sets `Active = this`, Dispose auto-cleans up.
- `get_ecs_status` null manager guard.
- `safe_write_batch` ECB create_entity tempIndex fix → direct execution.
- `AccessBuilder` struct copy fix.

### Changed
- `SystemBase` removed `OnCreate`/`OnDestroy`/hooks → migrated to `EcsSystem` (backward compatible, zero user changes).
- `SystemTicker.Register<T>()` constraint changed from `SystemBase` to `EcsSystem`.
- `SystemGroup : SystemBase : EcsSystem` inheritance chain unchanged.

---

## [0.3.0] — Editor Visual Debugging Tools

### Added
- **Systems Window** (`Window > Ember > Systems`): Ticker dropdown, system execution order list, lifecycle hook identifiers (EC/ED/CA/CR), SystemGroup highlighting.
- **Entities Window** (`Window > Ember > Entities`): Component name filter + pagination (200/page), entity list with Archetype/Components/Placed columns, expandable component value panel below selection.
- **Archetypes Window** (`Window > Ember > Archetypes`): Global fill rate bar, archetype distribution list, Chunk Fill Histogram, expandable per-chunk details.
- **Component Types Window** (`Window > Ember > Component Types`): Full component registry overview, TypeId/Kind/Size/Alignment columns, search filter, full type info on selection.
- **Component Inspector**: Reflection-based rendering of component struct fields from raw Chunk bytes; distinguishes IDataComponent/ITagComponent/IBufferElement.
- **DebugView system**: `WorldDebugView`, `EntityDebugView`, `ArchetypeDebugView`, `ChunkDebugView`, `SystemTickerDebugView`, `SystemInfoDebugView` — read-only snapshot structs, constructed only when Editor windows are open (zero overhead when closed).
- **ECSManager.Active**: Static reference; Editor windows access the runtime Manager instance through this property.
- **ECSManager.GetDebugViews()** / **World.Debug** / **World.GetComponentBoxed()** / **World.GetEntityComponentTypes()**: Debug API layer.

### Changed
- **ECSManager**: Added `Active` static property and `GetDebugViews()` public method.
- **SystemTicker**: Added `GetDebugView(int)` internal method.
- **World.Listeners**: `IsOverridden` changed from private to internal static for SystemTicker reuse.
- **csproj**: Added `InternalsVisibleTo("Ember.Editor")`, excluded `Editor/**` from compilation.
- **Package structure**: Added `Editor/Ember.Editor.dll` (~20KB) + `Editor/Ember.Editor.asmdef` + `.meta` files.

---

## [0.2.1] — API Simplification / Performance Optimization / Unit Tests

### Added
- **EntityQuery static factory**: `EntityQuery.With<Health, Position>().None<DeadTag>()` chainable construction, 0GC struct builder.
- **SystemContext convenience methods**: `ctx.Get<T>(e)` / `ctx.Set<T>(e,v)` / `ctx.Has<T>(e)` direct component access, zero-overhead wrappers.
- **6 new ProfilerMarkers**: CreateEntity, DestroyEntity, ChunkColumn.Access, ECB.Playback, FlushDeferredCreates, QueryMatches.
- **NUnit unit test project**: `tests/Unit/`, 205 tests, 12 pure C# types fully covered, `dotnet test` one-click run.

### Changed
- **Removed ColumnAccessor&lt;T&gt;**: Eliminated per-column×per-chunk heap allocation and redundant Dictionary lookup; `SystemChunk.Get<T>()` goes directly to `Chunk.GetColumn<T>()` (O(1) array index).
- **ChunkColumn.At() + AggressiveInlining**: Ensures JIT inlines pointer arithmetic.
- **EMBER003 diagnostic upgraded from Warning to Error**.
- **README**: Buffer/BufferElement separated into two chapters, EntityQuery examples updated, ComponentMask With vs WithAdded warnings.

### Fixed
- **ECB silently dropped on exception**: System OnTick exception no longer causes ECB commands to be silently discarded.

---

## [0.2.0] — Lifecycle Hooks / DeferredDestroy / ComponentPack Generator / PairQuery

### Added
- **Entity creation during iteration**: `SystemContext.CreateEntity()` allows creating entities during query iteration and immediately obtaining real `Entity` references; chunk placement automatically deferred to end of `foreach`. Solves the ECB temporary ID limitation for component storage.
- **EntityRecord.Placed flag**: Distinguishes allocated but not-yet-chunk-placed entities; all component access methods return clear errors for pending entities.
- **ComponentPackAdapterGenerator**: Compile-time auto-generation of `IComponentPackAdapter<T>.Describe()` via Roslyn SemanticModel analysis of Read/Write method bodies; eliminates risk of manual Describe() / code mismatch. Same DLL as `ComponentRegistrationGenerator`; HybridCLR compatible.
- **Entity/Component lifecycle hooks**: `SystemBase` adds 4 `protected virtual` hooks — `OnEntityCreated`, `OnEntityDestroyed`, `OnComponentAdded`, `OnComponentRemoved`. Override as needed; no override = zero overhead. World maintains independent listener lists for precise delivery.
- **DeferredDestroy auto-flush**: After each system Tick completes (before ECB playback), deferred-destroy entities are automatically destroyed. Previously deferred entities were only cleaned up on World.Dispose.
- **World.cs split**: 1615 lines split into 10 partial files by #region responsibility; zero logic changes.
- **Structural change error messages upgraded**: Fixed missing `Placed` guard in `AddComponent<T>`; numeric typeId replaced with component type names; Chunk internal errors include context and "this is a framework bug" guidance.

### Removed
- **Aspect subsystem**: `IAspect`, `AspectRegistry`, `AspectQueryBuilder` completely removed. Never actually used; queries now unified through `SystemContext.QueryChunks()` / `EcsAPI.Query()`.
- **SystemBase.CreateQuery / GetQuery / GetChunks**: Old query declaration pattern removed. In-system queries use `SystemContext.QueryChunks()`.
- **SystemTicker.runOnce**: `Register<T>(runOnce: true)` parameter removed. One-time initialization uses `OnCreate()` or in-system flags.
- **QueryBuilder.Any<T> / None<T> / GetEnumerator()**: Chainable `Any`/`None` methods and implicit `foreach` support removed from `QueryBuilder`; simplified API surface.
- **File deletions**: `SparseSet<T>`, `ListPool<T>`, `IBufferStore`, `ReadOnlyChunkView` and other never-integrated or replaced files removed.
- **World dead code**: `GetReadOnlyChunkView()`, `TryGetEntityLocation()`, `CollectAliveIndices()`, `EnsureEntityCapacity()` and other uncalled methods removed.
- **Chunk(NativeArray<int>) constructor**: Flawed and never-used overload removed.

### Changed
- **IComponent no longer inherits IDisposable**: Component structs no longer need `Dispose()`.
- **SystemBase slimmed**: `OnTick(World, float)` overload removed; sole entry point is `OnTick(SystemContext ctx)`. Access through `ctx.World` / `ctx.DeltaTime`.
- **IBufferStore → IDisposable**: `BufferStore<T>` internal interface simplified; `IBufferStore` removed, implements `IDisposable` directly.

### Fixed
- **Native memory leaks**: Fixed `World.Dispose()` skipping `DisposeBufferStores()` and `DisposeEcsCore()` under exception scenarios, causing Native container leaks. Three-layer defense: `SystemTicker.Dispose` try-catch per-system `OnDestroy`, `ECSManager.Dispose` try-finally ensures World destruction, `World.Dispose` try-finally ensures BufferStore and Archetype/Chunk Native memory release.
- **README section numbering**: Fixed numbering drift after multiple refactors; all subsection numbers now aligned with parent chapters.
- **Benchmark compilation**: Fixed `StructuralChangeBenchmarks` foreach over removed `QueryBuilder.GetEnumerator()` → `.AsRows()`.
- **ECB silently dropped on exception**: Fixed ECB commands silently discarded on system `OnTick` exception. ECB (and DeferredDestroy flush) now always execute in `EndTick`, regardless of system success.

---

## [0.1.0] — Architecture Unification

### Added
- **ECSManager**: Top-level system manager holding multiple integer-indexed `SystemTicker` instances; user decides which Unity lifecycle drives them (`FixedUpdate` / `Update` / `LateUpdate`).
- **SystemTicker**: Zero-allocation Tick loop, conservative execution order by registration, automatic ECB playback.
- **SystemGroup**: System composition pattern; `Configure(SystemTicker)` expands sub-systems and nested groups; registration order = execution order.
- **SystemContext**: Unified system query entry point. Provides `QueryChunks<T>()`, `Lookup<T>()`, `Pack<T>()`, `ECB` with reuse caches.
- **ComponentPack**: Packs query results into contiguous arrays for high-frequency computation via `IComponentPackAdapter<T>`.
- **IBufferElement**: Entity-level dynamic array components; entities can carry multiple elements of the same type.
- **Source Generator**: Compile-time automatic scanning of `IComponent` implementations generating registration code (`src/Generator/`).
- **ArchetypeIndex**: BitSet-based component-type-to-archetype bitmap index for accelerated query matching.
- **ComponentTypeRegistry.Seal()**: Locks registry after registration, preventing accidental runtime registrations.
- **EmberProfiler**: Unity ProfilerMarker instrumentation support, enabled via `EmberEnableProfiling=true` compile switch.
- **DeferredDestroySet**: O(1) deduplicated deferred-destroy set.

### Changed
- **`ECSManager` replaces `ECSServer`**: System lifecycle management from monolithic Server to multi-Ticker architecture.
- **`SystemBase` unification**: `TickSystem`, `ChunkSystem`, `EntitySystem` three system subtypes merged into single `SystemBase`; all expressed through `OnTick(SystemContext)`.
- **Manual registration replaces attribute annotation**: All execution order attributes removed; systems explicitly registered via `ticker.Register<T>()`.

### Removed
- `ECSServer` (replaced by `ECSManager`)
- `TickSystem` / `ChunkSystem` / `EntitySystem` (unified into `SystemBase`)
- System execution order attributes and reflection invocation
- `InitSystem` (replaced by `SystemBase.OnCreate()`)
- `ChunkReadLease` / `ReadOnlyChunkView` (obsolete thread safety model)
