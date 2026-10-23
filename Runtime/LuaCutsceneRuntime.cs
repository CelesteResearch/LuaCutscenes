using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Runtime.CompilerServices;
using Celeste.Game.Audio.Interop;
using Celeste.Game.Audio;
using Celeste.Game.Execution.Processes;
using Celeste.Simulation.Audio;
using Celeste.Game.Audio.State.Globals;
using Celeste.Core.Scheduling;
using Celeste.Core.Audio;
using Celeste.Everest;
using Celeste.Everest.Content.Packages;
using Celeste.Everest.Diagnostics.Logging;
using Celeste.Everest.Scripting.Lua;
using Celeste.Everest.Scripting.Scopes;
using Celeste.Everest.Reflection;
using Celeste.Everest.Modules.Runtime;
using Celeste.Game.Execution.Levels;
using Celeste.Game.UI.Dialogue.Textboxes;
using Celeste.Mods.LuaCutscenes.Entities;
using Celeste.Scene.Entities;
using Celeste.Simulation;
using Celeste.Simulation.Gameplay.Player.Intro;
using NLua;

namespace Celeste.Mods.LuaCutscenes.Runtime
{
    public sealed partial class LuaCutsceneRuntime : IDisposable
    {
        public EverestLuaEngineScope Scope { get; }
        public EverestModuleMetadata Metadata { get; }
        public OriginalLevelSceneRoot Scene => Scope.Engine.Frame.Scenes.CurrentScene as OriginalLevelSceneRoot;
        public LuaCutsceneMethodWrappers MethodWrappers { get; } = new LuaCutsceneMethodWrappers();
        private LuaTable cutsceneHelper;
        private LuaFunction wrapPlayer;
        private LuaFunction wrapScene;
        private LuaFunction wrapEntity;
        private EverestLuaMemberProjection projection;

        private EverestLuaMemberProjection Projection => projection ??=
            new EverestLuaMemberProjection(((ISourceEverestReflectionEnvironment)Scope.Engine).Reflection
                .SourceMembers);

        public LuaCutsceneRuntime(ISourceEngineContext engine, EverestModuleMetadata metadata)
        {
            Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
            Scope = new EverestLuaEngineScope(engine);
            audioTrackViewFactory = GetAudioTrackView;
        }

        public void Initialize()
        {
            if (cutsceneHelper != null) return;
            try
            {
                Scope.RegisterType("Celeste.Facings", typeof(LuaPlayerFacing));
                Scope.RegisterType("Microsoft.Xna.Framework.Color", typeof(Celeste.Core.Display.PackedColor));
                Scope.RegisterType("Celeste.PlayerInventory",
                    typeof(Celeste.Simulation.Gameplay.Player.Inventory.RoomPlayerInventory));
                Scope.RegisterType("Celeste.Player.IntroTypes", typeof(PlayerIntroType));
                Scope.Context.WithState(lua =>
                {
                    lua["__cutscene_runtime"] = this;
                    return 0;
                });
                Scope.Context.Evaluate(Bootstrap, "LuaCutscenes scoped interop");
                string source = GetFileContent(Metadata.Name + ":/Assets/LuaCutscenes/cutscene_helper");
                if (source == null)
                    throw new InvalidOperationException("LuaCutscenes owner cutscene_helper asset is missing.");
                cutsceneHelper =
                    (LuaTable)Scope.Context.Evaluate(source,
                        "LuaCutscenes:/Assets/LuaCutscenes/cutscene_helper.lua")[0];
                wrapPlayer = Scope.Context.WithState(lua => lua.GetFunction("__cutscene_wrap_player"));
                wrapScene = Scope.Context.WithState(lua => lua.GetFunction("__cutscene_wrap_scene"));
                wrapEntity = Scope.Context.WithState(lua => lua.GetFunction("__cutscene_wrap_entity"));
            }
            catch (Exception initializationFailure)
            {
                try
                {
                    ReleaseLuaHandles();
                }
                catch (Exception cleanupFailure)
                {
                    initializationFailure.Data["LuaHandleCleanupFailure"] = cleanupFailure;
                }

                try
                {
                    Scope.Reset();
                }
                catch (Exception cleanupFailure)
                {
                    initializationFailure.Data["LuaScopeCleanupFailure"] = cleanupFailure;
                }

                throw;
            }
        }

        public string GetFileContent(string path)
        {
            var content = ((Celeste.Everest.Content.Registry.IEverestContentRegistryEnvironment)Scope.Engine)
                .SourceContentRegistry;
            using var stream = content.Get(path)?.Stream;
            if (stream == null) return null;
            using var reader = new System.IO.StreamReader(stream);
            return reader.ReadToEnd();
        }

        public object ParseArguments(string source)
        {
            try
            {
                object[] values = Scope.Context.Evaluate("return " + source, "LuaCutscenes arguments");
                if (values.Length == 1) return values[0] ?? source;
                LuaTable table = Scope.CreateTable();
                for (int index = 0; index < values.Length; index++) table[index + 1] = values[index];
                return table;
            }
            catch
            {
                return source;
            }
        }

        public object[] Load(string filename, string arguments, RoomPlayerSession player, object entity, object trigger,
            bool talker = false)
        {
            Initialize();
            using LuaTable data = Scope.CreateTable();
            data["player"] = player == null ? null : wrapPlayer.Call(player)[0];
            data[talker ? "talker" : "cutsceneEntity"] = entity == null ? null : wrapEntity.Call(entity)[0];
            data["cutsceneTrigger"] = trigger;
            data["modMetaData"] = Metadata;
            data["arguments"] = ParseArguments(arguments);
            using var loader = (LuaFunction)cutsceneHelper[talker ? "getTalkerData" : "getCutsceneData"];
            object[] result = loader.Call(filename, data);
            if (result == null || result.Length == 0 || result[0] == null)
            {
                Log(2, "Lua Cutscenes", "Failed to load cutscene, target file does not exist: \"" + filename + "\"");
                return Array.Empty<object>();
            }

            return result;
        }

        public IEnumerator Say(string dialog) => TextboxState.Say(dialog, Scope.Engine);

        public void InvokePlayerCallback(LuaFunction callback, RoomPlayerSession player) =>
            callback?.Call(player == null ? null : wrapPlayer.Call(player)[0]);

        public void InvokeEndCallback(LuaFunction callback, OriginalLevelSceneRoot level, bool wasSkipped) =>
            callback?.Call(wrapScene.Call(level)[0], wasSkipped);

        public EverestLuaCoroutine CreateCoroutine(LuaTable proxy) => new EverestLuaCoroutine(proxy);
        public void SetFacing(RoomPlayerSession player, object facing) => player.Data.Facing = Convert.ToInt32(facing);
        public bool HasProjectedMember(object instance, string name) => Projection.HasMember(instance, name);
        public object ReadProjectedMember(object instance, string name) => Projection.Read(instance, name);

        private readonly ConditionalWeakTable<EventTrackState, OriginalAudioTrackView> audioTrackViews = new();
        private readonly Func<EventTrackState, OriginalAudioTrackView> audioTrackViewFactory;

        public OriginalAudioTrackView GetAudioTrackView(EventTrackState track)
        {
            if (track == null) return null;
            if (audioTrackViews.TryGetValue(track, out var view)) return view;
            view = new OriginalAudioTrackView(track,
                ((IOriginalAudioStateGlobalsEnvironment)Scope.Engine).AudioStateGlobals, audioTrackViewFactory);
            audioTrackViews.Add(track, view);
            return view;
        }

        public void ApplyAudio(RoomAudioState state, bool forceSixteenthNoteHack = false)
        {
            var process = (IOriginalGameplayProcess)Scope.Engine;
            OriginalAudioSession.Apply(state, process.AudioSession.ChannelOutput, process.AudioApplyPolicy,
                forceSixteenthNoteHack);
        }

        public object UnwrapAudioTrackView(object value) =>
            value is OriginalAudioTrackView view ? view.ActualState : value;

        public void WriteProjectedMember(object instance, string name, object value) =>
            Projection.Write(instance, name, value);

        public void AddEntity(OriginalLevelSceneRoot level, SceneEntityState entity) =>
            level.Scheduler.QueueSourceEntity(entity);

        public void RemoveEntity(OriginalLevelSceneRoot level, SceneEntityState entity) => level.RemoveEntity(entity);

        public object PlaySound(string path, Vector2? position = null) => position.HasValue
            ? ((ISourceSpatialAudioEnvironment)Scope.Engine).SpatialAudio.Play(path, position.Value)
            : ((ISourceAudioEnvironment)Scope.Engine).SourceAudio.Play(path);

        public void Log(int level, string tag, string message) =>
            ((IEverestLoggerProcessEnvironment)Scope.Engine).Logger.Log((EverestLogLevel)level, tag, message);

        private bool SafeMoveNext(EverestLuaCoroutine routine)
        {
            try
            {
                return routine.MoveNext();
            }
            catch (Exception exception)
            {
                var logger = ((IEverestLoggerProcessEnvironment)Scope.Engine).Logger;
                logger.Log(EverestLogLevel.Error, "Lua Cutscenes", "Failed to resume coroutine");
                logger.LogDetailed(exception);
                return false;
            }
        }

        public IEnumerator RunCoroutine(EverestLuaCoroutine routine)
        {
            while (routine != null && SafeMoveNext(routine))
            {
                object current = routine.Current;
                yield return current is double || current is long ? Convert.ToSingle(current) : current;
            }

            yield return null;
        }

        public void Dispose()
        {
            try
            {
                ReleaseLuaHandles();
            }
            finally
            {
                projection = null;
                Scope.Dispose();
            }
        }

        private void ReleaseLuaHandles()
        {
            getNpc = null;
            entityMemberNames.Clear();
            IDisposable[] handles = { wrapPlayer, wrapScene, wrapEntity, cutsceneHelper };
            wrapPlayer = null;
            wrapScene = null;
            wrapEntity = null;
            cutsceneHelper = null;
            List<Exception> failures = null;
            foreach (var handle in handles)
            {
                try
                {
                    handle?.Dispose();
                }
                catch (Exception error)
                {
                    (failures ??= new List<Exception>()).Add(error);
                }
            }

            if (failures != null) throw new AggregateException("LuaCutscenes handle cleanup failed.", failures);
        }

        private const string Bootstrap = @"
local runtime = __cutscene_runtime
local function isNativeMethod(value)
  if type(value) == 'function' then return true end
  local meta = type(value) == 'userdata' and getmetatable(value)
  return meta ~= nil and meta ~= false and meta.__name == 'luaNet_function'
end
local function actualObject(value)
  if type(value)=='table' then return rawget(value,'__actual') or value end
  return value
end
local mod = { LuaCoroutine = function(proxy) return runtime:CreateCoroutine(proxy) end }
local logger = { log = function(level, tag, message) runtime:Log(level, tag, message) end }
mod.logger = logger
mod.LogLevel = { Error = 4, Info = 2 }
mod.logLevel = { error = 4, info = 2 }
mod.LuaCutscenes = { LuaHelper = { GetFileContent = function(path) return runtime:GetFileContent(path) end } }
mod.LuaCutscenes.MethodWrappers = {
  TeleportTo=function(level,player,room,intro,spawn) return runtime.MethodWrappers:TeleportTo(actualObject(level),actualObject(player),room,intro,spawn) end,
  InstantTeleport=function(level,player,room,sameRelative,x,y) return runtime.MethodWrappers:InstantTeleport(actualObject(level),actualObject(player),room,sameRelative,x,y) end
}
local function wrapQueryEntity(entity)
  if entity == nil then return nil end
  if runtime:IsPlayerEntity(entity) then return __cutscene_wrap_player(entity) end
  return __cutscene_wrap_entity(entity)
end
local function wrapQueryEntities(entities)
  if entities == nil then return nil end
  for index,entity in ipairs(entities) do entities[index]=wrapQueryEntity(entity) end
  return entities
end
mod.LuaCutscenes.MethodWrappers.GetTypeFromString=function(name,prefix) return runtime:GetEntityType(name,prefix or 'Celeste.') end
mod.LuaCutscenes.MethodWrappers.GetEntity=function(name,prefix) return wrapQueryEntity(runtime:GetEntity(name,prefix or 'Celeste.')) end
mod.LuaCutscenes.MethodWrappers.GetEntities=function(name,prefix) return wrapQueryEntities(runtime:GetEntities(name,prefix or 'Celeste.')) end
mod.LuaCutscenes.MethodWrappers.GetFirstEntity=function(name,prefix) return wrapQueryEntity(runtime:GetFirstEntity(name,prefix or 'Celeste.')) end
mod.LuaCutscenes.MethodWrappers.GetAllEntities=function(name,prefix) return wrapQueryEntities(runtime:GetAllEntities(name,prefix or 'Celeste.')) end
mod.LuaCutscenes.MethodWrappers.getAllEntities=mod.LuaCutscenes.MethodWrappers.GetAllEntities
local function actualArguments(first,...)
  if select('#',...) == 0 then return actualObject(first) end
  return actualObject(first),actualArguments(...)
end
local playerStates = {
  StNormal=0, StClimb=1, StDash=2, StSwim=3, StBoost=4, StRedDash=5, StHitSquash=6,
  StLaunch=7, StPickup=8, StDreamDash=9, StSummitLaunch=10, StDummy=11,
  StIntroWalk=12, StIntroJump=13, StIntroRespawn=14, StIntroWakeUp=15,
  StBirdDashTutorial=16, StFrozen=17, StReflectionFall=18, StStarFly=19,
  StTempleFall=20, StCassetteFly=21, StAttract=22, StIntroMoonJump=23,
  StFlingBird=24, StIntroThinkForABit=25 }
mod.MaxHelpingHand = { Entities = { MoreCustomNPC = {
  GetNPC=function(name) return runtime:GetNpc(name) end } } }
local celeste = { mod=mod, Mod=mod, Player=playerStates,
  SoundSource=function(first,second)
    if second ~= nil then return runtime:CreateSoundSource(first,second) end
    if first ~= nil then return runtime:CreateSoundSource(first) end
    return runtime:CreateSoundSource()
  end,
  BadelineDummy=function(position) return __cutscene_wrap_entity(runtime:CreateBadelineDummy(position)) end,
  CutsceneEntity={CameraTo=function(position,duration,ease) return runtime:CameraTo(position,duration,ease) end},
  Textbox={Say=function(dialog) return runtime:Say(dialog) end},
  Audio={Play=function(path, position) return runtime:PlaySound(path, position) end,
    SetMusicParam=function(name,value) return runtime:SetMusicParam(name,value) end} }
setmetatable(celeste, {__index=function(_,key) return require('#Celeste.' .. key) end})
local dust = { BurstFG=function(position,direction,count,range,particle)
  return runtime:DustBurstFG(position,direction,count or 1,range or 4,particle) end }
local calc = { Approach=function(value,target,amount) return runtime:Approach(value,target,amount) end }
local engine = setmetatable({}, {__index=function(_,key)
  if key == 'Scene' or key == 'scene' then return __cutscene_wrap_scene(runtime.Scene) end
  if key == 'DeltaTime' then return runtime.Scope.Engine.DeltaTime end
  if key == 'RawDeltaTime' then return runtime.Scope.Engine.RawDeltaTime end
  if key == 'TimeRate' then return runtime.TimeRate end
  error('Unsupported engine script member: ' .. tostring(key))
end, __newindex=function(_,key,value)
  if key == 'TimeRate' then runtime.TimeRate=value; return end
  error('Unsupported engine script member: ' .. tostring(key))
end})
runtime.Scope:Register('#celeste', celeste)
runtime.Scope:Register('#celeste.mod', mod)
runtime.Scope:Register('#monocle.engine', engine)
runtime.Scope:Register('#microsoft.xna.framework.vector2', require('#System.Numerics.Vector2'))
runtime.Scope:Register('#celeste.audio', celeste.Audio)
runtime.Scope:Register('#celeste.dust', dust)
runtime.Scope:Register('#monocle.calc', calc)
runtime.Scope:Register('#celeste.badelineDummy', celeste.BadelineDummy)
runtime.Scope:Register('#celeste.soundsource', celeste.SoundSource)
runtime.Scope:Register('#celeste.cutsceneentity', celeste.CutsceneEntity)
runtime.Scope:Register('#monocle.entity', function(position)
  if position == nil then return __cutscene_wrap_entity(runtime:CreatePlainEntity()) end
  return __cutscene_wrap_entity(runtime:CreatePlainEntity(position))
end)
runtime.Scope:Register('#monocle.coroutine', function(...)
  if select('#',...) == 0 then return runtime:CreateMonocleCoroutine() end
  local routine,remove=...
  if type(routine)=='boolean' then return runtime:CreateMonocleCoroutine(routine) end
  return runtime:CreateMonocleCoroutine(routine,remove ~= false)
end)
runtime.Scope:Register('#celeste.mod.maxhelpinghand.entities.morecustomnpc', mod.MaxHelpingHand.Entities.MoreCustomNPC)
runtime.Scope:Register('#celeste.textbox', celeste.Textbox)
runtime.Scope:Register('#celeste.player', celeste.Player)
runtime.Scope:Register('#celeste.mod.luacutscenes', mod.LuaCutscenes)
runtime.Scope:Register('#celeste.mod.luacutscenes.methodwrappers', mod.LuaCutscenes.MethodWrappers)
_G.monocle = require('#monocle')
function __cutscene_wrap_player(actual)
  return setmetatable({__actual=actual}, {
    __index=function(_,key)
      if runtime:HasProjectedMember(actual,key) then return runtime:ReadProjectedMember(actual,key) end
      local value = actual[key]
      if isNativeMethod(value) then return function(_,...) return value(actual,...) end end
      return value
    end,
    __newindex=function(_,key,value)
      if runtime:HasProjectedMember(actual,key) then runtime:WriteProjectedMember(actual,key,value) else actual[key]=value end
    end
  })
end
local sessionWrappers = setmetatable({}, {__mode='k'})
local audioWrappers = setmetatable({}, {__mode='k'})
local function wrapAudio(actual)
  if actual == nil then return nil end
  if audioWrappers[actual] ~= nil then return audioWrappers[actual] end
  local wrapped = setmetatable({__actual=actual}, {
    __index=function(_,key)
      if key == 'Apply' then return function(_,force) if force == nil then force=false end; runtime:ApplyAudio(actual,force) end end
      local value=actual[key]
      if key == 'Music' or key == 'Ambience' then return runtime:GetAudioTrackView(value) end
      if isNativeMethod(value) then return function(_,...) return value(actual,...) end end
      return value
    end,
    __newindex=function(_,key,value)
      actual[key]=runtime:UnwrapAudioTrackView(actualObject(value))
    end
  })
  audioWrappers[actual]=wrapped
  return wrapped
end
local mapValueWrappers = setmetatable({}, {__mode='k'})
local function wrapMapValue(actual,isMap)
  if actual == nil then return nil end
  if mapValueWrappers[actual] ~= nil then return mapValueWrappers[actual] end
  local wrapped = setmetatable({__actual=actual}, {
    __index=function(_,key)
      if isMap and (key == 'getAt' or key == 'GetAt') then return function(_,position) return wrapMapValue(actual:GetAt(position),false) end end
      if not isMap and key == 'bounds' then return actual.Bounds end
      local value=actual[key]
      if isNativeMethod(value) then return function(_,...) return value(actual,...) end end
      return value
    end,
    __newindex=function(_,key,value)
      if not isMap and key == 'bounds' then key='Bounds' end
      actual[key]=actualObject(value)
    end
  })
  mapValueWrappers[actual]=wrapped
  return wrapped
end
local function wrapSession(actual)
  if actual == nil then return nil end
  if sessionWrappers[actual] ~= nil then return sessionWrappers[actual] end
  local wrapped = setmetatable({__actual=actual}, {
    __index=function(_,key)
      local value=actual[key]
      if key == 'Audio' then return wrapAudio(value) end
      if key == 'MapData' then return wrapMapValue(value,true) end
      if isNativeMethod(value) then return function(_,...) return value(actual,...) end end
      return value
    end,
    __newindex=function(_,key,value) actual[key]=actualObject(value) end
  })
  sessionWrappers[actual]=wrapped
  return wrapped
end
local sceneWrappers = setmetatable({}, {__mode='k'})
function __cutscene_wrap_scene(actual)
  if actual == nil then return nil end
  if sceneWrappers[actual] ~= nil then return sceneWrappers[actual] end
  local wrapped = setmetatable({__actual=actual}, {
    __index=function(_,key)
      if key == 'Add' or key == 'add' then return function(_,entity) runtime:AddEntity(actual,actualObject(entity)) end end
      if key == 'Remove' or key == 'remove' then return function(_,entity) runtime:RemoveEntity(actual,actualObject(entity)) end end
      if key == 'Session' then return wrapSession(runtime:ReadProjectedMember(actual,key)) end
      if runtime:HasProjectedMember(actual,key) then return runtime:ReadProjectedMember(actual,key) end
      local value=actual[key]
      if isNativeMethod(value) then
        value=__everest_wrap_instance_method(actual,key,value)
        return function(_,...) return value(actual,...) end
      end
      return value
    end,
    __newindex=function(_,key,value)
      if runtime:HasProjectedMember(actual,key) then runtime:WriteProjectedMember(actual,key,value) else actual[key]=value end
    end
  })
  sceneWrappers[actual]=wrapped
  return wrapped
end
function __cutscene_wrap_entity(actual)
  return setmetatable({__actual=actual}, {
    __index=function(_,key)
      if key == 'Add' or key == 'add' then return function(_,component) actual:Add(actualObject(component)); return component end end
      key=runtime:ResolveEntityMemberName(actual,key)
      if key=='EndCutscene' then return function(_,level,removeSelf) return actual:EndCutscene(actualObject(level),removeSelf ~= false) end end
      if key=='Appear' and runtime:IsBadelineDummy(actual) then return function(_,level,silent) return runtime:AppearBadeline(actual,actualObject(level),silent == true) end end
      local value=actual[key]
      if isNativeMethod(value) then return function(_,...)
        if select('#',...) == 0 then return value(actual) end
        return value(actual,actualArguments(...))
      end end
      return value
    end,
    __newindex=function(_,key,value)
      key=runtime:ResolveEntityMemberName(actual,key)
      actual[key]=value
    end
  })
end";
    }
}
