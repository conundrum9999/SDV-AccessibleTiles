using Microsoft.Xna.Framework;
using StardewModdingAPI.Events;
using StardewValley;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Timers;

namespace AccessibleTiles.Modules.ObjectTracker
{
    internal class ObjectTracker
    {
        private bool sortByProxy = true;

        private readonly ModEntry Mod;
        private readonly ModConfig ModConfig;

        private TrackedObjects? TrackedObjects;

        private Vector2? LastTargetedTile = null;

        public string? SelectedCategory;
        public string? SelectedObject;

        private readonly int msBetweenCheckingPathfindingController = 1000;
        private readonly Timer checkPathingTimer = new();
        private int pathfindingRetryAttempts = 0;

        private readonly Timer footstepTimer = new();

        public ObjectTracker(ModEntry mod, ModConfig? config)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config), "The provided config must not be null.");

            this.Mod = mod;
            this.ModConfig = config;

            checkPathingTimer.Interval = msBetweenCheckingPathfindingController;
            checkPathingTimer.Elapsed += CheckPathingTimer_Elapsed;

            footstepTimer.Interval = mod.GridMovement!.minMillisecondsBetweenSteps + 50;
            footstepTimer.Elapsed += FootstepTimer_Elapsed;
        }

        public void HandleKeys(object? sender, ButtonsChangedEventArgs e)
        {
            if (ModConfig.OTCycleUpCategory.JustPressed())
            {
                Cycle(cycleCategories: true, back: true);
            }
            else if (ModConfig.OTCycleDownCategory.JustPressed())
            {
                Cycle(cycleCategories: true);
            }
            else if (ModConfig.OTCycleUpObject.JustPressed())
            {
                Cycle(cycleCategories: false, back: true);
            }
            else if (ModConfig.OTCycleDownObject.JustPressed())
            {
                Cycle(cycleCategories: false);
            }
            else if (ModConfig.OTReadSelectedObject.JustPressed())
            {
                GetLocationObjects(resetFocus: false);
                ReadCurrentlySelectedObject();
            }
            else if (ModConfig.OTSwitchSortingMode.JustPressed())
            {
                this.sortByProxy = !this.sortByProxy;

                Mod.Output("Sort By Proximity: " + (sortByProxy ? "Enabled" : "Disabled"), true);
                GetLocationObjects(resetFocus: false);
            }

            if (ModConfig.OTMoveToSelectedObject.JustPressed())
            {
                GetLocationObjects(resetFocus: false);
                MoveToCurrentlySelectedObject();
            }
            else if (ModConfig.OTReadSelectedObjectTileLocation.JustPressed())
            {
                GetLocationObjects(resetFocus: false);
                ReadCurrentlySelectedObject(readTileOnly: true);
            }
        }

        private void FootstepTimer_Elapsed(object? sender, ElapsedEventArgs e)
        {
            Farmer player = Game1.player;
            if (player.controller == null) return;

            player.currentLocation.playTerrainSound(player.getTileLocation());
        }

        private void CheckPathingTimer_Elapsed(object? sender, ElapsedEventArgs e)
        {
            Farmer player = Game1.player;
            GameLocation location = Game1.currentLocation;

            if (player.controller != null && (Game1.activeClickableMenu == null || Game1.IsMultiplayer))
            {
                if (player.controller.timerSinceLastCheckPoint > 500)
                {
                    if (IsFocusValid() && pathfindingRetryAttempts < 5)
                    {
                        pathfindingRetryAttempts++;

                        Mod.Output($"Attempting to restart pathfinding attempt {pathfindingRetryAttempts}");

                        if (pathfindingRetryAttempts == 1)
                        {
                            Mod.Output($"Target unreachable, re-trying...", true);

                            if (TrackedObjects != null && TrackedObjects.GetObjects().TryGetValue("characters", out var characters))
                            {
                                foreach (var kvp in characters)
                                {
                                    NPC? character = kvp.Value.character;

                                    if (character is not null && character.getTileLocation() == player.getTileLocation() && !character.IsInvisible)
                                    {
                                        character.IsInvisible = true;
                                        _ = SetCharacterVisible(character);
                                    }
                                }
                            }
                        }
                        else if (pathfindingRetryAttempts == 5)
                        {
                            pathfindingRetryAttempts = 0;
                            Mod.Output("Pathfinding forcibly stopped. Target Lost.", true);

                            player.controller.endBehaviorFunction(player, location);
                            GetLocationObjects(resetFocus: true);
                            player.controller = null;
                        }
                    }
                }
            }
        }

        private static async Task SetCharacterVisible(NPC npc)
        {
            await Task.Delay(100);
            npc.IsInvisible = false;
        }

        private bool IsFocusValid()
        {
            if (SelectedCategory != null && SelectedObject != null)
                return TrackedObjects?.GetObjects().ContainsKey(SelectedCategory) == true && TrackedObjects.GetObjects()[SelectedCategory].ContainsKey(SelectedObject);
            return false;
        }

        private bool IsValidSelection()
        {
            return TrackedObjects != null && SelectedCategory != null && SelectedObject != null;
        }

        private void MoveToCurrentlySelectedObject()
        {
            Mod.Output("Attempt pathfinding.", true);
            pathfindingRetryAttempts = 0;

            if (IsFocusValid())
            {
                ReadCurrentlySelectedObject();
            }

            Farmer player = Game1.player;
            SpecialObject? sObject = GetCurrentlySelectedObject();

            #pragma warning disable IDE0059 // Unnecessary assignment of a value
            Vector2 playerTile = player.getTileLocation();
            #pragma warning restore IDE0059 // Unnecessary assignment of a value
            Vector2? sObjectTile = (sObject != null) ? sObject.TileLocation : (Vector2?)null;

            Vector2? closestTile = sObject is not null ? (sObject.PathfindingOverride != null ? Utility.GetClosestTilePath((Vector2)sObject.PathfindingOverride) : Utility.GetClosestTilePath(sObjectTile)) : null;

            if (closestTile != null)
            {
                Mod.Output($"Moving to {closestTile.Value.X}-{closestTile.Value.Y}.", true);
                StartPathfinding(player, Game1.currentLocation, closestTile.Value.ToPoint());
            }
            else
            {
                Mod.Output("Could not find path to object.", true);
            }
        }

        private void StartPathfinding(Farmer player, GameLocation location, Point targetTile, int direction = -1)
        {
            LastTargetedTile = targetTile.ToVector2();
            StopTimers();
            StartTimers();

            player.controller = new PathFindController(player, location, targetTile, direction, (Character farmer, GameLocation location) =>
            {
                StopPathfinding();
            });
        }

        private void StopPathfinding()
        {
            Farmer player = Game1.player;
            StopTimers();

            ReadCurrentlySelectedObject();
            Utility.FixCharacterMovement();
            player.controller = null;

            FacePlayerToLastTargetedTile();
        }

        private void StartTimers()
        {
            footstepTimer.Start();
            checkPathingTimer.Start();
        }

        private void StopTimers()
        {
            footstepTimer.Stop();
            checkPathingTimer.Stop();
        }

        private void FacePlayerToLastTargetedTile()
        {
            Farmer player = Game1.player;

            if (LastTargetedTile == null) return;

            string faceDirection = Utility.GetDirection(player.getTileLocation(), LastTargetedTile.Value);
            switch (faceDirection)
            {
                case "North":
                    player.faceDirection(0);
                    break;
                case "East":
                    player.faceDirection(1);
                    break;
                case "South":
                    player.faceDirection(2);
                    break;
                case "West":
                    player.faceDirection(3);
                    break;
            }
        }


        private void ReadCurrentlySelectedObject(bool readTileOnly = false)
        {
            if (!IsValidSelection())
                return;

            Farmer player = Game1.player;
            SpecialObject? sObject = GetCurrentlySelectedObject();

            Vector2 playerTile = player.getTileLocation();
            Vector2? sObjectTile = sObject?.TileLocation;

            
            if (sObject != null)
            {
                string direction = Utility.GetDirection(playerTile, sObject.TileLocation);
                string distance = Utility.GetDistance(playerTile, sObject.TileLocation).ToString();
                if (sObjectTile != null)
                    Mod.Output(ReplacePlaceholders(readTileOnly ? ModConfig.OTReadSelectedObjectTileText : ModConfig.OTReadSelectedObjectText, playerTile, sObjectTile.Value, direction, distance), true);
            }
        }

        private string ReplacePlaceholders(string s, Vector2 playerTile, Vector2 sObjectTile, string direction, string distance)
        {
            return s.ToLower()
                .Replace("{object}", SelectedObject)
                .Replace("{objectX}", $"{sObjectTile.X}")
                .Replace("{objectY}", $"{sObjectTile.Y}")
                .Replace("{playerX}", $"{playerTile.X}")
                .Replace("{playerY}", $"{playerTile.Y}")
                .Replace("{direction}", $"{direction}")
                .Replace("{distance}", $"{distance}");
        }

        private void Cycle(bool cycleCategories, bool back = false)
        {
            if (!IsValidSelection())
                return;

            var objects = TrackedObjects?.GetObjects();
            string suffixText;

            if (cycleCategories)
            {
                string[] categories = objects?.Keys.ToArray() ?? Array.Empty<string>();
                suffixText = SelectedCategory is null ? string.Empty : Utility.DoCycle(ref SelectedCategory!, categories, back);
                SetDefaultCategoryAndFocusedObject(setFirstObjectInCategory: true);
            }
            else
            {
                string[] objectKeys = SelectedCategory != null && objects?.ContainsKey(SelectedCategory) == true
                    ? objects[SelectedCategory].Keys.ToArray()
                    : Array.Empty<string>();
                suffixText = SelectedObject is null ? string.Empty : Utility.DoCycle(ref SelectedObject!, objectKeys, back);
            }

            suffixText = suffixText.Length > 0 ? ", " + suffixText : string.Empty;
            Mod.Output($"{SelectedCategory ?? "No Category"}, {SelectedObject ?? "No Object"}" + suffixText, true);
        }

        private SpecialObject? GetCurrentlySelectedObject()
        {
            return SelectedCategory != null && SelectedObject != null && TrackedObjects?.GetObjects()?.TryGetValue(SelectedCategory, out var categoryObjects) == true
                ? categoryObjects.TryGetValue(SelectedObject, out var selectedObject) ? selectedObject : null
                : null;
        }

        private void SetDefaultCategoryAndFocusedObject(bool setFirstObjectInCategory = true)
        {
            var objects = TrackedObjects?.GetObjects();
            if (objects?.Any() != true)
            {
                Mod.Output("No objects found.");
            }
            else
            {
                SelectedCategory = objects.Keys.FirstOrDefault();

                if (setFirstObjectInCategory && SelectedCategory != null)
                {
                    SelectedObject = objects.TryGetValue(SelectedCategory, out var catObjects)
                        ? catObjects.Keys.FirstOrDefault()
                        : null;
                }

                string outputCategory = SelectedCategory ?? "No Category";
                string outputObject = SelectedObject ?? "No Object";
                Mod.Output($"Category: {outputCategory} | Object: {outputObject}");
            }
        }

        internal void GetLocationObjects(bool resetFocus = true)
        {
            TrackedObjects trackedObjects = new(Mod);
            trackedObjects.FindObjectsInArea(!sortByProxy);
            TrackedObjects = trackedObjects;

            var objects = trackedObjects.GetObjects();

            if (!resetFocus && SelectedCategory != null)
            {
                if (!objects.ContainsKey(SelectedCategory))
                {
                    resetFocus = true;
                }
            }

            if (resetFocus)
            {
                SetDefaultCategoryAndFocusedObject();
            }

            if (TrackedObjects == null || SelectedCategory == null || SelectedObject == null)
            {
                return;
            }
            else
            {
                if (!objects.ContainsKey(SelectedCategory) || !objects[SelectedCategory].ContainsKey(SelectedObject))
                {
                    SetDefaultCategoryAndFocusedObject(false);
                }
            }
        }
    }
}
