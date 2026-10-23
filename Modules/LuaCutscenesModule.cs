using System;
using System.Runtime.CompilerServices;
using Celeste.Core.Scheduling;
using Celeste.Everest.Modules.Runtime;
using Celeste.Mods.LuaCutscenes.Runtime;

namespace Celeste.Mods.LuaCutscenes.Modules
{
    public sealed class LuaCutscenesModule : SourceEverestModule
    {
        private static readonly ConditionalWeakTable<ISourceEngineContext, LuaCutsceneRuntime> runtimes =
            new ConditionalWeakTable<ISourceEngineContext, LuaCutsceneRuntime>();

        private LuaCutsceneRuntime runtime;

        public LuaCutscenesModule(ISourceEngineContext engine) : base(engine)
        {
        }

        public override void Load()
        {
            runtime = new LuaCutsceneRuntime(SourceEngine, Metadata);
            runtimes.Add(SourceEngine, runtime);
        }

        public override void LoadContent(bool firstLoad) => runtime.Initialize();

        public override void Unload()
        {
            runtimes.Remove(SourceEngine);
            runtime?.Dispose();
            runtime = null;
        }

        internal static LuaCutsceneRuntime GetRuntime(ISourceEngineContext engine) =>
            runtimes.TryGetValue(engine, out var runtime)
                ? runtime
                : throw new InvalidOperationException("LuaCutscenes module has not loaded in this engine.");
    }
}
