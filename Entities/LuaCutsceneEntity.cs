using System;
using System.Collections;
using System.Linq;
using Celeste.Core.Scheduling;
using Celeste.Formats.Map.Entities.Structure;
using Celeste.Game.Execution.Levels;
using Celeste.Game.Execution.Levels.Cutscenes;
using Celeste.Everest;
using Celeste.Everest.Scripting.Lua;
using Celeste.Everest.Diagnostics.Logging;
using Celeste.Mods.LuaCutscenes.Runtime;
using Celeste.Simulation;
using NLua;

namespace Celeste.Mods.LuaCutscenes.Entities
{
    public sealed class LuaCutsceneEntity : OriginalCutsceneEntityState
    {
        private readonly ISourceEngineContext engine;
        private readonly LuaCutsceneRuntime runtime;
        private readonly IEnumerator beginRoutine;
        private readonly LuaFunction endFunction;
        private readonly LuaFunction enterFunction;
        private readonly LuaFunction stayFunction;
        private readonly LuaFunction leaveFunction;
        private readonly bool unskippable;
        private readonly LuaTable environment;
        private LuaCutsceneHandles handles;
        private bool removed;
        private bool triggerReleased;
        public bool Finished;

        public LuaCutsceneEntity(LuaCutsceneRuntime runtime, LuaCutsceneTrigger trigger, RoomPlayerSession player,
            EntityData data, bool unskippable = false) : base(runtime.Scope.Engine)
        {
            engine = runtime.Scope.Engine;
            this.runtime = runtime;
            this.unskippable = unskippable;
            triggerReleased = trigger == null;
            string filename = data.Attr("filename");
            if (string.IsNullOrEmpty(filename)) return;
            object[] values = runtime.Load(filename, data.Attr("arguments"), player, this, trigger);
            environment = values.ElementAtOrDefault(0) as LuaTable;
            var coroutine = values.ElementAtOrDefault(1) as EverestLuaCoroutine;
            beginRoutine = coroutine == null ? null : runtime.RunCoroutine(coroutine);
            endFunction = values.ElementAtOrDefault(2) as LuaFunction;
            enterFunction = values.ElementAtOrDefault(3) as LuaFunction;
            stayFunction = values.ElementAtOrDefault(4) as LuaFunction;
            leaveFunction = values.ElementAtOrDefault(5) as LuaFunction;
            handles = new LuaCutsceneHandles(values);
        }

        private IEnumerator BeginRoutine(OriginalLevelSceneRoot level)
        {
            yield return beginRoutine;
            EndCutscene(level);
        }

        public override void OnBegin(OriginalLevelSceneRoot level)
        {
            if (beginRoutine == null) return;
            OwnedComponents.Add(new SimulationCoroutine(engine, BeginRoutine(level)));
            if (unskippable)
            {
                level.InCutscene = false;
                level.CancelCutscene();
            }
        }

        public override void OnEnd(OriginalLevelSceneRoot level)
        {
            try
            {
                runtime.InvokeEndCallback(endFunction, level, WasSkipped);
                Finished = true;
            }
            catch (Exception exception)
            {
                var logger = ((IEverestLoggerProcessEnvironment)engine).Logger;
                logger.Log(EverestLogLevel.Error, "Lua Cutscenes", "Failed to call OnEnd");
                logger.LogDetailed(exception);
            }
        }

        internal void ReleaseTrigger()
        {
            triggerReleased = true;
            if (removed) ReleaseHandles();
        }

        private void ReleaseHandles()
        {
            var owned = handles;
            handles = null;
            owned?.Dispose();
        }

        public override void Removed(object scene)
        {
            base.Removed(scene);
            removed = true;
            if (triggerReleased) ReleaseHandles();
        }

        public override void SceneEnd(object scene)
        {
            base.SceneEnd(scene);
            ReleaseHandles();
        }

        public void OnEnter(RoomPlayerSession player)
        {
            try
            {
                runtime.InvokePlayerCallback(enterFunction, player);
            }
            catch (Exception exception)
            {
                var logger = ((IEverestLoggerProcessEnvironment)engine).Logger;
                logger.Log(EverestLogLevel.Error, "Lua Cutscenes", "Failed to call OnEnter");
                logger.LogDetailed(exception);
            }
        }

        public void OnStay(RoomPlayerSession player)
        {
            try
            {
                runtime.InvokePlayerCallback(stayFunction, player);
            }
            catch (Exception exception)
            {
                var logger = ((IEverestLoggerProcessEnvironment)engine).Logger;
                logger.Log(EverestLogLevel.Error, "Lua Cutscenes", "Failed to call OnStay");
                logger.LogDetailed(exception);
            }
        }

        public void OnLeave(RoomPlayerSession player)
        {
            try
            {
                runtime.InvokePlayerCallback(leaveFunction, player);
            }
            catch (Exception exception)
            {
                var logger = ((IEverestLoggerProcessEnvironment)engine).Logger;
                logger.Log(EverestLogLevel.Error, "Lua Cutscenes", "Failed to call OnLeave");
                logger.LogDetailed(exception);
            }
        }
    }
}
