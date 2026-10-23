using System;
using System.Collections;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using Celeste.Core.Components;
using Celeste.Core.Mathematics;
using Celeste.Core.Mathematics.Extrema;
using Celeste.Core.Scheduling;
using Celeste.Everest.Reflection.Runtime;
using Celeste.Game.Entities.BadelineDummy;
using Celeste.Game.Execution.Levels.Cutscenes;
using Celeste.Game.Execution.Levels;
using Celeste.Simulation.Audio.Sources;
using Celeste.Game.Execution.Processes;
using Celeste.Game.Presentation.Particles;
using Celeste.Scene.Entities;
using Celeste.Simulation.Presentation.Particles.Definitions;
using NLua;

namespace Celeste.Mods.LuaCutscenes.Runtime
{
    public sealed partial class LuaCutsceneRuntime
    {
        private Func<ISourceEngineContext, string, SceneEntityState> getNpc;

        private readonly Dictionary<(Type Type, string Name), string> entityMemberNames = new();

        public string ResolveEntityMemberName(object entity, string name)
        {
            var type = entity.GetType();
            var key = (type, name);
            if (entityMemberNames.TryGetValue(key, out var resolved)) return resolved;
            resolved = name;
            if (type.GetMember(name, BindingFlags.Public | BindingFlags.Instance).Length == 0 && name.Length != 0)
            {
                var canonical = char.ToUpperInvariant(name[0]) + name.Substring(1);
                if (type.GetMember(canonical, BindingFlags.Public | BindingFlags.Instance).Length != 0)
                    resolved = canonical;
            }

            entityMemberNames.Add(key, resolved);
            return resolved;
        }

        public bool IsBadelineDummy(object entity) => entity is BadelineDummyPresentation;

        public void AppearBadeline(BadelineDummyPresentation entity, OriginalLevelSceneRoot level, bool silent = false)
            => entity.Appear(level, silent);

        public SoundSourceComponentState CreateSoundSource()
            => ((ISourceSoundSourceEnvironment)Scope.Engine).SoundSources.Create();

        public SoundSourceComponentState CreateSoundSource(string path)
            => new SoundSourceComponentState(Scope.Engine, path);

        public SoundSourceComponentState CreateSoundSource(Vector2 offset, string path)
            => new SoundSourceComponentState(Scope.Engine, offset, path);

        public float TimeRate
        {
            get => Scope.Engine.Effects.TimeRate;
            set => Scope.Engine.Effects.TimeRate = value;
        }

        public BadelineDummyPresentation CreateBadelineDummy(Vector2 position)
            => new BadelineDummyPresentation(Scope.Engine, position);

        public SceneEntityState CreatePlainEntity() => CreatePlainEntity(Vector2.Zero);

        public SceneEntityState CreatePlainEntity(Vector2 position)
            => new SceneEntityState(((ISourceEntitySourceDataEnvironment)Scope.Engine).EntitySourceDataRegistration,
                position);

        public SimulationCoroutine CreateMonocleCoroutine(bool removeOnComplete = true)
            => new SimulationCoroutine(Scope.Engine, removeOnComplete);

        public SimulationCoroutine CreateMonocleCoroutine(object routine, bool removeOnComplete = true)
            => new SimulationCoroutine(Scope.Engine,
                routine is LuaTable table ? CreateCoroutine(table) : (IEnumerator)routine, removeOnComplete);

        public IEnumerator CameraTo(Vector2 position, float duration, Func<float, float> ease = null)
            => OriginalCutsceneEntityState.CameraTo(Scope.Engine, position, duration, ease);

        public float Approach(float value, float target, float amount)
            => ScalarMath.Approach(value, target, amount, ((ISourceExtremaEnvironment)Scope.Engine).ExtremaArithmetic);

        public void DustBurstFG(Vector2 position, float direction, int count = 1, float range = 4,
            ParticleTypePresentation particle = null)
            => OriginalDust.BurstFG(Scope.Engine, position, direction, count, range, particle);

        public void SetMusicParam(string name, float value)
            => ((IOriginalGameplayProcess)Scope.Engine).AudioSession.SetMusicParam(name, value);

        public SceneEntityState GetNpc(string name)
        {
            if (getNpc == null)
            {
                var discovery = ((ISourceRuntimeTypeDiscoveryEnvironment)Scope.Engine).RuntimeTypeDiscovery;
                var type = discovery.ResolveRuntimeTypeAlias("Celeste.Mod.MaxHelpingHand.Entities.MoreCustomNPC")
                           ?? throw new TypeLoadException("Celeste.Mod.MaxHelpingHand.Entities.MoreCustomNPC");
                var method = type.GetMethod("GetNPC", BindingFlags.Public | BindingFlags.Static, null,
                                 new[] { typeof(ISourceEngineContext), typeof(string) }, null)
                             ?? throw new MissingMethodException(type.FullName, "GetNPC(ISourceEngineContext, string)");
                getNpc = (Func<ISourceEngineContext, string, SceneEntityState>)method.CreateDelegate(
                    typeof(Func<ISourceEngineContext, string, SceneEntityState>));
            }

            return getNpc(Scope.Engine, name);
        }
    }
}
