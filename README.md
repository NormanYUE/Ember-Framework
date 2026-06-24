# Ember 使用手册

## 目录

1. [快速开始](#1-快速开始)
2. [组件定义与注册](#2-组件定义与注册)
3. [实体操作](#3-实体操作)
4. [系统（System）](#4-系统system)
5. [查询（Query）](#5-查询query)
6. [Buffer 句柄（BufferHandle）](#6-buffer-句柄bufferhandle)
7. [实体动态数组（IBufferElement）](#7-实体动态数组ibufferelement)
8. [单例实体（Singleton）](#8-单例实体singleton)
9. [延迟命令缓冲区（ECB）](#9-延迟命令缓冲区ecb)
10. [延迟销毁](#10-延迟销毁)
11. [系统组合（SystemGroup）](#11-系统组合systemgroup)
12. [性能与诊断](#12-性能与诊断)
13. [架构概览](#13-架构概览)
14. [MCP Server](#14-mcp-server)

---

## 1. 快速开始

### 1.1 创建 ECSManager 并注册系统

```csharp
using Ember;

var manager = new ECSManager();
ECSManager.Active = manager; // 让 Editor 调试工具和 MCP 发现此实例

// 创建 Ticker（返回整数索引，热路径零开销）
int fixedIdx = manager.CreateTicker();
int updateIdx = manager.CreateTicker();
int lateIdx = manager.CreateTicker();

// 注册系统到指定 Ticker
manager.GetTicker(fixedIdx).Register<MovementSystem>();
manager.GetTicker(updateIdx).Register<DamageSystem>();
manager.GetTicker(lateIdx).Register<CameraSystem>();
manager.GetTicker(updateIdx).Register<SpawnSystem>();
manager.Start();
```

### 1.2 Unity 中驱动

```csharp
public class BattleBootstrap : MonoBehaviour
{
    private ECSManager m_Manager;
    private int m_FixedIdx, m_UpdateIdx, m_LateIdx;

    void Start()
    {
        m_Manager = new ECSManager();
        ECSManager.Active = m_Manager; // 让 Editor 调试工具和 MCP 发现此实例

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

## 2. 组件定义与注册

### 2.1 定义组件

组件必须是 `unmanaged` struct，并且实现且只实现一种组件用途接口：`IDataComponent`、`ITagComponent`、`ISingletonComponent` 或 `IBufferElement`。
可以按业务继续定义子接口，例如 `ICombatStateComponent : IDataComponent`；但同一个组件最终只能归入一个分类分支。

| 接口 | 语义 | 存储 |
|------|------|------|
| `IDataComponent` | 数据组件 | Chunk SOA 列，可读写 |
| `ITagComponent` | 标记组件 | 仅存于 ComponentMask，无数据列，不占内存 |
| `ISingletonComponent` | 单例组件 | 同数据组件，全局唯一实体 |
| `IBufferElement` | 缓冲区元素 | 实体级动态数组，可携带多个同类型元素 |

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
    // 标记组件，零字段，不占内存
}
```

### 2.2 注册组件

`ComponentTypeId` 是框架内部自动管理的类型 ID。`ComponentMask` 前 256 个组件使用内联位，超过后自动扩展，用户无需手动定义枚举或关心容量。

**方式一：Source Generator 自动注册（推荐）**

框架内置 Source Generator，编译时自动扫描当前程序集里的 `IComponent` 实现并生成注册代码。
注册时会校验组件分类；`ITagComponent` 必须是零字段 struct。

```csharp
// World 创建时自动调用，无需手动干预
```

`World` 创建时会优先调用已加载程序集里的生成注册代码；没有生成注册代码的程序集会走反射扫描兜底。热更新程序集只要在 `World` 创建前加载，就会被自动注册。

**方式二：手动注册**

```csharp
ComponentTypeRegistry.Register<Health>();   // → ComponentTypeId(1)
ComponentTypeRegistry.Register<Position>(); // → ComponentTypeId(2)
```

注册是幂等的，重复注册同一类型不会报错，返回已分配的 ID。

---

## 3. 实体操作

### 3.1 创建与销毁

```csharp
World world = m_Manager.World;

// 创建空实体
Entity entity = world.CreateEntity();

// 创建带初始组件的实体
var mask = new ComponentMask();
mask.Add<Health>();
mask.Add<Position>();
Entity player = world.CreateEntity(mask);

// 检查实体是否存活
if (world.Exists(player)) { ... }

// 立即销毁
world.DestroyEntity(player);

// 延迟销毁（帧末统一处理，遍历中安全）
world.DestroyEntityDeferred(player.Index);
```

### 3.2 组件增删改查

```csharp
// 添加组件
world.AddComponent(entity, new Health { Current = 100, Max = 100 });

// 设置组件值
world.SetComponent(entity, new Health { Current = 80, Max = 100 });

// 获取组件引用（可直接修改）
ref var hp = ref world.GetComponent<Health>(entity);
hp.Current -= 10;

// 检查组件
if (world.HasComponent<Health>(entity)) { ... }

// 移除组件
world.RemoveComponent<DeadTag>(entity);

// 批量结构变化：同一批实体加/删同一个组件时优先使用
world.AddComponentBatch(entities, new Health { Current = 100, Max = 100 });
world.RemoveComponentBatch<DeadTag>(entities);
```

### 3.3 高性能 int 索引访问

在系统遍历中，可直接用 `entityIndex`（Entity.Index）访问，避免 Exists 检查：

```csharp
ref var hp = ref world.GetComponent<Health>(entityIndex);
world.SetComponent<Health>(entityIndex, newHp);

if (world.HasComponent<Health>(entityIndex)) { ... }

bool found = world.TryGetComponent<Health>(entityIndex, out var health);
```

### 3.4 查询实体数量

```csharp
int alive = world.GetAliveCount();
bool alive = world.IsAlive(entityIndex);
Entity entity = world.GetEntity(entityIndex); // index → Entity 句柄
```

---

## 4. 系统（System）

### 4.1 唯一系统基类

业务系统统一继承 `SystemBase`，重写 `OnTick(SystemContext ctx)`。全局逻辑、Chunk 遍历、实体遍历都通过 `SystemContext` 提供的查询 API 表达。

系统通过 `ticker.Register<T>()` 注册到指定 Ticker，由用户决定在 Unity 哪个生命周期驱动。

### 4.2 SystemContext 查询

推荐在 `SystemBase` 中重写 `OnTick(SystemContext ctx)`。`SystemContext` 会复用查询缓存、Chunk 列表和列访问缓存，并自动管理查询遍历 safety guard：

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

### 4.3 列式 Chunk 遍历

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

### 4.4 直接 World 访问

通过 `SystemContext` 可直接访问组件，无需经过 `ctx.World`：

```csharp
public class InputSystem : SystemBase
{
    protected override void OnTick(SystemContext ctx)
    {
        var world = ctx.World;
        var dt = ctx.DeltaTime;
        // ctx.Get<T>(entity) / ctx.Set<T>(entity, value) 等同 ctx.World.GetComponent / SetComponent
    }
}
```

`ctx.Get<T>(entity)`、`ctx.Set<T>(entity, value)`、`ctx.Has<T>(entity)` 是 `ctx.World.GetComponent` 等的零开销快捷方法。

### 4.5 ComponentPack

当一个系统需要把查询结果打包成连续数组做高频计算，再批量写回组件列时，可以使用 `SystemContext.Pack<TItem, TAdapter>()`。Pack 会复用 compiled query、列 accessor、内部 item 数组和 chunk range；每帧 `Build()` / `WriteBack()` 不会重新创建 pack 对象。

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

`Read<T>()` 只能读取 adapter 在 `Describe()` 中声明为 `Read<T>()` 的组件，`Write<T>()` 只能写入声明为 `Write<T>()` 的组件。`Build()` 和 `WriteBack()` 之间不能发生结构变化，否则 `WriteBack()` 会抛出明确异常。

### 4.6 结构变更规则

普通系统不需要声明组件访问模式，也不需要声明是否会执行结构变更。Ticker 默认按注册顺序保守执行系统，保证上一系统的变更对下一系统可见。

唯一需要注意的是：不要在 `SystemContext.QueryChunks(...)`、`EcsAPI.Query(...).AsRows()` 或其他 query 遍历过程中直接创建/销毁实体、添加/移除组件。遍历中需要结构变更时，先记录到列表或 `EntityCommandBuffer`，遍历结束后再执行。

**例外**：`ctx.CreateEntity()` 可以在遍历中安全创建实体，立即返回真实 `Entity`（chunk placement 自动延迟到当前 query iteration 结束时执行）。详见 §4.7。

### 4.7 迭代中创建实体

`ctx.CreateEntity()` 允许在 query 遍历过程中创建实体并立即拿到真实 `Entity` 引用——解决 ECB 临时 ID 无法存入组件数据的问题。实体 index 和 version 立即分配，chunk placement 延迟到 `foreach` 结束时自动执行。

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

                // 在 foreach 体内直接创建，拿到真实 Entity
                var minion = ctx.CreateEntity(minionMask);
                ctx.ECB.AddComponent(minion, new Minion { Owner = chunk.EntityAt(i) });
                squads[i].MinionA = minion; // 真实 ID 立即存入
            }
        }
        // ← foreach 结束，minion 自动进入 chunk，本系统后续查询可见
    }
}
```

约束：当前 `foreach` 体内不能对 pending 实体调用 `GetComponent` / `SetComponent`——实体的初始状态应通过 `ctx.ECB` 设置，ECB 会在系统 Tick 结束后回放。`ctx.CreateEntity()` 也接受可选的 `ComponentMask` 参数指定初始 archetype。

### 4.8 并行 System（JobSystemBase）

继承 `JobSystemBase` 声明读写访问，框架自动推导依赖图。当前阶段依赖图层结构已就位，执行走串行 fallback；Phase 4 将实现 IJobChunk 真并行调度。

```csharp
public class MovementSystem : JobSystemBase
{
    protected override void DeclareAccess(AccessBuilder access)
    {
        // 声明本系统读/写哪些组件，框架据此自动推导依赖图
        access.Read<Velocity>().Write<Position>();
    }

    // Phase 4: 重写 CompileJob 返回 IJobChunk，实现 Chunk 级并行 + Burst
}

// 注册方式和 SystemBase 完全一样
ticker.Register<MovementSystem>();
```

**类层次：**
```
EcsSystem（共享生命周期）
  ├── SystemBase  — OnTick 手写循环（单线程）
  └── JobSystemBase — DeclareAccess + CompileJob（并行）
```

`SystemBase` 未声明访问时自动采用保守策略（与所有系统互斥），确保不加声明就不会引入并发问题。

## 5. 查询（Query）

系统内查询通过 `SystemContext.QueryChunks()` 或 `EcsAPI.Query()` 两种方式：

**构造 EntityQuery**——推荐使用静态工厂 `EntityQuery.With<T>()`：

```csharp
// 推荐（链式 builder，0GC）：
var query = EntityQuery.With<Health, Position>().None<DeadTag>().Build();

// 等价传统写法：
var query = new EntityQuery(
    all: new ComponentMask().With<Health>().With<Position>(),
    none: new ComponentMask().With<DeadTag>());
```

`EntityQueryBuilder` 是 struct，按值传递无分配。

### 5.1 外部查询（QueryBuilder）

在系统外部或自定义逻辑中使用 `EcsAPI.Query()`：

```csharp
World world = m_Manager.World;

// 遍历所有有 Health 的实体
foreach (var row in EcsAPI.Query(world).Read<Health>().AsRows())
{
    ref readonly var hp = ref row.RO<Health>();
    Debug.Log($"Entity {row.Entity}: HP = {hp.Current}");
}

// 读写查询
foreach (var row in EcsAPI.Query(world).Write<Health>().AsRows())
{
    ref var hp = ref row.RW<Health>();
    hp.Current = Mathf.Min(hp.Current + healAmount, hp.Max);
}

// 按 Chunk 遍历（更高效）
foreach (var chunkView in EcsAPI.Query(world).Read<Position>().AsChunks())
{
    var positions = chunkView.Column<Position>();
    for (int i = 0; i < chunkView.Count; i++)
    {
        ref readonly var pos = ref positions[i];
        // ...
    }
}

// 读写查询
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

### 5.2 QueryBuilder 方法说明

| 方法 | 作用 |
|------|------|
| `Read<T>()` | 组件加入 All 掩码 + 读掩码 |
| `Write<T>()` | 组件加入 All 掩码 + 写掩码 |
| `Build()` | 返回 EntityQuery |
| `Compile()` | 返回 CompiledQuery |
| `AsRows()` | 返回行级枚举（foreach） |
| `AsChunks()` | 返回 Chunk 级枚举（foreach） |

---

## 6. Buffer 句柄（BufferHandle）

通过 `BufferHandle` 引用、全局池化管理的动态数组。适合共享数据池（如帧数据缓存、事件队列）。**不绑定实体**，由 `BufferStore<T>` (NativeList) 底层存储，通过句柄访问。

> 如果需要**每个实体独立**的动态数组，见 §7 IBufferElement。

### 6.1 创建与销毁

```csharp
World world = m_Manager.World;

// 创建缓冲区
BufferHandle damageLog = world.CreateBuffer<DamageRecord>(initialCapacity: 16);

// 销毁缓冲区
world.DestroyBuffer<DamageRecord>(damageLog);
```

### 6.2 读写操作

```csharp
// 添加元素
world.AddBufferElement(damageLog, new DamageRecord { Target = entity, Amount = 50 });

// 读取
BufferSpan<DamageRecord> span = world.GetBuffer<DamageRecord>(damageLog);
for (int i = 0; i < span.Length; i++)
{
    ref readonly var record = ref span[i];
    Debug.Log($"Damage: {record.Amount}");
}

// 设置元素
world.SetBufferElement(damageLog, 0, newRecord);

// 删除元素（与最后一个交换）
world.RemoveBufferElementAtSwapBack(damageLog, 0);

// 清空
world.ClearBuffer<DamageRecord>(damageLog);

// 获取长度
int len = world.GetBufferLength<DamageRecord>(damageLog);
```

### 6.3 BufferSpan 直接指针访问

```csharp
BufferSpan<DamageRecord> span = world.GetBuffer<DamageRecord>(damageLog);
unsafe
{
    DamageRecord* ptr = span.UnsafePtr;
    for (int i = 0; i < span.Length; i++)
    {
        // 直接指针操作，零开销
    }
}
```

---

## 7. 实体动态数组（IBufferElement）

`IBufferElement` 允许**每个实体独立携带**多个同类型元素，类似 per-entity 动态数组。适用于技能列表、伤害队列、Buff 叠加等场景。底层使用托管 `List<T>`，通过 Entity 直接访问。

> 注意：`World.AddBufferElement(BufferHandle, ...)` 和 `World.AddBufferElement(Entity, ...)` 是**两个不同的方法**——前者操作 §6 的句柄式 Buffer，后者操作本条目的实体数组。签名不同，不会混淆。

### 定义缓冲区元素

```csharp
public struct DamageRecord : IBufferElement
{
    public Entity Source;
    public float Amount;
}
```

### 使用缓冲区元素

```csharp
var entity = world.CreateEntity();

// 添加元素
world.AddBufferElement(entity, new DamageRecord { Source = attacker, Amount = 50 });
world.AddBufferElement(entity, new DamageRecord { Source = attacker, Amount = 30 });

// 获取访问器
BufferElementAccessor<DamageRecord> buffer = world.GetBufferElement<DamageRecord>(entity);
for (int i = 0; i < buffer.Length; i++)
    Debug.Log(buffer[i].Amount);

// 修改元素
buffer[0] = new DamageRecord { Source = attacker, Amount = 100 };

// 移除元素（与最后一个交换）
world.RemoveBufferElementAt<DamageRecord>(entity, 0);

// 清空
world.ClearBufferElements<DamageRecord>(entity);

// 获取数量
int count = world.GetBufferElementCount<DamageRecord>(entity);
```

缓冲区元素在实体销毁时自动清理。

---

## 8. 单例实体（Singleton）

单例实体用于存储全局共享数据（如游戏状态、配置）。

### 8.1 定义单例组件

```csharp
public struct GameState : ISingletonComponent
{
    public int Score;
    public float TimeRemaining;
}
```

单例组件同类型全局只允许存在一个实体实例。

### 8.2 使用单例

```csharp
World world = m_Manager.World;

// 获取或创建（不存在则自动创建）
Entity stateEntity = world.GetOrCreateSingleton<GameState>();

// 读写
ref var state = ref world.GetComponent<GameState>(stateEntity);
state.Score += 10;

// 仅获取（不存在则抛异常）
Entity e = world.GetSingleton<GameState>();

// 安全获取
if (world.TryGetSingleton<GameState>(out Entity e))
{
    ref var s = ref world.GetComponent<GameState>(e);
}
```

---

## 9. 延迟命令缓冲区（ECB）

ECB 用于在遍历中收集结构变更（创建/销毁实体、添加/移除组件），并在遍历结束后统一回放。

> **重要**：系统内优先使用 `SystemBase.ECB`，框架会在当前系统 Tick 结束后自动回放并释放。只有系统外或需要自定义回放时机时，才手动创建并 `World.Playback(ecb)`。

### 9.1 系统中使用自动 ECB

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

系统 Tick 正常结束后，框架会自动回放 `ECB` 并释放它。系统执行异常时，本帧已记录的自动 ECB 不会回放。

### 9.2 手动 ECB

系统外或需要自定义回放时机时，可以手动创建 ECB：

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

播放失败默认抛出异常，确保结构变更错误不会被静默忽略。

### 9.3 创建临时实体

```csharp
EntityCommandBuffer ecb = world.CreateCommandBuffer();

// 创建临时实体（返回负 Index 的临时句柄）
Entity tempEntity = ecb.CreateEntity(ComponentMask.Empty);
ecb.AddComponent(tempEntity, new Health { Current = 100, Max = 100 });

// 回放时自动映射为真实 Entity
```

---

## 10. 延迟销毁

### 10.1 标记销毁

```csharp
// 在遍历中标记（不立即销毁）
world.DestroyEntityDeferred(entityIndex);
```

### 10.2 检查待销毁

```csharp
// 在系统遍历中，框架自动跳过待销毁实体
// 手动检查：
if (world.CachedNeedRemoveIndices.Contains(entityIndex))
{
    // 该实体已标记为待销毁
}
```

### 10.3 批量处理

```csharp
world.TryGetDeferredDestroyEntities(out BufferSpan<int> entities);
for (int i = 0; i < entities.Length; i++)
{
    int entityId = entities[i];
    // 处理待销毁实体
}

// 清空标记（帧末调用）
world.ClearDeferredDestroyEntities();
```

---

## 11. 系统组合（SystemGroup）

### 11.1 定义系统组合

`SystemGroup` 用于将多个系统注册到同一个 Ticker，方便批量注册。组合可以嵌套。

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

### 11.2 嵌套组合

```csharp
public class GameplayGroup : SystemGroup
{
    public override void Configure(SystemTicker ticker)
    {
        ticker.Register<MovementGroup>();   // 嵌套子组合
        ticker.Register<CombatGroup>();     // 嵌套子组合
        ticker.Register<AudioSystem>();     // 单个系统
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

### 11.3 注册与执行顺序

```csharp
var manager = new ECSManager();
int updateIdx = manager.CreateTicker();
var updateTicker = manager.GetTicker(updateIdx);
updateTicker.Register<GameplayGroup>();   // 展开为：PhysicsSystem → MovementSystem → DamageSystem → HealthSystem → AudioSystem
manager.Start();
```

注册顺序即执行顺序。通过 `SystemGroup` 组合可以清晰地组织系统层级，同时保持精确的执行顺序控制。

---

## 12. 性能与诊断

### 12.1 热路径建议

优先在系统中使用 `Chunk.GetColumn<T>()` 做列式读写。`World.GetComponent<T>(Entity)` 和 `World.GetComponent<T>(int)` 适合少量随机访问；对同一查询结果批量处理时，不要在内层循环反复走 `World.GetComponent`。

结构变化会触发 archetype 迁移、共享组件复制、旧 chunk swap-back 和实体记录更新。对同一批实体添加或移除同一个组件时，优先使用：

```csharp
world.AddComponentBatch(entities, value);
world.RemoveComponentBatch<MyComponent>(entities);
```

`ITagComponent` 没有数据列，但 add/remove tag 仍然是结构变化。高频状态切换不要用频繁添加/移除 tag 表达，优先使用普通数据组件中的 `bool` 或 `enum` 字段。

### 12.2 ComponentMask API

`ComponentMask` 是可变 struct。`With<T>()` / `Without<T>()` **修改当前变量并返回它**——这是性能优化（避免 struct 拷贝），但意味着它们不纯函数：对已有变量调 `With<T>()` 会改变它。适合链式构造新 mask：

> ⚠️ `With<T>()` / `Without<T>()` 就地修改。从已有 mask 派生新 mask 请用 `WithAdded<T>()` / `WithRemoved<T>()`（复制后修改）。

```csharp
var queryMask = new ComponentMask()
    .With<Health>()
    .With<Position>();
```

如果需要从已有 mask 派生新 mask，不要直接对原变量调用 `With<T>()`。使用不可污染原变量的 API：

```csharp
var baseMask = new ComponentMask().With<Health>();
var energyMask = baseMask.WithAdded<Energy>();
var withoutEnergy = energyMask.WithRemoved<Energy>();
```

需要明确原地修改时，使用：

```csharp
baseMask.Add<Position>();
baseMask.Remove<DeadTag>();
```

### 12.3 ProfilerMarker

框架内置 Unity Profiler marker，但默认生产包不编译 marker 代码，避免空跑分析逻辑。需要分析时用 MSBuild 属性启用：

```bash
dotnet build -c Release /p:EmberEnableProfiling=true
```

启用后 marker 名称保持稳定，便于 Unity Profiler 过滤：

- `Ember.World.GetChunks`
- `Ember.QueryCache.Rebuild`
- `Ember.World.MigrateEntity`
- `Ember.Chunk.MoveEntityTo`
- `Ember.Chunk.RemoveAtSwapBack`
- `Ember.World.AddComponent`
- `Ember.World.RemoveComponent`
- `Ember.SystemTicker.Tick`

### 12.4 微基准

仓库包含框架微基准工程：

```bash
dotnet build tests/Performance/Ember.Performance.csproj -c Release
```

基准覆盖组件读写、chunk 列访问、query cache、AddComponent/RemoveComponent、tag add/remove、批量结构变化、ComponentPack 和 GC allocation。基准使用真实 `World` / `Chunk` / `Unity.Collections.NativeArray` 路径，实际跑分需要 Unity 兼容运行时；普通 .NET CLI 可编译工程，但不能执行 Unity native ECall-backed `NativeArray` 分配。cached query、chunk column 读写与 warmed ComponentPack build/write 应保持 0 GC。

---

## 13. 架构概览

### 13.1 存储模型

```
ECSManager (系统管理)
 ├── CreateTicker()             创建 Ticker，返回索引
 ├── GetTicker(index)           获取 Ticker 实例
 ├── Start()                    创建 World，初始化所有 Ticker
 ├── Tick(index, dt)            驱动指定 Ticker（整数索引，零开销）
 ├── Dispose()                  清理 Ticker + World
 └── World                      ECS 数据容器
      ├── EntityRecord[]         实体 → Archetype/Chunk 映射
      ├── Archetype[]            组件组合模板
      │    ├── ArchetypeLayout   类型 → 列索引/偏移量 O(1) 查找
      │    └── Chunk[]           128 实体/块，SOA 布局
      │         └── NativeArray<byte>   连续内存，按组件类型分区（Tag 无数据列）
      ├── QueryCache             查询缓存（按 EntityQueryKey 字典查找，Archetype 版本号失效）
      ├── BufferStore<T>         动态缓冲区池
      ├── BufferElement          实体级实体动态数组（IBufferElement）
      └── Singleton              单例实体映射

SystemTicker (系统调度)
 ├── Register<T>()             注册系统/组合
 ├── Init()                     OnCreate 调用
 ├── Tick(world, dt)            按注册顺序保守执行：
 │    ├── 调用 OnTick
 │    └── 成功结束后自动回放系统 ECB
 └── Dispose()                  OnDestroy + 清理
```

### 13.2 组件类型语义

| 类型 | 存储 | 查询 | 数据访问 |
|------|------|------|---------|
| `IDataComponent` | Chunk SOA 列 | ✓ | `GetComponent<T>` / `GetColumn<T>` |
| `ITagComponent` | 仅 ComponentMask | ✓ | ✗（抛异常） |
| `ISingletonComponent` | 同数据组件 | ✓ | 全局唯一实体 |
| `IBufferElement` | 实体级动态数组 | ✗ | `GetBufferElement<T>` |

### 13.3 系统调度

```
ECSManager.CreateTicker()
  → 创建 SystemTicker 实例，返回整数索引

ticker.Register<T>()
  → SystemGroup 子类展开其 Configure(this)
  → SystemBase 子类加入待注册列表

ECSManager.Start()
  → 创建 World 实例
  → 调用每个 Ticker.Init() → OnCreate()

ECSManager.Tick(index, dt)
  → ticker.Tick(world, dt)
  → 逐系统执行：
     1. 进入系统安全上下文
     2. 调用 OnTick
     3. 系统成功结束后自动回放 ECB
     4. 退出系统安全上下文

结构变更只在查询遍历过程中被禁止；需要在遍历中修改结构时，先记录到系统 `ECB` 或临时列表，遍历结束后再执行。

ECSManager.Dispose()
  → Ticker.Dispose() → OnDestroy()
  → World.Dispose() → 清理 ECS 数据
```

### 13.4 关键类型关系

```
IComponent
  ↓ 分类为
IDataComponent / ITagComponent / ISingletonComponent / IBufferElement
  ↓ 注册到
ComponentTypeRegistry (Type ↔ ComponentTypeId + Size/Alignment + Kind)
  ↓ 引用
ComponentMask (inline 256-bit + dynamic extra words) + EntityQuery (All/Any/None)
  ↓ 匹配
Archetype.Layout (O(1) 查找) → Chunk[] → ChunkColumn<T>
  ↓ 遍历
SystemBase / SystemContext
```

---

## 14. MCP Server

MCP Server 让 AI 编码助手（Claude Code、Codex 等）通过标准 [Model Context Protocol](https://modelcontextprotocol.io) 直接操作运行中的 ECS World——查询实体、检查 Archetype、修改组件，全部由 AI 自动完成，无需手动编写调试脚本。

### 14.1 架构概览

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

- **EmberBridge**：Unity Editor 内的 TCP 服务器，在 9090-9099 范围内自动扫描可用端口，将请求分派到主线程执行，结果通过 TCP 返回
- **MCP Server**（`Ember.Mcp.Server.dll`）：独立的 .NET 控制台应用，作为 AI 客户端和 Unity 之间的标准 MCP 协议适配层。启动时从 `~/.ember/instance.json` 读取端口号，通过 stdio 与 AI 客户端通信
- **安全模型**：默认只读——AI 可以查询 World 但不能修改。需要写操作时，必须显式添加 `--allow-write` 参数启动 MCP Server

### 14.2 安装与配置

#### 14.2.1 Unity 侧（EmberBridge）

打开 Ember MCP 窗口：**`Window > Ember > MCP`**

窗口顶部状态栏显示当前状态：

- `● Connected`（绿色）— MCP Server 已连接，可以处理请求
- `◉ Waiting`（黄色）— Bridge 已启动，等待客户端连接
- `○ Stopped`（灰色）— Bridge 未运行

点击 **`Start`** 启动 TCP 监听，点击 **`Stop`** 关闭。如果你希望每次打开 Unity 项目时自动启动，在 **`Settings`** 折叠区勾选 `AutoStart`。

> Bridge 仅在 Unity Editor 中运行。读操作无需 Play Mode，但写操作（创建/销毁实体、增删组件）必须在 Play Mode 下执行。

#### 14.2.2 AI 客户端配置

在 **`Client Setup`** 折叠区，可以一键安装/卸载 AI 客户端的 MCP 配置：

- **Claude Code** → `.mcp.json`（项目根目录）
- **Codex** → `.codex/config.toml`

点击 `Install` 后，窗口会自动生成指向 `Tools~/Ember.Mcp.Server.dll` 的配置。包更新后（Tools~ 路径中的 git hash 变化），窗口打开时也会自动修复过期的配置路径。

也可以手动编辑配置文件。以 Claude Code 为例：

```json
{
  "mcpServers": {
    "ember": {
      "command": "dotnet",
      "args": ["exec", "Assets/Packages/com.ember.ecs/Tools~/Ember.Mcp.Server.dll", "--allow-write"]
    }
  }
}
```

**启动参数：**

| 参数 | 说明 |
|------|------|
| `--allow-write` | 启用写工具（默认只读） |
| `--port <n>` | 手动指定端口（通常不需要，MCP Server 会从 `instance.json` 自动读取） |

### 14.3 Command Reference (26 commands via `ember_execute`)

The `ember_execute` tool accepts a `commands` array. Each command has an `op` field.

**Read (10):** world_info, query_entities, get_entity, get_archetypes, get_systems, get_component_types, has_component, get_singleton, get_buffer_elements, entity_counts
**Write (6):** create_entity, destroy_entity, add_component, remove_component, set_component, set_singleton
**Batch (2):** add_component_batch, remove_component_batch
**Buffer (4):** add_buffer_element, remove_buffer_element, clear_buffer_elements, set_buffer_element
**Manager (3):** mcp_status, get_ecs_status, advance_frame
**Aux (1):** resolve_component

Example: `{"op": "query_entities", "components": ["Position"], "limit": 10}`

### 14.4 使用示例

以下是 AI 客户端中一次典型的交互流程：

> **用户**: 查询所有有 Health 组件的实体，看看谁血量低  
> **AI** 调用 `ember_query_entities(components=["Health"])`  
> → 返回 3 个实体，实体 #1 的 Health.Current = 80，实体 #2 的 Health.Current = 5  
>
> **用户**: 实体 #2 快死了，看看它的详细信息  
> **AI** 调用 `ember_get_entity(entityIndex=2)`  
> → 返回 Entity #2 的全部组件：Health { Current: 5, Max: 100 }，Position { X: 10, Y: 2, Z: 0 }，DeadTag（标记）  
>
> **用户**: 给我在它旁边（X+3）创建一个新实体，带相同的组件  
> **AI** 调用 `ember_create_entity(components=["Health","Position"])` → 拿到新 entityIndex=15  
> **AI** 调用 `ember_set_component(15, "Health", {"Current":100,"Max":100})`  
> **AI** 调用 `ember_set_component(15, "Position", {"X":13,"Y":2,"Z":0})`  
> → 实体 #15 创建完成

### 14.5 调试与故障排查

#### 常见问题

| 问题 | 原因 | 解决 |
|------|------|------|
| "Not connected to Unity" | Bridge 未启动或 Unity 未运行 | `Window > Ember > MCP` → Start |
| "Write operations are disabled" | 未启用 `--allow-write` | 在配置中添加 `--allow-write` 后重启客户端 |
| "Write operations require Play Mode" | 写操作必须在 Play Mode 执行 | 进入 Play Mode |
| 客户端启动后卡住 / 无响应 | 端口冲突 | Settings 中调整端口范围，或手动 `--port` |
| 包更新后配置失效 | Tools~ 路径中的 hash 变化 | 窗口 `AutoUpdateConfigPaths()` 自动修复，重启 `Ember MCP` 窗口即可 |

#### 日志诊断

- **Unity Console**：过滤 `[Ember MCP]` 前缀，可查看 Bridge 启动状态、客户端连接/断开、请求方法名和截断的 JSON 内容
- **Ember MCP 窗口 → Request Log**：实时显示最近 100 条请求的方法名、响应耗时（ms）和结果预览
