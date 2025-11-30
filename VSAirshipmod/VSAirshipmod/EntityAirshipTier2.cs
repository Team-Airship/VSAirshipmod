using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using System.Linq;
using Vintagestory.API.Util;




namespace VSAirshipmod
{
    //public class EntityAirship : Entity, IRenderer, ISeatInstSupplier, IMountableListener
    public class EntityAirshipTier2 : EntityAirship
    {


        public virtual int TemporalGearCount
        {
            get => WatchedAttributes.GetInt("TemporalGearCount", 0);
            set => WatchedAttributes.SetInt("TemporalGearCount", value);
        }
        public long TemporalFuelUsage
        {
            get => WatchedAttributes.GetLong("TemporalFuelUsage", 0);
            set => WatchedAttributes.SetLong("TemporalFuelUsage", value);
        }
        public virtual string CoalItemCode//Just gonna do it similarly to the temporal system :P
        {
            get => WatchedAttributes.GetString("CoalItemCode", null);
            set => WatchedAttributes.SetString("CoalItemCode", value);
        }

        public virtual int CoalStackSize
        {
            get => WatchedAttributes.GetInt("CoalStackSize", 0);
            set => WatchedAttributes.SetInt("CoalStackSize", value);
        }
        public float CoalBurnDuration
        {
            get => WatchedAttributes.GetFloat("CoalBurnDuration", 0);
            set => WatchedAttributes.SetFloat("CoalBurnDuration", value);
        }
        public long CoalFuelUsage
        {
            get => WatchedAttributes.GetLong("CoalFuelUsage", 0);
            set => WatchedAttributes.SetLong("CoalFuelUsage", value);
        }
        public bool HasCoal => !string.IsNullOrEmpty(CoalItemCode) && CoalStackSize > 0;

        private const int browncoalfueltimeinseconds = 90; //plumb these to configs
        private const int blackcoalfueltimeinseconds = 60; //plumb these to configs
        private const int anthracitefueltimeinseconds = 30; //plumb these to configs
        private const int charcoalfueltimeinseconds = 10; //plumb these to configs

        const int MaxCoalStack = 64;

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

        private bool CoalFuelJustSpent = false;
        private long CoalFuelSpentTimestamp = 0;

         long nextLogTime = 0;


            double ForwardAcceleration = 0.05;
            double TurnSpeed = 0.25;
            double TurnAcceleration = 0.15;
            double RiseSpeed = 15;
            double RiseAcceleration = 0.07;

            float pitchStrength = 0.5f;


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
            bool isReversing = seat.controls.Backward;
            if (isReversing == false)
            {
                animLeft = seat.controls.Left;
                animRight = seat.controls.Right;
            }
            else
            {
                animLeft = seat.controls.Right;
                animRight = seat.controls.Left;
            }


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

            //Inversion logic for making it turn backwards, using reflection here too like the loom
            bool isReversing = animBackward;
            float propellerSpeed = isReversing ? -1f : 1f;

            if (AnimManager.ActiveAnimationsByAnimCode.TryGetValue("Propeller", out var anim))
            {
                var field = anim.GetType().GetField("AnimationSpeed", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);

                if (field != null)
                {
                    field.SetValue(anim, propellerSpeed);
                }
            }


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
                forwardpitch = -((float)this.ForwardSpeed * this.pitchStrength);
            }

            var esr = Properties.Client.Renderer as EntityShapeRenderer;
            if (esr == null) return;

            esr.xangle = mountAngle.X + curRotMountAngleZ;
            esr.yangle = mountAngle.Y;
            esr.zangle = mountAngle.Z + forwardpitch; // Weird. Pitch ought to be xangle.





            if (Api.Side == EnumAppSide.Client)//Darth's approach, but animation starting is insured in tick alongside
            {
                if (this.AnimManager.Animator != null)
                {
                    {

                        //Coal fuel "meter" animation barrel raising up and down
                        RunningAnimation anim = this.AnimManager.GetAnimationState("fuelcoal");
                        if (anim != null)
                        {
                            //Api.Logger.Notification("" + anim.CurrentFrame);
                            anim.CurrentFrame = (float)Math.Clamp((1 - ((CoalStackSize) / 64f)) * 64f, 0, 64); // * 57.295776f / 10f;
                            anim.BlendedWeight = 1f;
                            anim.EasingFactor = 0.1f;
                            //Api.Logger.Notification("[AirshipTier2] Animation frame is: {0}", anim.CurrentFrame);
                        }

                        //Temporal
                        RunningAnimation animTemporal = this.AnimManager.GetAnimationState("fueltemp");
                        if (animTemporal != null)
                        {
                            float fuelSeconds = Math.Max(0f, TemporalFuelUsage / 1000f);
                            float totalSeconds = MinutesPerGear * 60f;
                            float fraction = fuelSeconds / totalSeconds;
                            fraction = Math.Clamp(fraction, 0f, 1f);
                            int maxFrame = 14;
                            animTemporal.CurrentFrame = fraction * maxFrame;

                            animTemporal.BlendedWeight = 1f;
                            animTemporal.EasingFactor = 0.1f;
                            //Api.Logger.Notification($"Temporal fuel fraction: {fraction}, frame: {animTemporal.CurrentFrame}");
                        }




                    }
                }
            }

        }

        public override void Initialize(EntityProperties properties, ICoreAPI api, long chunk)
        {
            base.Initialize(properties, api, chunk);

             ForwardAcceleration = properties.Attributes["ForwardAcceleration"].AsDouble(0.05);
             TurnSpeed = properties.Attributes["TurnSpeed"].AsDouble(0.25);
             TurnAcceleration = properties.Attributes["TurnAcceleration"].AsDouble(0.15);
             RiseSpeed = properties.Attributes["RiseSpeed"].AsDouble(15);
             RiseAcceleration = properties.Attributes["RiseAcceleration"].AsDouble(0.07);
             pitchStrength  = properties.Attributes["pitchStrength"].AsFloat(0.5f);

            //Listener for TemporalGearCount changes marks the shape modified like sail boat unfurling
            WatchedAttributes.RegisterModifiedListener("TemporalGearCount", MarkShapeModified);

            //WatchedAttributes.RegisterModifiedListener("CoalStackSize", MarkShapeModified);

            if (capi != null) capi.Event.RegisterRenderer(this, EnumRenderStage.Before);

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

            //Temporal Gear Hiding
            for (int i = 1; i <= 3; i++)//The swap of the century, this makes the proper gears show at the right times
            {
                //Hide temporal if less than this gear
                if (TemporalGearCount < i) entityShape.RemoveElementByName($"TEMPHIDE{i}");
                //Hide rusty if this temporal gear exists
                if (TemporalGearCount >= i) entityShape.RemoveElementByName($"RUSTHIDE{i}");
            }
            //Coal Barrel Hiding - no longer needed since its handled by coal fuel animation
            /*const int maxCoalVisuals = 14;//Amount of coals in the shape, make sure to change this if the shape is updated!
            int coalToShow = 0;
            if (CoalStackSize >= 60)//Little lee way so it does not INSTANTLY lose one
            {
                coalToShow = maxCoalVisuals;
            }
            else if (CoalStackSize > 0)
            {
                //Roughly scales  scales 1–59 coal into the visual steps
                coalToShow = (int)Math.Ceiling((CoalStackSize / 60f) * maxCoalVisuals);

                //Force at least 1 to appears if any coal exists
                coalToShow = Math.Max(1, coalToShow);
            }
            else
            {
                coalToShow = 0;
            }
            for (int i = 1; i <= maxCoalVisuals; i++)
            {
                if (i > coalToShow)
                {
                    entityShape.RemoveElementByName($"coal{i}");
                }
            }*/



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

            if (!AnimManager.IsAnimationActive("FuelCoal"))
            {
                StartAnimation("FuelCoal");
                //Api.Logger.Notification("[AirshipTier2] FuelCoal Animation Started");
            }
            if (!AnimManager.IsAnimationActive("FuelTemp"))
            {
                StartAnimation("FuelTemp");
                //Api.Logger.Notification("[AirshipTier2] FuelTemp Animation Started");
            }
            //Api.Logger.Notification("[AirshipTier2] logic running");


            if (shouldSpin && !AnimManager.IsAnimationActive("GearsSpin"))
            {
                StartAnimation("GearsSpin");
                //Api.Logger.Notification("[AirshipTier2] GearsSpin Animation Started");
            }
            else if (!shouldSpin && AnimManager.IsAnimationActive("GearsSpin"))
            {
                StopAnimation("GearsSpin");
                //Api.Logger.Notification("[AirshipTier2] GearsSpin Animation Stopped");
            }

            if (CoalStackSize == 0 && !AnimManager.IsAnimationActive("CoalStorageClosed"))
            {
                StartAnimation("CoalStorageClosed");
            }
            else if (CoalStackSize != 0 && AnimManager.IsAnimationActive("CoalStorageClosed"))
            {
                StopAnimation("CoalStorageClosed");
            }
            //fuel draining throttling to prevent sync issues, will work perfectly since min amount of time stamp is always 60 seconds atleast
            if (TemporalFuelJustSpent)
            {
                if (World.ElapsedMilliseconds - TemporalFuelSpentTimestamp > 10000)
                {
                    TemporalFuelJustSpent = false;
                }
            }

            if (CoalFuelJustSpent)//Still works since its less than the min burn times
            {
                if (World.ElapsedMilliseconds - CoalFuelSpentTimestamp > 10000)
                {
                    CoalFuelJustSpent = false;
                }
            }
            //Descending Particle Logic, rotated and offset to be around the release hatch
            if (Api.Side == EnumAppSide.Client && animDown)
            {
                double yawRad = Pos.Yaw;
                yawRad = -yawRad;        //JUST HAD TO invert rotation to match entity visuals

                //And not forget to normalize
                yawRad %= 2 * Math.PI;
                if (yawRad < 0) yawRad += 2 * Math.PI;

                Vec3d offset = new Vec3d(0, 9.8, 0.7);//This is the offset to set it to the hatch

                double cos = Math.Cos(yawRad);
                double sin = Math.Sin(yawRad);

                Vec3d emitPos = Pos.XYZ.AddCopy(new Vec3d(
                    offset.X * cos - offset.Z * sin,
                    offset.Y,
                    offset.X * sin + offset.Z * cos
                ));

                DescendingEffects(emitPos);

                //Api.Logger.Notification($"[AirshipTier2] Client Yaw: {GameMath.RAD2DEG * Pos.Yaw}");
            }
            base.OnGameTick(dt);
        }

        double horizontalmodifier = 3;
        protected void updateBoatAngleAndMotion(float dt)
        {
            dt = Math.Min(0.5f, dt);

            float step = GlobalConstants.PhysicsFrameTime;
            var motion = SeatsToMotion(step);

  


                double target = motion.X * SpeedMultiplier * 3;
                double diff = Math.Abs(target - ForwardSpeed);
                double normalized = target != 0.0 ? Math.Clamp(diff / target, 0.0, 1.0) : 0.0;

                double accelPower = Math.Clamp(1.0 - ForwardAcceleration * 0.4, 0.1, 1.0);
                double slowPower  = Math.Clamp(1.0 - ForwardAcceleration * 0.2, 0.1, 1.0);

                double shaped = Math.Pow(normalized, accelPower) * Math.Pow(1.0 - normalized, slowPower);
                double accelBoost = 1.0 + shaped * (ForwardAcceleration * 0.3);
                double lerpFactor = 1.0 - Math.Exp(-ForwardAcceleration * accelBoost * dt);

                lerpFactor = Math.Min(lerpFactor, 0.03);

                if (target < 0 && ForwardSpeed >= 0.01 )
                {
                    ForwardSpeed += (3 * target - ForwardSpeed) * lerpFactor;
                }
                else if (target > 0 && ForwardSpeed <= -0.01)
                {
                    ForwardSpeed += (5 * target - ForwardSpeed) * lerpFactor;
                }
                else if (target == 0 && (ForwardSpeed >= 0.01|| ForwardSpeed <= -0.01))
                {
                    ForwardSpeed += (1.5 * target - ForwardSpeed) * lerpFactor;
                }
                else
                {
                    ForwardSpeed += (target - ForwardSpeed) * lerpFactor;
                }


            this.AngularVelocity += (motion.Z * (double)TurnSpeed - this.AngularVelocity) * (double)dt*TurnAcceleration;

            this.HorizontalVelocity += (motion.Y * (double)RiseSpeed - this.HorizontalVelocity) * (double)dt*RiseAcceleration;









            //Coal fuel usage logic
            if (IsFlying)
            {
                //if (CoalStackSize > 0)
                if (CoalStackSize >= 2)
                {
                    CoalFuelUsage -= (long)(dt * 1000f);

                    if (CoalFuelUsage <= 0 && !CoalFuelJustSpent)
                    {
                        CoalFuelJustSpent = true;
                        CoalFuelSpentTimestamp = World.ElapsedMilliseconds;

                        //CoalStackSize--;
                        CoalStackSize -= 2;
                        CoalFuelUsage = (long)(CoalBurnDuration * 1000f);   //Reset timer for next coal piece

                        WatchedAttributes.MarkPathDirty("CoalStackSize");
                        WatchedAttributes.MarkPathDirty("CoalFuelUsage");

                        StartAnimation("UsingCoal");

                        if (Api.Side == EnumAppSide.Server)
                        {
                            /*int r = World.Rand.Next(1, 4);
                            World.PlaySoundAt(
                                new AssetLocation($"game:sounds/block/charcoal{r}"), 
                                this
                            );*/
                            World.PlaySoundAt(new AssetLocation("game:sounds/held/torch-equip"), this);
                        }
                    }
                }
                /*else
                {
                    //No coal left so tack on passive descent
                        //if(verticalMotion >= 0){
                        //   verticalMotion = 0;
                        //}
                        verticalMotion -= 0.01f;
                }*/
            }


            applyGravity = IsEmptyOfPlayers() ? true : false;

            if (!IsFlying && HorizontalVelocity == 0) return;


            var pos = SidedPos;

            if (ForwardSpeed != 0.0)
            {
                if (Math.Abs(target) <= 0 && Math.Abs(this.ForwardSpeed) <= 0.02)
                {
                    this.ForwardSpeed = 0.0;
                    pos.Motion.X = 0.0;
                    pos.Motion.Z = 0.0;
                }
                else
                {
                    Vec3d targetmotion = pos.GetViewVector().Mul((float)(-(float)this.ForwardSpeed)).ToVec3d();
                    pos.Motion.X = targetmotion.X;
                    pos.Motion.Z = targetmotion.Z;
                }
            }

            if (HorizontalVelocity > 0.0)
            {
                pos.Motion.Y = 0.1 * this.HorizontalVelocity;
            }

            if (HorizontalVelocity < 0.0 && (IsFlying))
            {
                pos.Motion.Y = 0.1 * this.HorizontalVelocity;
            }

            /*if ((CoalStackSize <= 0 && motion.Y <= 0f) && (!OnGround || !Swimming))
            {
                pos.Motion.Y -= 0.003 * dt;
            }*/
            if ((CoalStackSize < 2 && motion.Y <= 0f) && (!OnGround || !Swimming))
            {
                pos.Motion.Y -= 0.003 * dt;
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



			if (this.Api is ICoreClientAPI && (this.ForwardSpeed != 0 || this.AngularVelocity != 0 ) )
			{

            long now = World.ElapsedMilliseconds;

            if (now >= nextLogTime)

            {
                nextLogTime = now + 1000;

                 //  Api.Logger.Debug("[AirshiptDebug] Flying at = {0:0} / {1:0} Speed", this.ForwardSpeed * 10000, target * 10000);
                 //  Api.Logger.Debug("[AirshiptDebug] Turnspeed: {0:0.0} / RiseSpeed: {1:0.0}", this.AngularVelocity * 1000, this.HorizontalVelocity * 10);
        

                }
			}


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

                //Easy way to tell if the engine is working + plus dont allow movement unless in air to prevent model twitching
                if (animPropeller && !OnGround) horizontalMotionActive = true;

                //Vertical
                if (CoalStackSize >= 2)
                {//Cant resist downwards force when out of fuel
                    if (seat.controls.Jump) verticalMotion += dt * 1f;
                }
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
            //Force things off if no controllable passengers remain
            if (!anyControllablePassenger)
            {
                if (cruise_control)
                {
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

        /*private float GetFuelBurnDuration(ItemStack stack)//Helper pulled from another mod of mine directly, used for coal fuel.
        {
            if (stack == null) return 0;

            float duration = stack.Collectible.Attributes?["burnDuration"].AsFloat(0) ?? 0;
            if (duration > 0) return duration;

            return stack.Collectible.CombustibleProps?.BurnDuration ?? 0;
        }*/
        private static readonly HashSet<string> AllowedCoalTypes = new HashSet<string>
        {
            "ore-lignite",
            //"coke",
            "ore-bituminouscoal",
            "charcoal",
            "ore-anthracite"
        };


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
                            if (TemporalGearCount == 0)
                            {
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
                        (byEntity.World.Api as ICoreClientAPI)?.TriggerIngameError(this, "tier2fullofgears", Lang.Get("vsairshipmod:engineatmax3gears"));
                        return;
                    }
                }
            }


            //Coal Refilling
            if (selBoxes != null && selBoxes.IsAPCode(player?.EntitySelection, "CoalBarrelAP"))
            {
                ItemSlot HeldSlot = itemslot;
                var held = HeldSlot?.Itemstack;

                // Adding coal
                if (mode == EnumInteractMode.Interact && held != null)
                {
                    string path = held.Collectible.Code.Path;

                    //Api.Logger.Notification("[AirshipTier2] Adding COal Started.");
                    //Must be valid item
                    if (AllowedCoalTypes.Contains(path) && CoalStackSize < 64)
                    {
                        //float burn = GetFuelBurnDuration(held);
                        float burn = 0;



                        if (path == "ore-lignite")
                        {
                            burn = browncoalfueltimeinseconds;
                        }
                        if (path == "ore-bituminouscoal")
                        {
                            burn = blackcoalfueltimeinseconds;
                        }
                        if (path == "charcoal")
                        {
                            burn = charcoalfueltimeinseconds;
                        }
                        if (path == "ore-anthracite")
                        {
                            burn = anthracitefueltimeinseconds;
                        }

                        /*
                                                if(path == "game:ore-lignite"){
                                                    burn = browncoalfueltimeinseconds;
                                                }
                                                if(path == "game:ore-bituminouscoal"){
                                                    burn = blackcoalfueltimeinseconds;
                                                }
                                                if(path == "game:charcoal"){
                                                    burn = anthracitefueltimeinseconds;
                                                }
                                                if(path == "game:anthracite"){
                                                    burn = charcoalfueltimeinseconds;
                                                }*/

                        //Api.Logger.Notification("[AirshipTier2] Burn is {0}.",burn);

                        //Coal addition, handled together now for initial and additional coal
                        if (Api.Side == EnumAppSide.Server)
                        {
                            int current = CoalStackSize;
                            int heldCount = held.StackSize;

                            //Reject different coal types when one is already stored
                            if (HasCoal && CoalItemCode != path)
                            {
                                return;
                            }

                            //If we don't have any coal yet
                            if (!HasCoal) current = 0;

                            //First coal load set the coal values
                            if (!HasCoal)
                            {
                                CoalItemCode = path;
                                CoalBurnDuration = burn;
                                //HasCoal = true;
                            }

                            //Actually calculate how many can be added take without exceeding MaxCoalStack
                            int spaceLeft = MaxCoalStack - current;
                            if (spaceLeft <= 0)
                            {
                                // Storage already full so abort
                                return;
                            }

                            int amountToTake = Math.Min(spaceLeft, heldCount);

                            CoalStackSize = current + amountToTake;
                            CoalFuelUsage = (long)(CoalBurnDuration * 1000f); // keep fuel usage consistent

                            //Remove exactly amountToTake
                            HeldSlot.TakeOut(amountToTake);
                            HeldSlot.MarkDirty();

                            WatchedAttributes.MarkPathDirty("CoalItemCode");
                            WatchedAttributes.MarkPathDirty("CoalBurnDuration");
                            WatchedAttributes.MarkPathDirty("CoalStackSize");

                            World.PlaySoundAt(new AssetLocation($"game:sounds/block/charcoal{World.Rand.Next(1, 4)}"), this);

                            return;
                        }

                    }
                }


                //Remove coal with an empty hand
                if (mode == EnumInteractMode.Interact && (held == null || held.StackSize == 0))
                {
                    //if(!IsFlying){
                    if (HasCoal && Api.Side == EnumAppSide.Server)
                    {
                        var coalItem = World.GetItem(new AssetLocation("game:" + CoalItemCode));
                        if (CoalBurnDuration * 1000f != CoalFuelUsage)
                        {//Subtract one coal if it was burnt any amount so ya cant just reset with the same piece
                            CoalStackSize--;
                            World.PlaySoundAt(new AssetLocation("game:sounds/held/torch-equip"), this);
                        }
                        if (coalItem != null && CoalStackSize != 0)
                        {
                            var outStack = new ItemStack(coalItem, CoalStackSize);

                            if (!player.TryGiveItemStack(outStack))
                            {
                                World.SpawnItemEntity(outStack, byEntity.ServerPos.XYZ);
                            }
                        }

                        //Clear all coal data
                        CoalItemCode = null;
                        CoalStackSize = 0;
                        CoalBurnDuration = 0;
                        CoalFuelUsage = 0;
                        CoalFuelJustSpent = false;

                        WatchedAttributes.MarkPathDirty("CoalFuelUsage");
                        WatchedAttributes.MarkPathDirty("CoalItemCode");
                        WatchedAttributes.MarkPathDirty("CoalBurnDuration");
                        WatchedAttributes.MarkPathDirty("CoalStackSize");
                        //WatchedAttributes.MarkAllDirty();
                        World.PlaySoundAt(new AssetLocation($"game:sounds/block/charcoal{World.Rand.Next(1, 4)}"), this);
                    }
                    return;
                    //}else{
                    //    //Otherwise ya can just spam add and remove the same coal constantly resetting the burn timer lol
                    //    (byEntity.World.Api as ICoreClientAPI)?.TriggerIngameError(this, "cantremovefuelinflight",Lang.Get("vsairshipmod:cannotremovefuelinflight"));
                    //    return;
                    //}
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

        public override void OnEntityDespawn(EntityDespawnData despawn)
        {
            base.OnEntityDespawn(despawn);

            capi?.Event.UnregisterRenderer(this, EnumRenderStage.Before);
        }

        public override WorldInteraction[] GetInteractionHelp(IClientWorldAccessor world, EntitySelection es, IClientPlayer player)
        {
            var interactions = base.GetInteractionHelp(world, es, player);
            var selBoxes = this.GetBehavior<EntityBehaviorSelectionBoxes>();

            //Gears interaction
            if (selBoxes?.IsAPCode(es, "GearCubeAP") ?? false)
            {
                interactions = ArrayExtensions.Append(interactions, new WorldInteraction
                {
                    ActionLangCode = "vsairshipmod:insert-temporal-gear", // lang key you define in lang files
                    MouseButton = EnumMouseButton.Right,
                    Itemstacks = new ItemStack[] // indicates the player needs this item to perform the action
                    {
                            new ItemStack(World.GetItem(new AssetLocation("game:gear-temporal")), 1)
                    }
                });
            }

            // Cruise interaction
            if (selBoxes?.IsAPCode(es, "ThrottleLockAP") ?? false)
            {
                interactions = ArrayExtensions.Append(interactions, new WorldInteraction
                {
                    ActionLangCode = "vsairshipmod:toggle-cruise",
                    MouseButton = EnumMouseButton.Right
                });
            }


            //Coal interactions
            if (selBoxes?.IsAPCode(es, "CoalBarrelAP") ?? false)
            {
                //Remove coal with empty hand
                interactions = ArrayExtensions.Append(interactions, new WorldInteraction
                {
                    ActionLangCode = "vsairshipmod:remove-coal",
                    MouseButton = EnumMouseButton.Right,
                    RequireFreeHand = true
                });

                //Add coals
                string[] allowedCoals = new string[]
                {
                        "coke",
                        "ore-bituminouscoal",
                        "ore-lignite",
                        "charcoal"
                };

                // Create ItemStack array for all allowed coals
                ItemStack[] coalStacks = allowedCoals
                    .Select(path => new ItemStack(World.GetItem(new AssetLocation("game:" + path)), 1))
                    .ToArray();

                interactions = ArrayExtensions.Append(interactions, new WorldInteraction
                {
                    ActionLangCode = "vsairshipmod:add-coal",
                    MouseButton = EnumMouseButton.Right,
                    Itemstacks = coalStacks//Very satisfying way to set these :D
                });
            }

            return interactions;
        }

        public override string GetInfoText()
        {
            base.GetInfoText();
            string text = base.GetInfoText();

            //Temporal Fuel Display
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

            if (TemporalGearCount != 0)
            {
                text += "\n" + Lang.Get("vsairshipmod:int-temporalfuelusage", timeString);
            }
            else
            {
                text += "\n" + Lang.Get("vsairshipmod:temporalfueldepletedhorizontal");
            }

            //Coal Fuel Display, shows the time of the whole stack unlike gears
            //if(CoalStackSize != 0)
            /*if(CoalStackSize !>= 2)
            {
                long currentCoalSeconds = Math.Max(0, CoalFuelUsage / 1000);
                int remainingPieces = CoalStackSize - 1;
                long totalCoalSeconds = currentCoalSeconds + (long)(remainingPieces * CoalBurnDuration);

                string coalTime;
                if (totalCoalSeconds >= 60)
                {
                    long minutes = totalCoalSeconds / 60;
                    long seconds = totalCoalSeconds % 60;
                    coalTime = $"{minutes}m {seconds}s";
                }
                else
                {
                    coalTime = $"{totalCoalSeconds}s";
                }

                text += "\n" + Lang.Get("vsairshipmod:int-coalstacksize", CoalStackSize);
                text += "\n" + Lang.Get("vsairshipmod:int-coalfuelusage", coalTime);
            }else{
                text += "\n" + Lang.Get("vsairshipmod:coalfueldepletedvertical");
            }*/

            if (CoalStackSize >= 2)
            {
                // CoalFuelUsage is ms, convert to seconds
                long currentPairSeconds = Math.Max(0, CoalFuelUsage / 1000);


                //One active pair is already burning remove 2 coal from stack to get remaining full pairs.
                int remainingCoalAfterActivePair = CoalStackSize - 2;

                // Each full pair = 2 coal consumed at once
                int additionalFullPairs = Math.Max(0, remainingCoalAfterActivePair / 2);

                //Total burn time
                long totalcoalSeconds =
                    currentPairSeconds +                     //Time left on current burning pair
                    (long)(additionalFullPairs * CoalBurnDuration);  // full pairs ahead of it

                string coalTime;

                if (totalcoalSeconds >= 60)
                {
                    long minutes = totalcoalSeconds / 60;
                    long seconds = totalcoalSeconds % 60;
                    coalTime = $"{minutes}m {seconds}s";
                }
                else coalTime = $"{totalcoalSeconds}s";

                //Append metadata ---
                text += "\n" + Lang.Get("vsairshipmod:int-coalstacksize", CoalStackSize);
                text += "\n" + Lang.Get("vsairshipmod:int-coalfuelusage", coalTime);
            }
            else
            {
                //Less than 2 coal so cannot burn at all under new rules
                text += "\n" + Lang.Get("vsairshipmod:int-coalstacksize", CoalStackSize);
                text += "\n" + Lang.Get("vsairshipmod:coalfueldepletedvertical");
            }


            return text;
        }


    }
}