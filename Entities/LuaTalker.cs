using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Celeste.Core.Components;
using Celeste.Core.Geometry;
using Celeste.Core.Scheduling;
using Celeste.Everest.Modules.Entities.Metadata;
using Celeste.Everest.Scripting.Lua;
using Celeste.Formats.Map.Entities.Structure;
using Celeste.Game.Components.Talking;
using Celeste.Game.Execution.Levels;
using Celeste.Mods.LuaCutscenes.Modules;
using Celeste.Mods.LuaCutscenes.Runtime;
using Celeste.Scene.Entities;
using Celeste.Simulation;
using NLua;

namespace Celeste.Mods.LuaCutscenes.Entities
{
    [CustomEntity("luaCutscenes/luaTalker")]
    public sealed class LuaTalker : SceneEntityState
    {
        private readonly ISourceEngineContext engine;
        private readonly EntityData data;
        private readonly bool onlyOnce;
        private readonly bool unskippable;
        private readonly OriginalTalkComponent talk;
        private LuaFunction endFunction;
        private LuaTable environment;
        private readonly List<LuaCutsceneHandles> handles = new List<LuaCutsceneHandles>();
        private bool wasSkipped;
        private bool shouldDisable;

        public LuaTalker(ISourceEngineContext engine, EntityData data, Vector2 offset)
            : base(((ISourceEntitySourceDataEnvironment)engine).EntitySourceDataRegistration, data.Position + offset)
        {
            this.engine = engine;
            this.data = data;
            onlyOnce = data.Bool("onlyOnce", true);
            unskippable = data.Bool("unskippable");
            talk = new OriginalTalkComponent((IOriginalTalkProcess)engine,
                    new IntRect(0, 0, data.Width, data.Height), data.Nodes.First() + offset - Position, OnTalk)
                { Enabled = true, PlayerMustBeFacing = false };
            OwnedComponents.Add(talk);
        }

        private void OnTalk(RoomPlayerSession player)
        {
            var runtime = LuaCutscenesModule.GetRuntime(engine);
            object[] values = runtime.Load(data.Attr("filename"), data.Attr("arguments"), player, this, null, true);
            environment = values.ElementAtOrDefault(0) as LuaTable;
            var routine = values.ElementAtOrDefault(1) as EverestLuaCoroutine;
            endFunction = values.ElementAtOrDefault(2) as LuaFunction;
            handles.Add(new LuaCutsceneHandles(values));
            if (routine == null) return;
            var level = (OriginalLevelSceneRoot)Scene;
            if (!unskippable) level.StartCutscene(Skip);
            OwnedComponents.Add(new SimulationCoroutine(engine,
                BeginRoutine(level, runtime.RunCoroutine(routine))));
            if (onlyOnce) shouldDisable = true;
        }

        private IEnumerator BeginRoutine(OriginalLevelSceneRoot level, IEnumerator routine)
        {
            yield return routine;
            End(level);
        }

        private void Skip(OriginalLevelSceneRoot level)
        {
            wasSkipped = true;
            End(level);
        }

        private void End(OriginalLevelSceneRoot level)
        {
            LuaCutscenesModule.GetRuntime(engine).InvokeEndCallback(endFunction, level, wasSkipped);
            level.EndCutscene();
        }

        private void ReleaseHandles()
        {
            foreach (var owned in handles) owned.Dispose();
            handles.Clear();
        }

        public override void Removed(object scene)
        {
            base.Removed(scene);
            ReleaseHandles();
        }

        public override void SceneEnd(object scene)
        {
            base.SceneEnd(scene);
            ReleaseHandles();
        }

        public override void Update()
        {
            if (shouldDisable && talk.Enabled) talk.Enabled = false;
            base.Update();
        }
    }
}
