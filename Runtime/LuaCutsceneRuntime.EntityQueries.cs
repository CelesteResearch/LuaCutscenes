using System;
using System.Collections.Generic;
using Celeste.Everest.Reflection.Runtime;
using Celeste.Everest.Reflection;
using Celeste.Scene.Tracking;
using Celeste.Simulation;
using NLua;

namespace Celeste.Mods.LuaCutscenes.Runtime
{
    public sealed partial class LuaCutsceneRuntime
    {
        public bool IsPlayerEntity(object entity) => entity is RoomPlayerSession;

        private readonly Dictionary<string, Type> entityQueryTypes = new(StringComparer.Ordinal);
        private long entityQueryDiscoveryRevision = -1;
        private long entityQuerySourceRevision = -1;

        public Type GetEntityType(string name, string prefix = "Celeste.")
        {
            var reflection = ((ISourceEverestReflectionEnvironment)Scope.Engine).Reflection;
            var discovery = ((ISourceRuntimeTypeDiscoveryEnvironment)Scope.Engine).RuntimeTypeDiscovery;
            if (entityQueryDiscoveryRevision != discovery.Revision ||
                entityQuerySourceRevision != reflection.SourceMembers.ActualTypeBindingsRevision)
            {
                entityQueryTypes.Clear();
                entityQueryDiscoveryRevision = discovery.Revision;
                entityQuerySourceRevision = reflection.SourceMembers.ActualTypeBindingsRevision;
            }

            var fullName = prefix + name;
            if (!entityQueryTypes.TryGetValue(fullName, out var type))
            {
                type = reflection.GetFakeEntryAssembly().GetType(fullName);
                entityQueryTypes.Add(fullName, type);
            }

            return type;
        }

        public object GetEntity(string name, string prefix = "Celeste.")
        {
            try
            {
                var entities = Scene.SourceScene.Entities.GetTrackedEntities(GetEntityType(name, prefix));
                return entities.Count == 0 ? null : entities[0];
            }
            catch (Exception error)
            {
                Log(4, "Lua Cutscenes", "Failed to get entity: " + error);
                return null;
            }
        }

        public LuaTable GetEntities(string name, string prefix = "Celeste.")
        {
            try
            {
                var entities = Scene.SourceScene.Entities.GetTrackedEntities(GetEntityType(name, prefix));
                var result = Scope.CreateTable();
                for (var index = 0; index < entities.Count; index++) result[index + 1] = entities[index];
                return result;
            }
            catch (Exception error)
            {
                Log(4, "Lua Cutscenes", "Failed to get entities: " + error);
                return null;
            }
        }

        public object GetFirstEntity(string name, string prefix = "Celeste.")
        {
            try
            {
                var type = GetEntityType(name, prefix) ?? throw new ArgumentNullException(nameof(name));
                var entities = Scene.SourceScene.Entities.Entities;
                for (var index = 0; index < entities.Count; index++)
                    if (type.IsInstanceOfType(entities[index]))
                        return entities[index];
                return null;
            }
            catch (Exception error)
            {
                Log(4, "Lua Cutscenes", "Failed to get entity: " + error);
                return null;
            }
        }

        public LuaTable GetAllEntities(string name, string prefix = "Celeste.")
        {
            try
            {
                var type = GetEntityType(name, prefix) ?? throw new ArgumentNullException(nameof(name));
                var result = Scope.CreateTable();
                var entities = Scene.SourceScene.Entities.Entities;
                var resultIndex = 1;
                for (var index = 0; index < entities.Count; index++)
                    if (type.IsInstanceOfType(entities[index]))
                        result[resultIndex++] = entities[index];
                return result;
            }
            catch (Exception error)
            {
                Log(4, "Lua Cutscenes", "Failed to get entities: " + error);
                return null;
            }
        }
    }
}
