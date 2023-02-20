using Microsoft.Xna.Framework;
using StardewModdingAPI.Events;
using StardewValley;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Timers;
using xTile.Dimensions;
using Rectangle = Microsoft.Xna.Framework.Rectangle;

namespace AccessibleTiles.Modules.GridMovement {
    public class GridMovement {

        private readonly ModEntry Mod;

        public Boolean is_warping = false;
        public Boolean is_moving = false;

        //stop player from moving too fast
        public int minMillisecondsBetweenSteps = 210;
        readonly Timer timer = new();

        public GridMovement(ModEntry mod) {
            this.Mod = mod;

            //set is_moving after x time to allow the next grid movement
            timer.Interval = minMillisecondsBetweenSteps;
            timer.Elapsed += Timer_Elapsed;

        }

        private void Timer_Elapsed(object sender, ElapsedEventArgs e) {
            is_moving = false;

            //this is called if it hasn't already naturally been called by the game so the player doesn't freeze if the warp is unsuccessful
            if(is_warping) {
                this.HandleFinishedWarping(true);
            }
            timer.Stop();
        }

        public void HandleGridMovement(int direction, InputButton pressedButton) {

            Farmer player = Game1.player;
            GameLocation location = Game1.currentLocation;

            this.timer.Interval = minMillisecondsBetweenSteps - (player.addedSpeed * (minMillisecondsBetweenSteps / 9));

            this.Mod.LastGridMovementButtonPressed = pressedButton;
            this.Mod.LastGridMovementDirection = direction;

            if (Game1.player.FacingDirection != direction) {
                Game1.player.faceDirection(direction);
                Game1.playSound("dwop");
                is_moving = true;
                timer.Start();
                return;
            }

            if (this.is_warping == true || is_moving || Game1.IsChatting || !Game1.player.CanMove || (Game1.CurrentEvent!=null && !Game1.CurrentEvent.canMoveAfterDialogue())) return;

            is_moving = true;
            timer.Start();

            Mod.Output($"Move Direction: {direction}");

            Vector2 tileLocation = player.getTileLocation();

            switch(direction) {
                case 0:
                    tileLocation = Vector2.Add(tileLocation, new(0, -1));
                    break;
                case 1:
                    tileLocation = Vector2.Add(tileLocation, new(1, 0));
                    break;
                case 2:
                    tileLocation = Vector2.Add(tileLocation, new(0, 1));
                    break;
                case 3:
                    tileLocation = Vector2.Add(tileLocation, new(-1, 0));
                    break;
            }

            Mod.Output($"Move To: {tileLocation}");

            Rectangle position = new((int)tileLocation.X * Game1.tileSize, (int)tileLocation.Y * Game1.tileSize, Game1.tileSize, Game1.tileSize);
            Warp warp = location.isCollidingWithWarpOrDoor(position, Game1.player);
            if(warp != null) {
                if (isDoorAtTile((int)tileLocation.X, (int)tileLocation.Y))
                {
                    // Manually check for door and pressActionButton() method instead of warping (warping also works when the door is locked, for example it warps to the Pierre's shop before it's opening time)
                    Mod.Output("Collides with Door");
                    Game1.pressActionButton(Game1.GetKeyboardState(), Game1.input.GetMouseState(), Game1.input.GetGamePadState());
                } else {
                    Mod.Output("Collides with Warp");

                    if (location.checkAction(new Location((int)tileLocation.X * Game1.tileSize, (int)tileLocation.Y * Game1.tileSize), Game1.viewport, Game1.player)) {
                        timer.Stop();
                    } else {
                        //repurpose timer to wait a short period to prevent the player from spam warping
                        //this prevents the door sound from going off multiple times
                        //it also prevents the player from being locked up if a warp was unsuccessful
                        timer.Stop();
                        timer.Interval = 1000;
                        timer.Start();

                        Game1.playSound("doorOpen");
                        Game1.player.warpFarmer(warp);
                        this.is_warping = true;
                    }
                }

            } else {

                PathFindController pathfinder = new(player, location, tileLocation.ToPoint(), direction);
                if (pathfinder.pathToEndPoint != null) {
                    //valid point
                    player.Position = tileLocation * Game1.tileSize;
                    location.playTerrainSound(tileLocation);
                    CenterPlayer();

                }

            }

            CenterPlayer();

        }

        private static Boolean isDoorAtTile(int x, int y)
        {
            Point tilePoint = new(x, y);
            StardewValley.Network.NetPointDictionary<string, Netcode.NetString> doorList = Game1.currentLocation.doors;

            for (int i = 0; i < doorList.Count(); i++)
            {
                if (doorList.ContainsKey(tilePoint))
                {
                    return true;
                }
            }

            return false;
        }

        internal void PlayerWarped(object? sender, WarpedEventArgs e) {
            this.HandleFinishedWarping();
        }

        private void HandleFinishedWarping(bool failWarp = false) {
            Game1.player.canMove = true;
            this.is_moving = false;
            if (this.is_warping) {
                this.is_warping = false;

                if(failWarp) {
                    this.Mod.Output("Failed to walk through entrance.");
                } else {
                    Game1.playSound("doorClose");
                }
                    
            }
        }

        private static void CenterPlayer() {
            Game1.player.Position = Vector2.Divide(Game1.player.Position, Game1.tileSize) * Game1.tileSize;
        }

    }
}
