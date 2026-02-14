using System;
using System.Collections.Generic;
using System.Numerics;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace VSAirshipmod
{


    public class EntityAirshipTier1 : EntityAirship
    {



        /// <summary>
        /// Amount of time we have been Inflating the balloon.
        /// </summary>
        /// <remarks>
        /// Latches at 3f.
        /// </remarks>
        public virtual float Inflate
        {
            get
            {
                return WatchedAttributes.GetFloat("Inflate");
            }
            set
            {
                WatchedAttributes.SetFloat("Inflate", Math.Clamp(value, 0f, 3f));
            }
        }

        /// <summary>
        /// If we are ready to take off.
        /// </summary>
        /// <value>
        /// <c>True</c> if <see cref = "Inflate"/> is 3f or above.
        /// </value>
        public virtual bool Ready => WatchedAttributes.GetFloat("Inflate") >= 3f;

        /// <summary>
        /// If we are running the burner.
        /// </summary>
        /// <value>
        /// Burner valve state
        /// </value>
        public virtual bool Idler
        {
            get
            {
                return WatchedAttributes.GetBool("Idler");
            }
            set
            {
                WatchedAttributes.SetBool("Idler", value);
            }
        }
        ///<summary>
        ///Used to check when we need to retessalate the model.
        ///</summary>
        bool reTryTesselation = false;
        float curRotMountAngleZ = 0f;
        public Vec3f mountAngle = new Vec3f();
        ///<summary>
        ///When to hide the main balloon and show the flat one.
        ///</summary>
        bool Deflated = true;
        double horizontalmodifier = 3;
        ///<value>
        ///Seconds untill next <see cref="Fuel"/> is burned.
        ///</value>
        double FuelTimer = 0;

        /// <value>
        /// Amount of Fuel the Airship has.
        /// </value>
        public virtual float Fuel
        {
            get
            {
                return WatchedAttributes.GetFloat("Fuel");
            }
            set
            {
                WatchedAttributes.SetFloat("Fuel", value);
            }
        }
        /*
        /// <summary>
        /// Seconds added to <see cref="FuelTimer"/> when <seealso cref="Fuel"/> is burned.
        /// </summary>
        private static int SecondsPerRot = 6;
        */
        /// <summary>
        /// Amount of Rot that can be stored in <see cref="Fuel"/>.
        /// </summary>
        private static int MaxStackofRot = 64;
        /// <summary>
        /// dicount given to rot when just hovering and keeping the balloon inflated
        /// </summary>
        private static float RotDiscoundWhenHovering = 4;
        /// <summary>
        /// Amount of time in minutes given per temporal gear.
        /// </summary>
        //private static int MinutesPerGear = 15;//Plumb to config!
        public int MinutesPerGear;
        public int SecondsPerRot;

        public long Tier1SpeedMultiplier2;


        float pitchStrength = 2f;
        /*
        /// <summary>
        /// Causes the airship to play its flying animation
        /// </summary>
        /// <value>
        /// Not <see cref="Entity.OnGround"/>
        /// </value>
        public override bool IsFlying => !OnGround;
        */


        //string weatherVaneAnimCode;

        public EntityAirshipTier1() { }

        public override void Initialize(EntityProperties properties, ICoreAPI api, long InChunkIndex3d)
        {
            base.Initialize(properties, api, InChunkIndex3d);

            MinutesPerGear = VSAirshipmodModSystem.Config.Tier1MinutesPerGear;
            SecondsPerRot = VSAirshipmodModSystem.Config.Tier1SecondsPerFuel;

            Tier1SpeedMultiplier2 = VSAirshipmodModSystem.Config.Tier1SpeedMultiplier2;

            if (Fuel == 0)
                Fuel = this.Attributes.GetFloat("Fuel");
            //if (TemporalGearCount == -1)
            //    TemporalGearCount = properties.Attributes["TemporalGearCount"].AsInt(1);
            if (TemporalFuelUsage == -1)
                TemporalFuelUsage = properties.Attributes["TemporalFuelUsage"].AsInt(MinutesPerGear * 60 * 1000);

            pitchStrength = properties.Attributes["pitchStrength"].AsFloat(2f);
            //Listener for TemporalGearCount changes marks the shape modified like sail boat unfurling
            //WatchedAttributes.RegisterModifiedListener("TemporalFuelUsage", MarkShapeModified);

            //this.weatherVaneAnimCode = properties.Attributes["weatherVaneAnimCode"].AsString(null);

            //api.Logger.Notification("Fuel Handled: " + Fuel);

        }


        public override void OnTesselation(ref Shape entityShape, string shapePathForLogging)
        {
            var shape = entityShape;

            if (AnimManager.Animator != null)
            {
                if (Api is ICoreClientAPI)
                {
                    if (shape == entityShape) entityShape = entityShape.Clone();

                    if (Deflated)
                    {
                        entityShape.RemoveElementByName("FULLBALLOON");
                    }
                    else
                    {
                        entityShape.RemoveElementByName("FLATBALLOON");
                    }
                    if (TemporalFuelUsage == 0)
                    {
                        entityShape.RemoveElementByName("TEMPHIDE");
                    }
                    else
                    {
                        entityShape.RemoveElementByName("RUSTHIDE");
                    }
                }
            }
            else
            {
                reTryTesselation = true;
            }

            base.OnTesselation(ref entityShape, shapePathForLogging);
        }
        private ILoadedSound engine_sound;
        private bool engineSoundPlaying = false;
        private void apply_engine_sound(EntityAirshipSeat seat)
        {
            ICoreClientAPI capi = this.Api as ICoreClientAPI;
            if (capi == null) return;

            // Load sound only if not already loaded
            if (this.engine_sound == null)
            {
                this.engine_sound = capi.World.LoadSound(new SoundParams()
                {
                    Location = new AssetLocation("game:sounds/environment/fire"),
                    DisposeOnFinish = false,
                    Position = this.Pos.XYZ.ToVec3f(),
                    ShouldLoop = true,
                });

                if (this.engine_sound == null)
                {
                    capi.Logger.Warning("[AirshipTier1] Failed to load burner sound!");
                    return;
                }
            }

            // Update position
            this.engine_sound.SetPosition((float)this.Pos.X, (float)this.Pos.Y, (float)this.Pos.Z);

            //Sound depending on propeller animation
            if ((seat is not null && seat.controls.Jump || Idler) && !engineSoundPlaying)
            {
                this.engine_sound.Start();
                engineSoundPlaying = true;
                //capi.Logger.Notification("[AirshipTier2] Started engine sound.");
            }
            else if (((seat is null || !seat.controls.Jump) && !Idler) && engineSoundPlaying)
            {
                this.engine_sound.Stop();
                engineSoundPlaying = false;
                //capi.Logger.Notification("[AirshipTier2] Stopped engine sound.");
            }
            if (engineSoundPlaying)
                this.engine_sound.SetVolume(seat.controls.Jump ? 0.5f : 0.25f);
        }

        public override void OnRenderFrame(float dt, EnumRenderStage stage)
        {
            // Client side we update every frame for smoother turning
            if (capi.IsGamePaused) return;

            updateBoatAngleAndMotion(dt);

            long ellapseMs = capi.InWorldEllapsedMilliseconds;
            float forwardpitch = 0;
            if (IsFlying || Swimming)
            {
                double gamespeed = capi.World.Calendar.SpeedOfTime / 60f;
                float intensity = (0.15f + GlobalConstants.CurrentWindSpeedClient.X * 0.9f) * (!Swimming ? Math.Min((float)Pos.Y / 100f, 1f) : 1f);
                float diff = GameMath.DEG2RAD / 2f * intensity;
                mountAngle.X = GameMath.Sin((float)(ellapseMs / 1000.0 * 2 * gamespeed)) * 8 * diff;
                mountAngle.Y = GameMath.Cos((float)(ellapseMs / 2000.0 * 2 * gamespeed)) * 3 * diff;
                mountAngle.Z = -GameMath.Sin((float)(ellapseMs / 3000.0 * 2 * gamespeed)) * 8 * diff;

                curRotMountAngleZ += ((float)AngularVelocity * 5 * Math.Sign(ForwardSpeed) - curRotMountAngleZ) * dt * 5;
                forwardpitch = -((float)this.ForwardSpeed * this.pitchStrength);
            }
            else
            {
                mountAngle.X = 0;
                mountAngle.Y = 0;
                mountAngle.Z = 0;
                curRotMountAngleZ = 0;
                forwardpitch = 0;
            }

            var esr = Properties.Client.Renderer as EntityShapeRenderer;
            if (esr == null) return;

            esr.xangle = mountAngle.X + curRotMountAngleZ;
            esr.yangle = mountAngle.Y;
            esr.zangle = mountAngle.Z + forwardpitch; // Weird. Pitch ought to be xangle.

            if (this.AnimManager.Animator != null)
            {
                if (reTryTesselation)
                {
                    reTryTesselation = false;// now that the animator exists reTesselate the shape to fix
                    MarkShapeModified();
                }

                //---------------------------------------Burner valve animation toggle---------------------------------------//

                if (Idler)
                {
                    if (!AnimManager.IsAnimationActive("burnervalve"))
                        AnimManager.StartAnimation("burnervalve");
                }
                else
                {
                    AnimManager.StopAnimation("burnervalve");
                }

                //---------------------------------------Up/ Down animation handlers---------------------------------------//

                if (HorizontalVelocity > 0)
                {
                    if (!AnimManager.IsAnimationActive("goup"))
                        AnimManager.StartAnimation("goup");
                    AnimManager.StartAnimation("pump");
                    AnimManager.StopAnimation("godown");
                }
                else if (HorizontalVelocity < 0)
                {
                    if (!AnimManager.IsAnimationActive("godown"))
                        AnimManager.StartAnimation("godown");
                    AnimManager.StopAnimation("goup");
                    AnimManager.StopAnimation("pump");
                }
                else
                {
                    AnimManager.StopAnimation("goup");
                    AnimManager.StopAnimation("godown");
                    AnimManager.StopAnimation("pump");
                }

                //Descending Particle Logic, rotated and offset to be around the release hatch
                bool ShouldPlayParticles = (!AnimManager.IsAnimationActive("deflation") && !AnimManager.IsAnimationActive("deflated") && !AnimManager.IsAnimationActive("inflation"));
                if (Api.Side == EnumAppSide.Client && (AnimManager.IsAnimationActive("godown") && ShouldPlayParticles))
                {
                    //disabled extra calculations because frankly its centered enough for the tier 1!
                    //double yawRad = Pos.Yaw;
                    //yawRad = -yawRad;        //JUST HAD TO invert rotation to match entity visuals

                    //And not forget to normalize
                    //yawRad %= 2 * Math.PI;
                    //if (yawRad < 0) yawRad += 2 * Math.PI;

                    Vec3d offset = new Vec3d(0, 11.75, 0);//This is the offset to set it to the hatch

                    //double cos = Math.Cos(yawRad);
                    //double sin = Math.Sin(yawRad);

                    Vec3d emitPos = Pos.XYZ.AddCopy(new Vec3d(
                        offset.X /** cos*/ - offset.Z /** sin*/,
                        offset.Y,
                        offset.X /** sin*/ + offset.Z /** cos*/
                    ));

                    DescendingEffects(emitPos);
                }


                //---------------------------------------Inflation State Machine---------------------------------------//

                if (Inflate > 0)
                {
                    if (AnimManager.IsAnimationActive("deflation"))
                    {
                        AnimManager.StopAnimation("deflation");
                    }
                    else if (AnimManager.IsAnimationActive("deflated"))
                    {
                        AnimManager.StartAnimation("inflation");
                        AnimManager.StopAnimation("deflated");
                    }
                    if (Deflated)
                    {
                        Deflated = false;
                        MarkShapeModified();
                    }
                }
                else if (!Deflated)
                {
                    if (!AnimManager.IsAnimationActive("deflation"))
                        AnimManager.StartAnimation("deflation");
                    RunningAnimation deflation = this.AnimManager.GetAnimationState("deflation");
                    if (deflation != null)
                    {
                        if (deflation.AnimProgress >= 1f)
                        {
                            Deflated = true;
                            MarkShapeModified();
                        }
                    }
                }
                else
                {
                    AnimManager.StopAnimation("deflation");
                    if (!AnimManager.IsAnimationActive("deflated"))
                        AnimManager.StartAnimation("deflated");
                }

                //this.weatherVaneAnimCode = "weathervane";

                //---------------------------------------Fuel Dial Code---------------------------------------//

                if (!AnimManager.IsAnimationActive("fuelrot"))
                {
                    this.AnimManager.StartAnimation("fuelrot");
                }

                RunningAnimation anim = this.AnimManager.GetAnimationState("fuelrot");
                if (anim != null)
                {
                    anim.CurrentFrame = (float)Math.Clamp((1 - (Fuel / (float)MaxStackofRot)) * 29f, 0f, 29f);
                    anim.BlendedWeight = 1f;
                    anim.EasingFactor = 0.1f;
                }

                if (!AnimManager.IsAnimationActive("fueltemp"))
                {
                    this.AnimManager.StartAnimation("fueltemp");
                }

                anim = this.AnimManager.GetAnimationState("fueltemp");
                if (anim != null)
                {
                    anim.CurrentFrame = (float)Math.Clamp((1f - (TemporalFuelUsage / (MinutesPerGear * 60) / 1000f)) * 29f, 0f, 29f);
                    anim.BlendedWeight = 1f;
                    anim.EasingFactor = 0.1f;
                }

                if (!AnimManager.IsAnimationActive("weathervane"))
                {
                    AnimManager.StartAnimation("weathervane");
                }

                float targetWindDir = GameMath.Mod((float)Math.Atan2(WindDirection.X, WindDirection.Z) + GameMath.TWOPI - Pos.Yaw, GameMath.TWOPI);
                anim = AnimManager.GetAnimationState("weathervane");
                if (anim != null)
                {
                    anim.CurrentFrame = Math.Clamp((targetWindDir * GameMath.RAD2DEG) % 360 / 10f, 0f, 35f);
                    anim.BlendedWeight = 1f;
                    //anim.EasingFactor = 1f;
                }
            }
        }


        public override void OnGameTick(float dt)
        {
            base.OnGameTick(dt);
            if (World.Side == EnumAppSide.Server)
            {
                updateBoatAngleAndMotion(dt, true);
            }

            bool shouldSpin = TemporalFuelUsage > 0;
            if (shouldSpin && !AnimManager.IsAnimationActive("GearsSpin"))
            {
                StartAnimation("GearsSpin");
                //Api.Logger.Notification("[AirshipTier1] GearsSpin Animation Started");
            }
            else if (!shouldSpin && AnimManager.IsAnimationActive("GearsSpin"))
            {
                StopAnimation("GearsSpin");
                MarkShapeModified();//THIS WORKS TO CATCH THE GEAR AND MAKE IT RUSTY WITHOUT OTHER TRACKING, WILL ONLY HAPPEN ONCE SINCE ANIM STATE HOLDS - Pal
                //Api.Logger.Notification("[AirshipTier1] GearsSpin Animation Stopped");
            }

        }


        public override void OnAsyncParticleTick(float dt, IAsyncParticleManager manager)
        {
            base.OnAsyncParticleTick(dt, manager);
            if (Swimming)
            {
                double disturbance = Math.Abs(ForwardSpeed) + Math.Abs(AngularVelocity);
                if (disturbance > 0.01)
                {
                    float minx = -3f;
                    float addx = 6f;
                    float minz = -0.75f;
                    float addz = 1.5f;

                    EntityPos herepos = Pos;
                    var rnd = Api.World.Rand;
                    SplashParticleProps.AddVelocity.Set((float)herepos.Motion.X * 20, (float)herepos.Motion.Y, (float)herepos.Motion.Z * 20);
                    SplashParticleProps.AddPos.Set(0.1f, 0, 0.1f);
                    SplashParticleProps.QuantityMul = 0.5f * (float)disturbance;

                    double y = herepos.Y - 0.15;

                    for (int i = 0; i < 10; i++)
                    {
                        float dx = minx + (float)rnd.NextDouble() * addx;
                        float dz = minz + (float)rnd.NextDouble() * addz;

                        double yaw = Pos.Yaw + GameMath.PIHALF + Math.Atan2(dx, dz);
                        double dist = Math.Sqrt(dx * dx + dz * dz);

                        SplashParticleProps.BasePos.Set(
                            herepos.X + Math.Sin(yaw) * dist,
                            y,
                            herepos.Z + Math.Cos(yaw) * dist
                        );

                        manager.Spawn(SplashParticleProps);
                    }
                }
            }
        }


        protected void updateBoatAngleAndMotion(float dt, bool Gametick = false)
        {
            // Ignore lag spikes
            dt = Math.Min(0.5f, dt);

            float step = GlobalConstants.PhysicsFrameTime;
            var motion = SeatsToMotion(step);

            // Add some easing to it


            ForwardSpeed += (motion.X * SpeedMultiplier * Tier1SpeedMultiplier2 - ForwardSpeed) * dt;// "Tier1SpeedMultiplier2" is a quick solution for some day 1 speed config stuff through the config, probably replace since its on top of the JSON one. . .
            AngularVelocity += (motion.Z * (TurnMultiplier / AngularVelocityDivider) - AngularVelocity) * dt;
            HorizontalVelocity = 0;
            if (motion.Y < 0 && Ready)
            {
                HorizontalVelocity = motion.Y * dt;
            }
            else if (motion.Y > 0 || Idler)
            {
                //if ((FuelTimer > 0 || Fuel > 0)&& (TemporalFuelUsage > 0 || TemporalGearCount > 0))
                if ((FuelTimer > 0 || Fuel > 0) && TemporalFuelUsage > 0)
                {
                    if (Gametick)
                    {
                        Inflate = Math.Min(Inflate + dt, 3);
                        if (FuelTimer <= 0)
                        {
                            FuelTimer = SecondsPerRot;
                            if (Api is ICoreServerAPI sapi)
                            {
                                Fuel -= 1;
                            }
                        }
                        else
                        {
                            FuelTimer -= motion.Y > 0 ? dt : dt / RotDiscoundWhenHovering;
                        }
                        /*if (TemporalFuelUsage <= 0)
                        {
                            TemporalFuelUsage = MinutesPerGear * 60 * 1000;
                                if (Api is ICoreServerAPI sapi)
                                {
                                    TemporalGearCount -= 1;
                                }

                        }
                        else*/
                        if (TemporalFuelUsage >= 0)
                        {
                            TemporalFuelUsage -= motion.Y > 0 ? (long)(dt * 1000) : 0;
                        }
                    }

                    if (Ready && motion.Y > 0)
                        HorizontalVelocity = motion.Y * dt;//+= (motion.Y * SpeedMultiplier - HorizontalVelocity) * dt;
                }
                else
                {
                    Idler = false;
                }
            }
            else if (!Ready || ((OnGround || Swimming) && IsEmptyOfPlayers()))
            {
                Inflate = 0;
            }


            if (OnGround && HorizontalVelocity == 0) return;


            var pos = SidedPos;

            Vec3d temp = new();

            if (ForwardSpeed != 0.0)
            {
                var targetmotion = pos.GetViewVector().Mul((float)-ForwardSpeed).ToVec3d();

                temp.X = targetmotion.X;
                temp.Z = targetmotion.Z;
            }

            if (HorizontalVelocity > 0.0)
            {
                if (pos.Y - Api.World.SeaLevel < (Api.World.BlockAccessor.MapSizeY - Api.World.SeaLevel - 10) * MaxAltitude)
                {
                    pos.Motion.Y += 0.013 * dt;
                    pos.Motion.Y = Math.Min(0.013 * horizontalmodifier, pos.Motion.Y);
                }
            }

            applyGravity = IsEmptyOfPlayers();

            if (!Idler && !Swimming && !applyGravity)
            {
                pos.Motion += WindDirection;
                //pos.Motion.X = Math.Clamp(pos.Motion.X, -Math.Abs(WindDirection.X), Math.Abs(WindDirection.X)) + temp.X;
                //pos.Motion.Y = Math.Clamp(pos.Motion.Y, -WindDirection.Length(), WindDirection.Length()) + temp.Y;
                //pos.Motion.Z = Math.Clamp(pos.Motion.Z, -Math.Abs(WindDirection.Z), Math.Abs(WindDirection.Z)) + temp.Z;
                Vec3d tempMotion = pos.Motion;
                tempMotion.X = pos.Motion.HorLength() <= WindDirection.HorLength() ? pos.Motion.X + temp.X : pos.Motion.X * (WindDirection.HorLength() / pos.Motion.HorLength()) + temp.X;
                tempMotion.Z = pos.Motion.HorLength() <= WindDirection.HorLength() ? pos.Motion.Z + temp.Z : pos.Motion.Z * (WindDirection.HorLength() / pos.Motion.HorLength()) + temp.Z;
                pos.Motion = tempMotion;
            }
            else if (ForwardSpeed != 0.0)
            {
                pos.Motion.X = temp.X;
                pos.Motion.Z = temp.Z;
            }


            if (!OnGround && !Swimming && !applyGravity)
            {
                if (HorizontalVelocity < 0.0)
                    pos.Motion.Y -= 0.026 * dt;
                else if (!Idler && motion.Y <= 0f)
                {
                    pos.Motion.Y -= 0.013 * dt;

                }
                pos.Motion.Y = Math.Max(pos.Motion.Y, -0.013 * horizontalmodifier);
            }
            else if (applyGravity && !Swimming)
            {
                pos.Motion.Y -= 0.013 * dt;
                pos.Motion.Y = Math.Max(pos.Motion.Y, -0.013 * horizontalmodifier);
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

        private void ApplyShipAnimation(EntityAirshipSeat seat)
        {
            bool keyW = seat.controls.Forward;
            bool keyA = seat.controls.Left;
            bool keyS = seat.controls.Backward;
            bool keyD = seat.controls.Right;
            bool keyUp = seat.controls.Jump;
            bool keyDown = seat.controls.Sprint;

            bool animForwardLeft = false;
            bool animForwardRight = false;
            bool animBackwardLeft = false;
            bool animBackwardRight = false;
            bool animGoUp = false;
            bool animGoDown = false;

            //Vertical has priority
            if (keyUp)
            {
                animGoUp = true;

                if (keyW || keyA)
                {
                    animForwardRight = true;
                }
                else if (keyS || keyD)
                {
                    animBackwardRight = true;
                }
            }
            else if (keyDown)
            {
                animGoDown = true;

                if (keyW || keyD)
                {
                    animForwardLeft = true;
                }
                else if (keyA || keyS)
                {
                    animBackwardLeft = true;
                }
            }
            else
            {
                //No vertical input, horizontal behavior
                if (keyW)
                {
                    animForwardLeft = true;
                    animForwardRight = true;
                }
                else if (keyS)
                {
                    animBackwardLeft = true;
                    animBackwardRight = true;
                }
                else if (keyA)
                {
                    animForwardRight = true;
                }
                else if (keyD)
                {
                    animForwardLeft = true;
                }
            }


            if (!animForwardLeft) StopAnimation("ForwardLeft");
            if (animForwardLeft) StartAnimation("ForwardLeft");

            if (!animForwardRight) StopAnimation("ForwardRight");
            if (animForwardRight) StartAnimation("ForwardRight");

            if (!animBackwardLeft) StopAnimation("BackwardLeft");
            if (animBackwardLeft) StartAnimation("BackwardLeft");

            if (!animBackwardRight) StopAnimation("BackwardRight");
            if (animBackwardRight) StartAnimation("BackwardRight");

            if (!animGoUp) StopAnimation("GoUp");
            if (animGoUp) StartAnimation("GoUp");

            if (!animGoDown) StopAnimation("GoDown");
            if (animGoDown) StartAnimation("GoDown");

        }
        public override void DidUnmount(EntityAgent entityAgent)
        {
            base.DidUnmount(entityAgent);

            string[] anims = { "ForwardLeft", "ForwardRight", "BackwardLeft", "BackwardRight", "GoUp", "GoDown" };

            foreach (var anim in anims)
            {
                if (AnimManager.IsAnimationActive(anim))
                {
                    StopAnimation(anim);
                    //Api.Logger.Notification("[AirshipTier1] tier one seat eject animation stop fired");
                }
            }
        }

        private void ApplySeatAnimation(EntityAirshipSeat seat)
        {
            bool keyW = seat.controls.Forward;
            bool keyA = seat.controls.Left;
            bool keyS = seat.controls.Backward;
            bool keyD = seat.controls.Right;
            bool keyUp = seat.controls.Jump;
            bool keyDown = seat.controls.Sprint;

            bool animLeft = false;
            bool animRight = false;
            bool animUp = false;
            bool animDown = false;

            // Vertical states
            if (keyUp)
            {
                animUp = true;

                //Only row if a horizontal key is actually held
                if (keyW || keyS || keyA || keyD)
                {
                    animRight = true; //UP + anything rows right
                }
            }
            else if (keyDown)
            {
                animDown = true;

                if (keyW || keyS || keyA || keyD)
                {
                    animLeft = true; //DOWN + anything rows left
                }
            }
            else
            {
                //No vertical input, horizontal logic
                if (keyW || keyS)
                {
                    animLeft = true;
                    animRight = true;
                }
                else if (keyA)
                {
                    animRight = true;
                }
                else if (keyD)
                {
                    animLeft = true;
                }
            }

            // Stop inactive animations
            if (!animLeft) seat.Passenger.AnimManager.StopAnimation(MountAnimations["PilotTurnLeft"]);
            if (!animRight) seat.Passenger.AnimManager.StopAnimation(MountAnimations["PilotTurnRight"]);
            if (!animUp) seat.Passenger.AnimManager.StopAnimation(MountAnimations["PilotGoUp"]);
            if (!animDown) seat.Passenger.AnimManager.StopAnimation(MountAnimations["PilotGoDown"]);

            // Start active animations
            if (animLeft) seat.Passenger.AnimManager.StartAnimation(MountAnimations["PilotTurnLeft"]);
            if (animRight) seat.Passenger.AnimManager.StartAnimation(MountAnimations["PilotTurnRight"]);
            if (animUp) seat.Passenger.AnimManager.StartAnimation(MountAnimations["PilotGoUp"]);
            if (animDown) seat.Passenger.AnimManager.StartAnimation(MountAnimations["PilotGoDown"]);

        }


        public virtual Vec3d SeatsToMotion(float dt)
        {
            double linearMotion = 0;
            double angularMotion = 0;
            double horizontalMotion = 0;

            var bh = GetBehavior<EntityBehaviorSeatable>();
            bh.Controller = null;
            EntityAirshipSeat seat = null;
            foreach (var sseat in bh.Seats)
            {
                seat = sseat as EntityAirshipSeat;
                if (seat == null || seat.Passenger == null) continue;

                if (!(seat.Passenger is EntityPlayer))
                {
                    seat.Passenger.SidedPos.Yaw = SidedPos.Yaw;
                }
                if (seat.Config.BodyYawLimit != null && seat.Passenger is EntityPlayer eplr)
                {
                    eplr.BodyYawLimits = new AngleConstraint(Pos.Yaw + seat.Config.MountRotation.Y * GameMath.DEG2RAD, (float)seat.Config.BodyYawLimit);
                    eplr.HeadYawLimits = new AngleConstraint(Pos.Yaw + seat.Config.MountRotation.Y * GameMath.DEG2RAD, GameMath.PIHALF);
                }

                if (!seat.Config.Controllable || bh.Controller != null) continue;
                var controls = seat.controls;

                bh.Controller = seat.Passenger;
                /*
                                if (controls.Left == controls.Right)
                                {
                                    StopAnimation("turnLeft");
                                    StopAnimation("turnRight");
                                }
                                if (controls.Left && !controls.Right)
                                {
                                    StartAnimation("turnLeft");
                                    StopAnimation("turnRight");
                                }
                                if (controls.Right && !controls.Left)
                                {
                                    StopAnimation("turnLeft");
                                    StartAnimation("turnRight");
                                }*/

                //Apply ship animations
                ApplyShipAnimation(seat);
                //Apply player animations
                ApplySeatAnimation(seat);

                //controls altitude (horizontal motion). its before tries to move, becouse tries to move ignores up and down motion and it was not working 
                if (controls.Jump || controls.Sprint)
                {

                    float dir = controls.Jump ? 1 : -1;

                    horizontalMotion += dir * dt * 1f;
                }

                if (!controls.TriesToMove)
                {
                    seat.actionAnim = null;
                    if (seat.Passenger.AnimManager != null && !seat.Passenger.AnimManager.IsAnimationActive(MountAnimations["ready"]))
                    {
                        seat.Passenger.AnimManager.StartAnimation(MountAnimations["ready"]);
                    }
                    continue;
                }
                else
                {
                    if (controls.Right && !controls.Backward && !controls.Forward)
                    {
                        seat.actionAnim = MountAnimations["backwards"];
                    }
                    else
                    {
                        seat.actionAnim = MountAnimations[controls.Backward ? "backwards" : "forwards"];
                    }

                    seat.Passenger.AnimManager?.StopAnimation(MountAnimations["ready"]);
                }


                if (controls.Left || controls.Right)
                {
                    float dir = controls.Left ? 1 : -1;
                    angularMotion += dir * dt;
                }


                if (controls.Forward || controls.Backward)
                {
                    float dir = controls.Forward ? 1 : -1;

                    linearMotion += dir * dt * 2f;
                }


            }

            apply_engine_sound(seat);
            return new Vec3d(linearMotion, horizontalMotion, angularMotion);
        }


        public override void OnInteract(EntityAgent byEntity, ItemSlot itemslot, Vec3d hitPosition, EnumInteractMode mode)
        {
            int seleBox = (byEntity as EntityPlayer).EntitySelection?.SelectionBoxIndex ?? -1;
            var bhs = GetBehavior<EntityBehaviorSelectionBoxes>();

            var player = byEntity as EntityPlayer;

            if (bhs != null && seleBox > 0)
            {
                var apap = bhs.selectionBoxes[seleBox - 1];
                string apname = apap.AttachPoint.Code;
                if (apname == "burnervalveAP")
                {
                    Idler = !Idler;
                    return;
                }
                //Api.Logger.Notification(apname);
            }

            if (mode == EnumInteractMode.Interact && AllowPickup() && IsEmpty())
            {
                if (tryPickup(byEntity, mode)) return;
            }
            if (((itemslot.Itemstack?.Collectible.Code == "rot") || (itemslot.Itemstack?.Collectible.Code == "vsairshipmod:airshipfat")) && Fuel < MaxStackofRot)
            //freaky told me to do it aaaaa testing
            {
                Fuel += itemslot.TakeOut((int)(MaxStackofRot - Math.Ceiling(Fuel))).StackSize;
                Api.Logger.Notification("Total: " + Fuel);
                return;
            }
            /*if (itemslot.Itemstack?.Collectible.Code == "vsairshipmod:airshipfat" && Fuel < MaxStackofRot-7)
            {
                Fuel += itemslot.TakeOut((int)((MaxStackofRot - Math.Ceiling(Fuel))/8)).StackSize*8;
                Api.Logger.Notification("Total: " + Fuel);
                return;
            }*/
            //if (itemslot.Itemstack?.Collectible.Code == "gear-temporal" && (TemporalGearCount + TemporalFuelUsage > 0? 1:0 ) < TemporalGearMaxCount)
            if (itemslot.Itemstack?.Collectible.Code == "gear-temporal" && TemporalFuelUsage <= 0)
            {
                //TemporalGearCount += itemslot.TakeOut((int)((TemporalGearMaxCount - TemporalGearCount) - TemporalFuelUsage > 0 ? 1 : 0)).StackSize;
                //Api.Logger.Notification("Total: " + TemporalGearCount);
                itemslot.TakeOut(1);
                TemporalFuelUsage = MinutesPerGear * 60 * 1000;//Adding the fuel immediately here like in T2, not sure why this wasnt done off the bat :/ - Pal
                MarkShapeModified();//to update gear visual from rusty
                if (Api.Side == EnumAppSide.Server)
                {
                    World.PlaySoundAt(new AssetLocation("game:sounds/effect/latch"), this);
                }
                var RustyGear = World.GetItem(new AssetLocation("game:gear-rusty"));
                if (RustyGear != null)
                {
                    var stack = new ItemStack(RustyGear, 1);
                    if (!player.TryGiveItemStack(stack))
                        World.SpawnItemEntity(stack, byEntity.ServerPos.XYZ);
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
            // shift + click to remove boat
            if (byEntity.Controls.ShiftKey)
            {
                ItemStack stack = new ItemStack(World.GetItem(Code));
                stack.Attributes.SetFloat("Fuel", Fuel);
                Api.Logger.Notification("Fuel Picked Up: " + stack.Attributes.GetFloat("Fuel"));
                stack.Attributes.SetLong("TemporalFuelUsage", TemporalFuelUsage);
                Api.Logger.Notification("Usage logged: " + stack.Attributes.GetLong("TemporalFuelUsage"));
                //stack.Attributes.SetInt("TemporalGearCount", TemporalGearCount);
                //Api.Logger.Notification("Gears Picked Up: " + stack.Attributes.GetInt("TemporalGearCount"));
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
            EnumHandling handled = EnumHandling.PassThrough;

            List<WorldInteraction> interactions = new List<WorldInteraction>();

            int seleBox = player.Entity.EntitySelection?.SelectionBoxIndex ?? -1;
            var bhs = GetBehavior<EntityBehaviorSelectionBoxes>();

            if (bhs != null && seleBox > 0)
            {
                var apap = bhs.selectionBoxes[seleBox - 1];
                string apname = apap.AttachPoint.Code;
                if (apname == "burnervalveAP")
                {
                    interactions.Add(new WorldInteraction
                    {
                        ActionLangCode = "vsairshipmod:toggleburner",
                        MouseButton = EnumMouseButton.Right
                    });
                    return interactions.ToArray();
                }
                //Api.Logger.Notification(apname);
            }

            foreach (EntityBehavior behavior in SidedProperties.Behaviors)
            {
                WorldInteraction[] wis = behavior.GetInteractionHelp(world, es, player, ref handled);
                if (wis != null) interactions.AddRange(wis);

                if (handled == EnumHandling.PreventSubsequent) break;
            }

            return interactions.ToArray();
        }


        public override string GetInfoText()
        {
            base.GetInfoText();
            string text = base.GetInfoText();
            text += "\n" + Lang.Get("vsairshipmod:float-fuel", Fuel);


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

            //if (TemporalGearCount != 0 || TemporalFuelUsage > 0)
            if (TemporalFuelUsage > 0)
            {
                text += "\n" + Lang.Get("vsairshipmod:int-temporalfuelusage", timeString);
            }
            else
            {
                text += "\n" + Lang.Get("vsairshipmod:temporalfueldepletedhorizontal");
            }

            if (Idler)
                text += "\n" + Lang.Get("vsairshipmod:idle");
            return text;
        }

        public override void OnEntityDespawn(EntityDespawnData despawn)
        {
            this.engine_sound?.Dispose();
            base.OnEntityDespawn(despawn);
        }
    }
}