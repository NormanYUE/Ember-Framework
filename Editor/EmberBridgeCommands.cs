using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;

namespace Ember.Editor
{
    /// <summary>
    /// 命令执行器 — 在主线程执行 Ember MCP 命令并返回 JSON 结果。
    /// </summary>
    internal static class EmberBridgeCommands
    {
        private static readonly JsonSerializerOptions s_JsonOpts = new() { PropertyNameCaseInsensitive = true };

        public static string Execute(string method, JsonElement? args)
        {
            var manager = ECSManager.Active;
            if (manager?.World == null)
                throw new InvalidOperationException("No active ECSManager. Set ECSManager.Active in your game code.");

            if (IsWriteMethod(method) && !EditorApplication.isPlaying)
                throw new InvalidOperationException("Write operations require Play Mode.");

            return method switch
            {
                "ember_world_info" => HandleWorldInfo(manager),
                "ember_query_entities" => HandleQueryEntities(manager, args),
                "ember_get_entity" => HandleGetEntity(manager, args),
                "ember_get_archetypes" => HandleGetArchetypes(manager),
                "ember_get_systems" => HandleGetSystems(manager, args),
                "ember_get_component_types" => HandleGetComponentTypes(),
                "ember_create_entity" => HandleCreateEntity(manager, args),
                "ember_destroy_entity" => HandleDestroyEntity(manager, args),
                "ember_add_component" => HandleAddComponent(manager, args),
                "ember_remove_component" => HandleRemoveComponent(manager, args),
                "ember_set_component" => HandleSetComponent(manager, args),
                _ => throw new InvalidOperationException($"Unknown method: {method}")
            };
        }

        // ── Read handlers ──

        private static string HandleWorldInfo(ECSManager manager)
        {
            var view = manager.World.Debug;
            return JsonSerializer.Serialize(new
            {
                entityCapacity = view.EntityCapacity,
                aliveEntityCount = view.AliveEntityCount,
                archetypeCount = view.ArchetypeCount,
                chunkCount = view.ChunkCount,
                totalEntityCapacity = view.TotalEntityCapacity,
                globalFillRate = view.GlobalFillRate
            });
        }

        private static string HandleQueryEntities(ECSManager manager, JsonElement? args)
        {
            var view = manager.World.Debug;
            if (view.AliveEntities == null)
                return JsonSerializer.Serialize(new { entities = Array.Empty<object>(), count = 0 });

            string[] filter = null;
            int offset = 0;
            int limit = 100;

            if (args.HasValue)
            {
                var obj = args.Value;
                if (obj.TryGetProperty("components", out var comps))
                {
                    filter = new string[comps.GetArrayLength()];
                    for (int i = 0; i < filter.Length; i++)
                        filter[i] = comps[i].GetString();
                }

                if (obj.TryGetProperty("offset", out var off)) offset = off.GetInt32();
                if (obj.TryGetProperty("limit", out var lim)) limit = Math.Min(lim.GetInt32(), 500);
            }

            var results = new List<object>();
            int count = 0;
            foreach (var entity in view.AliveEntities)
            {
                if (MatchesFilter(entity, filter))
                {
                    if (count >= offset && results.Count < limit)
                    {
                        results.Add(new
                        {
                            index = entity.Index,
                            version = entity.Version,
                            placed = entity.Placed,
                            archetype = entity.ArchetypeIndex,
                            components = entity.ComponentNames
                        });
                    }

                    count++;
                }
            }

            return JsonSerializer.Serialize(new { entities = results, count, offset, limit });
        }

        private static bool MatchesFilter(EntityDebugView entity, string[] filter)
        {
            if (filter == null || filter.Length == 0) return true;
            foreach (var f in filter)
            {
                bool found = false;
                foreach (var c in entity.ComponentNames)
                    if (c.Equals(f, StringComparison.OrdinalIgnoreCase))
                    {
                        found = true;
                        break;
                    }

                if (!found) return false;
            }

            return true;
        }

        private static string HandleGetEntity(ECSManager manager, JsonElement? args)
        {
            int entityIndex = args?.GetProperty("entityIndex").GetInt32() ?? -1;
            if (entityIndex < 0)
                throw new ArgumentException("entityIndex is required");

            var entity = new Entity(entityIndex,
                manager.World.Debug.AliveEntities != null
                    ? GetVersion(manager, entityIndex)
                    : 1);

            if (!manager.World.Exists(entity))
                return JsonSerializer.Serialize(new { error = "Entity not found or not alive" });

            var typeIds = manager.World.GetEntityComponentTypes(entity);
            var components = new List<object>();

            foreach (var typeId in typeIds)
            {
                var info = ComponentTypeRegistry.GetInfo(typeId);
                var comp = new Dictionary<string, object>
                {
                    ["type"] = info.Type.Name,
                    ["kind"] = info.Kind.ToString()
                };

                if (info.Kind != ComponentKind.Tag)
                {
                    try
                    {
                        var boxed = manager.World.GetComponentBoxed(entity, typeId);
                        if (boxed != null)
                        {
                            var fields = info.Type.GetFields(BindingFlags.Instance |
                                                                    BindingFlags.Public | BindingFlags.NonPublic);
                            foreach (var f in fields)
                                comp[f.Name] = f.GetValue(boxed);
                        }
                    }
                    catch { /* skip if can't read */ }
                }

                components.Add(comp);
            }

            return JsonSerializer.Serialize(new { entityIndex, components });
        }

        private static int GetVersion(ECSManager manager, int entityIndex)
        {
            foreach (var e in manager.World.Debug.AliveEntities)
                if (e.Index == entityIndex)
                    return e.Version;
            return 1;
        }

        private static string HandleGetArchetypes(ECSManager manager)
        {
            var view = manager.World.Debug;
            if (view.Archetypes == null)
                return JsonSerializer.Serialize(new { archetypes = Array.Empty<object>() });

            var list = new List<object>();
            foreach (var arch in view.Archetypes)
            {
                list.Add(new
                {
                    components = arch.ComponentNames,
                    typeCount = arch.ComponentTypeCount,
                    chunkCount = arch.ChunkCount,
                    totalEntities = arch.TotalEntities,
                    totalCapacity = arch.TotalCapacity,
                    fillRate = arch.FillRate
                });
            }

            return JsonSerializer.Serialize(new { archetypes = list });
        }

        private static string HandleGetSystems(ECSManager manager, JsonElement? args)
        {
            int tickerIndex = -1;
            if (args.HasValue && args.Value.TryGetProperty("tickerIndex", out var ti))
                tickerIndex = ti.GetInt32();

            var views = manager.GetDebugViews();
            var tickers = new List<object>();

            for (int i = 0; i < views.Length; i++)
            {
                if (tickerIndex >= 0 && i != tickerIndex) continue;

                var t = views[i];
                var systems = new List<object>();
                foreach (var s in t.Systems)
                {
                    systems.Add(new
                    {
                        name = s.Name,
                        baseType = s.BaseType,
                        hooks = new
                        {
                            entityCreated = s.HasEntityCreatedHook,
                            entityDestroyed = s.HasEntityDestroyedHook,
                            componentAdded = s.HasComponentAddedHook,
                            componentRemoved = s.HasComponentRemovedHook
                        }
                    });
                }

                tickers.Add(new { index = t.Index, systems });
            }

            return JsonSerializer.Serialize(new { tickers });
        }

        private static string HandleGetComponentTypes()
        {
            var types = new List<object>();
            for (int i = 0; i < ComponentTypeRegistry.Count; i++)
            {
                var info = ComponentTypeRegistry.GetInfo(new ComponentTypeId(i));
                if (info.Type == null) continue;
                types.Add(new
                {
                    id = info.TypeId.Value,
                    name = info.Type.Name,
                    kind = info.Kind.ToString(),
                    size = info.Size,
                    alignment = info.Alignment
                });
            }

            return JsonSerializer.Serialize(new { types });
        }

        // ── Write handlers ──

        private static string HandleCreateEntity(ECSManager manager, JsonElement? args)
        {
            var compsArg = args?.GetProperty("components");
            if (compsArg == null || compsArg.Value.ValueKind != JsonValueKind.Array)
                throw new ArgumentException("components array is required");

            var mask = new ComponentMask();
            foreach (var comp in compsArg.Value.EnumerateArray())
            {
                string typeName = comp.GetString();
                var type = FindComponentType(typeName);
                if (type == null)
                    throw new ArgumentException($"Unknown component type: {typeName}");
                mask.AddRaw(type);
            }

            var entity = manager.World.CreateEntity(mask);
            return JsonSerializer.Serialize(new { index = entity.Index, version = entity.Version });
        }

        private static string HandleDestroyEntity(ECSManager manager, JsonElement? args)
        {
            int entityIndex = args?.GetProperty("entityIndex").GetInt32() ?? -1;
            if (entityIndex < 0)
                throw new ArgumentException("entityIndex is required");

            var entity = new Entity(entityIndex, GetVersion(manager, entityIndex));
            manager.World.DestroyEntity(entity);
            return JsonSerializer.Serialize(new { destroyed = true, entityIndex });
        }

        private static string HandleAddComponent(ECSManager manager, JsonElement? args)
        {
            int entityIndex = args?.GetProperty("entityIndex").GetInt32() ?? -1;
            string typeName = args?.GetProperty("componentType").GetString();

            var type = FindComponentType(typeName);
            if (type == null)
                throw new ArgumentException($"Unknown component type: {typeName}");

            var entity = new Entity(entityIndex, GetVersion(manager, entityIndex));

            // Use reflection to call AddComponent<T>
            var method = typeof(World).GetMethod("AddComponent",
                BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(Entity) }, null);
            var genericMethod = method.MakeGenericMethod(type);
            genericMethod.Invoke(manager.World, new object[] { entity });

            return JsonSerializer.Serialize(new { added = true, entityIndex, component = typeName });
        }

        private static string HandleRemoveComponent(ECSManager manager, JsonElement? args)
        {
            int entityIndex = args?.GetProperty("entityIndex").GetInt32() ?? -1;
            string typeName = args?.GetProperty("componentType").GetString();

            var type = FindComponentType(typeName);
            if (type == null)
                throw new ArgumentException($"Unknown component type: {typeName}");

            var entity = new Entity(entityIndex, GetVersion(manager, entityIndex));

            var method = typeof(World).GetMethod("RemoveComponent",
                BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(Entity) }, null);
            var genericMethod = method.MakeGenericMethod(type);
            genericMethod.Invoke(manager.World, new object[] { entity });

            return JsonSerializer.Serialize(new { removed = true, entityIndex, component = typeName });
        }

        private static string HandleSetComponent(ECSManager manager, JsonElement? args)
        {
            int entityIndex = args?.GetProperty("entityIndex").GetInt32() ?? -1;
            string typeName = args?.GetProperty("componentType").GetString();

            var type = FindComponentType(typeName);
            if (type == null)
                throw new ArgumentException($"Unknown component type: {typeName}");

            var entity = new Entity(entityIndex, GetVersion(manager, entityIndex));
            var typeId = ComponentTypeRegistry.GetInfo(type).TypeId;

            // Get current value as boxed object
            var boxed = manager.World.GetComponentBoxed(entity, typeId);
            if (boxed == null)
                throw new InvalidOperationException($"Cannot get component {typeName} (tag component?)");

            // Apply field values from args
            if (args.HasValue && args.Value.TryGetProperty("fieldValues", out var fields))
            {
                foreach (var field in fields.EnumerateObject())
                {
                    var fi = type.GetField(field.Name,
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (fi != null)
                    {
                        object value = ConvertJsonElement(field.Value, fi.FieldType);
                        fi.SetValue(boxed, value);
                    }
                }
            }

            // Write back: use SetComponent<T> via reflection
            var setMethod = typeof(World).GetMethod("SetComponent",
                BindingFlags.Public | BindingFlags.Instance, null,
                new[] { typeof(Entity), type }, null);
            setMethod.MakeGenericMethod(type).Invoke(manager.World,
                new[] { entity, boxed });

            return JsonSerializer.Serialize(new { set = true, entityIndex, component = typeName });
        }

        // ── Helpers ──

        private static Type FindComponentType(string name)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                foreach (var t in asm.GetTypes())
                {
                    if ((t.Name == name || t.FullName == name) &&
                        typeof(IComponent).IsAssignableFrom(t) &&
                        t.IsValueType)
                        return t;
                }
            }

            return null;
        }

        private static void AddRaw(this ComponentMask mask, Type type)
        {
            // Use ComponentMask's internal Add method via reflection
            // Actually, ComponentMask has With<T>() which calls the internal Add
            var method = typeof(ComponentMask).GetMethod("Add",
                BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(Type) }, null);
            if (method != null)
            {
                method.Invoke(mask, new object[] { type });
            }
            else
            {
                // Fallback: use public With method via reflection
                var withMethod = typeof(ComponentMask).GetMethod("With",
                    BindingFlags.Public | BindingFlags.Instance)
                    ?.MakeGenericMethod(type);
                withMethod?.Invoke(mask, null);
            }
        }

        private static bool IsWriteMethod(string method) => method switch
        {
            "ember_create_entity" => true,
            "ember_destroy_entity" => true,
            "ember_add_component" => true,
            "ember_remove_component" => true,
            "ember_set_component" => true,
            _ => false
        };

        private static object ConvertJsonElement(JsonElement element, Type targetType)
        {
            if (targetType == typeof(int)) return element.GetInt32();
            if (targetType == typeof(float)) return (float)element.GetDouble();
            if (targetType == typeof(double)) return element.GetDouble();
            if (targetType == typeof(bool)) return element.GetBoolean();
            if (targetType == typeof(string)) return element.GetString();
            if (targetType == typeof(long)) return element.GetInt64();
            return JsonSerializer.Deserialize(element.GetRawText(), targetType, s_JsonOpts);
        }
    }
}
