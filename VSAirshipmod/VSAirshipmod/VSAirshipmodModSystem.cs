using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using VSAirshipmod.NSBlock;
using VSAirshipmod.NSBlockEntity;

namespace VSAirshipmod
{
    public class VSAirshipmodModSystem : ModSystem
    {
        // Called on server and client
        // Useful for registering block/entity classes on both sides
        public override void Start(ICoreAPI api)
        {
            base.Start(api);
            
            api.RegisterEntity("EntityAirship", typeof(EntityAirship));
            api.RegisterMountable("airship", EntityAirshipSeat.GetMountable);
            api.RegisterItemClass("ItemAirship", typeof(ItemAirship));

            api.RegisterBlockClass("BlockSpinner", typeof(BlockSpinner));
            api.RegisterBlockClass("BlockSewingMachine", typeof(BlockSewingMachine));
            api.RegisterBlockClass("BlockAutoloom", typeof(BlockAutoloom));

            api.RegisterBlockEntityClass("SewingMachine", typeof(BlockEntitySewingMachine)); 
            api.RegisterBlockEntityClass("Spinner", typeof(BlockEntitySpinner));
            api.RegisterBlockEntityClass("Autoloom", typeof(BlockEntityAutoloom));

            api.RegisterItemClass("ItemVSAirshipmodRoller", typeof(ItemVSAirshipmodRoller));
            api.RegisterEntity("EntityVSAirshipmodConstruction", typeof(EntityVSAirshipmodConstruction));
            //Mod.Logger.Notification("Hello there from template mod: " + api.Side);
        }

        /*public override void StartServerSide(ICoreServerAPI api)
        {
            Mod.Logger.Notification("Hello from template mod server side: " + Lang.Get("vsairshipmod:hello"));
        }

        public override void StartClientSide(ICoreClientAPI api)
        {
            Mod.Logger.Notification("Hello from template mod client side: " + Lang.Get("vsairshipmod:hello"));
        }*/

    }
}


