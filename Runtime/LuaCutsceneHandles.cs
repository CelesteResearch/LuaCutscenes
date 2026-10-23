using System;
using Celeste.Everest.Scripting.Lua;
using NLua;

namespace Celeste.Mods.LuaCutscenes.Runtime
{
    internal sealed class LuaCutsceneHandles : IDisposable
    {
        private object[] values;

        internal LuaCutsceneHandles(object[] values)
        {
            this.values = values;
        }

        public void Dispose()
        {
            var owned = values;
            values = null;
            if (owned == null) return;
            foreach (var value in owned)
            {
                if (value is EverestLuaCoroutine routine) routine.Proxy.Dispose();
                else if (value is LuaBase handle) handle.Dispose();
            }
        }
    }
}
