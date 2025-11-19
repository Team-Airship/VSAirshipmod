using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Vintagestory.GameContent.Mechanics;

namespace VSAirshipmod.NSBlockEntity
{
    public class BlockEntityAutoloom : BlockEntityOpenableContainer
    {
        static int inputnum = 2;
        static int outputnum = 1;

        ILoadedSound ambientSound;
        internal InventoryQuern inventory;

        public float inputGrindTime;
        public float prevInputGrindTime;

        GuiDialogBlockEntityQuern clientDialog;
        public BEBehaviorMPConsumer mpc;
        BEBehaviorAnimatable animatable => GetBehavior<BEBehaviorAnimatable>();
        BlockEntityAnimationUtil animUtil => animatable?.animUtil;

        private float prevSpeed = float.NaN;
        bool automated;
        int nowOutputFace;
        bool beforeGrinding;
        public bool IsGrinding => automated && mpc?.TrueSpeed > 0f;

        // axle rotation state
        //internal float axleRotation;

        // axle renderer
        private IRenderer clientRenderer;
        private MeshRef axleMeshRef;

        //for shuttle sounds
        float lastProgress = 0f;
        int playedcount = 0;


        public string Material => Block.LastCodePart();

        public float GrindSpeed
        {
            get
            {
                if (automated && mpc?.Network != null)
                    return mpc.TrueSpeed / 2f;
                return 0;
            }
        }


        public virtual float maxGrindingTime() => 60f;
        public override string InventoryClassName => "autoloom";
        public virtual string DialogTitle => Lang.Get("Autoloom");
        public override InventoryBase Inventory => inventory;


        public BlockEntityAutoloom()
        {
            inventory = new InventoryQuern(null, null);
            inventory.SlotModified += OnSlotModified;
        }


        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            inventory.LateInitialize("autoloom-" + Pos.X + "/" + Pos.Y + "/" + Pos.Z, api);

            RegisterGameTickListener(Every100ms, 100);
            RegisterGameTickListener(Every500ms, 500);

            if (api.Side == EnumAppSide.Client)
            {
                float rotY = Block?.Shape != null ? Block.Shape.rotateY : 0f;
                animUtil?.InitializeAnimator("vsairshipmod:block/autoloom", null, null, new Vec3f(0, rotY, 0));

                var capi = api as ICoreClientAPI;

                // Generate mesh once
                MeshData mesh = GenAxleMesh();
                if (mesh != null)
                {
                    axleMeshRef = capi.Render.UploadMesh(mesh);
                }

                // Register renderer
                clientRenderer = new AutoloomAxleRenderer(this);
                capi.Event.RegisterRenderer(clientRenderer, EnumRenderStage.Opaque, "autoloom-axle-render");


            }
        }


        public override void CreateBehaviors(Block block, IWorldAccessor worldForResolve)
        {
            base.CreateBehaviors(block, worldForResolve);

            mpc = GetBehavior<BEBehaviorMPConsumer>();
            if (mpc != null)
            {
                mpc.OnConnected = () =>
                {
                    automated = true;
                };
                mpc.OnDisconnected = () =>
                {
                    automated = false;
                };
            }
        }


        private void Every100ms(float dt)
        {
            float grindSpeed = GrindSpeed;

                if (Api.Side == EnumAppSide.Client)
                {

                    if (!animUtil.activeAnimationsByAnimCode.ContainsKey("idle"))//this is a dummy animation to keep the shape drawn
                    {
                        animUtil.StartAnimation(new AnimationMetaData()
                        {
                            Animation = "idle",
                            Code = "idle",
                            AnimationSpeed = 1,
                            EaseInSpeed = 1,
                            EaseOutSpeed = 1,
                            Weight = 1,
                            BlendMode = EnumAnimationBlendMode.Add
                        });
                    }



                    //Api.Logger.Debug($"Loom speed: {mpc.TrueSpeed}, prevSpeed: {prevSpeed}");
                    //if(animUtil.activeAnimationsByAnimCode.ContainsKey("looming")){
                        //animUtil.StopAnimation("looming");
                        //Api.Logger.Debug("Stopping Loom Animation");
                    //}
                    if (mpc?.TrueSpeed > 0.01f)
                    {

                        string animCode = "looming";
                        BlockFacing facing = BlockFacing.FromCode(Block.LastCodePart());
                        if (mpc != null /*&& mpc.isRotationReversed()*/)
                        {
                            //animCode = "looming2";

                            //0 is north
                            //1 is east
                            //2 is south
                            //3 is west

                            if(facing == BlockFacing.HORIZONTALS[3] && mpc.isRotationReversed()){
                                animCode = "looming2";
                            }
                            if(facing == BlockFacing.HORIZONTALS[1] && !mpc.isRotationReversed()){
                                animCode = "looming2";
                            }
                            if(facing == BlockFacing.HORIZONTALS[0] && !mpc.isRotationReversed()){
                                animCode = "looming2";
                            }
                            if(facing == BlockFacing.HORIZONTALS[2] && mpc.isRotationReversed()){
                                animCode = "looming2";
                            }
                        }

                        if (!animUtil.activeAnimationsByAnimCode.ContainsKey(animCode))
                        {
                            animUtil.StartAnimation(new AnimationMetaData()
                            {
                                Animation = animCode,
                                Code = animCode,
                                AnimationSpeed = 1,
                                EaseInSpeed = 10,
                                EaseOutSpeed = 10,
                                Weight = 1,
                                BlendMode = EnumAnimationBlendMode.Add
                            });
                        }

                        var anim = animUtil.activeAnimationsByAnimCode[animCode];

                        // Alright fuck it I'm just using reflection to access the field directly, no more starting stopping nonsense. Certified hYPERION (Pal5k) moment
                        var field = anim.GetType().GetField("AnimationSpeed", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                        if (field != null)
                        {
                            field.SetValue(anim, GameMath.Clamp(mpc.TrueSpeed*3f, 0.0f, 5.5f));//last value is the max clamp
                            //Api.Logger.Debug($"Looming speed updated dynamically: {GameMath.Clamp(mpc.TrueSpeed*1.3f, 0.0f, 5.5f)}");
                        }

                        //This is for a sound trigger based on animation progress
                        var state = animUtil?.animator?.GetAnimationState(animCode);
                        float progress = state?.AnimProgress ?? 0f;

                        if (progress < lastProgress)
                        {
                            playedcount = 0;
                        }

                        // Fire sound at about 25% of animation cycle
                        if ((progress > 0.25f && progress < 0.74) && playedcount == 0)//range incase of imprecision
                        {
                            Api.World.PlaySoundAt(
                                new AssetLocation("sounds/tool/padlock.ogg"),
                                Pos.X + 0.5,
                                Pos.Y + 0.5,
                                Pos.Z + 0.5,
                                null,
                                0.5f,//pitch
                                20f//range
                            );
                            playedcount = 1;
                        }
                        if ((progress > 0.75f && progress < 0.99) && playedcount == 1)//another time
                        {
                            Api.World.PlaySoundAt(
                                new AssetLocation("sounds/tool/padlock.ogg"),
                                Pos.X + 0.5,
                                Pos.Y + 0.5,
                                Pos.Z + 0.5,
                                null,
                                0.5f,
                                10f
                            );
                            playedcount = 2;
                        }
                        lastProgress = progress;
                    }
                    else
                    {
                        //Api.Logger.Debug("Looming animation stopped!");
                        animUtil.StopAnimation("looming");
                        animUtil.StopAnimation("looming2");
                    }


                if (ambientSound != null && automated && mpc.TrueSpeed != prevSpeed)
                {
                    prevSpeed = mpc.TrueSpeed;
                    ambientSound.SetPitch((0.11f + prevSpeed) * 0.9f);
                    ambientSound.SetVolume(Math.Min(1f, prevSpeed * 3f));
                }
                else prevSpeed = float.NaN;

                return;
            }

            // Server-side grind tick
            if (CanGrind() && grindSpeed > 0)
            {
                inputGrindTime += dt * grindSpeed;
                if (inputGrindTime >= maxGrindingTime())
                {
                    grindInput();
                    inputGrindTime = 0;
                }

                MarkDirty();
            }
        }


        private void grindInput()
        {
            ItemStack grindedStack = InputGrindProps.Clone();

            if (OutputSlot.Itemstack == null)
            {
                OutputSlot.Itemstack = grindedStack;
            }
            else
            {
                int mergableQuantity = OutputSlot.Itemstack.Collectible.GetMergableQuantity(OutputSlot.Itemstack, grindedStack, EnumMergePriority.AutoMerge);
                if (mergableQuantity > 0)
                {
                    OutputSlot.Itemstack.StackSize += grindedStack.StackSize;
                }
                else
                {
                    BlockFacing face = BlockFacing.HORIZONTALS[nowOutputFace];
                    nowOutputFace = (nowOutputFace + 1) % 4;

                    Block block = Api.World.BlockAccessor.GetBlock(Pos.AddCopy(face));
                    if (block.Replaceable < 6000) return;
                    Api.World.SpawnItemEntity(grindedStack, Pos.ToVec3d().Add(0.5 + face.Normalf.X * 0.7, 0.75, 0.5 + face.Normalf.Z * 0.7), new Vec3d(face.Normalf.X * 0.02f, 0, face.Normalf.Z * 0.02f));
                }
            }

            InputSlot.TakeOut(inputnum);
            InputSlot.MarkDirty();
            OutputSlot.MarkDirty();
        }


        private void Every500ms(float dt)
        {
            if (Api.Side == EnumAppSide.Server && (GrindSpeed > 0 || prevInputGrindTime != inputGrindTime))
            {
                MarkDirty();
            }

            prevInputGrindTime = inputGrindTime;
            updateGrindingState();
        }


        void updateGrindingState()
        {
            if (Api?.World == null) return;

            bool nowGrinding = IsGrinding;
            if (nowGrinding != beforeGrinding)
            {
                updateSoundState(nowGrinding);
                if (Api.Side == EnumAppSide.Server) MarkDirty();
            }
            beforeGrinding = nowGrinding;
        }


        public void updateSoundState(bool nowGrinding)
        {
            if (nowGrinding) startSound();
            else stopSound();
        }

        public void startSound()
        {
            if (ambientSound == null && Api?.Side == EnumAppSide.Client)
            {
                ambientSound = (Api as ICoreClientAPI).World.LoadSound(new SoundParams()
                {
                    Location = new AssetLocation("sounds/effect/gears.ogg"),
                    ShouldLoop = true,
                    Position = Pos.ToVec3f().Add(0.5f, 0.25f, 0.5f),
                    DisposeOnFinish = false,
                    Volume = 0.25f
                });
                ambientSound.Start();
            }
        }

        public void stopSound()
        {
            if (ambientSound != null)
            {
                ambientSound.Stop();
                ambientSound.Dispose();
                ambientSound = null;
            }
        }


        private void OnSlotModified(int slotid)
        {
            if (Api is ICoreClientAPI)
            {
                clientDialog?.Update(inputGrindTime, maxGrindingTime());
            }

            if (slotid == 0 && InputSlot.Empty)
            {
                inputGrindTime = 0.0f;
                MarkDirty();
            }
        }
        internal MeshData GenAxleMesh()
        {
            var block = Api.World.BlockAccessor.GetBlock(Pos);
            if (block.BlockId == 0) return null;

            MeshData modelData;
            ((ICoreClientAPI)Api).Tesselator.TesselateShape(block, Shape.TryGet(Api, "vsairshipmod:shapes/block/autoloom-axle.json"), out modelData);
            return modelData;
        }


        //replaced with dummy "idle" animation
/*        public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tessThreadTesselator)
        {
            if (Block == null) return false;

            if (!animUtil.activeAnimationsByAnimCode.ContainsKey("looming"))//this is totally flawless and brilliant
            {
                MeshData mesh = GenMesh();
                if (mesh != null)
                {
                    // nowhere as elegant as animation one
                    // assume pivot at block center
                    Vec3f pivot = new Vec3f(0.5f, 0f, 0.5f);

                    // Translate to pivot
                    mesh.Translate(-pivot.X, -pivot.Y, -pivot.Z);

                    // Rotate around Y
                    float rotYRad = Block.Shape != null ? Block.Shape.rotateY * GameMath.DEG2RAD : 0f;
                    mesh.Rotate(new Vec3f(0, 1, 0), 0f, rotYRad, 0f);

                    // Translate back
                    mesh.Translate(pivot.X, pivot.Y, pivot.Z);

                    mesher.AddMeshData(mesh);
                }



            }

            return base.OnTesselation(mesher, tessThreadTesselator);
        }



        internal MeshData GenMesh(string type = "base")
        {
            Block block = Api.World.BlockAccessor.GetBlock(Pos);
            if (block.BlockId == 0)
            {
                return null;
            }

            //((ICoreClientAPI)Api).Tesselator.TesselateShape(block, Shape.TryGet(Api, "shapes/block/stone/quern/" + type + ".json"), out var modeldata);
            ((ICoreClientAPI)Api).Tesselator.TesselateShape(block, Shape.TryGet(Api, "vsairshipmod:shapes/block/autoloom.json"), out var modeldata);
            return modeldata;
        }
*/
        public bool CanGrind() => InputGrindProps != null;

        public override bool OnPlayerRightClick(IPlayer byPlayer, BlockSelection blockSel)
        {
            if (blockSel.SelectionBoxIndex == 1) return false;

            if (Api.Side == EnumAppSide.Client)
            {
                toggleInventoryDialogClient(byPlayer, () =>
                {
                    clientDialog = new GuiDialogBlockEntityQuern(DialogTitle, Inventory, Pos, Api as ICoreClientAPI);
                    clientDialog.Update(inputGrindTime, maxGrindingTime());
                    return clientDialog;
                });
            }
            return true;
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
        {
            base.FromTreeAttributes(tree, worldForResolving);
            Inventory.FromTreeAttributes(tree.GetTreeAttribute("inventory"));
            inputGrindTime = tree.GetFloat("inputGrindTime");
            nowOutputFace = tree.GetInt("nowOutputFace");

            if (Api?.Side == EnumAppSide.Client && clientDialog != null)
            {
                clientDialog.Update(inputGrindTime, maxGrindingTime());
            }
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            ITreeAttribute invtree = new TreeAttribute();
            Inventory.ToTreeAttributes(invtree);
            tree["inventory"] = invtree;

            tree.SetFloat("inputGrindTime", inputGrindTime);
            tree.SetInt("nowOutputFace", nowOutputFace);
        }

        public override void OnBlockRemoved()
        {
            base.OnBlockRemoved();

            if (ambientSound != null)
            {
                ambientSound.Stop();
                ambientSound.Dispose();
            }

            clientDialog?.TryClose();
            if (Api.Side == EnumAppSide.Client && clientRenderer != null)
            {
                (Api as ICoreClientAPI).Event.UnregisterRenderer(clientRenderer, EnumRenderStage.Opaque);
                clientRenderer = null;

                axleMeshRef?.Dispose();
                axleMeshRef = null;
            }
        }
        public override void OnBlockUnloaded()
        {
            base.OnBlockUnloaded();
            if (Api.Side == EnumAppSide.Client && clientRenderer != null)
            {
                (Api as ICoreClientAPI).Event.UnregisterRenderer(clientRenderer, EnumRenderStage.Opaque);
                clientRenderer = null;

                axleMeshRef?.Dispose();
                axleMeshRef = null;
            }
        }
        public ItemSlot InputSlot => inventory[0];
        public ItemSlot OutputSlot => inventory[1];

        public ItemStack InputGrindProps
        {
            get
            {
                ItemSlot slot = inventory[0];
                if (slot.Itemstack == null || slot.Itemstack.Collectible.Code != "flaxtwine" || slot.Itemstack.StackSize < inputnum)
                    return null;
                return new ItemStack(Api.World.GetBlock(new AssetLocation("game:linen-normal-down")), outputnum);
            }
        }

    }

    //Might wanna move this to its own file depending on how other people want to keep things organized
    public class AutoloomAxleRenderer : IRenderer
    {
        BlockEntityAutoloom be;
        ICoreClientAPI capi;
        internal MeshRef meshRef;

        public Matrixf ModelMat = new Matrixf();
        public float AngleRad;

        public AutoloomAxleRenderer(BlockEntityAutoloom be)
        {
            this.be = be;
            capi = be.Api as ICoreClientAPI;

            // Generate mesh once
            MeshData mesh = be.GenAxleMesh();
            if (mesh != null)
            {
                meshRef = capi.Render.UploadMesh(mesh);
            }
        }

        public double RenderOrder => 0.5;
        public int RenderRange => 24;

        public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
        {
            if (meshRef == null) return;
            if (stage != EnumRenderStage.Opaque) return;


            Vec3d camPos = capi.World.Player.Entity.CameraPos;
            IRenderAPI rpi = capi.Render;

            rpi.GlDisableCullFace();
            rpi.GlToggleBlend(true);

            IStandardShaderProgram prog = rpi.PreparedStandardShader(be.Pos.X, be.Pos.Y, be.Pos.Z);
            prog.Tex2D = capi.BlockTextureAtlas.AtlasTextures[0].TextureId;


            // Determine block rotation from facing
            float GetFacingAngle(BlockFacing facing)
            {
                if (facing == BlockFacing.HORIZONTALS[0]) return 0f;    // North
                if (facing == BlockFacing.HORIZONTALS[1]) return 270f;   // East
                if (facing == BlockFacing.HORIZONTALS[2]) return 180f;  // South
                if (facing == BlockFacing.HORIZONTALS[3]) return 90f;  // West
                return 0f;
            }

            bool ShouldInvertRotation(BlockFacing facing)
            {
                //invert for north and east
                return facing == BlockFacing.HORIZONTALS[0] || facing == BlockFacing.HORIZONTALS[1];
            }

            //Rotation angle based DIRECTLY on mechanical power
            if (be.mpc != null)
            {
                BlockFacing facing = BlockFacing.FromCode(be.Block.LastCodePart());
                AngleRad = be.mpc.AngleRad;

                if (ShouldInvertRotation(facing))
                {
                    AngleRad = -AngleRad;
                }
            }

            float rotY = 0f;
            if (be.Block.LastCodePart() != null)
            {
                BlockFacing facing = BlockFacing.FromCode(be.Block.LastCodePart());
                rotY = GetFacingAngle(facing);
            }


            prog.ModelMatrix = ModelMat
                .Identity()
                .Translate(be.Pos.X - camPos.X + 0.5f, be.Pos.Y - camPos.Y + 0.5f, be.Pos.Z - camPos.Z + 0.5f) //center of block
                .RotateY(rotY * GameMath.DEG2RAD)// block orientation
                .Rotate(AngleRad, 0f, 0f)
                .Translate(-0.5f, -0.5f, -0.5f)
                .Values;


            prog.ViewMatrix = rpi.CameraMatrixOriginf;
            prog.ProjectionMatrix = rpi.CurrentProjectionMatrix;

            rpi.RenderMesh(meshRef);
            prog.Stop();
        }


        public void Dispose()
        {
            if (meshRef != null)
            {
                meshRef.Dispose();
                meshRef = null;
            }

            capi?.Event.UnregisterRenderer(this, EnumRenderStage.Opaque);
        }
    }


}
