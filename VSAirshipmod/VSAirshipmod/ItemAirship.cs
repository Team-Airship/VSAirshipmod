using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

#nullable disable

namespace VSAirshipmod
{
    public class ItemAirship : Item
    {

        public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handHandling)
        {
            if (blockSel == null) return;

            base.OnHeldInteractStart(slot, byEntity, blockSel, entitySel, firstEvent, ref handHandling);
            if (handHandling == EnumHandHandling.PreventDefault) return;

            IPlayer player = byEntity.World.PlayerByUid((byEntity as EntityPlayer).PlayerUID);

            if (!byEntity.World.Claims.TryAccess(player, blockSel.Position, EnumBlockAccessFlags.BuildOrBreak))
            {
                return;
            }
            float Fuel = slot.Itemstack.Attributes.GetFloat("Fuel");

            if (!(byEntity is EntityPlayer) || player.WorldData.CurrentGameMode != EnumGameMode.Creative)
            {
                slot.TakeOut(1);
                slot.MarkDirty();
            }

            AssetLocation asset = this.Code;
            EntityProperties type = byEntity.World.GetEntityType(asset);
            if (type == null)
            {
                byEntity.World.Logger.Error("ItemRowboat: No such entity - {0}", asset);
                if (api.World.Side == EnumAppSide.Client)
                {
                    (api as ICoreClientAPI).TriggerIngameError(this, "nosuchentity", string.Format("No such entity '{0}' loaded.", asset));
                }
                return;
            }

            Entity entity = byEntity.World.ClassRegistry.CreateEntity(type);

            if (entity != null)
            {
                entity.ServerPos.X = blockSel.Position.X + (blockSel.DidOffset ? 0 : blockSel.Face.Normali.X) + 0.5f;
                entity.ServerPos.Y = blockSel.Position.Y + (blockSel.DidOffset ? 0 : blockSel.Face.Normali.Y);
                entity.ServerPos.Z = blockSel.Position.Z + (blockSel.DidOffset ? 0 : blockSel.Face.Normali.Z) + 0.5f;
                entity.ServerPos.Yaw = byEntity.ServerPos.Yaw; // player yaw

                entity.Pos.SetFrom(entity.ServerPos);
                entity.PositionBeforeFalling.Set(entity.ServerPos.X, entity.ServerPos.Y, entity.ServerPos.Z);

                entity.Attributes.SetString("origin", "playerplaced");

                

                entity.Attributes.SetFloat("Fuel", Fuel);

                byEntity.World.SpawnEntity(entity);
                handHandling = EnumHandHandling.PreventDefaultAction;
            }
        }

        public override WorldInteraction[] GetHeldInteractionHelp(ItemSlot inSlot)
        {
            return new WorldInteraction[] {
                new WorldInteraction()
                {
                    ActionLangCode = "heldhelp-place",
                    MouseButton = EnumMouseButton.Right,
                }
            }.Append(base.GetHeldInteractionHelp(inSlot));
        }

        public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
        {
            base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
        }
    }



}