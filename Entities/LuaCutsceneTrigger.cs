using System.Numerics;
using Celeste.Core.Components;
using Celeste.Core.Mathematics.Extrema;
using Celeste.Core.Maps;
using Celeste.Core.Scheduling;
using Celeste.Everest.Modules.Entities.Metadata;
using Celeste.Formats.Map.Entities.Structure;
using Celeste.Game.Execution.Levels;
using Celeste.Mods.LuaCutscenes.Modules;
using Celeste.Mods.LuaCutscenes.Runtime;
using Celeste.Simulation;
using Celeste.Simulation.Gameplay.Triggers.Foundation;

namespace Celeste.Mods.LuaCutscenes.Entities
{
    [CustomEntity("luaCutscenes/luaCutsceneTrigger")]
    public sealed class LuaCutsceneTrigger : SourceTriggerEntityState
    {
        private readonly ISourceEngineContext engine;
        private readonly EntityData data;
        private readonly MapEntityId id;
        private readonly bool onlyOnce;
        private readonly bool oncePerSession;
        private readonly bool unskippable;
        private bool played;
        private LuaCutsceneEntity cutscene;

        public LuaCutsceneTrigger(ISourceEngineContext engine, EntityData data, Vector2 offset, MapEntityId id)
            : base(((ISourceEntitySourceDataEnvironment)engine).EntitySourceDataRegistration, data.Position + offset,
                data.Width, data.Height, ((ISourceExtremaEnvironment)engine).ExtremaArithmetic)
        {
            this.engine = engine;
            this.data = data;
            this.id = id;
            onlyOnce = data.Bool("onlyOnce", true);
            oncePerSession = data.Bool("oncePerSession");
            unskippable = data.Bool("unskippable");
        }

        public override void OnEnter(RoomPlayerSession player)
        {
            if (onlyOnce && played) return;
            var level = (OriginalLevelSceneRoot)Scene;
            if (cutscene == null || cutscene.Finished)
            {
                cutscene?.ReleaseTrigger();
                level.Scheduler.QueueSourceEntity(cutscene =
                    new LuaCutsceneEntity(LuaCutscenesModule.GetRuntime(engine), this, player, data, unskippable));
            }

            played = true;
            cutscene.OnEnter(player);
            if (oncePerSession)
            {
                RemoveSelf();
                level.Session.DoNotLoad.Add(id);
            }

            base.OnEnter(player);
        }

        public override void Removed(object scene)
        {
            base.Removed(scene);
            cutscene?.ReleaseTrigger();
            cutscene = null;
        }

        public override void SceneEnd(object scene)
        {
            base.SceneEnd(scene);
            cutscene?.ReleaseTrigger();
            cutscene = null;
        }

        public override void OnStay(RoomPlayerSession player)
        {
            cutscene?.OnStay(player);
            base.OnStay(player);
        }

        public override void OnLeave(RoomPlayerSession player)
        {
            cutscene?.OnLeave(player);
            base.OnLeave(player);
        }
    }
}
