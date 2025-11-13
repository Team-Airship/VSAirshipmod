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
    public class BlockEntitySpinner : BlockEntityOpenableContainer
    {
        static int inputnum = 2;
        static int outputnum = 1;

        ILoadedSound ambientSound;
        internal InventoryQuern inventory;

        public float inputGrindTime;
        public float prevInputGrindTime;

        GuiDialogBlockEntityQuern clientDialog;
        BEBehaviorMPConsumer mpc;
        BEBehaviorAnimatable animatable => GetBehavior<BEBehaviorAnimatable>();
        BlockEntityAnimationUtil animUtil => animatable?.animUtil;

        private float prevSpeed = float.NaN;
        bool automated;
        int nowOutputFace;
        bool beforeGrinding;
        public bool IsGrinding => automated && mpc?.TrueSpeed > 0f;


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
        public override string InventoryClassName => "spinner";
        public virtual string DialogTitle => Lang.Get("Spinner");
        public override InventoryBase Inventory => inventory;


        public BlockEntitySpinner()
        {
            inventory = new InventoryQuern(null, null);
            inventory.SlotModified += OnSlotModified;
        }


        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            inventory.LateInitialize("spinner-" + Pos.X + "/" + Pos.Y + "/" + Pos.Z, api);

            RegisterGameTickListener(Every100ms, 100);
            RegisterGameTickListener(Every500ms, 500);

            if (api.Side == EnumAppSide.Client)
            {
                float rotY = Block?.Shape != null ? Block.Shape.rotateY : 0f;
                animUtil?.InitializeAnimator("vsairshipmod:block/spinner", null, null, new Vec3f(0, rotY, 0));
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




                    if (mpc?.TrueSpeed > 0.01f)
                    {
                        if (!animUtil.activeAnimationsByAnimCode.ContainsKey("spinning"))
                        {
                            //Api.Logger.Debug("Looming animation starting!");
                            animUtil.StartAnimation(new AnimationMetaData()
                            {
                                Animation = "spinning",
                                Code = "spinning",
                                AnimationSpeed = 1,
                                EaseInSpeed = 10,
                                EaseOutSpeed = 10,
                                Weight = 1,
                                BlendMode = EnumAnimationBlendMode.Add
                            });
                        }
                        var anim = animUtil.activeAnimationsByAnimCode["spinning"];

                        var field = anim.GetType().GetField("AnimationSpeed", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                        if (field != null)
                        {
                            field.SetValue(anim, GameMath.Clamp(mpc.TrueSpeed*3.19f, 0.0f, 3.7f));//last value is the max clamp
                        }
                    }
                    else
                    {
                        animUtil.StopAnimation("spinning");
                    }


                if (ambientSound != null && automated && mpc.TrueSpeed != prevSpeed)
                {
                    prevSpeed = mpc.TrueSpeed;
                    ambientSound.SetPitch((0.5f + prevSpeed) * 0.9f);
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
                    Location = new AssetLocation("sounds/block/quern.ogg"),
                    ShouldLoop = true,
                    Position = Pos.ToVec3f().Add(0.5f, 0.25f, 0.5f),
                    DisposeOnFinish = false,
                    Volume = 0.75f
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
        }

        public ItemSlot InputSlot => inventory[0];
        public ItemSlot OutputSlot => inventory[1];

        public ItemStack InputGrindProps
        {
            get
            {
                ItemSlot slot = inventory[0];
                if (slot.Itemstack == null || slot.Itemstack.Collectible.Code != "flaxfibers" || slot.Itemstack.StackSize < inputnum)
                    return null;
                return new ItemStack(Api.World.GetItem(new AssetLocation("game:flaxtwine")), outputnum);
            }
        }

    }
}
