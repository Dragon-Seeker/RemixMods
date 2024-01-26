using System;
using System.Linq;
using UnityEngine;
using MonoMod.Cil;
using Mono.Cecil.Cil;
using HUD;
using System.Collections.Generic;

namespace SplitScreenCoop
{
    public partial class SplitScreenCoop
    {
        /// <summary>
        /// Creates extra RoomRealizer for p2
        /// </summary>
        private void MakeRealizer2(RainWorldGame self)
        {
            Logger.LogInfo("MakeRealizer2");
            if (self.session.Players.Count < 2 || self.roomRealizer == null) return;
            var player = self.session.Players.FirstOrDefault(p => p != self.roomRealizer.followCreature);
            if (player == null) return;
            Logger.LogInfo("MakeRealizer2 making RoomRealizer");
            realizer2 = new RoomRealizer(player, self.world)
            {
                realizedRooms = self.roomRealizer.realizedRooms,
                recentlyAbstractedRooms = self.roomRealizer.recentlyAbstractedRooms,
                realizeNeighborCandidates = self.roomRealizer.realizeNeighborCandidates
            };
        }

        private void MakeRealizers(RainWorldGame self)
        {
            realizers.Clear();
            
            int maxPlayerCount = self.session.Players.Count;
            
            Logger.LogInfo("Attempt to make more Realizer!");
            
            if (maxPlayerCount < 2 || self.roomRealizer == null) return;

            var primaryRealizer = self.roomRealizer;
            
            for (int i = 1; i < maxPlayerCount; i++)
            {
                var player = self.session.Players[i];
                
                realizers.Add(new RoomRealizer(player, self.world)
                {
                    realizedRooms = primaryRealizer.realizedRooms,
                    recentlyAbstractedRooms = primaryRealizer.recentlyAbstractedRooms,
                    realizeNeighborCandidates = primaryRealizer.realizeNeighborCandidates
                });
            }
            
            Logger.LogInfo("Finished making extra realizers!");
        }

        /// <summary>
        /// Realizer2 in new world
        /// </summary>
        public void OverWorld_WorldLoaded(On.OverWorld.orig_WorldLoaded orig, OverWorld self)
        {
            ConsiderColapsing(self.game, true);
            orig(self);
            if (!addRealizerPerPlayer && realizer2 != null) MakeRealizer2(self.game);
            if (addRealizerPerPlayer && !realizers.isEmpty()) MakeRealizers(self.game);
        }

        /// <summary>
        /// Room realizers that aren't the main one re-assigning themselves to cameras[0].followcreature
        /// dont reasign if cam.followcreature is null, you dumb fuck
        /// </summary>
        public void RoomRealizer_Update(ILContext il)
        {
            try
            {
                // skip this.followCreature = cam[0].followCreature if this != game.roomRealizer || game.cam[0].follow==null || would switch to follow already followed
                var c = new ILCursor(il);
                c.GotoNext(MoveType.Before,
                    i => i.MatchStfld<RoomRealizer>("followCreature"),
                    i => i.MatchLdarg(0),
                    i => i.MatchLdfld<RoomRealizer>("followCreature"));
                c.Index++;
                c.MoveAfterLabels();
                var skip = c.MarkLabel();
                c.GotoPrev(MoveType.Before,
                    i => i.MatchLdarg(0),
                    i => i.MatchLdarg(0),
                    i => i.MatchLdfld<RoomRealizer>("world"));
                c.Emit(OpCodes.Ldarg_0);
                c.EmitDelegate((RoomRealizer self) =>
                {
                    RainWorldGame game = self.world?.game;
                    if(self != game?.roomRealizer /* I'm realizer2 */|| game?.cameras[0].followAbstractCreature == null /* or I'd assign null */)return true;

                    if (addRealizerPerPlayer)
                    {
                        for (var i = 0; i < realizers.Count; i++)
                        {
                            if (realizerCheck(game, self, realizers[i]))
                            {
                                return true;
                            }
                        }
                    }
                    else
                    {
                        if (realizerCheck(game, self, realizer2))
                        {
                            return true; // then don't
                        }
                    }
                    
                    return false;
                });
                c.Emit(OpCodes.Brtrue, skip);
            }
            catch (Exception e)
            {
                Logger.LogError(e);
                throw;
            }
        }

        public bool realizerCheck(RainWorldGame game, RoomRealizer self, RoomRealizer other)
        {
            return self.followCreature != null && other != null && game.cameras[0].followAbstractCreature == other.followCreature /* or I'd reassign to a creature that is followed by re2 */;
        }

        public bool rrNestedLock;
        /// <summary>
        /// Realizers work together
        /// </summary>
        public bool RoomRealizer_CanAbstractizeRoom(On.RoomRealizer.orig_CanAbstractizeRoom orig, RoomRealizer self, RoomRealizer.RealizedRoomTracker tracker)
        {
            var r = orig(self, tracker);

            if (!rrNestedLock) // if other exists, not recursive
            {
                var game = self?.world?.game;
                
                if (addRealizerPerPlayer)
                {
                    for (var i = 0; i < realizers.Count(); i++)
                    {
                        var otherRealizer = realizers[i];

                        checkOtherRealizerAbstracize(game, self, otherRealizer, tracker, ref r);
                    }
                }
                else if(realizer2 != null)
                {
                    checkOtherRealizerAbstracize(game, self, realizer2, tracker, ref r);
                }
            }

            /*if (!rrNestedLock && realizer2 != null) // if other exists, not recursive
            {
                RoomRealizer other;
                RoomRealizer prime = self?.world?.game?.roomRealizer;
                if (prime == self) other = realizer2;
                else other = prime;

                if (other != null && other.followCreature != null)
                {
                    rrNestedLock = true;
                    r = r && other.CanAbstractizeRoom(tracker);
                    rrNestedLock = false;
                }
            }*/
            return r;
        }

        public void checkOtherRealizerAbstracize(RainWorldGame game, RoomRealizer self, RoomRealizer other, RoomRealizer.RealizedRoomTracker tracker, ref bool r)
        {
            RoomRealizer prime = game.roomRealizer;
            if (prime != self) other = prime;
            
            if (other != null && other.followCreature != null)
            {
                rrNestedLock = true;
                r = r && other.CanAbstractizeRoom(tracker);
                rrNestedLock = false;
            }
        }
    }
    
    static class ListExtension {
        public static bool isEmpty<T>(this List<T> list)
        {
            return list.Count == 0;
        }
    }
}
