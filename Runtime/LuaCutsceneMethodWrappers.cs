using System.Numerics;
using Celeste.Game.Execution.Levels;
using Celeste.Simulation;
using Celeste.Simulation.Gameplay.Player.Intro;

namespace Celeste.Mods.LuaCutscenes.Runtime
{
    public sealed class LuaCutsceneMethodWrappers
    {
        public void TeleportTo(OriginalLevelSceneRoot level, RoomPlayerSession player, string room,
            PlayerIntroType introType = PlayerIntroType.Transition, Vector2? nearestSpawn = null)
        {
            level.Scheduler.OnEndOfFrame += () => level.TeleportTo(player, room, introType, nearestSpawn);
        }

        public void InstantTeleport(OriginalLevelSceneRoot level, RoomPlayerSession player, string room,
            bool sameRelativePosition, float positionX, float positionY)
        {
            if (string.IsNullOrEmpty(room))
            {
                Vector2 displacement = new Vector2(positionX, positionY) - player.Position;
                player.Position = new Vector2(positionX, positionY);
                level.Camera.Position += displacement;
                player.Hair.MoveHairBy(displacement);
                return;
            }

            level.Scheduler.OnEndOfFrame += () =>
            {
                Vector2 oldOffset = level.LevelOffset;
                Vector2 playerOffset = player.Position - oldOffset;
                Vector2 cameraOffset = level.Camera.Position - oldOffset;
                int facing = player.Facing;
                level.RemoveEntity(player);
                level.UnloadLevel();
                level.Session.Level = room;
                level.Session.RespawnPoint = level.GetSpawnPoint(new Vector2(level.Bounds.Left, level.Bounds.Top));
                level.Session.FirstLevel = false;
                level.LoadLevel(PlayerIntroType.Transition);
                Vector2 displacement = sameRelativePosition
                    ? Vector2.Zero
                    : new Vector2(positionX, positionY) - level.LevelOffset - playerOffset;
                level.Camera.Position = level.LevelOffset + cameraOffset + displacement;
                level.Scheduler.QueueSourceEntity(player);
                player.Position = sameRelativePosition
                    ? level.LevelOffset + playerOffset
                    : new Vector2(positionX, positionY);
                player.Data.Facing = facing;
                player.Hair.MoveHairBy(level.LevelOffset - oldOffset + displacement);
                level.Wipe?.Cancel();
            };
        }
    }
}
