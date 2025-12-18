using System;
using System.Collections.Generic;
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
                WatchedAttributes.SetFloat("Inflate", Math.Clamp(value,0f,3f));
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

        /// <summary>
        /// Seconds added to <see cref="FuelTimer"/> when <seealso cref="Fuel"/> is burned.
        /// </summary>
        private static int SecondsPerRot = 6;
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
        private static int MinutesPerGear = 6;

        /// <summary>
        /// Causes the airship to play its flying animation
        /// </summary>
        /// <value>
        /// Not <see cref="Entity.OnGround"/>
        /// </value>
        public override bool IsFlying => !OnGround;


        //string weatherVaneAnimCode;

        public EntityAirshipTier1() { }

        public override void Initialize(EntityProperties properties, ICoreAPI api, long InChunkIndex3d)
        {
            base.Initialize(properties, api, InChunkIndex3d);
            if (Fuel == 0)
                Fuel = this.Attributes.GetFloat("Fuel");
            if (TemporalGearCount == -1)
                TemporalGearCount = properties.Attributes["TemporalGearCount"].AsInt(1);
            if (TemporalFuelUsage == -1)
                TemporalFuelUsage = properties.Attributes["TemporalFuelUsage"].AsInt(0);

            //this.weatherVaneAnimCode = properties.Attributes["weatherVaneAnimCode"].AsString(null);

            //api.Logger.Notification("Fuel Handled: " + Fuel);

        }

        public override void OnTesselation(ref Shape entityShape, string shapePathForLogging)
        {
            var shape = entityShape;

            if (AnimManager.Animator != null) {
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
                }
            }
            else
            {
                reTryTesselation = true;
            }

            base.OnTesselation(ref entityShape, shapePathForLogging);
        }


        public override void OnRenderFrame(float dt, EnumRenderStage stage)
        {
            // Client side we update every frame for smoother turning
            if (capi.IsGamePaused) return;

            updateBoatAngleAndMotion(dt);

            long ellapseMs = capi.InWorldEllapsedMilliseconds;
            float forwardpitch = 0;
            if (IsFlying)//(!onGround)//
            {
                double gamespeed = capi.World.Calendar.SpeedOfTime / 60f;
                float intensity = 0.15f + GlobalConstants.CurrentWindSpeedClient.X * 0.9f;
                float diff = GameMath.DEG2RAD / 2f * intensity;
                mountAngle.X = GameMath.Sin((float)(ellapseMs / 1000.0 * 2 * gamespeed)) * 8 * diff;
                mountAngle.Y = GameMath.Cos((float)(ellapseMs / 2000.0 * 2 * gamespeed)) * 3 * diff;
                mountAngle.Z = -GameMath.Sin((float)(ellapseMs / 3000.0 * 2 * gamespeed)) * 8 * diff;

                curRotMountAngleZ += ((float)AngularVelocity * 5 * Math.Sign(ForwardSpeed) - curRotMountAngleZ) * dt * 5;
                forwardpitch = (float)ForwardSpeed * 1.3f;
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
                    anim.CurrentFrame = (float)Math.Clamp((1f - (TemporalFuelUsage / (MinutesPerGear * 60 ) / 1000f)) * 29f, 0f, 29f);
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
                    anim.CurrentFrame = Math.Clamp((targetWindDir * GameMath.RAD2DEG)%360 / 10f,0f,35f);
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
                updateBoatAngleAndMotion(dt,true);
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


        protected void updateBoatAngleAndMotion(float dt,bool Gametick = false)
        {
            // Ignore lag spikes
            dt = Math.Min(0.5f, dt);

            float step = GlobalConstants.PhysicsFrameTime;
            var motion = SeatsToMotion(step);

            // Add some easing to it


            ForwardSpeed += (motion.X * SpeedMultiplier - ForwardSpeed) * dt;
            AngularVelocity += (motion.Z * (TurnMultiplier / AngularVelocityDivider) - AngularVelocity) * dt;
            HorizontalVelocity = 0;
            if (motion.Y < 0 && Ready)
            {
                HorizontalVelocity = motion.Y * dt;
            }
            else if (motion.Y > 0 || Idler)
            {
                if ((FuelTimer > 0 || Fuel > 0)&& (TemporalFuelUsage > 0 || TemporalGearCount > 0))
                {
                    if (Gametick) {
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
                        if (TemporalFuelUsage <= 0)
                        {
                            TemporalFuelUsage = MinutesPerGear * 60 * 1000;
                            if (Api is ICoreServerAPI sapi)
                            {
                                TemporalGearCount -= 1;
                            }
                        }
                        else
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


            if (!IsFlying && HorizontalVelocity == 0) return;


            var pos = SidedPos;

            Vec3d temp = new();

            if (ForwardSpeed != 0.0)
            {
                var targetmotion = pos.GetViewVector().Mul((float)-ForwardSpeed).ToVec3d();

                temp.X = targetmotion.X;
                temp.Z = targetmotion.Z;
                pos.Motion.X = temp.X;
                pos.Motion.X = temp.Y;
            }

            if (HorizontalVelocity > 0.0)
            {
                pos.Motion.Y = 0.013 * horizontalmodifier;
            }


            if (!Idler && !Swimming)
            {
                pos.Motion += WindDirection * 0.013f;
                pos.Motion.X = Math.Clamp(pos.Motion.X, -Math.Abs(WindDirection.X), Math.Abs(WindDirection.X)) + temp.X;
                //pos.Motion.Y = Math.Clamp(pos.Motion.Y, -WindDirection.Length(), WindDirection.Length()) + temp.Y;
                pos.Motion.Z = Math.Clamp(pos.Motion.Z, -Math.Abs(WindDirection.Z), Math.Abs(WindDirection.Z)) + temp.Z;
            }
            else if(ForwardSpeed != 0.0)
            {
                pos.Motion.X = temp.X;
                //pos.Motion.Y = temp.Y;
                pos.Motion.Z = temp.Z;
            }

                applyGravity = IsEmptyOfPlayers();

            if (!OnGround && !Swimming && !applyGravity) {
                if (HorizontalVelocity < 0.0)
                    pos.Motion.Y = -0.013 * horizontalmodifier;
                else if (!Idler && motion.Y <= 0f)
                {
                    pos.Motion.Y -= 0.013 * dt;
                    pos.Motion.Y = Math.Max(pos.Motion.Y, -0.013 * horizontalmodifier);
                }
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


        public virtual Vec3d SeatsToMotion(float dt)
        {
            double linearMotion = 0;
            double angularMotion = 0;
            double horizontalMotion = 0;

            var bh = GetBehavior<EntityBehaviorSeatable>();
            bh.Controller = null;
            foreach (var sseat in bh.Seats)
            {
                var seat = sseat as EntityAirshipSeat;
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
                }

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
            return new Vec3d(linearMotion, horizontalMotion, angularMotion);
        }


        public override void OnInteract(EntityAgent byEntity, ItemSlot itemslot, Vec3d hitPosition, EnumInteractMode mode)
        {
            int seleBox = (byEntity as EntityPlayer).EntitySelection?.SelectionBoxIndex ?? -1;
            var bhs = GetBehavior<EntityBehaviorSelectionBoxes>();

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
            if (itemslot.Itemstack?.Collectible.Code == "rot" && Fuel < MaxStackofRot)
            {
                Fuel += itemslot.TakeOut((int)(MaxStackofRot - Math.Ceiling(Fuel))).StackSize;
                Api.Logger.Notification("Total: " + Fuel);
                return;
            }
            if (itemslot.Itemstack?.Collectible.Code == "vsairshipmod:airshipfat" && Fuel < MaxStackofRot-7)
            {
                Fuel += itemslot.TakeOut((int)((MaxStackofRot - Math.Ceiling(Fuel))/8)).StackSize*8;
                Api.Logger.Notification("Total: " + Fuel);
                return;
            }
            if (itemslot.Itemstack?.Collectible.Code == "gear-temporal" && (TemporalGearCount + TemporalFuelUsage > 0? 1:0 ) < TemporalGearMaxCount)
            {
                TemporalGearCount += itemslot.TakeOut((int)((TemporalGearMaxCount - TemporalGearCount) - TemporalFuelUsage > 0 ? 1 : 0)).StackSize;
                Api.Logger.Notification("Total: " + TemporalGearCount);
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
                stack.Attributes.SetInt("TemporalGearCount", TemporalGearCount);
                Api.Logger.Notification("Gears Picked Up: " + stack.Attributes.GetInt("TemporalGearCount"));
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

            if (TemporalGearCount != 0 || TemporalFuelUsage > 0)
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
    }
}