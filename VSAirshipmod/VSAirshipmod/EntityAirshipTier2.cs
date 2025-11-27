using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using System.Linq;


namespace VSAirshipmod
{
    //public class EntityAirship : Entity, IRenderer, ISeatInstSupplier, IMountableListener
    public class EntityAirshipTier2 : EntityAirship
    {


        public virtual int TemporalGearCount
        {
            get
            {
                return WatchedAttributes.GetInt("TemporalGearCount");
            }
            set
            {
                WatchedAttributes.SetInt("TemporalGearCount", value);
            }
        }
        public virtual long TemporalFuelUsage
        {
            get => WatchedAttributes.GetLong("TemporalFuelUsage", 0);
            set
            {
                WatchedAttributes.SetLong("TemporalFuelUsage", value);
            }
        }


        private bool pendingCruiseToggle = false; //Signals SeatsToMotion to handle the toggle
        private long lastCruiseToggleTime = 0;    //for cooldown
        private const int cruiseToggleCooldown = 4000; // in ms
        private ILoadedSound engine_sound;
        private bool cruise_control;
        /*enum AnimationState { Goodnight sweet prince :'[ -Pal
            None,
            Forward,
            Backward,
            Left,
            Right
        }*/
        private bool animForward = false;
        private bool animBackward = false;
        private bool animLeft = false;
        private bool animRight = false;
        private bool animPropeller = false;
        private bool animDown = false;

        private bool engineSoundPlaying = false;

        private bool TemporalFuelJustSpent = false;
        private long TemporalFuelSpentTimestamp = 0;


        static int MinutesPerGear = 15;//Central spot to set this, will be good for configs or something too :P


        private void apply_engine_sound()
        {
            ICoreClientAPI capi = this.Api as ICoreClientAPI;
            if (capi == null) return;

            // Load sound only if not already loaded
            if (this.engine_sound == null)
            {
                this.engine_sound = capi.World.LoadSound(new SoundParams()
                {
                    Location = new AssetLocation("sounds/effect/gears"),
                    DisposeOnFinish = false,
                    Position = this.Pos.XYZ.ToVec3f(),
                    ShouldLoop = true,
                });

                if (this.engine_sound == null)
                {
                    capi.Logger.Warning("[AirshipTier2] Failed to load engine sound!");
                    return;
                }
            }

            // Update position
            this.engine_sound.SetPosition((float)this.Pos.X, (float)this.Pos.Y, (float)this.Pos.Z);

            //Sound depending on propeller animation
            if (animPropeller && !engineSoundPlaying)
            {
                this.engine_sound.Start();
                engineSoundPlaying = true;
                //capi.Logger.Notification("[AirshipTier2] Started engine sound.");
            }
            else if (!animPropeller && engineSoundPlaying)
            {
                this.engine_sound.Stop();
                engineSoundPlaying = false;
                //capi.Logger.Notification("[AirshipTier2] Stopped engine sound.");
            }
        }

        private void set_animations(EntityRideableSeat seat)
        {
            animLeft = seat.controls.Left;
            animRight = seat.controls.Right;

            animBackward = seat.controls.Backward;
            animForward = !animBackward && (seat.controls.Forward || cruise_control || animLeft || animRight);

            //Propeller spins whenever forward/backward or turning
            animPropeller = animForward || animBackward || animLeft || animRight;

            //down animation.
            animDown = seat.controls.Sprint;
        }


        private void apply_animations(bool cruise_control_was_set)
        {
            //Stop all animations if no longer active
            if ((!animForward && (!animLeft && !animRight)) || animBackward) StopAnimation("Forward");
            if (!animBackward) StopAnimation("Backward");
            if (!animLeft) StopAnimation("TurnLeft");
            if (!animRight) StopAnimation("TurnRight");
            if (!animPropeller) StopAnimation("Propeller");

            //Start animations if active
            if (animForward) StartAnimation("Forward");
            if (animBackward) StartAnimation("Backward");
            if (animLeft) StartAnimation("TurnLeft");
            if (animRight) StartAnimation("TurnRight");
            if (animPropeller) StartAnimation("Propeller");

            //Cruise control toggle animation
            if (cruise_control_was_set)
            {
                if (cruise_control) StartAnimation("throttlelock");
                else StopAnimation("throttlelock");
            }

            //Down animation
            if (animDown) StartAnimation("GoDown");
            else StopAnimation("GoDown");
            

            // Engine sound
            apply_engine_sound();
        }


        float curRotMountAngleZ = 0f;
        public Vec3f mountAngle = new Vec3f();

        public override void OnRenderFrame(float dt, EnumRenderStage stage)
        {
            // Client side we update every frame for smoother turning
            if (capi.IsGamePaused) return;

            updateBoatAngleAndMotion(dt);

            long ellapseMs = capi.InWorldEllapsedMilliseconds;
            float forwardpitch = 0;
            if (IsFlying)//(Swimming)//
            {
                double gamespeed = capi.World.Calendar.SpeedOfTime / 60f;
                float intensity = 0.15f + GlobalConstants.CurrentWindSpeedClient.X * 0.9f;
                float diff = GameMath.DEG2RAD / 2f * intensity;
                mountAngle.X = GameMath.Sin((float)(ellapseMs / 1000.0 * 2 * gamespeed)) * 8 * diff;
                mountAngle.Y = GameMath.Cos((float)(ellapseMs / 2000.0 * 2 * gamespeed)) * 3 * diff;
                mountAngle.Z = -GameMath.Sin((float)(ellapseMs / 3000.0 * 2 * gamespeed)) * 8 * diff;

                curRotMountAngleZ += ((float)AngularVelocity * 5 * Math.Sign(ForwardSpeed) - curRotMountAngleZ) * dt * 5;
                forwardpitch = -(float)ForwardSpeed * 1.3f;
            }

            var esr = Properties.Client.Renderer as EntityShapeRenderer;
            if (esr == null) return;

            esr.xangle = mountAngle.X + curRotMountAngleZ;
            esr.yangle = mountAngle.Y;
            esr.zangle = mountAngle.Z + forwardpitch; // Weird. Pitch ought to be xangle.
        }

        public override void Initialize(EntityProperties properties, ICoreAPI api, long chunk)
        {
            base.Initialize(properties, api, chunk);

            //Listener for TemporalGearCount changes marks the shape modified like sail boat unfurling
            WatchedAttributes.RegisterModifiedListener("TemporalGearCount", MarkShapeModified);

            if (capi != null)capi.Event.RegisterRenderer(this, EnumRenderStage.Before);

        }

/*        public override void OnTesselation(ref Shape entityShape, string shapePathForLogging)
        {
            if (entityShape == null) return;

            entityShape = entityShape.Clone();

            if (TemporalGearCount < 3) entityShape.RemoveElementByName("TEMPHIDE3");
            if (TemporalGearCount < 2) entityShape.RemoveElementByName("TEMPHIDE2");
            if (TemporalGearCount < 1) entityShape.RemoveElementByName("TEMPHIDE1");

            base.OnTesselation(ref entityShape, shapePathForLogging);
        }*/
        public override void OnTesselation(ref Shape entityShape, string shapePathForLogging)
        {
            if (entityShape == null) return;

            entityShape = entityShape.Clone();

            for (int i = 1; i <= 3; i++)//The swap of the century, this makes the proper gears show at the right times
            {
                //Hide temporal if less than this gear
                if (TemporalGearCount < i) entityShape.RemoveElementByName($"TEMPHIDE{i}");
                //Hide rusty if this temporal gear exists
                if (TemporalGearCount >= i) entityShape.RemoveElementByName($"RUSTHIDE{i}");
            }

            base.OnTesselation(ref entityShape, shapePathForLogging);
        }

        public override void OnGameTick(float dt)
        {
            if (World.Side == EnumAppSide.Server)
            {
                var ela = World.ElapsedMilliseconds;
                if (IsOnFire && (World.ElapsedMilliseconds - OnFireBeginTotalMs > 10000))
                {
                    Die();
                }

                updateBoatAngleAndMotion(dt);

            }

            //Idle animation for gears
            bool shouldSpin = TemporalGearCount > 0;

            if (shouldSpin && !AnimManager.IsAnimationActive("GearsSpin"))
            {
                StartAnimation("GearsSpin");
            }
            else if (!shouldSpin && AnimManager.IsAnimationActive("GearsSpin"))
            {
                StopAnimation("GearsSpin");
            }


            //fuel draining throttling to prevent sync issues, will work perfectly since min amount of time stamp is always 60 seconds atleast
            if (TemporalFuelJustSpent)
            {
                if (World.ElapsedMilliseconds - TemporalFuelSpentTimestamp > 10000)
                {
                    TemporalFuelJustSpent = false;
                }
            }

            base.OnGameTick(dt);
        }

        double horizontalmodifier = 3;
        protected void updateBoatAngleAndMotion(float dt)
        {
            dt = Math.Min(0.5f, dt);

            float step = GlobalConstants.PhysicsFrameTime;
            var motion = SeatsToMotion(step);

            //Some easing to it
            ForwardSpeed += (motion.X * SpeedMultiplier - ForwardSpeed) * dt;
            AngularVelocity += (motion.Z * (SpeedMultiplier / AngularVelocityDivider) - AngularVelocity) * dt;
            HorizontalVelocity = motion.Y * dt;//+= (motion.Y * SpeedMultiplier - HorizontalVelocity) * dt;


            if (!IsFlying && HorizontalVelocity == 0) return;


            var pos = SidedPos;

            if (ForwardSpeed != 0.0)
            {
                var targetmotion = pos.GetViewVector().Mul((float)-ForwardSpeed).ToVec3d();
                pos.Motion.X = targetmotion.X;
                pos.Motion.Z = targetmotion.Z;
            }

            if (true)
            {
                if (HorizontalVelocity > 0.0)
                {
                    pos.Motion.Y = 0.013* horizontalmodifier;
                }

                applyGravity = IsEmptyOfPlayers() ? true : false;

                if (HorizontalVelocity < 0.0 || (IsEmptyOfPlayers() && (!OnGround || !Swimming)))
                {
                    pos.Motion.Y = -0.013* horizontalmodifier;
                }
            }


            var bh = GetBehavior<EntityBehaviorPassivePhysicsMultiBox>();
            bool canTurn = true;

            if (AngularVelocity != 0.0)
            {
                float yawDelta = (float)AngularVelocity * dt * 30f;

                if (bh.AdjustCollisionBoxesToYaw(dt, true, SidedPos.Yaw + yawDelta))
                {
                    pos.Yaw += yawDelta;
                }
                else canTurn = false;
            }
            else
            {
                canTurn = bh.AdjustCollisionBoxesToYaw(dt, true, SidedPos.Yaw);
            }

            if (!canTurn)
            {
                if (bh.AdjustCollisionBoxesToYaw(dt, true, SidedPos.Yaw - 0.1f))
                {
                    pos.Yaw -= 0.0002f;
                }
                else if (bh.AdjustCollisionBoxesToYaw(dt, true, SidedPos.Yaw + 0.1f))
                {
                    pos.Yaw += 0.0002f;
                }
            }

            pos.Roll = 0;
        }

        public virtual Vec3d SeatsToMotion(float dt)
        {
            double linearMotion = 0;
            double angularMotion = 0;
            double verticalMotion = 0;

            long currentTime = World.ElapsedMilliseconds;
            bool cruiseControlWasSet = false;

            var seatBehavior = GetBehavior<EntityBehaviorSeatable>();
            seatBehavior.Controller = null;

            bool anyControllablePassenger = false;
            bool horizontalMotionActive = false; //Track if the engine can actually be used

            foreach (var rawSeat in seatBehavior.Seats)
            {
                var seat = rawSeat as EntityAirshipSeat;
                if (seat == null || seat.Passenger == null) continue;

                //Only if the seat is the pilot one
                if (!seat.Config.Controllable) continue;

                // Determine controller
                if (seat.Config.Controllable && seatBehavior.Controller == null)
                {
                    seatBehavior.Controller = seat.Passenger;
                    anyControllablePassenger = true;
                }

                set_animations(seat);

                //Easy way to tell if the engine is working
                if (animPropeller) horizontalMotionActive = true;

                //Vertical
                if (seat.controls.Jump) verticalMotion += dt * 1f;
                if (seat.controls.Sprint) verticalMotion -= dt * 1f;

                //Horizontal rotation
                if (seat.controls.Left || seat.controls.Right)
                {
                    float dir = seat.controls.Left ? 1 : -1;
                    angularMotion += dir * dt;
                }

                //Cruise toggling
                if (pendingCruiseToggle)
                {
                    cruiseControlWasSet = true;
                    pendingCruiseToggle = false;
                }

                //Forward/backward motion AND backward cruise shut off
                if (cruise_control && seat.controls.Backward)
                {
                    cruise_control = false;
                    cruiseControlWasSet = true;
                }
                if (seat.controls.Forward || seat.controls.Backward || cruise_control)
                {
                    float dir = (seat.controls.Forward || cruise_control) ? 1 : -1;
                    //linearMotion += dir * dt * (seat.controls.Backward ? 0.5f : 1f);
                    linearMotion += dir * dt;
                }


                //Only decrement fuel once per tick if engine is active
                if (TemporalGearCount == 0)//No gears, No motion B)
                {
                    linearMotion = 0;
                    angularMotion = 0;
                    animForward = animPropeller = false;
                }
                else if (horizontalMotionActive)
                {
                    TemporalFuelUsage -= (long)(dt * 1000f);

                    if (TemporalFuelUsage <= 0 && !TemporalFuelJustSpent)
                    {
                        TemporalFuelJustSpent = true;
                        TemporalFuelSpentTimestamp = World.ElapsedMilliseconds;

                        TemporalGearCount--;
                        TemporalFuelUsage = MinutesPerGear * 60 * 1000;

                        WatchedAttributes.MarkPathDirty("TemporalGearCount");
                        WatchedAttributes.MarkPathDirty("TemporalFuelUsage");

                        if (Api.Side == EnumAppSide.Server)
                        {
                            World.PlaySoundAt(new AssetLocation("game:sounds/effect/latch"), this);
                        }
                    }
                }







            }

            //Force things off if no controllable passengers remain**
            if (!anyControllablePassenger)
            {
                if(cruise_control){
                    cruise_control = false;
                    cruiseControlWasSet = true;
                }


                //Clear animation flags so propeller/forward/etc.
                animForward = animBackward = animLeft = animRight = animPropeller = false;
                if (engineSoundPlaying)
                {
                    engineSoundPlaying = false;
                    if (engine_sound != null) engine_sound.Stop();
                }
            }

            // Apply animations after processing all seats
            apply_animations(cruiseControlWasSet);

            return new Vec3d(linearMotion, verticalMotion, angularMotion);
        }


        public override void OnInteract(EntityAgent byEntity, ItemSlot itemslot, Vec3d hitPosition, EnumInteractMode mode)
        {
            if (mode == EnumInteractMode.Interact && AllowPickup() && IsEmpty())
            {
                if (tryPickup(byEntity, mode)) return;
            }

            var player = byEntity as EntityPlayer;
            var selBoxes = GetBehavior<EntityBehaviorSelectionBoxes>();




            //Temporal Gear Refilling
            {
                if (selBoxes != null && selBoxes.IsAPCode(player?.EntitySelection, "GearCubeAP"))
                {
                    ItemSlot slot = itemslot;

                    //Only accept the temporal gears
                    bool isTemporalGear =
                        slot?.Itemstack?.Collectible?.Code != null &&
                        slot.Itemstack.Collectible.Code.Path == "gear-temporal";

                    //Only allow adding if not full
                    if (isTemporalGear && TemporalGearCount < 3)
                    {
                        //Server side
                        if (Api.Side == EnumAppSide.Server)
                        {
                            slot.TakeOut(1);
                            slot.MarkDirty();
                            //Directly adding fuel time here as well to make replacing the first gear less awkward otherwise it'd instantly drain after
                            if(TemporalGearCount == 0){
                                TemporalFuelUsage = MinutesPerGear * 60 * 1000;
                            }
                            TemporalGearCount = Math.Min(3, TemporalGearCount + 1);
                            WatchedAttributes.MarkPathDirty("TemporalGearCount");
                            World.PlaySoundAt(new AssetLocation("game:sounds/effect/latch"), this);

                            //Drop a rusty gear at the airship
                            var RustyGear = World.GetItem(new AssetLocation("game:gear-rusty"));
                            if (RustyGear != null)
                            {
                                var stack = new ItemStack(RustyGear, 1);
                                World.SpawnItemEntity(stack, byEntity.ServerPos.XYZ);
                            }
                        }
                        return;
                    }

                    //Warning for max gears
                    if (isTemporalGear && TemporalGearCount >= 3)
                    {
                        (byEntity.World.Api as ICoreClientAPI)?.TriggerIngameError(this, "tier2fullofgears",Lang.Get("This engine can only take three gears!"));
                        return;
                    }
                }
            }







            //Cruise Toggling
            if (selBoxes != null && selBoxes.IsAPCode(player?.EntitySelection, "ThrottleLockAP"))
            {
                long currentTime = World.ElapsedMilliseconds;

                //Only allow toggling if a controllable player is present
                bool hasDriver = GetBehavior<EntityBehaviorSeatable>().Seats
                                .OfType<EntityAirshipSeat>()
                                .Any(s => s.Config.Controllable && s.Passenger is EntityPlayer);

                if (hasDriver && currentTime - lastCruiseToggleTime >= cruiseToggleCooldown)
                {
                    lastCruiseToggleTime = currentTime;
                    cruise_control = !cruise_control;
                    pendingCruiseToggle = true; //signal SeatsToMotion
                }

                return;
            }

            EnumHandling handled = EnumHandling.PassThrough;
            foreach (EntityBehavior behavior in SidedProperties.Behaviors)
            {
                behavior.OnInteract(byEntity, itemslot, hitPosition, mode, ref handled);
                if (handled == EnumHandling.PreventSubsequent) break;
            }
        }


        private bool AllowPickup()
        {
            return Properties.Attributes?["rightClickPickup"].AsBool(false) == true;
        }

        private bool tryPickup(EntityAgent byEntity, EnumInteractMode mode)
        {
            // shift + click to remove boat, this will normally never be true but who knows maybe someone will make a patch mod or something
            if (byEntity.Controls.ShiftKey)
            {
                ItemStack stack = new ItemStack(World.GetItem(Code));
                if (!byEntity.TryGiveItemStack(stack))
                {
                    World.SpawnItemEntity(stack, ServerPos.XYZ);
                }

                Api.World.Logger.Audit("{0} Picked up 1x{1} at {2}.",
                    byEntity.GetName(),
                    stack.Collectible.Code,
                    Pos
                );

                Die();
                return true;
            }

            return false;
        }

        public override bool CanCollect(Entity byEntity)
        {
            return false;
        }


        public override WorldInteraction[] GetInteractionHelp(IClientWorldAccessor world, EntitySelection es, IClientPlayer player)
        {
            return base.GetInteractionHelp(world, es, player);
        }


        public override void OnEntityDespawn(EntityDespawnData despawn)
        {
            base.OnEntityDespawn(despawn);

            capi?.Event.UnregisterRenderer(this, EnumRenderStage.Before);
        }

        public override string GetInfoText()
        {
            base.GetInfoText();
            string text = base.GetInfoText();
            text += "\n" + Lang.Get("vsairshipmod:int-temporalgearcount", TemporalGearCount);

            //Convert milliseconds to seconds
            long totalSeconds = Math.Max(0, TemporalFuelUsage / 1000);

            string timeString;
            if (totalSeconds >= 60)
            {
                long minutes = totalSeconds / 60;
                long seconds = totalSeconds % 60;
                timeString = $"{minutes}m {seconds}s";//Appen either M for minutes or S for seconds
            }
            else
            {
                timeString = $"{totalSeconds}s";//Final countdown of the last minute
            }

            if(TemporalGearCount != 0){
                text += "\n" + Lang.Get("vsairshipmod:int-temporalfuelusage", timeString);
            }else{
                text += "\n" + Lang.Get("vsairshipmod:temporalfueldepletedhorizontal");
            }

            return text;
        }


    }
}