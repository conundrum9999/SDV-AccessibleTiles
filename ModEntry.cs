﻿using StardewModdingAPI;
using StardewModdingAPI.Events;
using AccessibleTiles.Integrations;
using AccessibleTiles.Modules.GridMovement;
using StardewValley;
using System;
using AccessibleTiles.Modules.ObjectTracker;
using Microsoft.Xna.Framework.Input;

namespace AccessibleTiles {
    /// <summary>The mod entry point.</summary>
    public class ModEntry : Mod {

        public ModConfig? Config;
        public ModIntegrations? Integrations;

        public GridMovement? GridMovement;
        private ObjectTracker? ObjectTracker;

        public Boolean IsUsingPathfinding = false;

        public int? LastGridMovementDirection = null;
        public InputButton? LastGridMovementButtonPressed = null;
 
        int previousDebris = -999;
        int previousObjects = -999;
        int previousFurniture = -999;
        int previousResourceClumps = -999;
        int previousTerrainFeatures = -999;
        int previousLargeTerrainFeatures = -999;

        /*********
        ** Public methods
        *********/
        /// <summary>The mod entry point, called after the mod is first loaded.</summary>
        /// <param name="helper">Provides simplified APIs for writing mods.</param>
        public override void Entry(IModHelper helper) {

            this.Config = this.Helper.ReadConfig<ModConfig>()!;
            this.GridMovement = new GridMovement(this);
            this.ObjectTracker = new ObjectTracker(this, this.Config!);

            helper.Events.GameLoop.GameLaunched += GameLoop_GameLaunched;
            helper.Events.Input.ButtonPressed += Input_ButtonPressed;
            helper.Events.Input.ButtonsChanged += Input_ButtonsChanged;
            helper.Events.Player.Warped += Player_Warped;
            helper.Events.GameLoop.SaveLoaded += GameLoop_SaveLoaded;
            helper.Events.GameLoop.UpdateTicked += GameLoop_UpdateTicked;

        }

        public void Output(string text, bool say = false) {
            this.Monitor.Log(text + (!say ? " (Not Read)" : ""), say ? LogLevel.Info : LogLevel.Debug);
            if(say && Integrations is not null) {
                Integrations.SRSay(text);
            }
        }

        public ModConfig GetModConfig() {
            return this.Config!;
        }

        private void GameLoop_UpdateTicked(object? sender, UpdateTickedEventArgs e) {
            if(LastGridMovementButtonPressed != null) {

                SButton button = LastGridMovementButtonPressed.Value.ToSButton();
                if (Game1.activeClickableMenu == null && this.Config!.GridMovementActive && GridMovement is not null && !GridMovement.is_moving && !this.Config!.GridMovementOverrideKey.IsDown()  && (this.Helper.Input.IsDown(button) || this.Helper.Input.IsSuppressed(button))) {
                    GridMovement!.HandleGridMovement(LastGridMovementDirection!.Value, LastGridMovementButtonPressed!.Value);
                }
            }
            
            // Automatically refreshes the object tracker when the number of objects in current map changes.
            if (Game1.currentLocation != null && Config!.OTAutoRefreshing)
            {
                int currentDebris = Game1.currentLocation.debris.Count;
                int currentObjects = Game1.currentLocation.objects.Count();
                int currentFurniture = Game1.currentLocation.furniture.Count;
                int currentResourceClumps = Game1.currentLocation.resourceClumps.Count;
                int currentTerrainFeatures = Game1.currentLocation.terrainFeatures.Count();
                int currentLargeTerrainFeatures = Game1.currentLocation.largeTerrainFeatures.Count;
                
                if (previousDebris != currentDebris || previousObjects != currentObjects || previousFurniture != currentFurniture || previousResourceClumps != currentResourceClumps || previousTerrainFeatures != currentTerrainFeatures || previousLargeTerrainFeatures != currentLargeTerrainFeatures) {
                    previousDebris = currentDebris;
                    previousObjects = currentObjects;
                    previousFurniture = currentFurniture;
                    previousResourceClumps = currentResourceClumps;
                    previousTerrainFeatures = currentTerrainFeatures;
                    previousLargeTerrainFeatures = currentLargeTerrainFeatures;
                    
                    Output("Refreshing object tracker...", false);
                    ObjectTracker!.GetLocationObjects(reset_focus: false);
                }
            }
        }

        private void GameLoop_SaveLoaded(object? sender, SaveLoadedEventArgs e) {
            if (ObjectTracker is not null)
                ObjectTracker!.GetLocationObjects();
        }

        private void Player_Warped(object? sender, WarpedEventArgs e) {
            if (ObjectTracker is null || GridMovement is null)
                return;

            GridMovement!.PlayerWarped(sender, e);
            ObjectTracker!.GetLocationObjects(reset_focus: true);
        }

        private void Input_ButtonsChanged(object? sender, ButtonsChangedEventArgs e) {

            if (Game1.activeClickableMenu != null) return;

            if(this.Config!.ToggleGridMovementKey.JustPressed()) {
                this.Config!.GridMovementActive = !this.Config!.GridMovementActive;

                string output = "Grid Movement Status: " + (this.Config!.GridMovementActive ? "Active" : "Inactive");
                Output(output, true);

            } else {

                ObjectTracker.HandleKeys(sender, e);

            }

        }

        private void Input_ButtonPressed(object? sender, ButtonPressedEventArgs e) {

            if (Config is null)
                return;

            if (Game1.player.controller is not null) {

                if(this.Config!.OTCancelAutoWalking.JustPressed()) {
                    Game1.player.controller.endBehaviorFunction(Game1.player, Game1.currentLocation);
                }

                this.Helper.Input.Suppress(e.Button);
                return;

            }

            if (GridMovement is not null && GridMovement.is_warping == true) {
                this.Helper.Input.Suppress(e.Button);
            }

            e.Button.TryGetStardewInput(out InputButton keyboardButton);
            e.Button.TryGetController(out Buttons controllerButton);

            if (Game1.activeClickableMenu == null && !this.Config!.GridMovementOverrideKey.IsDown()) {

                if (this.Config!.GridMovementActive && GridMovement is not null) {
                    foreach (InputButton Button in Game1.options.moveUpButton) {
                        if (keyboardButton.Equals(Button) || controllerButton.Equals(Buttons.DPadUp)) {
                            GridMovement!.HandleGridMovement(0, Button);
                            this.Helper.Input.Suppress(e.Button);
                        }
                    }
                    foreach (InputButton Button in Game1.options.moveRightButton) {
                        if (keyboardButton.Equals(Button) || controllerButton.Equals(Buttons.DPadRight)) {
                            GridMovement!.HandleGridMovement(1, Button);
                            this.Helper.Input.Suppress(e.Button);
                        }
                    }
                    foreach (InputButton Button in Game1.options.moveDownButton) {
                        if (keyboardButton.Equals(Button) || controllerButton.Equals(Buttons.DPadDown)) {
                            GridMovement!.HandleGridMovement(2, Button);
                            this.Helper.Input.Suppress(e.Button);
                        }
                    }
                    foreach (InputButton Button in Game1.options.moveLeftButton) {
                        if ((keyboardButton.Equals(Button) || controllerButton.Equals(Buttons.DPadLeft)) && GridMovement is not null) {
                            GridMovement!.HandleGridMovement(3, Button);
                            this.Helper.Input.Suppress(e.Button);
                        }
                    }
                }
            }

        }

        private void GameLoop_GameLaunched(object? sender, GameLaunchedEventArgs e) {

            this.Integrations = new ModIntegrations(this);

        }

    }
}