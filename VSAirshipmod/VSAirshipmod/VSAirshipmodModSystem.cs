using HarmonyLib;
using ProtoBuf;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
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

        [ProtoContract]
        public class ConfigPacket
        {
            [ProtoMember(1)]
            public int Tier1MinutesPerGear;
            [ProtoMember(2)]
            public int Tier1SecondsPerFuel;
            [ProtoMember(3)]
            public int Tier2browncoalfueltimeinseconds;
            [ProtoMember(4)]
            public int Tier2blackcoalfueltimeinseconds;
            [ProtoMember(5)]
            public int Tier2anthracitefueltimeinseconds;
            [ProtoMember(6)]
            public int Tier2charcoalfueltimeinseconds;
            [ProtoMember(7)]
            public int Tier2MinutesPerGear;
            [ProtoMember(8)]
            public long Tier2SpeedMultiplier2;//this is a multiplier on the JSON one, kinda skuffed
            [ProtoMember(9)]
            public long Tier1SpeedMultiplier2;//this one too
        }

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

            api.Network.RegisterChannel("Config").RegisterMessageType(typeof(ConfigPacket));

            if (api.ModLoader.IsModEnabled("overhaullib")){
                harmony = new Harmony(Mod.Info.ModID);
                TryPatchProjectileInit(api);
            }
            //Mod.Logger.Notification("Hello there from template mod: " + api.Side);
        }

        private void TryPatchProjectileInit(ICoreAPI api)
        {
            try
            {
                Type projectileType = AccessTools.TypeByName("CombatOverhaul.RangedSystems.ProjectileEntity");
                if (projectileType == null)
                {
                    api.Logger.Warning("[vsairshipmod] CombatOverhaul projectile type was not found; skipping mount collision patch.");
                    return;
                }
                MethodInfo original = AccessTools.Method(projectileType, "Initialize");
                MethodInfo postfix = AccessTools.Method(typeof(VSAirshipmodModSystem), nameof(ProjectileInitPostfix));
                if (original == null || postfix == null)
                {
                    api.Logger.Warning("[vsairshipmod] Could not locate projectile Initialize patch points; skipping mount collision patch.");
                    return;
                }
                harmony.Patch(original, postfix: new HarmonyMethod(postfix));
            }
            catch (Exception ex)
            {
                api.Logger.Warning("[vsairshipmod] Failed applying CombatOverhaul projectile patch: {0}", ex.ToString());
            }
        }

        private static void ProjectileInitPostfix(object __instance)
        {
            if (__instance is not Entity projectile) return;

            PropertyInfo shooterIdProp = AccessTools.Property(__instance.GetType(), "ShooterId");
            if (shooterIdProp == null) return;

            object shooterIdRaw = shooterIdProp.GetValue(__instance);
            if (shooterIdRaw == null) return;

            long shooterId;
            try { shooterId = Convert.ToInt64(shooterIdRaw); }
            catch { return; }

            Entity mount = (projectile.Api?.World?.GetEntityById(shooterId) as EntityAgent)?.MountedOn?.Entity;
            if (mount == null) return;

            object collidedWithRaw = AccessTools.Property(__instance.GetType(), "CollidedWith")?.GetValue(__instance);
            if (collidedWithRaw is IList<long> typed)
            {
                if (!typed.Contains(mount.EntityId)) typed.Add(mount.EntityId);
            }
            else if (collidedWithRaw is IList untyped && !untyped.Contains(mount.EntityId))
            {
                untyped.Add(mount.EntityId);
            }
        }

        /*public override void StartServerSide(ICoreServerAPI api)
        {
            Mod.Logger.Notification("Hello from template mod server side: " + Lang.Get("vsairshipmod:hello"));
        }

        public override void StartClientSide(ICoreClientAPI api)
        {
            Mod.Logger.Notification("Hello from template mod client side: " + Lang.Get("vsairshipmod:hello"));
        }*/

        public override void Dispose()
        {
            harmony?.UnpatchAll(Mod.Info.ModID);
        }



        //config stuff
        private ICoreServerAPI sapi;
        public static AirshipModConfig Config { get; private set; }

        public class AirshipModConfig {//these are the values, define as the defaults
            public int Tier1MinutesPerGear = 15;
            public int Tier1SecondsPerFuel = 6;
            public int Tier2browncoalfueltimeinseconds = 90;
            public int Tier2blackcoalfueltimeinseconds = 60;
            public int Tier2anthracitefueltimeinseconds = 30;
            public int Tier2charcoalfueltimeinseconds = 10;
            public int Tier2MinutesPerGear = 15;
            public long Tier2SpeedMultiplier2 = 1;//this is a multiplier on the JSON one, kinda skuffed
            public long Tier1SpeedMultiplier2 = 1;//this one too
        }


        public override void StartServerSide(ICoreServerAPI api)
        {
            base.StartServerSide(api);
            sapi = api;

            try
            {
                Config = sapi.LoadModConfig<AirshipModConfig>("AirshipModConfig.json");
                if (Config == null)
                {
                    Config = new AirshipModConfig(); // use defaults
                    sapi.StoreModConfig(Config, "AirshipModConfig.json");
                }
            }
            catch (System.Exception e)
            {
                sapi.Logger.Error("[AirshipModConfig] Error loading config, using defaults: {0}", e);
                Config = new AirshipModConfig();
            }

            api.Event.PlayerNowPlaying += Event_PlayerNowPlaying;
        }

        private void Event_PlayerNowPlaying(IServerPlayer byPlayer)
        {
            sapi.Network.GetChannel("Config").SendPacket(new ConfigPacket() { 
                Tier1MinutesPerGear = Config.Tier1MinutesPerGear, 
                Tier1SecondsPerFuel = Config.Tier1SecondsPerFuel, 
                Tier2browncoalfueltimeinseconds = Config.Tier2browncoalfueltimeinseconds, 
                Tier2blackcoalfueltimeinseconds = Config.Tier2blackcoalfueltimeinseconds, 
                Tier2anthracitefueltimeinseconds = Config.Tier2anthracitefueltimeinseconds, 
                Tier2charcoalfueltimeinseconds = Config.Tier2charcoalfueltimeinseconds, 
                Tier2MinutesPerGear = Config.Tier2MinutesPerGear, 
                Tier2SpeedMultiplier2 = Config.Tier2SpeedMultiplier2, 
                Tier1SpeedMultiplier2 = Config.Tier1SpeedMultiplier2 
            }, byPlayer);
        }

        public override void StartClientSide(ICoreClientAPI api)
        {
            base.StartClientSide(api);
            api.Network.GetChannel("Config").SetMessageHandler<ConfigPacket>(handleConfig);
        }

        private void handleConfig(ConfigPacket packet) 
        {
            Config = new AirshipModConfig();
            Config.Tier1MinutesPerGear = packet.Tier1MinutesPerGear;
            Config.Tier1SecondsPerFuel = packet.Tier1SecondsPerFuel;
            Config.Tier2browncoalfueltimeinseconds = packet.Tier2browncoalfueltimeinseconds;
            Config.Tier2blackcoalfueltimeinseconds = packet.Tier2blackcoalfueltimeinseconds;
            Config.Tier2anthracitefueltimeinseconds = packet.Tier2anthracitefueltimeinseconds;
            Config.Tier2charcoalfueltimeinseconds = packet.Tier2charcoalfueltimeinseconds;
            Config.Tier2MinutesPerGear = packet.Tier2MinutesPerGear;
            Config.Tier2SpeedMultiplier2 = packet.Tier2SpeedMultiplier2;
            Config.Tier1SpeedMultiplier2 = packet.Tier1SpeedMultiplier2;
        }

    }
}


