# Ember 使用手册

## 目录

1. [快速开始](#1-快速开始)
2. [组件定义与注册](#2-组件定义与注册)
3. [实体操作](#3-实体操作)
4. [系统（System）](#4-系统system)
5. [查询（Query）](#5-查询query)
6. [Aspect](#6-aspect)
7. [动态缓冲区（Buffer）](#7-动态缓冲区buffer)
7.5. [缓冲区元素（IBufferElement）](#75-缓冲区元素ibufferelement)
8. [单例实体（Singleton）](#8-单例实体singleton)
9. [延迟命令缓冲区（ECB）](#9-延迟命令缓冲区ecb)
10. [延迟销毁](#10-延迟销毁)
11. [系统组合（SystemGroup）](#11-系统组合systemgroup)
12. [性能与诊断](#12-性能与诊断)
13. [架构概览](#13-架构概览)

---

## 1. 快速开始

### 1.1 创建 ECSManager 并注册系统

```csharp
using Ember;

var manager = new ECSManager();

// 创建 Ticker（返回整数索引，热路径零开销）
int fixedIdx = manager.CreateTicker();
int updateIdx = manager.CreateTicker();
int lateIdx = manager.CreateTicker();

// 注册系统到指定 Ticker
manager.GetTicker(fixedIdx).Register<MovementSystem>();
manager.GetTicker(updateIdx).Register<DamageSystem>();
manager.GetTicker(lateIdx).Register<CameraSystem>();
manager.GetTicker(updateIdx).Register<SpawnSystem>(runOnce: true);  // 只执行一次

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

    public void Dispose() { } // 无需释放资源时留空
}

public struct Position : IDataComponent
{
    public float X;
    public float Y;
    public float Z;

    public void Dispose() { }
}

public struct DeadTag : ITagComponent
{
    public void Dispose() { } // 标记组件，零字段
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

业务系统统一继承 `SystemBase`。框架不再区分 `TickSystem`、`ChunkSystem`、`EntitySystem`；全局逻辑、Chunk 遍历、实体遍历都通过 `OnTick` 和 `SystemContext` 组合表达。

系统通过 `ticker.Register<T>()` 注册到指定 Ticker，由用户决定在 Unity 哪个生命周期驱动。
`runOnce: true` 可让系统只执行一次后自动移除（替代原 InitSystem）。

### 4.2 SystemContext 查询

推荐在 `SystemBase` 中重写 `OnTick(SystemContext ctx)`。`SystemContext` 会复用查询缓存、Chunk 列表和列访问缓存，并自动管理查询遍历 safety guard：

```csharp
using Ember;

public class DamageSystem : SystemBase
{
    protected override void OnTick(SystemContext ctx)
    {
        var query = new EntityQuery(
            all: new ComponentMask().With<Health>(),
            none: new ComponentMask().With<DeadTag>());

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

仍可重写旧入口 `OnTick(World world, float deltaTime)` 处理全局逻辑、事件处理或自定义遍历：

```csharp
public class InputSystem : SystemBase
{
    public override void OnTick(World world, float deltaTime)
    {
        // 自定义逻辑
    }
}
```

### 4.5 结构变更规则

普通系统不需要声明组件访问模式，也不需要声明是否会执行结构变更。Ticker 默认按注册顺序保守执行系统，保证上一系统的变更对下一系统可见。

唯一需要注意的是：不要在 `SystemContext.QueryChunks(...)`、`EcsAPI.Query(...).AsRows()` 或其他 query 遍历过程中直接创建/销毁实体、添加/移除组件。遍历中需要结构变更时，先记录到列表或 `EntityCommandBuffer`，遍历结束后再执行。

### 4.6 初始化系统（runOnce）

通过 `runOnce: true` 注册，系统只执行一次后自动移除：

```csharp
public class SpawnPlayerSystem : SystemBase
{
    public override void OnTick(World world, float deltaTime)
    {
        var player = world.CreateEntity();
        world.AddComponent(player, new Health { Current = 100, Max = 100 });
        world.AddComponent(player, new Position());
    }
}

// 注册时标记 runOnce
updateTicker.Register<SpawnPlayerSystem>(runOnce: true);
```
## 5. 查询（Query）

### 5.1 SystemBase 内的查询

通过 `CreateQuery()` 声明，框架自动管理 Chunk 列表：
```c#
protected override EntityQuery CreateQuery()
{
    return new EntityQuery(
        all: new ComponentMask()
            .With<Health>()
            .With<Position>(),
        any: default, // 可选：至少有一个
        none: new ComponentMask().With<DeadTag>() // 排除
    );
}
```

### 5.2 外部查询（QueryBuilder）

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

// 复合查询
foreach (var row in EcsAPI.Query(world)
    .Read<Position>()
    .Write<Health>()
    .None<DeadTag>()
    .AsRows())
{
    // ...
}
```

### 5.3 QueryBuilder 方法说明

| 方法 | 作用 |
|------|------|
| `Read<T>()` | 组件加入 All 掩码 + 读掩码 |
| `Write<T>()` | 组件加入 All 掩码 + 写掩码 |
| `Any<T>()` | 组件加入 Any 掩码 |
| `None<T>()` | 组件加入 None 掩码（排除） |
| `Build()` | 返回 EntityQuery |
| `AsRows()` | 返回行级枚举（foreach） |
| `AsChunks()` | 返回 Chunk 级枚举（foreach） |

---

## 6. Aspect

Aspect 将多个组件组合为一个结构体，简化查询访问。

### 6.1 定义 Aspect

```csharp
public struct MovementAspect : IAspect
{
    public Entity Entity;
    public ref Position Pos;
    public ref Velocity Vel;

    public MovementAspect(Chunk chunk, int indexInChunk)
    {
        Entity = chunk.GetEntity(indexInChunk);
        Pos = ref chunk.GetComponent<Position>(indexInChunk);
        Vel = ref chunk.GetComponent<Velocity>(indexInChunk);
    }
}
```

### 6.2 注册 Aspect

在系统初始化时注册：

```csharp
var query = new EntityQuery(
    all: new ComponentMask()
        .With<Position>()
        .With<Velocity>()
);
AspectRegistry<MovementAspect>.Register(query, (chunk, index) => new MovementAspect(chunk, index));
```

### 6.3 使用 Aspect 查询

```csharp
foreach (var aspect in EcsAPI.Query<MovementAspect>(world).AsAspects())
{
    aspect.Pos.X += aspect.Vel.X * deltaTime;
    aspect.Pos.Y += aspect.Vel.Y * deltaTime;
}

// 带排除条件
foreach (var aspect in EcsAPI.Query<MovementAspect>(world).None<DeadTag>().AsAspects())
{
    // ...
}
```

---

## 7. 动态缓冲区（Buffer）

Buffer 是变长数组，用于存储不确定数量的数据（如技能列表、伤害队列）。

### 7.1 创建与销毁

```csharp
World world = m_Manager.World;

// 创建缓冲区
BufferHandle damageLog = world.CreateBuffer<DamageRecord>(initialCapacity: 16);

// 销毁缓冲区
world.DestroyBuffer<DamageRecord>(damageLog);
```

### 7.2 读写操作

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

### 7.3 BufferSpan 直接指针访问

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

## 7.5 缓冲区元素（IBufferElement）

> **设计说明**：`IBufferElement` 是在六阶段路线图之外的有意扩展。它提供了实体级动态数组存储能力，适用于技能列表、伤害队列、Buff 叠加等需要可变数量同类型数据的场景。该扩展已通过独立设计评审，作为框架标准功能保留。

`IBufferElement` 允许实体携带多个同类型的组件元素，类似动态数组。

### 定义缓冲区元素

```csharp
public struct DamageRecord : IBufferElement
{
    public Entity Source;
    public float Amount;
    public void Dispose() { }
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
    public void Dispose() { }
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
        var query = new EntityQuery(
            all: new ComponentMask().With<Health>(),
            none: new ComponentMask().With<DeadTag>());

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

`ComponentMask` 是可变 struct。`With<T>()` / `Without<T>()` 会修改当前变量并返回它，适合链式构造：

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

基准覆盖组件读写、chunk 列访问、query cache、AddComponent/RemoveComponent、tag add/remove、批量结构变化和 GC allocation。基准使用真实 `World` / `Chunk` / `Unity.Collections.NativeArray` 路径，实际跑分需要 Unity 兼容运行时；普通 .NET CLI 可编译工程，但不能执行 Unity native ECall-backed `NativeArray` 分配。cached query 与 chunk column 读写应保持 0 GC。

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
      ├── BufferElement          实体级缓冲区元素（IBufferElement）
      └── Singleton              单例实体映射

SystemTicker (系统调度)
 ├── Register<T>(runOnce)       注册系统/组合
 ├── Init()                     OnCreate 调用
 ├── Tick(world, dt)            按注册顺序保守执行：
 │    ├── 调用 OnTick
 │    ├── 成功结束后自动回放系统 ECB
 │    └── runOnce 系统自动移除
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
     5. runOnce 系统标记移除

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
