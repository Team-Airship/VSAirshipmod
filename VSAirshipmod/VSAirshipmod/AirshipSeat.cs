using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using VSAirshipmod;

#nullable disable

namespace Vintagestory.GameContent
{
    public class EntityAirshipSeat : EntityRideableSeat
    {
        public override EnumMountAngleMode AngleMode => config.AngleMode;
        Dictionary<string, string> animations => (Entity as EntityAirship).MountAnimations;
        public string actionAnim;

        public override AnimationMetaData SuggestedAnimation
        {
            get
            {
                if (actionAnim == null) return null;

                if (Passenger?.Properties?.Client.AnimationsByMetaCode?.TryGetValue(actionAnim, out var ameta) == true)
                {
                    return ameta;
                }

                return null;
            }
        }



        public EntityAirshipSeat(IMountable mountablesupplier, string seatId, SeatConfig config) : base(mountablesupplier, seatId, config)
        {
            RideableClassName = "airship";
            controls.OnAction = onControls;
        }

        public override bool CanMount(EntityAgent entityAgent)
        {
            if (config.Attributes?["ropeTieablesOnly"].AsBool(false) == true)
            {
                return entityAgent.HasBehavior<EntityBehaviorRopeTieable>();
            }

            return base.CanMount(entityAgent);
        }

        public override bool CanUnmount(EntityAgent entityAgent)
        {
                //bool can = !(Entity as EntityAirship).IsFlying || controls.Sprint || controls.ShiftKey;
                //if (!can){
                //    (entityAgent.Api as ICoreClientAPI)?.TriggerIngameError(this,"Can't_Dismount","Can't dismount in air without pressing Sprint key");
                //}
            return true;
        }

        public override void DidMount(EntityAgent entityAgent)
        {
            base.DidMount(entityAgent);

            entityAgent.AnimManager.StartAnimation(config.Animation ?? animations["idle"]);
        }

        public override void DidUnmount(EntityAgent entityAgent)
        {
            if (Passenger != null)
            {
                foreach ((string key,string animation) in animations)
                {
                    Passenger.AnimManager?.StopAnimation(animation);
                }
                Passenger.AnimManager?.StopAnimation(config.Animation);
                Passenger.SidedPos.Roll = 0;
            }

            base.DidUnmount(entityAgent);
        }

        protected override void tryTeleportToFreeLocation()
        {
            var world = Passenger.World;
            var ba = Passenger.World.BlockAccessor;

            double shortestDistance = 99;
            Vec3d shortestTargetPos = null;

            // var entityBoat = this.Entity;
            /*var ebox = entityBoat.CollisionBox.ToDouble().Translate(entityBoat.ServerPos.XYZ);
            var passengerBox = Passenger.CollisionBox.ToDouble().Translate(Passenger.ServerPos.XYZ);*/
            //&& !ebox.Intersects(passengerBox)

            for (int dx = -4; dx <= 4; dx++)
            {
                for (int dy = 0; dy < 2; dy++)
                {
                    for (int dz = -4; dz <= 4; dz++)
                    {
                        var targetPos = Passenger.ServerPos.XYZ.AsBlockPos.ToVec3d().Add(dx + 0.5, dy + 0.1, dz + 0.5);
                        var block = ba.GetBlockRaw((int)targetPos.X, (int)(targetPos.Y - 0.15), (int)targetPos.Z, BlockLayersAccess.MostSolid);
                        var upfblock = ba.GetBlockRaw((int)targetPos.X, (int)(targetPos.Y), (int)targetPos.Z, BlockLayersAccess.Fluid);
                        if (upfblock.Id == 0 && block.SideSolid[BlockFacing.UP.Index] && !world.CollisionTester.IsColliding(ba, Passenger.CollisionBox, targetPos, false))
                        {
                            var dist = targetPos.DistanceTo(Passenger.ServerPos.XYZ);
                            if (dist < shortestDistance)
                            {
                                shortestDistance = dist;
                                shortestTargetPos = targetPos;
                            }
                        }
                    }
                }
            }

            if (shortestTargetPos != null)
            {
                this.Passenger.TeleportTo(shortestTargetPos);
                return;
            }

            bool found = false;
            for (int dx = -1; !found && dx <= 1; dx++)
            {
                for (int dz = -1; !found && dz <= 1; dz++)
                {
                    var targetPos = Passenger.ServerPos.XYZ.AsBlockPos.ToVec3d().Add(dx + 0.5, 1.1, dz + 0.5);
                    if (!world.CollisionTester.IsColliding(ba, Passenger.CollisionBox, targetPos, false))
                    {
                        this.Passenger.TeleportTo(targetPos);
                        found = true;
                        break;
                    }
                }
            }
        }

        internal void onControls(EnumEntityAction action, bool on, ref EnumHandling handled)
        {
            if ((action == EnumEntityAction.Sneak || (Entity as EntityAirship).IsFlying && action == EnumEntityAction.Sprint) && on)// the logic for dismounting is here now
            {
                if (!(Entity as EntityAirship).IsFlying || (action == EnumEntityAction.Sneak?controls.Sprint: controls.Sneak)) {
                    (Passenger as EntityAgent)?.TryUnmount();
                    controls.StopAllMovement();
                    return;
                } 
                else if(action == EnumEntityAction.Sneak)
                {
                    (Passenger.Api as ICoreClientAPI)?.TriggerIngameError(this, "Can't_Dismount", "Can't dismount in air without pressing Sprint key");
                }
                
            }
        }
    }


}