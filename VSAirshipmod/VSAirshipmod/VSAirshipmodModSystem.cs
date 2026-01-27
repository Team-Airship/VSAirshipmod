using CombatOverhaul.RangedSystems;
using HarmonyLib;
using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace VSAirshipmod
{
    public class VSAirshipmodModSystem : ModSystem
    {
        Harmony harmony = null;
        // Called on server and client
        // Useful for registering block/entity classes on both sides
        public override void Start(ICoreAPI api)
        {
            base.Start(api);
            
            api.RegisterEntity("EntityAirshipTier1", typeof(EntityAirshipTier1));
            api.RegisterEntity("EntityAirshipTier2", typeof(EntityAirshipTier2));
            api.RegisterMountable("airship", EntityAirshipSeat.GetMountable);
            api.RegisterItemClass("ItemAirship", typeof(ItemAirship));

            api.RegisterItemClass("ItemVSAirshipmodRoller", typeof(ItemVSAirshipmodRoller));
            api.RegisterEntity("EntityVSAirshipmodConstruction", typeof(EntityVSAirshipmodConstruction));

            if (api.ModLoader.IsModEnabled("overhaullib")){
                harmony = new Harmony(Mod.Info.ModID);
                harmony.PatchAll();
            }
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

        [HarmonyPatch(typeof(ProjectileEntity), "Initialize")]
        public class FixMountBug
        {
            public static void Postfix(ref ProjectileEntity __instance)
            {
                Entity mount = (__instance.Api.World.GetEntityById(__instance.ShooterId) as EntityAgent).MountedOn?.Entity;
                if(mount is not null)
                    __instance.CollidedWith.Add(mount.EntityId);
            }
        }
        public override void Dispose()
        {
            harmony?.UnpatchAll(Mod.Info.ModID);
        }
    }
}


