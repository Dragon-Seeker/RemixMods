using System;
using System.Linq;
using UnityEngine;
using MonoMod.Cil;
using Mono.Cecil.Cil;
using HUD;
using System.Collections.Generic;
using MoreSlugcats;

namespace SplitScreenCoop
{
    public partial class SplitScreenCoop
    {
        public delegate AbstractCreature orig_get_FirstAlivePlayer(RainWorldGame self);
        public AbstractCreature get_FirstAlivePlayer(orig_get_FirstAlivePlayer orig, RainWorldGame self)
        {
            if (selfSufficientCoop) return self.session.Players.FirstOrDefault(p => !PlayerDeadOrMissing(p)) ?? orig(self); // null bad lmao
            return orig(self);
        }

        private void SaveState_SessionEnded(ILContext il)
        {
            try
            {
                var c = new ILCursor(il);
                c.GotoNext(MoveType.Before, // go to food clamped
                    i => i.MatchLdfld<SlugcatStats>("maxFood"),
                    i => i.MatchCallOrCallvirt(out _), // custom.intclamp
                    i => i.MatchStfld<SaveState>("food")
                    );
                c.GotoPrev(MoveType.Before, // go to start of clamp block
                    i => i.MatchLdarg(0),
                    i => i.MatchLdarg(0),
                    i => i.MatchLdfld<SaveState>("food")
                    );
                var skip = c.IncomingLabels.First(); // a jump that skipped vanilla
                var vanilla = il.DefineLabel();
                c.GotoPrev(MoveType.After, i => i.MatchBr(out var lab) && lab.Target == skip.Target); // right before vanilla block
                c.MoveAfterLabels();
                c.Emit<SplitScreenCoop>(OpCodes.Ldsfld, "selfSufficientCoop");
                c.Emit(OpCodes.Brfalse, vanilla);
                c.Emit(OpCodes.Ldarg_0);
                c.Emit(OpCodes.Ldarg_1);
                c.EmitDelegate(CoopSessionFood);
                c.Emit(OpCodes.Br, skip);
                c.MarkLabel(vanilla);
            }
            catch (Exception e)
            {
                Logger.LogError(e);
                throw;
            }
        }

        void CoopSessionFood(SaveState ss, RainWorldGame game)
        {
            Logger.LogInfo($"CoopSessionFood was {ss.food}");
            ss.food += (game.Players.Where(p => !PlayerDeadOrMissing(p)).OrderByDescending(p => (p.realizedCreature as Player).FoodInRoom(false)).First().realizedCreature as Player).FoodInRoom(true);
            Logger.LogInfo($"CoopSessionFood became {ss.food}");
        }

        private void RainWorldGame_ctor2(ILContext il)
        {
            try
            {
                var c = new ILCursor(il);
                int loc = 0;
                c.GotoNext(MoveType.Before, // go to food
                    i => i.MatchLdfld<SaveState>("food"),
                    i => i.MatchStloc(out loc)
                    );

                c.GotoNext(MoveType.Before, // go to start of 'vanilla while'
                    i => i.MatchBr(out _)
                    );
                var vanilla = il.DefineLabel();
                c.MoveAfterLabels();
                c.Emit<SplitScreenCoop>(OpCodes.Ldsfld, "selfSufficientCoop");
                c.Emit(OpCodes.Brfalse, vanilla);
                c.Emit(OpCodes.Ldarg_0);
                c.Emit(OpCodes.Ldloc, loc);
                c.EmitDelegate(CoopStartingFood);
                c.Emit(OpCodes.Stloc, loc);
                c.MarkLabel(vanilla);
            }
            catch (Exception e)
            {
                Logger.LogError(e);
                throw;
            }
        }

        int CoopStartingFood(RainWorldGame game, int foodInSave)
        {
            Logger.LogInfo("CoopStartingFood");
            if (selfSufficientCoop && coopSharedFood)
            {
                if (foodInSave > 0)
                {
                    Logger.LogInfo($"CoopStartingFood shared {foodInSave} food");
                    game.session.Players.ForEach(p => { (p.state as PlayerState).foodInStomach = foodInSave; });
                    Logger.LogInfo($"CoopStartingFood p0 has {(game.session.Players[0].state as PlayerState).foodInStomach} food");
                    foodInSave = 0;
                }
            }
            return foodInSave;
        }


        private int RegionGate_PlayersInZone(On.RegionGate.orig_PlayersInZone orig, RegionGate self)
        {
            if (selfSufficientCoop)
            {
                // vanilla logic was just wrong alltogether?
                if (self.room.game.Players.Any(p => (!PlayerDeadOrMissing(p) && p.Room != self.room.abstractRoom))) return -1;
            }
            return orig(self);
        }


        private void Creature_FlyAwayFromRoom(On.Creature.orig_FlyAwayFromRoom orig, Creature self, bool carriedByOther)
        {
            if (self is Player pl && selfSufficientCoop && !pl.isNPC && carriedByOther) pl.Die();
            orig(self, carriedByOther);
        }

        // vanilla assumes players[0].realizedcreature not null
        private void RegionGate_get_MeetRequirement(ILContext il)
        {
            try
            {
                var c = new ILCursor(il);
                c.GotoNext(MoveType.Before, // StorySession If
                    i => i.MatchCallOrCallvirt<RainWorldGame>("get_Players"),
                    i => i.MatchLdcI4(0),
                    i => i.MatchCallOrCallvirt(out _), // get_item
                    i => i.MatchCallOrCallvirt<AbstractCreature>("get_realizedCreature")
                    );

                c.Index++;
                c.EmitDelegate((List<AbstractCreature> players) =>
                {
                    if (selfSufficientCoop)
                    {
                        return players.Where(p => (!PlayerDeadOrMissing(p))).ToList();
                    }
                    return players;
                });
            }
            catch (Exception e)
            {
                Logger.LogError(e);
                throw;
            }
        }

        // we are multiplayer
        private bool ProcessManager_IsGameInMultiplayerContext(On.ProcessManager.orig_IsGameInMultiplayerContext orig, ProcessManager self)
        {
            if (self.currentMainLoop is RainWorldGame game && game.IsStorySession && selfSufficientCoop) return true;
            return orig(self);
        }

        // Jolly still doesn't know how to do it proper
        private void RoomCamera_ChangeCameraToPlayer(On.RoomCamera.orig_ChangeCameraToPlayer orig, RoomCamera self, AbstractCreature cameraTarget)
        {
            Logger.LogInfo("RoomCamera_ChangeCameraToPlayer");
            if(!allowCameraSwapping && self.game.cameras.Length >= self.game.Players.Count) // prevent camera switching
            {
                return;
            }
            if (cameraTarget.realizedCreature is Player player)
            {
                AssignCameraToPlayer(self, player);
            }
            orig(self, cameraTarget);
        }

        private void Player_TriggerCameraSwitch(ILContext il)
        {
            try
            {
                var c = new ILCursor(il);
                c.GotoNext(MoveType.Before, // roomcam = 
                    i => i.MatchStloc(out var _)
                    );

                // rcam on stack
                c.Emit(OpCodes.Ldarg_0);
                c.EmitDelegate((RoomCamera rc, Player self) => // use the cam that's my own or a cam that is free or cam0, in this order
                {
                    var wasrc = rc;
                    rc = self.abstractCreature.world.game.cameras.FirstOrDefault(c => c.followAbstractCreature == self.abstractCreature);
                    if (rc == null) rc = self.abstractCreature.world.game.cameras.FirstOrDefault(c => IsCreatureDead(c.followAbstractCreature));
                    if (rc == null) rc = self.abstractCreature.world.game.cameras.FirstOrDefault(c => c.cameraNumber == self.playerState.playerNumber);
                    if (rc == null) rc = wasrc;
                    return rc;
                });
            }
            catch (Exception e)
            {
                Logger.LogError(e);
                throw;
            }
        }

        private void Player_TriggerCameraSwitch1(On.Player.orig_TriggerCameraSwitch orig, Player self)
        {
            if (CurrentSplitMode == SplitMode.Split4Screen && self.abstractCreature.world.game.cameras.Length > self.playerState.playerNumber)
                ToggleCameraZoom(self.abstractCreature.world.game.cameras[self.playerState.playerNumber]);
            orig(self);
        }

        // $15
        private void Player_ctor(On.Player.orig_ctor orig, Player self, AbstractCreature abstractCreature, World world)
        {
            orig(self, abstractCreature, world);
            self.cameraSwitchDelay = -1; // there, you can have your fix, for free
        }

        private void SlugcatSelectMenu_StartGame(On.Menu.SlugcatSelectMenu.orig_StartGame orig, Menu.SlugcatSelectMenu self, SlugcatStats.Name storyGameCharacter)
        {
            if (selfSufficientCoop)
            {
                Logger.LogInfo("Requesting p2 rewired signin");
                self.manager.rainWorld.RequestPlayerSignIn(1, null);
            }
            orig(self, storyGameCharacter);
        }

        // Don't move a player when they have map opened
        private void Player_JollyInputUpdate(On.Player.orig_JollyInputUpdate orig, Player self)
        {
            orig(self);
            self.standStillOnMapButton = true;
        }
        
        /// <summary>
        /// Code below is used to send TextPrompts and Dialog messages to the other players hud's due to being different screens
        /// </summary>
        //--
        
        private bool textPromptLock;

        private void TextPrompt_AddMessage(On.HUD.TextPrompt.orig_AddMessage_string_int_int_bool_bool orig, TextPrompt self, string text, int wait, int time, bool darken, bool hideHud)
        {
            try
            {
                if (textPromptLock)
                {
                    orig(self, text, wait, time, darken, hideHud);
                }
                else
                {
                    textPromptLock = true;

                    orig(self, text, wait, time, darken, hideHud);
                    
                    AttemptCallOnTextPrompt(prompt => prompt.AddMessage(text, wait, time, darken, hideHud), self);

                    textPromptLock = false;
                }
            }
            catch (Exception e)
            {
                Logger.LogError("Unable to attempt to send TextPrompt Messsage!!");
                Logger.LogError(e.Message);
                Logger.LogError(e.StackTrace);
            }
            
        }
        
        private void TextPrompt_AddMessageWithList(On.HUD.TextPrompt.orig_AddMessage_string_int_int_bool_bool_float_List1 orig, TextPrompt self, string text,
            int wait,
            int time,
            bool darken,
            bool hideHud,
            float iconsX,
            List<MultiplayerUnlocks.SandboxUnlockID> iconIDs)
        {
            try
            {
                if (textPromptLock)
                {
                    orig(self, text, wait, time, darken, hideHud, iconsX, iconIDs);
                }
                else
                {
                    textPromptLock = true;
                    
                    orig(self, text, wait, time, darken, hideHud, iconsX, iconIDs);
                    
                    AttemptCallOnTextPrompt(
                        prompt => prompt.AddMessage(text, wait, time, darken, hideHud, iconsX, iconIDs.ToList()), self);
                    
                    textPromptLock = false;
                }

                
            }
            catch (Exception e)
            {
                Logger.LogError("Unable to attempt to send TextPrompt Messsage!!");
                Logger.LogError(e.Message);
                Logger.LogError(e.StackTrace);
            }
        }
        
        private void TextPrompt_AddMusic(On.HUD.TextPrompt.orig_AddMusicMessage orig, TextPrompt self, string text, int time)
        {
            try
            {
                if (textPromptLock)
                {
                    orig(self, text, time);
                }
                else
                {
                    textPromptLock = true;
                    
                    orig(self, text, time);
            
                    AttemptCallOnTextPrompt(prompt => prompt.AddMusicMessage(text, time), self);
                    
                    textPromptLock = false;
                }
            }
            catch (Exception e)
            {
                Logger.LogError("Unable to attempt to send TextPrompt Messsage!!");
                Logger.LogError(e.Message);
                Logger.LogError(e.StackTrace);
            }
        }

        private void AttemptCallOnTextPrompt(Action<TextPrompt> action, TextPrompt prime)
        {
            try
            {
                if (prime.hud.owner is Player player && player.playerState != null &&
                    player.playerState.playerNumber == 0)
                {
                    if (player.abstractCreature == null || player.abstractCreature.world == null ||
                        player.abstractCreature.world.game == null ||
                        player.abstractCreature.world.game.cameras == null) return;

                    var cameras = player.abstractCreature.world.game.cameras;

                    for (var i = 1; i < cameras.Length; i++)
                    {
                        var cameraHud = cameras[i].hud;

                        if (cameraHud == null) continue;

                        var prompt = cameraHud.textPrompt;

                        if (prime.hud != cameraHud)
                        {
                            action(prompt);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Logger.LogError("Unable to attempt to send TextPrompt Messsage!!");
                Logger.LogError(e.Message);
                Logger.LogError(e.StackTrace);
            }
        }
        
        //--

        private bool messageLock;

        private void DialogBox_Interrupt(On.HUD.DialogBox.orig_Interrupt orig, DialogBox self, string text, int extraLinger)
        {
            try
            {
                if (messageLock)
                {
                    orig(self, text, extraLinger);
                }
                else
                {
                    messageLock = true;
                
                    orig(self, text, extraLinger);
                
                    AttemptCallOnDialogBox(box => box.Interrupt(text, extraLinger), self);
                
                    messageLock = false;
                }
            }
            catch (Exception e)
            {
                Logger.LogError("Unable to attempt to send Dialog Messsage!!");
                Logger.LogError(e.Message);
                Logger.LogError(e.StackTrace);
            }
        }
        
        private void DialogBox_NewMessage(On.HUD.DialogBox.orig_NewMessage_string_int orig, DialogBox self, string text, int extraLinger)
        {
            try {
                if (messageLock)
                {
                    orig(self, text, extraLinger);
                }
                else
                {
                    messageLock = true;
                
                    orig(self, text, extraLinger);
                
                    AttemptCallOnDialogBox(box => box.NewMessage(text, extraLinger), self);
                
                    messageLock = false;
                }
            }
            catch (Exception e)
            {
                Logger.LogError("Unable to attempt to send Dialog Messsage!!");
                Logger.LogError(e.Message);
                Logger.LogError(e.StackTrace);
            }
        }
        
        private void DialogBox_NewMessageOrintate(On.HUD.DialogBox.orig_NewMessage_string_float_float_int orig, DialogBox self, string text, float xOrientation, float yPos, int extraLinger)
        {
            try {
                if (messageLock)
                {
                    orig(self, text, xOrientation, yPos, extraLinger);
                }
                else
                {
                    messageLock = true;
                
                    orig(self, text, xOrientation, yPos, extraLinger);
                
                    AttemptCallOnDialogBox(box => box.NewMessage(text, xOrientation, yPos, extraLinger), self);
                
                    messageLock = false;
                }
            }
            catch (Exception e)
            {
                Logger.LogError("Unable to attempt to send Dialog Messsage!!");
                Logger.LogError(e.Message);
                Logger.LogError(e.StackTrace);
            }
        }
        
        private void AttemptCallOnDialogBox(Action<DialogBox> action, DialogBox prime)
        {
            try
            {
                if (prime.hud.owner is Player player && player.playerState != null &&
                    player.playerState.playerNumber == 0)
                {
                    if (player.abstractCreature == null || player.abstractCreature.world == null ||
                        player.abstractCreature.world.game == null ||
                        player.abstractCreature.world.game.cameras == null) return;

                    var cameras = player.abstractCreature.world.game.cameras;

                    for (var i = 1; i < cameras.Length; i++)
                    {
                        var cameraHud = cameras[i].hud;

                        if (cameraHud == null) continue;

                        var dialogBox = cameraHud.dialogBox;

                        if (dialogBox == null)
                        {
                            dialogBox = cameraHud.InitDialogBox();
                        }

                        if (prime.hud != cameraHud)
                        {
                            action(dialogBox);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Logger.LogError("Unable to attempt to send Dialog Messsage!!");
                Logger.LogError(e.Message);
                Logger.LogError(e.StackTrace);
            }
        }
        
        
        //InitChatLog
        //DisposeChatLog
        
        private bool chatLogLock;
        
        private ChatLogDisplay HUD_InitChatLog(On.HUD.HUD.orig_InitChatLog orig, HUD.HUD self, string[] messages)
        {
            try {
                if (chatLogLock)
                {
                    return orig(self, messages);
                }
                else
                {
                    chatLogLock = true;
                
                    var display = orig(self, messages);
                
                    if (self.owner is Player player && player.playerState != null &&
                        player.playerState.playerNumber == 0)
                    {
                        if (player.abstractCreature != null && player.abstractCreature.world != null &&
                            player.abstractCreature.world.game != null &&
                            player.abstractCreature.world.game.cameras != null)
                        {
                            var cameras = player.abstractCreature.world.game.cameras;

                            for (var i = 1; i < cameras.Length; i++)
                            {
                                var cameraHud = cameras[i].hud;

                                if (cameraHud == null) continue;

                                var chatLog = cameraHud.chatLog;

                                if (chatLog == null && self != cameraHud)
                                {
                                    cameraHud.InitChatLog(messages.ToArray());
                                }
                            }
                        }
                    }
                
                    chatLogLock = false;

                    return display;
                }
            }
            catch (Exception e)
            {
                Logger.LogError("Unable to attempt to send ChatLog Messsage!!");
                Logger.LogError(e.Message);
                Logger.LogError(e.StackTrace);
            }

            return orig(self, messages);
        }
        
        private void HUD_DisposeChatLog(On.HUD.HUD.orig_DisposeChatLog orig, HUD.HUD self)
        {
            try {
                if (chatLogLock)
                {
                    orig(self);
                }
                else
                {
                    chatLogLock = true;
                
                    orig(self);
                
                    if (self.owner is Player player && player.playerState != null &&
                        player.playerState.playerNumber == 0)
                    {
                        if (player.abstractCreature == null || player.abstractCreature.world == null ||
                            player.abstractCreature.world.game == null ||
                            player.abstractCreature.world.game.cameras == null) return;

                        var cameras = player.abstractCreature.world.game.cameras;

                        for (var i = 1; i < cameras.Length; i++)
                        {
                            var cameraHud = cameras[i].hud;

                            if (cameraHud == null) continue;

                            if (self != cameraHud)
                            {
                                cameraHud.DisposeChatLog();
                            }
                        }
                    }
                
                    chatLogLock = false;
                }
            }
            catch (Exception e)
            {
                Logger.LogError("Unable to attempt to send ChatLog Messsage!!");
                Logger.LogError(e.Message);
                Logger.LogError(e.StackTrace);
            }
        }
        
        private void ChatLogDisplay_NewMessage(On.MoreSlugcats.ChatLogDisplay.orig_NewMessage_string_int orig, ChatLogDisplay self, string text, int extraLinger)
        {
            try {
                if (chatLogLock)
                {
                    orig(self, text, extraLinger);
                }
                else
                {
                    chatLogLock = true;
                
                    orig(self, text, extraLinger);
                
                    AttemptCallOnChatLogDisplay(box => box.NewMessage(text, extraLinger), self);
                
                    chatLogLock = false;
                }
            }
            catch (Exception e)
            {
                Logger.LogError("Unable to attempt to send ChatLog Messsage!!");
                Logger.LogError(e.Message);
                Logger.LogError(e.StackTrace);
            }
        }
        
        private void ChatLogDisplay_NewMessageOrintate(On.MoreSlugcats.ChatLogDisplay.orig_NewMessage_string_float_float_int orig, ChatLogDisplay self, string text, float xOrientation, float yPos, int extraLinger)
        {
            try {
                if (chatLogLock)
                {
                    orig(self, text, xOrientation, yPos, extraLinger);
                }
                else
                {
                    chatLogLock = true;
                
                    orig(self, text, xOrientation, yPos, extraLinger);
                
                    AttemptCallOnChatLogDisplay(box => box.NewMessage(text, xOrientation, yPos, extraLinger), self);
                
                    chatLogLock = false;
                }
            }
            catch (Exception e)
            {
                Logger.LogError("Unable to attempt to send ChatLog Messsage!!");
                Logger.LogError(e.Message);
                Logger.LogError(e.StackTrace);
            }
        }
        
        private void AttemptCallOnChatLogDisplay(Action<ChatLogDisplay> action, ChatLogDisplay prime)
        {
            try
            {
                if (prime.hud.owner is Player player && player.playerState != null &&
                    player.playerState.playerNumber == 0)
                {
                    if (player.abstractCreature == null || player.abstractCreature.world == null ||
                        player.abstractCreature.world.game == null ||
                        player.abstractCreature.world.game.cameras == null) return;

                    var cameras = player.abstractCreature.world.game.cameras;

                    for (var i = 1; i < cameras.Length; i++)
                    {
                        var cameraHud = cameras[i].hud;

                        if (cameraHud == null) continue;

                        var chatLog = cameraHud.chatLog;

                        if (chatLog == null)
                        {
                            chatLog = cameraHud.InitChatLog(new string[0]);
                        }

                        if (prime.hud != cameraHud)
                        {
                            action(chatLog);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Logger.LogError("Unable to attempt to send Dialog Messsage!!");
                Logger.LogError(e.Message);
                Logger.LogError(e.StackTrace);
            }
        }
        
        
        //--
    }
}
