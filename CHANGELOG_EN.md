# Changelog

All notable changes to the Ember ECS Framework.

## [1.9.2] — Remove MCP System Graph HTML Export

### Removed
- **Remove MCP `export_system_graph` command and HTML export**: Deleted `EmberBridgeCommands.Handle_export_system_graph`, `BuildSystemGraphHtml`, and `ECSManager.GetDependencyGraphDebugView(int tickerIndex)`; `SystemsWindow` no longer shows the "Export Graph HTML" button.

## [1.9.1] — MCP Version Sync

### Fixed
- **MCP capabilities version out of sync**: `capabilities` now returns `version` from `EmberBridge.PackageVersion` to stay consistent with the package version.

## [1.9.0] — Entity Debug UI Enhancements

### Added
- **EntitiesWindow**: Browse all entities, filter by component, and paginate; configurable columns (Entity, Archetype, Chunk, Row, ArchFill, ChunkFill, Bytes, Systems).
- **EntityInspectorWindow**: Single-entity detail window with a tabbed section container.
- **Component data expansion**: Component field values and Buffer element lists shown as foldable lists.
- **Location & memory info**: Shows the entity's Archetype, Chunk, Row, archetype fill rate, chunk fill rate, estimated byte size, and Placed status.
- **System access info**: Shows the count and list of systems that statically match the entity.
- **EntityDebugInfoProvider**: Unified provider for entity location, byte estimation, system matching, and buffer element enumeration.

## [1.8.0] — Configurable Chunk Pool & ECB Batch Playback

### Added
- **Configurable per-Archetype empty Chunk pool size**: Added the `World.MaxPooledChunksPerArchetype` property to limit how many empty Chunks each Archetype retains; defaults to 8, and can be set to 0 to disable pooling entirely.
- **Chunk pool diagnostics**: Added read-only `World.PooledChunkCount` and `World.TrimExcess()` to inspect and immediately release all pooled empty Chunks across Archetypes.
- **ECB batch playback mode**: `EntityCommandBuffer` now has an internal `BurstBatch` playback path that automatically batches Add/Remove/Destroy/Create commands of the same type once the threshold is reached, improving playback throughput in high-frequency structural-change scenarios.

### Changed
- **AppendChunk prefers pooled Chunks**: Before allocating a new Chunk, the runtime now tries to reuse one from `m_EmptyChunkPool`, reducing Native memory allocations.
- **EnsureFreeRows supports failure rollback**: When bulk migration pre-allocates Chunks and an exception occurs, the original Chunk list, pool count, and first-non-full index are restored.

## [1.7.0] — System Graph Visualization & Performance Hotspots

### Added
- **Editor-only system dependency graph visualization**: Added the `Ember.Diagnostics.Core` shared assembly with `SystemGraphReport`, `SystemGraphBuilder`, and `SystemGraphSvgRenderer` for pure .NET graph building and self-contained HTML/SVG rendering.
- **`Profiler.Attach(ECSManager)` API**: Editor-only opt-in that attaches `EmberDiagnosticsService` and caches system dependency graph snapshots at 1-second intervals.
- **Enhanced `get_dependency_graph` MCP command**: Now returns `systems`, `edges`, and `systemCount`/`edgeCount` while preserving the legacy `layers` output; supports optional `includeMetrics`.
- **New `export_system_graph` MCP command**: Exports a self-contained HTML file with an inline SVG dependency graph and JavaScript tooltips to `Application.temporaryCachePath/EmberGraphs/` or an explicit `outputPath`.
- **Systems Window performance hotspot coloring**: Graph nodes are tinted by average tick time (≥2ms red, ≥1ms orange, ≥0.5ms yellow) alongside existing access-validation colors.
- **Systems Window "Export Graph HTML" button**: One-click export from the Editor window with Finder reveal.

## [1.6.4] — ArchetypeIndex All Query Fix

### Fixed
- **ArchetypeIndex All-query false positives**: When a required component in `EntityQuery.All` has never appeared in any archetype and is not the first iterated component, `ArchetypeIndex.Query` now clears the result before returning an empty match set, preventing partial intersections from earlier components from leaking into the result.
- **QueryCache regression coverage**: Added tests for missing components at the beginning, middle, and end of `All`, combined `All + Any + None` queries, and `QueryCache.Rebuild` not caching incomplete query results.

## [1.6.3] — MCP Safe Serialization Fix

### Fixed
- **MCP singleton field serialization recursion crash**: `get_singleton` / `get_singletons(includeFields=true)` now serialize fields through a safe serializer with a maximum recursion depth, reference-cycle protection, and field-read error summaries, preventing Unity main-thread stack overflows from Native containers or complex reference graphs.
- **Unity Native container summaries**: `NativeArray<>`, `NativeList<>`, `NativeParallel*`, and other `Unity.Collections` containers are no longer recursively reflected. They are summarized with safe fields such as type, `isCreated`, `length`, and `capacity`.

### Changed
- **`get_singletons` no longer expands fields by default**: default `includeFields=false`; callers must explicitly pass `includeFields=true` to inspect fields. Prefer dedicated commands such as `get_buffer` for high-risk runtime state.

## [1.6.2] — Hardening & Parallel Hot-Path Optimization

### Perf
- **TickParallelLayer hot-path merge**: Reduced `BeginSystemExecution`/`EndSystemExecution` calls per system per tick from 4 to 2 by carrying the system context from schedule through cleanup, eliminating redundant context wrapping.
- **ComponentPackColumnCache unified caching**: Removed the stale-count fast path in `EnsureColumn`; count now always reads from `chunk.Count` for liveness.

### Fixed
- **Chunk.GetReadOnlyColumn missing inlining**: Restored `[MethodImpl(MethodImplOptions.AggressiveInlining)]`.
- **SystemTicker.Tick throws on disposed world**: `world.IsDisposed` changed from silent `return` to `ObjectDisposedException`, preventing hidden errors from ticking a disposed world.

### Changed
- **ComponentMask.Clone**: New deep-copy method; all dictionary-key sites in Archetype, DeferredCreate, FlushDeferredCreates, and BatchMigration now use Clone for defensive protection.
- **Chunk native pointer safety guards**: `PointerViewSafetyState` generation tracking; Chunk dispose/reset increments the generation; `ChunkColumn<T>`/`ReadOnlyChunkColumn<T>` validate on access to prevent dangling pointers.
- **ECB playback reentrancy guard**: `World.Playback` added `m_PlaybackDepth` counter plus nested-playback exception; `CreateCommandBuffer` also checks reentrancy.
- **ECSManager.Tick pre-checks**: Added World null/disposed/tickerIndex out-of-range validation.
- **DependencyGraph undeclared barrier**: Removed the all-undeclared single-layer fast path; undeclared systems now uniformly handled via `IsBarrier`, preventing undeclared JobSystems from incorrectly sharing a parallel layer.

### Added
- **BufferSpan staleness tests**: Buffer growth/clear/destroy after span acquisition throws `InvalidOperationException`.
- **ComponentMask.Clone tests**: Independent copy, inline-only copy, and empty mask copy.
- **DependencyGraph boundary tests**: Undeclared standalone layers and barrier splitting of declared layers.
- **SystemProfile.ExpandGroup tests**: Leaf expansion, nested flattening, empty group, duplicate ignore, and InsertBefore combination.

## [1.6.1] — Fix Parallel-Layer JobHandle Safety Handle Regression

### Fixed
- **Missing safety handle on parallel-layer JobHandle merge**: In 1.6.0, `TickParallelLayer` collected JobHandles via `NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray` and merged them with `JobHandle.CombineDependencies`, but the resulting array lacked a valid `AtomicSafetyHandle`. Under Unity 2022.3 + Burst this caused `AtomicSafetyHandle.CheckReadAndThrow` failures. The code now allocates a real `NativeArray<JobHandle>` with `Allocator.Temp`, copies handles into it, merges them, and disposes immediately.

## [1.6.0] — Robustness and Performance Optimizations

### Added
- **SystemProfile group expansion**: Added `SystemProfile.ExpandGroup<T>()` to flatten a `SystemGroup` into its leaf systems inside a profile, enabling per-leaf additions, removals, and reordering.
- **Source Generator component diagnostics**: The component registration generator now reports `EMBER015`–`EMBER018` diagnostics at compile time for multiple kind interfaces, missing kind interface, non-unmanaged struct, and tag components with instance fields.

### Changed
- **SystemGroup.Configure obsolete**: `SystemGroup.Configure(SystemTicker)` is now marked `[Obsolete]` with `error: false`, providing a migration window before it becomes abstract in a future major version.
- **Component registration error messages**: `ComponentTypeRegistry.GetInfo` now provides clearer errors for unregistered types and out-of-range IDs, suggesting checks for kind interfaces, Source Generator output, and assembly load order.
- **README MCP examples**: Corrected package README examples that incorrectly presented `ember_execute` commands as standalone tool names (`ember_get_entity`, `ember_create_entity`, etc.); examples now use the `commands` array format of `ember_execute`.

### Perf
- **TickParallelLayer JobHandle batching**: Parallel layers now collect all handles before calling `JobHandle.CombineDependencies` once, avoiding redundant pairwise merges.
- **AccessDeclaration caching**: `SystemTicker` caches each system's `ReadMask`/`WriteMask`/etc. during `Init`, removing repeated `GetAccessDeclaration` work from the hot path.
- **Parallel-layer entity count cache**: Diagnostic mode caches entity counts per tick so multiple systems do not repeat full queries.
- **DependencyGraph layer build O(n³)→O(n·c)**: Replaced per-round O(n) scans with `lastReadLayer`/`lastWriteLayer` arrays.
- **ComponentTypeInfo sort caching**: Added cached `AssemblyName` and `FullName` properties to reduce reflection overhead during chunk/column sorting.
- **CreateArchetype single enumeration**: Reduced two component-ID enumerations to one `List<ComponentTypeId>` pass.
- **RecordMask set-bit iteration**: `SystemContext.RecordMask` now iterates set bits instead of the full `ComponentTypeRegistry.Count`.
- **EntityQuery strong hash**: `GetHashCode` now uses the same prime-mixed algorithm as `EntityQueryKey` to reduce query-cache collisions.
- **GetChunks skips redundant validation**: Removed the redundant `query.Matches` check on the cache-miss path.

### Fixed
- **ECB playback exception safety**: `SystemContext.EndTick` disposes the old buffer and creates a new one when ECB playback fails, preventing stale unplayable commands.
- **ECB temp-entity index overflow**: `EntityCommandBuffer.CreateEntity` now throws a clear exception when `m_NextTempIndex == int.MinValue`.
- **Parallel-layer cleanup playback**: `TickParallelLayer` cleanup now bases `playbackDeferredChanges` on whether system context was entered successfully **and** no parallel errors have occurred, preventing stale ECBs from playing back on failure paths.
- **FlushDeferredCreates structural barrier**: `World.Query.cs` calls `m_Safety.BeforeStructuralChange()` at the start of `FlushDeferredCreates`.
- **WorldSafety parallel-layer nesting guard**: `BeginParallelLayer` now checks `m_InParallelLayer` to prevent nested parallel layers.

## [1.5.0] — Burst Job Compilation Policies

### Added
- **Generated Burst scheduling entry points**: The Source Generator emits a non-generic `IJobParallelFor` and direct scheduling method for each accessible concrete `JobSystem<TJob>`. Runtime resolution and strongly typed delegate caching happen once during system initialization; the steady-state Tick path uses no reflection or managed allocation.
- **Job compilation policies**: Adds `EmberJobCompilationAttribute` with `Auto`, `Managed`, `Burst`, and `BurstHotUpdate` modes, selectable at assembly, system-base, or concrete-system scope.
- **HybridCLR policy**: AOT systems can use regular Burst, standard hot-update assemblies can explicitly select `Managed`, and HybridCLR editions that support hot-update Burst can select `BurstHotUpdate` with a version salt. Same-assembly source changes automatically change the generated entry point.

### Changed
- **Optional Burst dependency**: `Ember.dll` does not reference `Unity.Burst` directly. Consumer assemblies containing Ember job systems must directly reference `Unity.Burst` in their asmdef to generate Burst entry points; `Auto` retains the compatible generic Unity Jobs path when that reference is absent.
- **Stable component slot order**: Runtime and generated code now sort non-tag component columns by assembly name and component metadata name, preventing cross-assembly slot drift.

### Fixed
- **Explicit policies do not silently fall back**: `Burst` and `BurstHotUpdate` produce clear compile-time or system-initialization errors when a scheduler cannot be generated or resolved. Concrete generic systems, inaccessible types, and missing Burst references have dedicated diagnostics.
- **Fully qualified scheduling**: Generated code calls `Unity.Jobs.IJobParallelForExtensions.Schedule` through its fully qualified name so consumer extension methods cannot hijack the execution path.

## [1.4.1] — Unity Bridge PlayMode Auto-Recovery

### Fixed
- **Unity Bridge manual-start recovery**: Starting the Ember MCP Bridge manually now persists a "keep running" intent. After PlayMode stop/restart temporarily shuts down the Bridge, it automatically restarts once Unity reaches a stable Play/Edit state unless the user explicitly clicks Stop.

## [1.4.0] — MCP Window and Project-Level Skills

### Added
- **Project-level skill install targets**: The Ember MCP window `Skills` section now provides an AI-tool dropdown and installs project-level skills for Codex `.agents/skills`, Claude Code `.claude/skills`, and OpenCode `.opencode/skills`.

### Changed
- **MCP window simplification**: Removes the `Client Setup` section. The Unity window no longer writes AI-client configuration; AI clients continue to use the global `ember-mcp stdio` setup while the window focuses on Bridge status, Skills installation, Server info, and request logs.
- **Skill documentation sync**: Updates installed Ember Skills and embedded window-installed Skills to use current `AccessBuilder` single-generic chaining, `ChunkJobMeta` slot access, `ComponentMask` mutable-struct semantics, and release validation guidance.

## [1.3.0] — Global MCP Server and Instance Discovery

### Added
- **Ember.Mcp.Server global .NET tool**: The MCP Server can now be published and installed as the `Ember.Mcp.Server` NuGet global tool. AI clients start it with `ember-mcp stdio` by default instead of binding to each Unity project's `Tools~/Ember.Mcp.Server.dll` path.
- **Bridge instance discovery**: Adds the `ember_instances` MCP tool and `ember-mcp list-instances` CLI command to list available Unity Ember Bridge instances, project paths, `projectHash` values, ports, and status.
- **Multi-project target selection**: `ember_execute` accepts optional `projectRoot` / `projectHash` arguments. When multiple Unity projects are open and no target is specified, the server returns a clear ambiguity error instead of connecting to the wrong project.
- **NuGet Trusted Publishing**: Adds a GitHub Actions OIDC publishing workflow so public NuGet releases no longer depend on a long-lived API key.

### Changed
- **MCP client configuration**: The Unity MCP window now writes the user-level `~/.codex/config.toml` entry with `command = "ember-mcp"` and `args = ["stdio"]`; legacy project-level DLL configurations are migrated to the global tool command.
- **Skill installation path**: Ember skills now install to user-level `~/.codex/skills` and `~/.claude/skills`, avoiding per-project duplication.

## [1.2.0] — SystemProfile Composition API

### Added
- **SystemProfile**: Adds a reusable and copyable system list API with `Add`, `Remove`, `InsertBefore`, `InsertAfter`, and `Replace`, useful for deriving test or gameplay variants from a base profile.
- **SystemTicker.ApplyProfile**: Adds a profile application entry point that constructs all profile systems before registering them into the ticker, avoiding partially registered ticker state if construction fails.
- **SystemTicker.Register(Type)**: Adds runtime type registration for editor, tooling, and dynamic configuration paths. Regular player code should still prefer `Register<T>()`; IL2CPP stripping scenarios must preserve the target system constructors in the consuming project.

### Changed
- **SystemProfile / SystemGroup split**: `SystemGroup` remains the fixed code-side composition and nesting primitive; `SystemProfile` is for copyable system lists that can be edited by add/remove/replace operations. Profiles do not accept `SystemGroup` entries directly; expand groups into leaf systems first.

## [1.1.2] — SystemTicker Parallel Hot Path Optimization

### Perf
- **TickParallelLayer safety call short-circuit**: `GetAccessDeclaration` + 4 out params + mask overload of `BeginTick` are skipped when `EMBER_ENABLE_SAFETY_CHECKS` is off, saving 1 is-cast + 4 ComponentMask copies per system per frame.
- **TickSystemAt hot path streamlined**: serial systems no longer evaluate `GetAccessDeclaration` per frame (when `#if EMBER_SAFETY_CHECKS` off, simple overloads only), reducing framework-side declaration query overhead for InteractionBuild and similar heavy serial systems.

### Fixed
- **BeginSystemExecution / BeginTick exception leak**: `worldSystemEntered` was not set to true when `context.BeginTick` failed, causing finally to skip `EndSystemExecution`, leaving WorldSafety system scope dangling. Now WorldSafety is no longer polluted on partial failures.

## [1.1.1] — Row/Pair Access Cache and Stability Fix

### Changed
- **QueryRow / ChunkRowRef hot path**: per-row reads and writes no longer repeat `Chunk.GetComponent<T>()` layout offset lookup; they reuse the column cache shared by `CompiledQueryCore`.
- **SystemChunk column access cache**: `SystemChunk.Read/Write<T>` now reuse the query-level column cache, reducing repeated offset/base pointer resolution during system chunk iteration.

### Fixed
- **Cached column disposed guard**: cached column hits still validate that the chunk is alive, preventing escaped row/ref values from bypassing stability checks and touching stale native pointers after World disposal.

## [1.1.0] — Hot Path and Diagnostic Sampling Optimizations

### Added
- **perf_summary trackingMode**: `perf_summary` now supports `trackingMode="total"` for total Tick wall-clock sampling without enabling system-level diagnostics. The default `trackingMode="systems"` keeps the existing per-system timing breakdown.
- **ECSManager.TickerCount**: Exposes the ticker count as a read-only property so tools and diagnostics can validate ticker ranges without creating debug views.

### Changed
- **DependencyGraph ready-set layering**: Dependency layers are built with a ready-set algorithm, allowing later independent systems to run in earlier layers while preserving undeclared-system and structural-change barriers.
- **MCP Bridge diagnostic switch**: Starting or stopping the MCP Bridge no longer increments `DebugWindowRefCount`, so merely connecting tools does not enable SystemTicker sampling.
- **ComponentPack row-access hot path**: `PackReadContext` / `PackWriteContext` no longer construct `ColumnAccessor<T>` per row access; the column cache uses `ComponentTypeCache<T>.TypeId` directly to cache chunk pointers and strides.

## [1.0.1] — Chunk Job Tag Query Fix

### Fixed
- **Chunk job tag queries**: `ITagComponent` still participates in `QueryMask` and dependency declarations, but no longer generates `ChunkMeta` data accessors or enters the `ChunkJobScheduler` data-column list. This fixes JobSystems that include tags failing on tag offset = -1.

## [1.0.0] — Core Hot Path and Robustness Hardening

### Added
- **ComponentPack**: descriptor-driven pack/build/writeback API for chunk-wise component column packing into dense arrays and batched writeback.
- **Read/write access primitives**: added `ReadOnlyComponentLookup<T>`, `WritableComponentLookup<T>`, `ReadOnlyChunkColumn<T>`, and the `SystemContext.Read/Write` access model.
- **Batch structural changes**: `AddComponentBatch` / `RemoveComponentBatch` now group by source archetype/chunk and reuse migration state.
- **Artifact/Consumer/Generator gates**: added public API, Profiler/Safety compile-symbol, generator, and consumer build checks.

### Changed
- **System API consolidation**: serial systems now use `SystemBase`; legacy `SimpleSystem`, `DeclaredSystem`, `ChunkSystem`, `EntitySystem`, and old `ComponentLookup<T>` are no longer public APIs.
- **Profiler/Safety default off**: production DLLs do not compile profiler marker or access-validation strings unless explicitly enabled through MSBuild properties.
- **Query/Column hot paths**: `CompiledQuery` shares query cores while keeping read/write masks independent; column access and component lookup use typed accessors, and high-ID `ArchetypeLayout` lookup uses a direct index array.
- **MCP command surface**: legacy `query_entities_v2` has been fully renamed to `query_entities`.

### Fixed
- **ComponentMask high-ID copy-on-write**: fixed copied masks sharing `m_ExtraWords`, which could mutate a base mask or dictionary key.
- **Chunk row reuse zeroing**: new rows and migration-added columns no longer read stale component data; migration initializers avoid clear-then-overwrite for newly added components.
- **Deferred destroy version safety**: deferred destroy records full `Entity` versions and no longer destroys a replacement entity with the same index.
- **Deferred singleton preflight**: deferred create batches validate singleton conflicts before placing any entity.
- **SystemTicker parallel lifecycle**: parallel layers now follow Begin/Complete/EndParallel/EndTick ordering, and failed systems no longer play back partial deferred changes.
- **BufferStore long-term memory**: destroyed buffer value ranges are reusable, with fragmentation metrics exposed for debugging.
- **Source Generator**: cross-syntax-tree and expression-bodied `DeclareAccess` analysis is stable, with explicit cross-assembly component slot ordering.

## [0.12.4] — Source Generator Path Fix

### Fixed
- **Removed stale Generator copy from Editor/**: `Ember.Generator.dll` only exists in `RoslynAnalyzers/`, no longer loaded by Unity as an Editor plugin.

## [0.12.3] — Generated Code Safety Fix

### Fixed
- **ChunkMeta removed unsafe**: Generated ChunkMeta wrappers no longer use `unsafe` pointer operations, instead calling `ChunkJobMeta.Ref<T>()` instance method. User assemblies no longer need `allowUnsafeCode` enabled.
- **Missing .meta files**: Added Unity `.meta` files for `Ember.Generator.dll` and `OpenUPMSetup.cs`.

## [0.12.2] — OpenUPM Installation Support

### Added
- **OpenUPM package name installation**: Support direct installation via `com.ember.ecs` package name without Git URL.
- **Auto-configuration tool**: Added `Tools > Ember > Setup OpenUPM Registry` menu for one-click OpenUPM registry configuration.
- **Package installation tool**: Added `Tools > Ember > Install Ember Package` menu to automatically add dependency.
- **Installation script**: Added `Tools~/setup-openupm.sh` command-line script for batch configuration.

### Fixed
- **Gitee sync workflow**: Fixed missing checkout step in GitHub Actions that caused sync failure.

### Changed
- **Installation documentation**: README updated with dual-mode installation instructions (package name + Git URL).
- **Package repository structure**: Removed `.github` directory from package repo; sync logic moved to main repository.

## [0.12.1] — Source Generator Compatibility Fixes

### Fixed
- **Generated code namespace qualification**: ComponentPack adapter and chunk meta generated code now uses `global::Ember` for core types, avoiding resolution to user-defined same-name symbols.
- **Abstract/generic JobSystem skip**: The chunk meta generator no longer emits wrappers for abstract or open generic JobSystem types, avoiding invalid generated output.
- **Registry sealed idempotency**: `ComponentTypeRegistry` now allows already registered types to return their existing id after sealing, avoiding false new-registration errors when generated registrars are scanned again.

### Perf
- **ChunkJobMeta accessor inlining**: Generated chunk wrappers inline offset/stride access logic directly, reducing generic helper calls on hot paths.

## [0.12.0] — Full MCP Command Coverage + Editor Control

### Added
- **Full MCP command coverage**: Expanded from 26 to 54 commands covering Read (15), Write (9), Buffer (6), Diag (8), System (4), Editor Control (4), Hierarchy (4). AI agents can perform nearly all ECS and Editor operations via `ember_execute`.
- **Editor control commands**: `playmode_control` (enter/exit Play Mode), `set_time_scale`, `reload_scene`, `reload_domain` — AI agents can control Unity Editor runtime state.
- **Hierarchy commands**: `create_child_entity`, `attach_child`, `detach_child`, `get_hierarchy` — manage parent-child entity relationships via MCP.
- **Scene introspection commands**: `get_scene_info`, `get_gameobject_info`, `read_console` — access Unity scene hierarchy and console logs.
- **SimpleJson.GetRawValue**: Extract field values as raw JSON text, preserving nested objects and arrays intact.
- **ConvertValue type extensions**: Enum type and custom struct auto-parsing, added uint/short/ushort/byte/sbyte/ulong type support.

### Fixed
- **Buffer element serialization**: `get_buffer` output now uses `JsonValue(elem)` serialization for valid JSON instead of `ToString()` which could produce invalid output.
- **EmberSafeWriteBatchTool apply mode**: Apply mode now correctly executes all operations instead of only validating.
- **Numeric parsing locale issue**: All numeric parsing unified to use `CultureInfo.InvariantCulture`, fixing failures on non-English systems.
- **Editor control command permissions**: `playmode_control` and similar commands no longer require Play Mode as a precondition.
- **query_entities_v2 rename**: Legacy name `query_entities_v2` unified to `query_entities`.

### Changed
- **MCP architecture**: Migrated from external MCP Server back to self-hosted approach, retaining full source control and debugging capability.

### Changed
- **MCP JSON engine**: Migrated MCP Server from `System.Text.Json` to `Newtonsoft.Json` (Unity built-in package `com.unity.nuget.newtonsoft-json@3.2.1`), eliminating hand-written JSON field parsing code.
- **package.json**: Added `com.unity.nuget.newtonsoft-json` dependency so users get automatic resolution on package install.

### Removed
- **TcpBridge manual parsing**: Removed `ParseHandshakeManually`, `ExtractJsonField`, `ExtractJsonInt`, `ExtractJsonBool` methods (~45 lines). Newtonsoft's lenient parsing handles all fallback scenarios previously covered by these.

---

## [0.11.3] — MCP Connection Reliability

### Fixed
- **MCP auto-reconnect**: The MCP Server no longer binds itself to a single port only at startup. Before each `ember_execute` call it resolves the current project status file and ensures the bridge is connected, covering Unity reloads, port changes, and initially disconnected sessions.
- **Reload-state waiting**: During Unity Play Mode/domain reload, the Bridge writes a `reloading` state. The MCP Server respects that state instead of bypassing it with port scanning, then reconnects after the Bridge returns to `ready`.

### Added
- **Bridge v2 status file**: `~/.ember/ember-status-{projectHash}.json` records `ready/starting/reloading/port_busy`, `seq`, `lastHeartbeatUnixMs`, protocol version, and package version so agents can diagnose connection state precisely.
- **MCP connection regression tests**: Added a standalone `tests/Mcp` test project covering status parsing, reloading waits, first-call auto-connect, and reconnect-on-next-call without replaying the failed request.

### Changed
- **Port discovery strategy**: The Unity Bridge records the last successful port and tries it first, then falls back to scanning 9090-9099. The MCP Server supports per-project status, explicit `--project-root`, and scan fallback.
- **Release stability**: The MCP Server disables apphost generation to avoid macOS apphost code-signing failures, and references Ember with profiling disabled to avoid Unity Profiler ECalls in plain .NET environments.

---

## [0.11.2] — Symmetric Hierarchy Cleanup

### Fixed
- **Child destroy orphan**: `DestroyEntity(child)` now removes the child reference from its parent's `ChildEntity` buffer, eliminating dangling entries.

---

## [0.11.1] — Entity Templates + Hierarchy

### Added
- **Entity templates**: `EntityTemplate` with `Add<T>(value)`, `AddTag<T>()`, `AddChild(tag, template)`. Register and instantiate with `world.Instantiate(name)` to create entities with their child hierarchy in one call.
- **Parent-child hierarchy**: `ParentComponent` + `ChildEntity : IBufferElement` for bidirectional references. `GetChildren`/`GetParent`/`RemoveChild` APIs.
- **Cascade destroy**: `DestroyEntity` recursively destroys all child entities.
- **Orphan detection**: `ValidateConsistency` detects `ParentComponent` pointing to non-existent entities in DEBUG mode.

---

## [0.11.0] — API Simplification

### Changed — Breaking
- **System rename**: `SystemBase` → `SimpleSystem`, `DeclaredSystemBase` → `DeclaredSystem`. Names now reflect usage: SimpleSystem = simple serial + global barrier, DeclaredSystem = declared access + dependency graph participant.

### Added
- **`Chunk.At<T>(index)`**: Direct typed component ref access without compIndex. `chunk.At<Position>(i)` replaces `chunk.Get<Position>(0).At(i)`.

---

## [0.10.11] — MCP Performance Diagnostics

### Added
- **`perf_summary` MCP command**: One-click performance diagnostic. Samples N frames, auto-ranks slowest systems, returns frame-level timing breakdown with Top-N slow systems. Supports `tickerIndex`/`sampleFrames`/`topN` parameters.
- **Lightweight PerfCollect**: `EmberEditorGuard.ForcePerfCollect` flag. MCP commands can temporarily enable system timing collection without opening debug windows.
- **Perf skill update**: `ember-perf-optimize` diagnostic workflow now lists `perf_summary` as the primary entry point.

---

## [0.10.10] — Hot-Path Performance Fix

### Fixed
- **Tick performance regression**: Removed `ValidateConsistency()` from hot `Tick()` path (now on-demand only), eliminating per-Tick O(N) traversal and `HashSet` allocation.

---

## [0.10.9] — Resource Boundary Protection

### Added
- **Entity limit**: `World.MaxEntities` (default 1,000,000). `CreateEntity` throws `InvalidOperationException` when limit is reached, preventing OOM from unbounded creation.
- **Chunk limit**: `World.MaxTotalChunks` (default 20,000). Checked on new Chunk allocation, protecting Native memory from exhaustion.
- **Alive entity count**: `World.AliveEntityCount` tracks the current number of alive entities in real time.

---

## [0.10.8] — ECB Robustness

### Fixed
- **ECB Dispose safety**: Added `ThrowIfDisposed()` guards to 6 public methods. Calls after Dispose throw `ObjectDisposedException` instead of accessing disposed NativeList.
- **ECB SetComponent entity validation**: Check entity alive status before replaying `SetComponent` commands, preventing hard crash on destroyed entities.
- **ECB temp entity cross-buffer detection**: Unresolved temp entity IDs log `LogWarning` in DEBUG mode, helping diagnose cross-buffer entity reference errors.

---

## [0.10.7] — Structural Change Exception Safety

### Fixed
- **Batch pre-allocation**: `AddComponentBatch`/`RemoveComponentBatch` pre-allocate all target chunk slots before migrating any entity, preventing partial state if allocation fails mid-batch.

### Added
- **Reverse consistency validation**: `ValidateConsistency` adds Chunk→Record back-pointer verification, detecting dual-placement and stale records (EntityRecord→Archetype→Chunk 3-way inconsistency).
- **Structural change tests**: 8 unit tests covering consistency validation, batch boundaries, and guard ordering.

---

## [0.10.6] — Crash Prevention Refinements

### Fixed
- **Missing ThrowIfDisposed guards**: Added guards to `World.Exists`, 5 BufferElement methods, and 4 UNITY_EDITOR Buffer introspection methods.
- **Dispose ordering**: Moved `m_Disposed = true` and `DisposeEcsCore()` into the finally block to ensure correct marking on exception paths.
- **SystemTicker entry guard**: Added `world.IsDisposed` early return in `Tick()` and `TickSerialFlat()`.

### Changed
- **DestroyEntity internal split**: Public `DestroyEntity` calls `ThrowIfDisposed` then delegates to `DestroyEntityInternal`. World.Dispose uses the internal path to avoid redundant checks.

---

## [0.10.5] — Crash Prevention Hardening

### Fixed
- **World post-Dispose crash**: Added `ThrowIfDisposed()` guards to 32 public API entry points. Calls after Dispose throw `ObjectDisposedException` instead of `NullReferenceException`.
- **Idempotent Dispose**: Repeated calls to `World.Dispose()` no longer throw.

### Added
- **`World.IsDisposed`**: Property to query whether a World has been disposed.
- **Internal consistency self-check**: `World.ValidateConsistency()` automatically validates EntityRecord → Archetype → Chunk 3-way consistency in DEBUG mode, detecting internal state corruption early.
- **Crash scenario tests**: 24 unit tests covering post-Dispose API calls, invalid Entity handles, and batch null parameters.

---

## [0.10.4] — Installation Documentation

### Added
- **Unity installation guide**: Added installation section to README.

---

## [0.10.3] — Chunk Pooling + GC Hot-Path Elimination

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

## [0.10.1] — ValidateAccess GC Allocation Fix

### Perf
- **ValidateAccess zero allocation**: 4 `new List<string>()` + 4 `.ToArray()` replaced with reusable caches + `Array.Empty<string>()`. Zero per-frame allocation when access declarations are correct, eliminating ~56 allocations/frame of GC pressure.

## [0.10.0] — Access Validation + Entity Naming + Editor Code Isolation

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

## [0.8.0] — Dependency Graph Visualization + MCP Stability Fixes

### Added
- **Dependency graph view**: Systems Window adds Graph/Table toggle button. Graph view displays dependencies as layered nodes. Parallel layers green background, serial layers gray. System nodes show type and Read/Write component access lists. Arrows indicate dependency direction. Selected node bottom panel shows hooks, component access, layer info.

### Fixed
- **MCP connection stability**: Fixed unreliable `s_Client.Connected` on macOS causing silent response drops, `ListenLoop` exception not breaking causing TCP connection closure in finally, `Execute()` throwing exceptions instead of returning error JSON, among other connection reset issues.
- **MCP installation flow**: Fixed hard-coded `--port`, absolute path dependencies, missing `runtimeconfig.json` causing `dotnet exec` crash, extra commas in JSON merge. Added one-click Install/Uninstall with automatic path updates.

---

## [0.7.5] — Skills One-Click Install + Data Flow View Optimization

### Added
- **Skills one-click install**: MCP window adds Skills foldout for one-click installation of `ember-perf-optimize` (performance optimization) and `ember-architecture` (architecture guide) to project `.claude/skills/`, auto-discovered by Claude Code and Codex. `EmberSkillManager` embeds full SKILL.md content as compile-time constants; `IsInstalled()` runtime file existence check.
- **ember-perf-optimize skill**: Covers 8 optimization domains: System type selection, DeclareAccess precision, Job struct design, ChunkJobMeta access, Structural Change optimization, NativeArray lifecycle, etc. MCP diagnostic workflow: world_info → system_status → get_dependency_graph → get_archetypes.
- **ember-architecture skill**: Covers storage model (Entity/Component/Archetype/Chunk/SOA), System type hierarchy (SystemBase/DeclaredSystemBase/JobSystem/SystemGroup), query pipeline (ctx.QueryChunks/QueryBuilder/Aspect), ECB/Buffer/Singleton, DependencyGraph parallel scheduling, MCP debug commands reference.

### Changed
- **Data flow view**: Display by execution layer order.

---

## [0.7.1] — Editor Window Column Alignment Fix + Entities UI Simplification

### Fixed
- **4 Editor window column alignment**: `SystemsWindow`/`EntitiesWindow`/`ArchetypesWindow`/`ComponentTypesWindow` header-data row width mismatch. Root cause: headers outside `ScrollView` using `EditorStyles.toolbar`, data rows inside using `EditorStyles.helpBox`; scrollbar + padding differences caused alignment offset. Fix: headers moved into `BeginScrollView`, using `EditorGUI.DrawRect` + manual `Rect` positioning, column widths as constants.

### Changed
- **EntitiesWindow simplification**: Removed inline Components column; clicking Entity automatically opens `EntityInspectorWindow` for standalone component detail view. Toolbar Filter uses `EditorStyles.toolbarSearchField`.

---

## [0.7.0] — Development Agent System + Editor Tool Enhancements

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

## [0.6.0] — True Parallel Scheduling + MCP Synchronization

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

## [0.5.2] — Performance + Robustness Sweep

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

## [0.5.1] — Mixed Layer Semantics Fix + Zero Allocation Hardening

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

## [0.5.0] — IJobParallelFor Default Path + Zero-Allocation Scheduling

### Changed — Breaking
- **`IEmberChunkJob.Execute` signature change**: `Execute(Chunk chunk, int)` → `Execute(ChunkJobMeta meta, int)`. ChunkJobMeta contains BufferPtr, EntityCount, Comp0-3 Offset/Stride, enabling direct component data access via unsafe pointers.
- **`JobSystem<TJob>` replaces `JobSystemBase`**: Parallel System inherits `JobSystem<MoveJob>` (generic), `CompileJob` returns concrete struct type, `Schedule<T>` automatically uses IJobParallelFor (zero delegate/closure/boxing).
- **Removed `Parallel.ForEach` path**: Default scheduling changed to `ChunkJobWrapper<T> : IJobParallelFor`, eliminating managed scheduling allocations.
- **Removed `ExecuteUnsafe`**: No longer need dual paths; IJobParallelFor is the sole default path.

## [0.4.1] — GC Zero Allocation + IJobParallelFor Integration + MCP Multi-Instance Fix

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

## [0.4.0] — Parallelization + MCP v0.4.0

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
