using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace Ember.Editor
{
    /// <summary>
    /// 命令执行器 — 在主线程执行 Ember MCP 命令并返回 JSON 结果。
    /// </summary>
    internal static class EmberBridgeCommands
    {
        public static string Execute(string method, string argsJson)
        {
            var manager = ECSManager.Active;

            if (IsWriteMethod(method) && !UnityEditor.EditorApplication.isPlaying)
                return BuildError("Write operations require Play Mode.");

            if (manager?.World == null)
                return BuildError("No active ECSManager. Enter Play Mode or set ECSManager.Active.");

            return method switch
            {
                "ember_world_info" => HandleWorldInfo(manager),
                "ember_query_entities" => HandleQueryEntities(manager, argsJson),
                "ember_get_entity" => HandleGetEntity(manager, argsJson),
                "ember_get_archetypes" => HandleGetArchetypes(manager),
                "ember_get_systems" => HandleGetSystems(manager, argsJson),
                "ember_get_component_types" => HandleGetComponentTypes(),
                "ember_create_entity" => HandleCreateEntity(manager, argsJson),
                "ember_destroy_entity" => HandleDestroyEntity(manager, argsJson),
                "ember_add_component" => HandleAddComponent(manager, argsJson),
                "ember_remove_component" => HandleRemoveComponent(manager, argsJson),
                "ember_set_component" => HandleSetComponent(manager, argsJson),
                _ => throw new InvalidOperationException($"Unknown method: {method}")
            };
        }

        // ── Read handlers ──

        private static string HandleWorldInfo(ECSManager manager)
        {
            var view = manager.World.Debug;
            return SimpleJson.BuildJson(
                ("entityCapacity", view.EntityCapacity.ToString()),
                ("aliveEntityCount", view.AliveEntityCount.ToString()),
                ("archetypeCount", view.ArchetypeCount.ToString()),
                ("chunkCount", view.ChunkCount.ToString()),
                ("totalEntityCapacity", view.TotalEntityCapacity.ToString()),
                ("globalFillRate", F(view.GlobalFillRate))
            );
        }

        private static string HandleQueryEntities(ECSManager manager, string args)
        {
            var view = manager.World.Debug;
            if (view.AliveEntities == null)
                return SimpleJson.BuildJson(("entities", "[]"), ("count", "0"));

            string[] filter = SimpleJson.GetStringArray(args, "components");
            int offset = SimpleJson.GetInt(args, "offset", 0);
            int limit = Math.Min(SimpleJson.GetInt(args, "limit", 100), 500);

            var results = new StringBuilder("[");
            int count = 0;
            int written = 0;
            foreach (var entity in view.AliveEntities)
            {
                if (MatchesFilter(entity, filter))
                {
                    if (count >= offset && written < limit)
                    {
                        if (written > 0) results.Append(',');
                        results.Append(EntityJson(entity));
                        written++;
                    }
                    count++;
                }
            }
            results.Append(']');

            return SimpleJson.BuildJson(
                ("entities", results.ToString()),
                ("count", count.ToString()),
                ("offset", offset.ToString()),
                ("limit", limit.ToString())
            );
        }

        private static string EntityJson(EntityDebugView entity)
        {
            var comps = new StringBuilder("[");
            for (int i = 0; i < entity.ComponentNames.Length; i++)
            {
                if (i > 0) comps.Append(',');
                comps.Append('"');
                comps.Append(Escape(entity.ComponentNames[i]));
                comps.Append('"');
            }
            comps.Append(']');

            return SimpleJson.BuildJson(
                ("index", entity.Index.ToString()),
                ("version", entity.Version.ToString()),
                ("placed", entity.Placed ? "true" : "false"),
                ("archetype", entity.ArchetypeIndex.ToString()),
                ("components", comps.ToString())
            );
        }

        private static bool MatchesFilter(EntityDebugView entity, string[] filter)
        {
            if (filter == null || filter.Length == 0) return true;
            foreach (var f in filter)
            {
                bool found = false;
                foreach (var c in entity.ComponentNames)
                    if (c.Equals(f, StringComparison.OrdinalIgnoreCase)) { found = true; break; }
                if (!found) return false;
            }
            return true;
        }

        private static string HandleGetEntity(ECSManager manager, string args)
        {
            int entityIndex = SimpleJson.GetInt(args, "entityIndex", -1);
            if (entityIndex < 0) throw new ArgumentException("entityIndex is required");

            var entity = new Entity(entityIndex, GetVersion(manager, entityIndex));
            if (!manager.World.Exists(entity))
                return SimpleJson.BuildJson(("error", "\"Entity not found\""));

            var typeIds = manager.World.GetEntityComponentTypes(entity);
            var sb = new StringBuilder("[");
            for (int i = 0; i < typeIds.Length; i++)
            {
                if (i > 0) sb.Append(',');
                var info = ComponentTypeRegistry.GetInfo(typeIds[i]);
                var fields = new StringBuilder("{");
                if (info.Kind != ComponentKind.Tag)
                {
                    try
                    {
                        var boxed = manager.World.GetComponentBoxed(entity, typeIds[i]);
                        if (boxed != null)
                        {
                            var fis = info.Type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                            for (int j = 0; j < fis.Length; j++)
                            {
                                if (j > 0) fields.Append(',');
                                var val = fis[j].GetValue(boxed);
                                fields.Append('"');
                                fields.Append(fis[j].Name);
                                fields.Append("\":");
                                fields.Append(JsonValue(val));
                            }
                        }
                    }
                    catch { }
                }
                fields.Append('}');

                sb.Append(SimpleJson.BuildJson(
                    ("type", $"\"{info.Type.Name}\""),
                    ("kind", $"\"{info.Kind}\""),
                    ("fields", fields.ToString())
                ));
            }
            sb.Append(']');

            return SimpleJson.BuildJson(
                ("entityIndex", entityIndex.ToString()),
                ("components", sb.ToString())
            );
        }

        private static int GetVersion(ECSManager manager, int entityIndex)
        {
            if (manager.World.Debug.AliveEntities != null)
                foreach (var e in manager.World.Debug.AliveEntities)
                    if (e.Index == entityIndex) return e.Version;
            return 1;
        }

        private static string HandleGetArchetypes(ECSManager manager)
        {
            var view = manager.World.Debug;
            if (view.Archetypes == null)
                return SimpleJson.BuildJson(("archetypes", "[]"));

            var sb = new StringBuilder("[");
            for (int i = 0; i < view.Archetypes.Length; i++)
            {
                if (i > 0) sb.Append(',');
                var arch = view.Archetypes[i];

                var comps = new StringBuilder("[");
                for (int j = 0; j < arch.ComponentNames.Length; j++)
                {
                    if (j > 0) comps.Append(',');
                    comps.Append('"');
                    comps.Append(Escape(arch.ComponentNames[j]));
                    comps.Append('"');
                }
                comps.Append(']');

                sb.Append(SimpleJson.BuildJson(
                    ("components", comps.ToString()),
                    ("typeCount", arch.ComponentTypeCount.ToString()),
                    ("chunkCount", arch.ChunkCount.ToString()),
                    ("totalEntities", arch.TotalEntities.ToString()),
                    ("totalCapacity", arch.TotalCapacity.ToString()),
                    ("fillRate", F(arch.FillRate))
                ));
            }
            sb.Append(']');

            return SimpleJson.BuildJson(("archetypes", sb.ToString()));
        }

        private static string HandleGetSystems(ECSManager manager, string args)
        {
            int tickerIndex = SimpleJson.GetInt(args, "tickerIndex", -1);
            var views = manager.GetDebugViews();

            var tickers = new StringBuilder("[");
            for (int i = 0; i < views.Length; i++)
            {
                if (tickerIndex >= 0 && i != tickerIndex) continue;
                if (i > 0) tickers.Append(',');

                var t = views[i];
                var systems = new StringBuilder("[");
                for (int j = 0; j < t.Systems.Length; j++)
                {
                    if (j > 0) systems.Append(',');
                    var s = t.Systems[j];
                    systems.Append(SimpleJson.BuildJson(
                        ("name", $"\"{Escape(s.Name)}\""),
                        ("baseType", $"\"{s.BaseType}\""),
                        ("hooks", SimpleJson.BuildJson(
                            ("entityCreated", s.HasEntityCreatedHook ? "true" : "false"),
                            ("entityDestroyed", s.HasEntityDestroyedHook ? "true" : "false"),
                            ("componentAdded", s.HasComponentAddedHook ? "true" : "false"),
                            ("componentRemoved", s.HasComponentRemovedHook ? "true" : "false")
                        ))
                    ));
                }
                systems.Append(']');

                tickers.Append(SimpleJson.BuildJson(
                    ("index", t.Index.ToString()),
                    ("systems", systems.ToString())
                ));
            }
            tickers.Append(']');

            return SimpleJson.BuildJson(("tickers", tickers.ToString()));
        }

        private static string HandleGetComponentTypes()
        {
            var sb = new StringBuilder("[");
            int count = 0;
            for (int i = 0; i < ComponentTypeRegistry.Count; i++)
            {
                var info = ComponentTypeRegistry.GetInfo(new ComponentTypeId(i));
                if (info.Type == null) continue;
                if (count > 0) sb.Append(',');
                sb.Append(SimpleJson.BuildJson(
                    ("id", info.TypeId.Value.ToString()),
                    ("name", $"\"{info.Type.Name}\""),
                    ("kind", $"\"{info.Kind}\""),
                    ("size", info.Size.ToString()),
                    ("alignment", info.Alignment.ToString())
                ));
                count++;
            }
            sb.Append(']');
            return SimpleJson.BuildJson(("types", sb.ToString()));
        }

        // ── Write handlers ──

        private static string HandleCreateEntity(ECSManager manager, string args)
        {
            var comps = SimpleJson.GetStringArray(args, "components");
            if (comps.Length == 0)
                throw new ArgumentException("components array is required");

            var mask = new ComponentMask();
            foreach (string typeName in comps)
            {
                var type = FindComponentType(typeName);
                if (type == null)
                    throw new ArgumentException($"Unknown component type: {typeName}");
                AddComponentToMask(mask, type);
            }

            var entity = manager.World.CreateEntity(mask);
            return SimpleJson.BuildJson(
                ("index", entity.Index.ToString()),
                ("version", entity.Version.ToString())
            );
        }

        private static string HandleDestroyEntity(ECSManager manager, string args)
        {
            int entityIndex = SimpleJson.GetInt(args, "entityIndex", -1);
            if (entityIndex < 0) throw new ArgumentException("entityIndex is required");

            var entity = new Entity(entityIndex, GetVersion(manager, entityIndex));
            manager.World.DestroyEntity(entity);
            return SimpleJson.BuildJson(
                ("destroyed", "true"),
                ("entityIndex", entityIndex.ToString())
            );
        }

        private static string HandleAddComponent(ECSManager manager, string args)
        {
            int entityIndex = SimpleJson.GetInt(args, "entityIndex", -1);
            string typeName = SimpleJson.GetString(args, "componentType");

            var type = FindComponentType(typeName);
            if (type == null) throw new ArgumentException($"Unknown component type: {typeName}");

            var entity = new Entity(entityIndex, GetVersion(manager, entityIndex));
            var method = typeof(World).GetMethod("AddComponent",
                BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(Entity) }, null);
            method.MakeGenericMethod(type).Invoke(manager.World, new object[] { entity });

            return SimpleJson.BuildJson(
                ("added", "true"),
                ("entityIndex", entityIndex.ToString()),
                ("component", $"\"{typeName}\"")
            );
        }

        private static string HandleRemoveComponent(ECSManager manager, string args)
        {
            int entityIndex = SimpleJson.GetInt(args, "entityIndex", -1);
            string typeName = SimpleJson.GetString(args, "componentType");

            var type = FindComponentType(typeName);
            if (type == null) throw new ArgumentException($"Unknown component type: {typeName}");

            var entity = new Entity(entityIndex, GetVersion(manager, entityIndex));
            var method = typeof(World).GetMethod("RemoveComponent",
                BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(Entity) }, null);
            method.MakeGenericMethod(type).Invoke(manager.World, new object[] { entity });

            return SimpleJson.BuildJson(
                ("removed", "true"),
                ("entityIndex", entityIndex.ToString()),
                ("component", $"\"{typeName}\"")
            );
        }

        private static string HandleSetComponent(ECSManager manager, string args)
        {
            int entityIndex = SimpleJson.GetInt(args, "entityIndex", -1);
            string typeName = SimpleJson.GetString(args, "componentType");

            var type = FindComponentType(typeName);
            if (type == null) throw new ArgumentException($"Unknown component type: {typeName}");

            var entity = new Entity(entityIndex, GetVersion(manager, entityIndex));
            var typeId = ComponentTypeRegistry.GetInfo(type).TypeId;
            var boxed = manager.World.GetComponentBoxed(entity, typeId);
            if (boxed == null) throw new InvalidOperationException($"Cannot get component {typeName}");

            // Apply field values from fieldValues sub-object
            string fieldValuesJson = SimpleJson.GetObject(args, "fieldValues");
            if (fieldValuesJson != null)
            {
                foreach (var fi in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    string strVal = SimpleJson.GetString(fieldValuesJson, fi.Name);
                    if (strVal != null)
                    {
                        object val = ConvertValue(strVal, fi.FieldType);
                        fi.SetValue(boxed, val);
                        continue;
                    }
                    int intVal = SimpleJson.GetInt(fieldValuesJson, fi.Name, int.MinValue);
                    if (intVal != int.MinValue)
                    {
                        fi.SetValue(boxed, Convert.ChangeType(intVal, fi.FieldType));
                    }
                }
            }

            var setMethod = typeof(World).GetMethod("SetComponent",
                BindingFlags.Public | BindingFlags.Instance, null,
                new[] { typeof(Entity), type }, null);
            setMethod.MakeGenericMethod(type).Invoke(manager.World, new[] { entity, boxed });

            return SimpleJson.BuildJson(
                ("set", "true"),
                ("entityIndex", entityIndex.ToString()),
                ("component", $"\"{typeName}\"")
            );
        }

        // ── Helpers ──

        private static string BuildError(string msg)
        {
            return SimpleJson.BuildJson(("error", $"\"{Escape(msg)}\""));
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

        private static void AddComponentToMask(ComponentMask mask, Type type)
        {
            var withMethod = typeof(ComponentMask).GetMethod("With",
                BindingFlags.Public | BindingFlags.Instance);
            withMethod?.MakeGenericMethod(type).Invoke(mask, null);
        }

        private static object ConvertValue(string s, Type targetType)
        {
            if (targetType == typeof(int)) return int.Parse(s);
            if (targetType == typeof(float)) return float.Parse(s);
            if (targetType == typeof(double)) return double.Parse(s);
            if (targetType == typeof(bool)) return bool.Parse(s);
            if (targetType == typeof(string)) return s;
            if (targetType == typeof(long)) return long.Parse(s);
            return s;
        }

        private static string JsonValue(object val)
        {
            if (val == null) return "null";
            if (val is bool b) return b ? "true" : "false";
            if (val is int i) return i.ToString();
            if (val is float f) return f.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (val is double d) return d.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (val is string s) return $"\"{Escape(s)}\"";
            return $"\"{Escape(val.ToString())}\"";
        }

        private static string F(float v) => v.ToString(System.Globalization.CultureInfo.InvariantCulture);

        private static string Escape(string s) => s?.Replace("\\", "\\\\").Replace("\"", "\\\"") ?? "";
    }
}
