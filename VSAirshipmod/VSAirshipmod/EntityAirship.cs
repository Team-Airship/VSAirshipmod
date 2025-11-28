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
    public class ModSystemAirshipSounds : ModSystem
    {
        public ILoadedSound travelSound;
        public ILoadedSound idleSound;

        public override bool ShouldLoad(EnumAppSide forSide) => true;

        ICoreAPI api;
        ICoreClientAPI capi;
        bool soundsActive;
        float accum;

        ModSystemProgressBar mspb;
        IProgressBar progressBar;

        public override void StartClientSide(ICoreClientAPI api)
        {
            this.api = api;
            capi = api;
            capi.Event.LevelFinalize += Event_LevelFinalize;
            capi.Event.RegisterGameTickListener(onTick, 0, 123);

            capi.Event.EntityMounted += Event_EntityMounted;
            capi.Event.EntityUnmounted += Event_EntityUnmounted;

            mspb = capi.ModLoader.GetModSystem<ModSystemProgressBar>();
        }


        public override void StartServerSide(ICoreServerAPI api)
        {
            this.api = api;
            api.Event.RegisterGameTickListener(onTickServer, 200);
            api.Event.EntityMounted += Event_EntityMounted;
        }

        Dictionary<string, EntityPlayer> playersOnRatlines = new();


        private void Event_EntityUnmounted(EntityAgent mountingEntity, IMountableSeat mountedSeat)
        {
            mspb.RemoveProgressbar(progressBar);
            progressBar = null;
        }

        private void Event_EntityMounted(EntityAgent mountingEntity, IMountableSeat mountedSeat)
        {
            bool willTire = false;

            if (mountingEntity is EntityPlayer eplr)
            {
                if (mountedSeat.Config.Attributes?.IsTrue("tireWhenMounted") == true)
                {
                    willTire = true;
                    playersOnRatlines[eplr.PlayerUID] = eplr;
                    if (!eplr.WatchedAttributes.HasAttribute("remainingMountedStrengthHours"))
                    {
                        eplr.WatchedAttributes.SetFloat("remainingMountedStrengthHours", 2);
                    }
                }
            }

            if (api.Side == EnumAppSide.Client && progressBar == null && willTire)
            {
                progressBar = mspb.AddProgressbar();
            }

        }


        double lastUpdateTotalHours = 0;
        private void onTickServer(float dt)
        {
            var hoursPassed = (float)(api.World.Calendar.TotalHours - lastUpdateTotalHours);
            if (hoursPassed < 0.1) return;

            List<string> playersToRemove = new List<string>();

            foreach (var eplr in playersOnRatlines.Values)
            {
                bool isOnRatlines = eplr.MountedOn != null && eplr.MountedOn.Config.Attributes?.IsTrue("tireWhenMounted") == true;

                var remainStrengthHours = eplr.WatchedAttributes.GetFloat("remainingMountedStrengthHours", 0);
                remainStrengthHours -= hoursPassed;
                eplr.WatchedAttributes.SetFloat("remainingMountedStrengthHours", remainStrengthHours);

                if (isOnRatlines)
                {
                    if (remainStrengthHours < 0)
                    {
                        eplr.TryUnmount();
                    }
                    // Reduce strength
                }
                else
                {
                    // Increase strength
                    if (remainStrengthHours < -1)
                    {
                        eplr.WatchedAttributes.RemoveAttribute("remainingMountedStrengthHours");
                        playersToRemove.Add(eplr.PlayerUID);
                    }
                }
            }

            foreach (var val in playersToRemove) playersOnRatlines.Remove(val);

            lastUpdateTotalHours = api.World.Calendar.TotalHours;
        }

        private void onTick(float dt)
        {
            var eplr = capi.World.Player.Entity;

            if (progressBar != null && eplr.WatchedAttributes.HasAttribute("remainingMountedStrengthHours"))
            {
                progressBar.Progress = eplr.WatchedAttributes.GetFloat("remainingMountedStrengthHours", 0) / 2f;
            }

            if (eplr.MountedOn is EntityAirshipSeat eairshipseat)
            {
                NowInMotion((float)eairshipseat.Entity.Pos.Motion.Length(), dt); ;
            }
            else
            {
                NotMounted();
            }
        }

        private void Event_LevelFinalize()// This doesn't crash anymore, but I still can't hear the sounds I'm putting here
        {
            travelSound = capi.World.LoadSound(new SoundParams()
            {
                Location = new AssetLocation("sounds/environment/wind.ogg"),// Sounds seem to need to be longer than 10 seconds to work right
                ShouldLoop = true,
                RelativePosition = false,
                DisposeOnFinish = false,
                Volume = 0
            });

            idleSound = capi.World.LoadSound(new SoundParams()
            {
                Location = new AssetLocation("sounds/weather/lowgrumble.ogg"),
                ShouldLoop = true,
                RelativePosition = false,
                DisposeOnFinish = false,
                Volume = 0.1f
            });
        }

        public void NowInMotion(float velocity, float dt)
        {
            accum += dt;
            if (accum < 0.2) return;
            accum = 0;

            if (!soundsActive)
            {
                idleSound.Start();
                soundsActive = true;
            }

            if (velocity > 0.01)
            {
                if (!travelSound.IsPlaying)
                {
                    travelSound.Start();
                }

                var volume = GameMath.Clamp((velocity - 0.025f) * 7, 0, 1);

                travelSound.FadeTo(volume, 0.5f, null);
            }
            else
            {
                if (travelSound.IsPlaying)
                {
                    travelSound.FadeTo(0, 0.5f, (s) => travelSound.Stop());
                }
            }
        }

        public override void Dispose()
        {
            travelSound?.Dispose();
            idleSound?.Dispose();
        }

        public void NotMounted()
        {
            if (soundsActive)
            {
                idleSound.Stop();
                travelSound.SetVolume(0);
                travelSound.Stop();
            }
            soundsActive = false;
        }
    }

















    //public class EntityAirship : Entity, IRenderer, ISeatInstSupplier, IMountableListener // yep this is how you do it dont derive from boat or you get boat sounds
    public class EntityAirship : Entity, IRenderer, ISeatInstSupplier, IMountableListener
    {
        public override double FrustumSphereRadius => base.FrustumSphereRadius * 2;
        public override bool IsCreature => true; // For RepulseAgents behavior to work

        // current forward speed
        public double ForwardSpeed = 0.0;

        // current turning speed (rad/tick)
        public double AngularVelocity = 0.0;

        //If you read this, hello traveler. The code below is responsible for the crasing of the game.... i'm joking. its just a variable that stores the Horizontal Velocity :)
        public double HorizontalVelocity = 0.0;
        public bool IsFlying => !OnGround;

        public string CreatedByPlayername => WatchedAttributes.GetString("createdByPlayername");
        public string CreatedByPlayerUID => WatchedAttributes.GetString("createdByPlayerUID");


        public double AngularVelocityDivider = 10;

        public ModSystemAirshipSounds modsysSounds;

        public override bool ApplyGravity => applyGravity;
        public bool applyGravity = true;

        public override bool IsInteractable
        {
            get { return true; }
        }


        public override float MaterialDensity
        {
            get { return 100f; }
        }

        

        public virtual float SpeedMultiplier { get; set; } = 1f;
        public virtual float TurnMultiplier { get; set; } = 1f;

        public double swimmingOffsetY;
        public override double SwimmingOffsetY => swimmingOffsetY;

        public double RenderOrder => 0;
        public int RenderRange => 999;


        public virtual void OnRenderFrame(float deltaTime, EnumRenderStage stage)
        {

        }


        public void Dispose()
        {
            Api.Logger.Notification("");
        }

        public IMountableSeat CreateSeat(IMountable mountable, string seatId, SeatConfig config)
        {
            return new EntityAirshipSeat(mountable, seatId, config);
        }


        public void DidUnmount(EntityAgent entityAgent)
        {
            MarkShapeModified();
        }

        public void DidMount(EntityAgent entityAgent)
        {
            MarkShapeModified();
        }

        public ICoreClientAPI capi;


        public bool IsEmptyOfPlayers()
        {
            var bhs = GetBehavior<EntityBehaviorSeatable>();
            //var bhr = GetBehavior<EntityBehaviorRideableAccessories>();
            return !bhs.AnyMounted();
        }

        public override void OnEntityDespawn(EntityDespawnData despawn)
        {
            base.OnEntityDespawn(despawn);

            capi?.Event.UnregisterRenderer(this, EnumRenderStage.Before);
        }

        public bool IsEmpty()
        {
            var bhs = GetBehavior<EntityBehaviorSeatable>();
            var bhr = GetBehavior<EntityBehaviorRideableAccessories>();
            return !bhs.AnyMounted() && (bhr == null || bhr.Inventory.Empty);
        }


        public override string GetInfoText()
        {
            string text = base.GetInfoText();
            if (CreatedByPlayername != null)
            {
                text += "\n" + Lang.Get("entity-createdbyplayer", CreatedByPlayername);
            }
            return text;
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

                //ApplyGravityIfNotMounted();
                //updateBoatAngleAndMotion(dt);

            }

            base.OnGameTick(dt);
        }


        public Dictionary<string, string> MountAnimations = new Dictionary<string, string>();
        public override void Initialize(EntityProperties properties, ICoreAPI api, long InChunkIndex3d)
        {
            swimmingOffsetY = properties.Attributes["swimmingOffsetY"].AsDouble();
            SpeedMultiplier = properties.Attributes["speedMultiplier"].AsFloat(1f);
            TurnMultiplier = properties.Attributes["turnMultiplier"].AsFloat(1f);
            /*if (Fuel == 0)
                Fuel = this.Attributes.GetFloat("Fuel");*/

            //this.weatherVaneAnimCode = properties.Attributes["weatherVaneAnimCode"].AsString(null);

            //api.Logger.Notification("Fuel Handled: " + Fuel);

            MountAnimations = properties.Attributes["mountAnimations"].AsObject<Dictionary<string, string>>();


            base.Initialize(properties, api, InChunkIndex3d);

            capi = api as ICoreClientAPI;

            if (capi != null)
            {
                capi.Event.RegisterRenderer(this, EnumRenderStage.Before, "boatsim");
                modsysSounds = api.ModLoader.GetModSystem<ModSystemAirshipSounds>();

                /*if (!Ready)
                {
                    if (!AnimManager.IsAnimationActive("deflated"))
                        AnimManager.StartAnimation("deflated");
                }*/
            }
        }



    }

}