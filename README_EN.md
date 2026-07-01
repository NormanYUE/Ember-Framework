# Ember User Manual

## Installation

Install the Ember ECS Framework in your Unity project:

1. Open `Window > Package Manager`
2. Click `+` → `Install package from git URL`
3. Enter the following URL:

```
https://github.com/NormanYUE/Ember-Framework.git
```

> Dependencies: `com.unity.collections` 2.4.3+, `com.unity.mathematics` 1.3.2+

---

## Table of Contents

1. [Quick Start](#1-quick-start)
2. [Component Definition and Registration](#2-component-definition-and-registration)
3. [Entity Operations](#3-entity-operations)
4. [Systems](#4-systems)
5. [Queries](#5-queries)
6. [BufferHandle](#6-bufferhandle)
7. [Entity Dynamic Arrays (IBufferElement)](#7-entity-dynamic-arrays-ibufferelement)
8. [Singleton Entities](#8-singleton-entities)
9. [EntityCommandBuffer (ECB)](#9-entitycommandbuffer-ecb)
10. [Deferred Destruction](#10-deferred-destruction)
11. [SystemGroup](#11-systemgroup)
12. [Performance and Diagnostics](#12-performance-and-diagnostics)
13. [Architecture Overview](#13-architecture-overview)
14. [MCP Server](#14-mcp-server)

---

## 1. Quick Start

### 1.1 Create ECSManager and Register Systems

```csharp
using Ember;

var manager = new ECSManager();
ECSManager.Active = manager; // Make this instance discoverable by Editor debugging tools and MCP

// Create Tickers (returns integer index, zero-overhead on hot paths)
int fixedIdx = manager.CreateTicker();
int updateIdx = manager.CreateTicker();
int lateIdx = manager.CreateTicker();

// Register systems on the appropriate Ticker
manager.GetTicker(fixedIdx).Register<MovementSystem>();
manager.GetTicker(updateIdx).Register<DamageSystem>();
manager.GetTicker(lateIdx).Register<CameraSystem>();
manager.GetTicker(updateIdx).Register<SpawnSystem>();
manager.Start();
```

### 1.2 Driving from Unity

```csharp
public class BattleBootstrap : MonoBehaviour
{
    private ECSManager m_Manager;
    private int m_FixedIdx, m_UpdateIdx, m_LateIdx;

    void Start()
    {
        m_Manager = new ECSManager();
        ECSManager.Active = m_Manager; // Make this instance discoverable by Editor debugging tools and MCP

        m_FixedIdx = m_Manager.CreateTicker();
        m_UpdateIdx = m_Manager.CreateTicker();
        m_LateIdx = m_Manager.CreateTicker();

        m_Manager.GetTicker(m_FixedIdx).Register<MovementSystem>();
        m_Manager.GetTicker(m_UpdateIdx).Register<DamageSystem>();
        m_Manager.GetTicker(m_LateIdx).Register<CameraSystem>();

        m_Manager.Start();
    }

    void FixedUpdate()
    {
        m_Manager.Tick(m_FixedIdx, Time.fixedDeltaTime);
    }

    void Update()
    {
        m_Manager.Tick(m_UpdateIdx, Time.deltaTime);
    }

    void LateUpdate()
    {
        m_Manager.Tick(m_LateIdx, Time.deltaTime);
    }

    void OnDestroy()
    {
        if (ECSManager.Active == m_Manager)
            ECSManager.Active = null;
        m_Manager.Dispose();
    }
}
```

---

## 2. Component Definition and Registration

### 2.1 Defining Components

Components must be `unmanaged` structs and must implement exactly one component-purpose interface: `IDataComponent`, `ITagComponent`, `ISingletonComponent`, or `IBufferElement`. You may define domain-specific subinterfaces for organizational purposes, e.g. `ICombatStateComponent : IDataComponent`; however, each concrete component must ultimately resolve to exactly one category branch.

| Interface | Semantics | Storage |
|-----------|-----------|---------|
| `IDataComponent` | Data component | Chunk SOA column, read/write |
| `ITagComponent` | Tag component | ComponentMask only, no data column, zero memory |
| `ISingletonComponent` | Singleton component | Same as data component, globally unique entity |
| `IBufferElement` | Buffer element | Per-entity dynamic array, multiple elements of the same type |

```csharp
using Ember;

public struct Health : IDataComponent
{
    public float Current;
    public float Max;
}

public struct Position : IDataComponent
{
    public float X;
    public float Y;
    public float Z;
}

public struct DeadTag : ITagComponent
{
    // Tag component, zero fields, zero memory
}
```

### 2.2 Registering Components

`ComponentTypeId` is an internal type ID automatically managed by the framework. `ComponentMask` uses inline bits for the first 256 components and automatically expands beyond that — users never need to manually define enums or worry about capacity.

**Method 1: Source Generator auto-registration (Recommended)**

The framework includes a Source Generator that automatically scans the current assembly for `IComponent` implementations at compile time and generates registration code. Registration validates the component category; `ITagComponent` must be a zero-field struct.

```csharp
// Automatically invoked during World creation — no manual intervention required
```

During `World` creation, the generated registration code from loaded assemblies is called first; assemblies without generated registration code fall back to reflection-based scanning. Hot-reload assemblies loaded before `World` creation are automatically registered.

**Method 2: Manual registration**

```csharp
ComponentTypeRegistry.Register<Health>();   // → ComponentTypeId(1)
ComponentTypeRegistry.Register<Position>(); // → ComponentTypeId(2)
```

Registration is idempotent — re-registering the same type does not error and returns the already-assigned ID.

---

## 3. Entity Operations

### 3.1 Creation and Destruction

```csharp
World world = m_Manager.World;

// Create an empty entity
Entity entity = world.CreateEntity();

// Create an entity with initial components
var mask = new ComponentMask();
mask.Add<Health>();
mask.Add<Position>();
Entity player = world.CreateEntity(mask);

// Check if an entity is alive
if (world.Exists(player)) { ... }

// Immediate destruction
world.DestroyEntity(player);

// Deferred destruction (batched at end of frame, safe during iteration)
world.DestroyEntityDeferred(player.Index);
```

### 3.2 Component Add / Remove / Get / Set

```csharp
// Add a component
world.AddComponent(entity, new Health { Current = 100, Max = 100 });

// Set component value
world.SetComponent(entity, new Health { Current = 80, Max = 100 });

// Get component reference (directly mutable)
ref var hp = ref world.GetComponent<Health>(entity);
hp.Current -= 10;

// Check for a component
if (world.HasComponent<Health>(entity)) { ... }

// Remove a component
world.RemoveComponent<DeadTag>(entity);

// Batch structural changes: prefer these when adding/removing the same component
// to a batch of entities at once
world.AddComponentBatch(entities, new Health { Current = 100, Max = 100 });
world.RemoveComponentBatch<DeadTag>(entities);
```

### 3.3 High-Performance int Index Access

During system iteration, use `entityIndex` (Entity.Index) directly to avoid Exists checks:

```csharp
ref var hp = ref world.GetComponent<Health>(entityIndex);
world.SetComponent<Health>(entityIndex, newHp);

if (world.HasComponent<Health>(entityIndex)) { ... }

bool found = world.TryGetComponent<Health>(entityIndex, out var health);
```

### 3.4 Querying Entity Count

```csharp
int alive = world.GetAliveCount();
bool alive = world.IsAlive(entityIndex);
Entity entity = world.GetEntity(entityIndex); // index → Entity handle
```

---

## 4. Systems

### 4.1 The Single System Base Class

All game systems inherit from `SystemBase` and override `OnTick(SystemContext ctx)`. Global logic, Chunk iteration, and entity traversal are all expressed through the query API provided by `SystemContext`.

Systems are registered to a specific Ticker via `ticker.Register<T>()`, and the user decides which Unity lifecycle drives each Ticker.

### 4.2 SystemContext Queries

The recommended approach is to override `OnTick(SystemContext ctx)` in `SystemBase`. `SystemContext` reuses the query cache, Chunk lists, column access caches, and automatically manages query traversal safety guards:

```csharp
using Ember;

public class DamageSystem : SystemBase
{
    protected override void OnTick(SystemContext ctx)
    {
        var query = EntityQuery.With<Health>().None<DeadTag>();

        foreach (var chunk in ctx.QueryChunks(query))
        {
            var health = chunk.Get<Health>();
            for (var i = 0; i < chunk.Count; i++)
            {
                ref var hp = ref health[i];
                hp.Current -= 10 * ctx.DeltaTime;
                if (hp.Current <= 0)
                    ctx.ECB.AddComponent(chunk.EntityAt(i), default(DeadTag));
            }
        }
    }
}
```

### 4.3 Columnar Chunk Iteration

```csharp
public class PhysicsSystem : SystemBase
{
    protected override void OnTick(SystemContext ctx)
    {
        foreach (var chunk in ctx.QueryChunks<Position, Velocity>())
        {
            var positions = chunk.Get<Position>();
            var velocities = chunk.Get<Velocity>();

            for (var i = 0; i < chunk.Count; i++)
            {
                positions[i].X += velocities[i].X * ctx.DeltaTime;
                positions[i].Y += velocities[i].Y * ctx.DeltaTime;
            }
        }
    }
}
```

### 4.4 Direct World Access

Access components directly through `SystemContext` without going through `ctx.World`:

```csharp
public class InputSystem : SystemBase
{
    protected override void OnTick(SystemContext ctx)
    {
        var world = ctx.World;
        var dt = ctx.DeltaTime;
        // ctx.Get<T>(entity) / ctx.Set<T>(entity, value) are equivalent to
        // ctx.World.GetComponent / SetComponent
    }
}
```

`ctx.Get<T>(entity)`, `ctx.Set<T>(entity, value)`, `ctx.Has<T>(entity)` are zero-overhead shortcuts for `ctx.World.GetComponent` etc.

### 4.5 ComponentPack

When a system needs to pack query results into a contiguous array for high-frequency computation and then write results back to component columns in bulk, use `SystemContext.Pack<TItem, TAdapter>()`. Pack reuses the compiled query, column accessors, internal item array, and chunk ranges; `Build()` and `WriteBack()` do not recreate the pack object each frame.

```csharp
public struct AgentItem
{
    public float X;
    public float Y;
    public float VX;
    public float VY;
}

public struct AgentPackAdapter : IComponentPackAdapter<AgentItem>
{
    public ComponentPackDescriptor Describe()
    {
        return ComponentPackDescriptor.Create()
            .Read<Position>()
            .Read<Velocity>()
            .Write<Position>()
            .Write<Velocity>()
            .None<DeadTag>();
    }

    public void Read(in PackReadContext ctx, int row, ref AgentItem item)
    {
        ref readonly var position = ref ctx.Read<Position>(row);
        ref readonly var velocity = ref ctx.Read<Velocity>(row);
        item.X = position.X;
        item.Y = position.Y;
        item.VX = velocity.X;
        item.VY = velocity.Y;
    }

    public void Write(in PackWriteContext ctx, int row, in AgentItem item)
    {
        ref var position = ref ctx.Write<Position>(row);
        ref var velocity = ref ctx.Write<Velocity>(row);
        position.X = item.X;
        position.Y = item.Y;
        velocity.X = item.VX;
        velocity.Y = item.VY;
    }
}

public class AgentSystem : SystemBase
{
    protected override void OnTick(SystemContext ctx)
    {
        var pack = ctx.Pack<AgentItem, AgentPackAdapter>();
        pack.Build();

        AgentSolver.Apply(pack.Items, pack.Count, ctx.DeltaTime);

        pack.WriteBack();
    }
}
```

`Read<T>()` can only read components declared as `Read<T>()` in the adapter's `Describe()`, and `Write<T>()` can only write components declared as `Write<T>()`. No structural changes may occur between `Build()` and `WriteBack()` — otherwise `WriteBack()` throws an explicit exception.

### 4.6 Structural Change Rules

Ordinary systems do not need to declare component access patterns or whether they perform structural changes. The Ticker conservatively executes systems in registration order by default, guaranteeing that one system's changes are visible to the next.

The only rule to remember: do not create/destroy entities or add/remove components directly during `SystemContext.QueryChunks(...)`, `EcsAPI.Query(...).AsRows()`, or any other query traversal. When structural changes are needed mid-iteration, record them to a list or `EntityCommandBuffer` first, then execute after the iteration finishes.

**Exception**: `ctx.CreateEntity()` can safely create entities during iteration, immediately returning a real `Entity` (chunk placement is automatically deferred to the end of the current query iteration). See Section 4.7.

### 4.7 Creating Entities During Iteration

`ctx.CreateEntity()` allows creating entities during query traversal and immediately obtaining a real `Entity` reference — solving the problem where ECB temporary IDs cannot be stored in component data. The entity index and version are assigned immediately; chunk placement is automatically performed when the `foreach` loop ends.

```csharp
public class SpawnMinionSystem : SystemBase
{
    protected override void OnTick(SystemContext ctx)
    {
        foreach (var chunk in ctx.QueryChunks<Enemy, MinionSquad>())
        {
            var enemies = chunk.Get<Enemy>();
            var squads = chunk.Get<MinionSquad>();

            for (var i = 0; i < chunk.Count; i++)
            {
                if (enemies[i].Health >= 50) continue;

                // Create directly inside the foreach body, get a real Entity
                var minion = ctx.CreateEntity(minionMask);
                ctx.ECB.AddComponent(minion, new Minion { Owner = chunk.EntityAt(i) });
                squads[i].MinionA = minion; // Real ID stored immediately
            }
        }
        // ← foreach ends, minion is automatically placed in chunk, visible to subsequent queries in this system
    }
}
```

Constraints: within the current `foreach` body, you cannot call `GetComponent` / `SetComponent` on pending entities — the entity's initial state should be set via `ctx.ECB`, which the system will play back after the Tick completes. `ctx.CreateEntity()` also accepts an optional `ComponentMask` parameter to specify the initial archetype.

### 4.8 Parallel Systems (JobSystem&lt;TJob&gt;)

Inherit from `JobSystem<TJob>`, declare read/write access + `CompileJob`, and the framework automatically schedules IJobParallelFor across Chunks (**zero allocation**).

```csharp
public class MovementSystem : JobSystem<MoveJob>
{
    protected override void DeclareAccess(AccessBuilder a)
    {
        a.Read<Velocity>().Write<Position>();
    }

    protected override MoveJob CompileJob(SystemContext ctx)
    {
        return new MoveJob { DeltaTime = ctx.DeltaTime };
    }

    struct MoveJob : IEmberChunkJob
    {
        public float DeltaTime;

        // meta contains BufferPtr, EntityCount, Comp0-3Offset/Stride
        // Comp0 = Position (smallest TypeId), Comp1 = Velocity
        public void Execute(ChunkJobMeta meta, int chunkIndex)
        {
            // Source Generator auto-generates MovementChunkMeta, with type-safe Position/Velocity access
            // Sorted by ComponentTypeId ascending → Position(Comp0), Velocity(Comp1)
            var m = MovementChunkMeta.Wrap(meta);
            for (int i = 0; i < m.EntityCount; i++)
            {
                ref var pos = ref m.Position(i);
                var vel = m.Velocity_RO(i);
                pos.X += vel.X * DeltaTime;
            }
        }
    }
}
```

**Class hierarchy:**
```
EcsSystem (shared lifecycle)
  ├── SystemBase       — OnTick manual loop (single-threaded)
  └── JobSystemBase     — DeclareAccess (base class, for testing)
       └── JobSystem<T>  — CompileJob → IJobParallelFor (zero allocation)
```

**Scheduling path:** `Schedule<T>` → `ChunkJobWrapper<T> : IJobParallelFor` → Unity Job System runs in parallel per Chunk → `Complete()` blocks until done. Zero delegates/closures/boxing.

**Class hierarchy:**
```
EcsSystem (shared lifecycle)
  ├── SystemBase  — OnTick manual loop (single-threaded)
  └── JobSystemBase — DeclareAccess + CompileJob → IEmberChunkJob (parallel)
```

`SystemBase` with undeclared access automatically adopts a conservative strategy (mutually exclusive with all other systems).

## 5. Queries

Ember provides the following query entry points — choose by context:

| Context | Entry Point | Notes |
|---------|-------------|-------|
| **Inside a System** | `ctx.QueryChunks<A, B>()` | Primary choice, used every frame |
| **Outside Systems (one-shot)** | `EcsAPI.Query(world).Read<A>().AsRows()` | Initialization or ad-hoc queries |
| **Cached query object** | `new EntityQuery(mask)` → `world.GetChunks()` | Reuse across Ticks to avoid rebuilding |

**Constructing an EntityQuery** — use the `EntityQuery.With<T>()` chainable builder:

```csharp
var query = EntityQuery.With<Health, Position>().None<DeadTag>().Build();
```

`EntityQueryBuilder` is a struct — pass by value with no allocation.

### 5.1 External Queries (QueryBuilder + EcsAPI)

Use `EcsAPI.Query()` outside of systems or in custom logic:

```csharp
World world = m_Manager.World;

// Iterate all entities with Health
foreach (var row in EcsAPI.Query(world).Read<Health>().AsRows())
{
    ref readonly var hp = ref row.RO<Health>();
    Debug.Log($"Entity {row.Entity}: HP = {hp.Current}");
}

// Read-write query
foreach (var row in EcsAPI.Query(world).Write<Health>().AsRows())
{
    ref var hp = ref row.RW<Health>();
    hp.Current = Mathf.Min(hp.Current + healAmount, hp.Max);
}

// Chunk-level iteration (more efficient)
foreach (var chunkView in EcsAPI.Query(world).Read<Position>().AsChunks())
{
    var positions = chunkView.Column<Position>();
    for (int i = 0; i < chunkView.Count; i++)
    {
        ref readonly var pos = ref positions[i];
        // ...
    }
}

// Read-write query
foreach (var row in EcsAPI.Query(world)
    .Read<Position>()
    .Write<Health>()
    .AsRows())
{
    ref readonly var pos = ref row.RO<Position>();
    ref var hp = ref row.RW<Health>();
    // ...
}
```

### 5.2 QueryBuilder Method Reference

| Method | Effect |
|--------|--------|
| `Read<T>()` | Adds component to All mask + Read mask |
| `Write<T>()` | Adds component to All mask + Write mask |
| `Build()` | Returns an EntityQuery |
| `Compile()` | Returns a CompiledQuery |
| `AsRows()` | Returns row-level enumeration (foreach) |
| `AsChunks()` | Returns Chunk-level enumeration (foreach) |

---

## 6. BufferHandle

A dynamically-sized array managed through `BufferHandle` references from a global pool. Suitable for shared data pools (frame data caches, event queues). **Not bound to a specific entity** — backed by `BufferStore<T>` (NativeList) storage, accessed via handle.

> If you need **per-entity** dynamic arrays, see Section 7 (IBufferElement).

### 6.1 Creation and Destruction

```csharp
World world = m_Manager.World;

// Create a buffer
BufferHandle damageLog = world.CreateBuffer<DamageRecord>(initialCapacity: 16);

// Destroy a buffer
world.DestroyBuffer<DamageRecord>(damageLog);
```

### 6.2 Read and Write Operations

```csharp
// Add an element
world.AddBufferElement(damageLog, new DamageRecord { Target = entity, Amount = 50 });

// Read
BufferSpan<DamageRecord> span = world.GetBuffer<DamageRecord>(damageLog);
for (int i = 0; i < span.Length; i++)
{
    ref readonly var record = ref span[i];
    Debug.Log($"Damage: {record.Amount}");
}

// Set an element
world.SetBufferElement(damageLog, 0, newRecord);

// Remove an element (swap-back with last)
world.RemoveBufferElementAtSwapBack(damageLog, 0);

// Clear
world.ClearBuffer<DamageRecord>(damageLog);

// Get length
int len = world.GetBufferLength<DamageRecord>(damageLog);
```

### 6.3 Direct Pointer Access via BufferSpan

```csharp
BufferSpan<DamageRecord> span = world.GetBuffer<DamageRecord>(damageLog);
unsafe
{
    DamageRecord* ptr = span.UnsafePtr;
    for (int i = 0; i < span.Length; i++)
    {
        // Direct pointer operations, zero overhead
    }
}
```

---

## 7. Entity Dynamic Arrays (IBufferElement)

`IBufferElement` allows **each entity** to independently carry multiple elements of the same type — analogous to per-entity dynamic arrays. Suitable for skill lists, damage queues, buff stacking, etc. Backed by a managed `List<T>` internally, accessed directly via Entity.

> Note: `World.AddBufferElement(BufferHandle, ...)` and `World.AddBufferElement(Entity, ...)` are **two different methods** — the former operates on Section 6 handle-based buffers, the latter on per-entity buffers described in this section. Signatures differ and are unambiguous.

### Defining Buffer Elements

```csharp
public struct DamageRecord : IBufferElement
{
    public Entity Source;
    public float Amount;
}
```

### Using Buffer Elements

```csharp
var entity = world.CreateEntity();

// Add elements
world.AddBufferElement(entity, new DamageRecord { Source = attacker, Amount = 50 });
world.AddBufferElement(entity, new DamageRecord { Source = attacker, Amount = 30 });

// Get accessor
BufferElementAccessor<DamageRecord> buffer = world.GetBufferElement<DamageRecord>(entity);
for (int i = 0; i < buffer.Length; i++)
    Debug.Log(buffer[i].Amount);

// Modify element
buffer[0] = new DamageRecord { Source = attacker, Amount = 100 };

// Remove element (swap-back with last)
world.RemoveBufferElementAt<DamageRecord>(entity, 0);

// Clear
world.ClearBufferElements<DamageRecord>(entity);

// Get count
int count = world.GetBufferElementCount<DamageRecord>(entity);
```

Buffer elements are automatically cleaned up when the entity is destroyed.

---

## 8. Singleton Entities

Singleton entities store globally shared data (game state, configuration, etc.).

### 8.1 Defining Singleton Components

```csharp
public struct GameState : ISingletonComponent
{
    public int Score;
    public float TimeRemaining;
}
```

Only one entity instance of a given singleton component type is allowed globally.

### 8.2 Using Singletons

```csharp
World world = m_Manager.World;

// Get or create (auto-creates if it does not exist)
Entity stateEntity = world.GetOrCreateSingleton<GameState>();

// Read and write
ref var state = ref world.GetComponent<GameState>(stateEntity);
state.Score += 10;

// Get only (throws if not found)
Entity e = world.GetSingleton<GameState>();

// Safe get
if (world.TryGetSingleton<GameState>(out Entity e))
{
    ref var s = ref world.GetComponent<GameState>(e);
}
```

---

## 9. EntityCommandBuffer (ECB)

ECB collects structural changes (create/destroy entities, add/remove components) during iteration and plays them back in bulk after iteration finishes.

> **Important**: Within a system, prefer using `SystemBase.ECB` — the framework automatically plays it back and disposes it after the current system Tick completes. Only create a manual ECB via `World.Playback(ecb)` when you are outside a system or need custom playback timing.

### 9.1 Using the Automatic ECB in Systems

```csharp
public class DeathSystem : SystemBase
{
    protected override void OnTick(SystemContext ctx)
    {
        var query = EntityQuery.With<Health>().None<DeadTag>();

        foreach (var chunk in ctx.QueryChunks(query))
        {
            var health = chunk.Get<Health>();
            for (var i = 0; i < chunk.Count; i++)
            {
                if (health[i].Current <= 0)
                    ctx.ECB.AddComponent(chunk.EntityAt(i), default(DeadTag));
            }
        }
    }
}
```

After the system Tick completes normally, the framework automatically plays back the `ECB` and disposes it. If the system throws an exception, the automatic ECB recorded for that frame is not played back.

### 9.2 Manual ECB

Use manual ECB when outside a system or when custom playback timing is needed:

```csharp
EntityCommandBuffer ecb = world.CreateCommandBuffer();

foreach (var row in EcsAPI.Query(world).Read<Health>().AsRows())
{
    ref readonly var hp = ref row.RO<Health>();
    if (hp.Current <= 0)
    {
        ecb.AddComponent(row.Entity, default(DeadTag));
    }
}

world.Playback(ecb);
ecb.Dispose();
```

Playback failure throws an exception by default, ensuring structural change errors are not silently ignored.

### 9.3 Creating Temporary Entities

```csharp
EntityCommandBuffer ecb = world.CreateCommandBuffer();

// Create a temporary entity (returns a temporary handle with a negative Index)
Entity tempEntity = ecb.CreateEntity(ComponentMask.Empty);
ecb.AddComponent(tempEntity, new Health { Current = 100, Max = 100 });

// Automatically mapped to a real Entity during playback
```

---

## 10. Deferred Destruction

### 10.1 Marking for Destruction

```csharp
// Mark during iteration (not destroyed immediately)
world.DestroyEntityDeferred(entityIndex);
```

### 10.2 Checking Pending Destruction

```csharp
// During system iteration, the framework automatically skips entities pending destruction
// Manual check:
if (world.CachedNeedRemoveIndices.Contains(entityIndex))
{
    // This entity has been marked for deferred destruction
}
```

### 10.3 Batch Processing

```csharp
world.TryGetDeferredDestroyEntities(out BufferSpan<int> entities);
for (int i = 0; i < entities.Length; i++)
{
    int entityId = entities[i];
    // Process entities pending destruction
}

// Clear markers (call at end of frame)
world.ClearDeferredDestroyEntities();
```

---

## 11. SystemGroup

### 11.1 Defining a System Group

`SystemGroup` registers multiple systems to the same Ticker for convenient batch registration. Groups can be nested.

```csharp
public class CombatGroup : SystemGroup
{
    public override void Configure(SystemTicker ticker)
    {
        ticker.Register<DamageSystem>();
        ticker.Register<HealthSystem>();
    }
}
```

### 11.2 Nested Groups

```csharp
public class GameplayGroup : SystemGroup
{
    public override void Configure(SystemTicker ticker)
    {
        ticker.Register<MovementGroup>();   // Nested sub-group
        ticker.Register<CombatGroup>();     // Nested sub-group
        ticker.Register<AudioSystem>();     // Individual system
    }
}

public class MovementGroup : SystemGroup
{
    public override void Configure(SystemTicker ticker)
    {
        ticker.Register<PhysicsSystem>();
        ticker.Register<MovementSystem>();
    }
}
```

### 11.3 Registration and Execution Order

```csharp
var manager = new ECSManager();
int updateIdx = manager.CreateTicker();
var updateTicker = manager.GetTicker(updateIdx);
updateTicker.Register<GameplayGroup>();   // Expands to: PhysicsSystem → MovementSystem → DamageSystem → HealthSystem → AudioSystem
manager.Start();
```

Registration order is execution order. `SystemGroup` allows you to clearly organize the system hierarchy while maintaining precise execution order control.

---

## 12. Performance and Diagnostics

### 12.1 Hot Path Recommendations

Prefer using `Chunk.GetColumn<T>()` for columnar read/write within systems. `World.GetComponent<T>(Entity)` and `World.GetComponent<T>(int)` are fine for occasional random access; when processing batched results from a query, do not repeatedly call `World.GetComponent` in inner loops.

Structural changes trigger archetype migration, shared component copying, old chunk swap-back, and entity record updates. When adding or removing the same component on a batch of entities, prefer:

```csharp
world.AddComponentBatch(entities, value);
world.RemoveComponentBatch<MyComponent>(entities);
```

`ITagComponent` has no data column, but adding/removing a tag is still a structural change. Do not express high-frequency state toggling through frequent tag add/remove; prefer a `bool` or `enum` field in a regular data component.

### 12.2 ComponentMask API

`ComponentMask` is a mutable struct. `With<T>()` / `Without<T>()` **modify the current variable and return it** — this is a performance optimization (avoiding struct copies), but means they are impure: calling `With<T>()` on an existing variable mutates it. Suitable for chaining to construct a new mask:

> Warning: `With<T>()` / `Without<T>()` mutate in place. To derive a new mask from an existing one, use `WithAdded<T>()` / `WithRemoved<T>()` (copy then modify).

```csharp
var queryMask = new ComponentMask()
    .With<Health>()
    .With<Position>();
```

If you need to derive a new mask from an existing mask, do not call `With<T>()` directly on the original variable. Use the non-mutating APIs:

```csharp
var baseMask = new ComponentMask().With<Health>();
var energyMask = baseMask.WithAdded<Energy>();
var withoutEnergy = energyMask.WithRemoved<Energy>();
```

When you explicitly intend in-place mutation, use:

```csharp
baseMask.Add<Position>();
baseMask.Remove<DeadTag>();
```

### 12.3 ProfilerMarker

The framework includes built-in Unity Profiler markers, but profiling marker code is excluded from production builds by default to avoid running analysis logic unnecessarily. Enable profiling with the MSBuild property:

```bash
dotnet build -c Release /p:EmberEnableProfiling=true
```

When enabled, marker names are stable for easy Unity Profiler filtering:

- `Ember.World.GetChunks`
- `Ember.QueryCache.Rebuild`
- `Ember.World.MigrateEntity`
- `Ember.Chunk.MoveEntityTo`
- `Ember.Chunk.RemoveAtSwapBack`
- `Ember.World.AddComponent`
- `Ember.World.RemoveComponent`
- `Ember.SystemTicker.Tick`

### 12.4 Microbenchmarks

The repository includes a framework microbenchmark project:

```bash
dotnet build tests/Performance/Ember.Performance.csproj -c Release
```

Benchmarks cover component read/write, chunk column access, query cache, AddComponent/RemoveComponent, tag add/remove, batch structural changes, ComponentPack, and GC allocation. Benchmarks use real `World` / `Chunk` / `Unity.Collections.NativeArray` paths. Actual profiling requires a Unity-compatible runtime; plain .NET CLI can compile the project but cannot execute Unity native ECall-backed `NativeArray` allocations. Cached queries, chunk column reads/writes, and warmed ComponentPack build/write should maintain 0 GC.

---

## 13. Architecture Overview

### 13.1 Storage Model

```
ECSManager (system management)
 ├── CreateTicker()             Create a Ticker, returns index
 ├── GetTicker(index)           Get Ticker instance
 ├── Start()                    Create World, initialize all Tickers
 ├── Tick(index, dt)            Drive the specified Ticker (integer index, zero overhead)
 ├── Dispose()                  Clean up Tickers + World
 └── World                      ECS data container
      ├── EntityRecord[]         Entity → Archetype/Chunk mapping
      ├── Archetype[]            Component combination templates
      │    ├── ArchetypeLayout   Type → column index/offset O(1) lookup
      │    └── Chunk[]           128 entities / chunk, SOA layout
      │         └── NativeArray<byte>   Contiguous memory, partitioned by component type (Tags have no data column)
      ├── QueryCache             Query cache (dictionary lookup by EntityQueryKey, invalidated by Archetype version)
      ├── BufferStore<T>         Dynamic buffer pool
      ├── BufferElement          Entity-level dynamic arrays (IBufferElement)
      └── Singleton              Singleton entity mapping

SystemTicker (system scheduling)
 ├── Register<T>()             Register system/group
 ├── Init()                    Call OnCreate
 ├── Tick(world, dt)            Conservative execution in registration order:
 │    ├── Call OnTick
 │    └── Auto-playback system ECB after successful completion
 └── Dispose()                 OnDestroy + cleanup
```

### 13.2 Component Type Semantics

| Type | Storage | Queryable | Data Access |
|------|---------|-----------|-------------|
| `IDataComponent` | Chunk SOA column | Yes | `GetComponent<T>` / `GetColumn<T>` |
| `ITagComponent` | ComponentMask only | Yes | No (throws) |
| `ISingletonComponent` | Same as data component | Yes | Globally unique entity |
| `IBufferElement` | Per-entity dynamic array | No | `GetBufferElement<T>` |

### 13.3 System Scheduling

```
ECSManager.CreateTicker()
  → Creates a SystemTicker instance, returns an integer index

ticker.Register<T>()
  → SystemGroup subclasses expand via Configure(this)
  → SystemBase subclasses added to pending registration list

ECSManager.Start()
  → Creates a World instance
  → Calls each Ticker.Init() → OnCreate()

ECSManager.Tick(index, dt)
  → ticker.Tick(world, dt)
  → Executes systems sequentially:
     1. Enter system safety context
     2. Call OnTick
     3. Auto-playback ECB after system completes successfully
     4. Exit system safety context

Structural changes are only prohibited during query traversal; when structural modifications
are needed mid-iteration, record them to the system `ECB` or a temporary list first, then
execute after iteration finishes.

ECSManager.Dispose()
  → Ticker.Dispose() → OnDestroy()
  → World.Dispose() → Clean up ECS data
```

### 13.4 Key Type Relationships

```
IComponent
  ↓ categorized as
IDataComponent / ITagComponent / ISingletonComponent / IBufferElement
  ↓ registered with
ComponentTypeRegistry (Type ↔ ComponentTypeId + Size/Alignment + Kind)
  ↓ referenced by
ComponentMask (inline 256-bit + dynamic extra words) + EntityQuery (All/Any/None)
  ↓ matched against
Archetype.Layout (O(1) lookup) → Chunk[] → ChunkColumn<T>
  ↓ iterated by
SystemBase / SystemContext
```

---

## 14. MCP Server

The MCP Server lets AI coding assistants (Claude Code, Codex, etc.) directly operate on a running ECS World via the standard [Model Context Protocol](https://modelcontextprotocol.io) — query entities, inspect Archetypes, modify components, all automated by AI without writing manual debug scripts.

### MCP Intelligent Diagnostics

Ember deeply integrates MCP, allowing AI agents to read and write the running Unity ECS world through 40+ `ember_execute` commands.

**Three Diagnostic Capabilities:**

| | |
|---|---|
| **🔍 Live Inspection** | `get_entity_full`, `query_entities`, `get_archetypes` — see inside the runtime ECS world |
| **📊 Performance Profiling** | `perf_summary` — one-click sampling with automatic slowest-system ranking; `system_status` — per-system avg/max Tick times |
| **🛠️ Runtime Intervention** | `add_component`, `set_singleton`, `safe_write_batch` — modify runtime data without writing code |

**Diagnostic in Action:**

```
User: "Help me analyze performance"
```

AI calls `perf_summary`, samples 10 frames, and produces a complete report in under a minute — system timing breakdown, parallel layer topology, Archetype fragmentation analysis, prioritized optimization recommendations with projected improvements. One `InteractionBuild` system consuming 91% of the frame budget prompts AI to suggest parallelizing Pack.Build and spatial-hashing Grid.Apply, projecting total tick reduction from 9.4ms to 3-5ms.

No breakpoints, no log diving. One conversation for diagnosis + solution + verification.

### 14.1 Architecture Overview

```
AI Client            MCP Server           Unity Editor
(Claude Code/Codex)  (.NET console app)   (EmberBridge TCP)
    │                     │                     │
    │── JSON-RPC stdio ──▶│                     │
    │                     │── TCP (127.0.0.1) ─▶│
    │                     │                     │── ECSManager.Active
    │                     │◀── JSON response ───│
    │◀── JSON-RPC stdio ──│                     │
```

- **EmberBridge**: A TCP server inside the Unity Editor that automatically scans ports 9090-9099 for an available one, dispatches requests to the main thread for execution, and returns results over TCP. The Bridge writes `~/.ember/ember-status-{projectHash}.json` with the current port, project root, `ready/reloading/port_busy` state, and heartbeat timestamp
- **MCP Server** (`Ember.Mcp.Server.dll`): A standalone .NET console application serving as the standard MCP protocol adaptation layer between the AI client and Unity. Before every `ember_execute` call, it rereads the project status file from `--project-root` and ensures the Bridge is connected; Unity reloads, port changes, and initially disconnected sessions recover automatically
- **Security model**: Write access is enforced on the Unity side by command type and Play Mode state. Read operations can run in Edit Mode; creating/destroying entities, adding/removing components, and writing components require Play Mode.

### 14.2 Installation and Configuration

#### 14.2.1 Unity Side (EmberBridge)

Open the Ember MCP window: **`Window > Ember > MCP`**

The status bar at the top displays the current state:

- `● Connected` (green) — MCP Server connected, ready to process requests
- `◉ Waiting` (yellow) — Bridge started, waiting for client connection
- `○ Stopped` (gray) — Bridge not running

Click **`Start`** to begin TCP listening, **`Stop`** to shut down. If you want the Bridge to start automatically each time you open the Unity project, enable `AutoStart` in the **`Settings`** foldout.

> The Bridge runs only in the Unity Editor. Read operations do not require Play Mode, but write operations (create/destroy entities, add/remove components) must be performed in Play Mode.

#### 14.2.2 AI Client Configuration

In the **`Client Setup`** foldout, you can install/uninstall MCP configurations for AI clients with one click:

- **Claude Code** → `.mcp.json` (project root)
- **Codex** → `.codex/config.toml`

After clicking `Install`, the window automatically generates a configuration pointing to `Tools~/Ember.Mcp.Server.dll`. When the package is updated (git hash in the Tools~ path changes), the window also auto-repairs stale configuration paths when opened.

You can also edit the configuration file manually. Example for Claude Code:

```json
{
  "mcpServers": {
    "ember": {
      "command": "dotnet",
      "args": [
        "exec",
        "Assets/Packages/com.ember.ecs/Tools~/Ember.Mcp.Server.dll",
        "--project-root",
        "/path/to/UnityProject"
      ]
    }
  }
}
```

**Launch arguments:**

| Argument | Description |
|----------|-------------|
| `--project-root <path>` | Unity project root. Recommended: the MCP Server uses it to read the matching project status file and avoid connecting to another Unity project |
| `--port <n>` | Manually specify a port. Usually unnecessary; use only when the status file is unavailable and the port is known |
| `--allow-write` | Legacy compatibility flag. It no longer controls permissions; writes are guarded by Unity-side Play Mode and command type checks |

### 14.3 Command Reference (43 commands via `ember_execute`)

The `ember_execute` tool accepts a `commands` array. Each command has an `op` field.

**Read (12):** world_info, query_entities, get_entity, get_entity_full, get_archetypes, get_systems, get_component_types, has_component, get_singleton, get_singletons, get_buffer_elements, entity_counts
**Write (6):** create_entity, destroy_entity, add_component, remove_component, set_component, set_singleton
**Batch (2):** add_component_batch, remove_component_batch
**Buffer (5):** add_buffer_element, remove_buffer_element, clear_buffer_elements, set_buffer_element, get_buffer
**Diagnostic (10):** mcp_status, component_schema, validate_component_payload, resolve_component, world_snapshot, snapshot_diff, get_ecs_status, capabilities, perf_summary, archetype_layout_report
**System (4):** system_status, get_system_info, get_dependency_graph, advance_frame
**Entity (2):** trace_entity, query_archetypes
**Write Safety (1):** safe_write_batch
**World (1):** list_worlds

Example: `{"op": "query_entities", "all": ["Position"], "limit": 10}`
Example: `{"op": "get_system_info", "tickerIndex": 0, "systemName": "MovementSystem"}`

### 14.4 Usage Examples

A typical interaction flow in an AI client:

> **User**: Query all entities with a Health component and find low-health ones
> **AI** calls `ember_execute({"op":"query_entities","all":["Health"]})`
> → Returns 3 entities: Entity #1 Health.Current = 80, Entity #2 Health.Current = 5
>
> **User**: Entity #2 is nearly dead, show its details
> **AI** calls `ember_get_entity(entityIndex=2)`
> → Returns all components on Entity #2: Health { Current: 5, Max: 100 }, Position { X: 10, Y: 2, Z: 0 }, DeadTag (tag)
>
> **User**: Create a new entity next to it (X+3) with the same components
> **AI** calls `ember_create_entity(components=["Health","Position"])` → gets new entityIndex=15
> **AI** calls `ember_set_component(15, "Health", {"Current":100,"Max":100})`
> **AI** calls `ember_set_component(15, "Position", {"X":13,"Y":2,"Z":0})`
> → Entity #15 created

### 14.5 Debugging and Troubleshooting

#### Common Issues

| Issue | Cause | Solution |
|-------|-------|----------|
| "No fresh Ember bridge status was found" | Bridge not started, status file stale, or `--project-root` points to the wrong project | `Window > Ember > MCP` → Start, and confirm `--project-root` is the Unity project root |
| "Unity bridge is reloading" | Unity is switching Play Mode or doing a domain reload | Wait until the Bridge returns to `ready`; the MCP Server reconnects automatically |
| "Write operations require Play Mode" | Write operations must be in Play Mode | Enter Play Mode |
| Client hangs / unresponsive after launch | Port conflict or stale connection | The Bridge retries the last successful port first, then 9090-9099; restart the Ember MCP window if needed |
| Configuration broken after package update | Tools~ path hash changed | Window `AutoUpdateConfigPaths()` auto-repairs; reopen the `Ember MCP` window |

#### Log Diagnostics

- **Unity Console**: Filter by `[Ember MCP]` prefix to view Bridge startup status, client connect/disconnect events, request method names, and truncated JSON content
- **Ember MCP Window → Request Log**: Real-time display of the most recent 100 requests showing method name, response time (ms), and result preview
- **`mcp_status` command**: Returns `running/state/reason/reloading/statusFile/statusSeq/clientCount`, useful for distinguishing Bridge stopped, Unity reloading, port busy, and no-client states
