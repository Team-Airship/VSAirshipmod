using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace VSAirshipmod
{
	// Token: 0x02000387 RID: 903
	public class EntityVSAirshipmodConstruction : Entity
	{
		// Token: 0x1700040F RID: 1039
		// (get) Token: 0x06001F15 RID: 7957 RVA: 0x001159F3 File Offset: 0x00113BF3
		public override double FrustumSphereRadius
		{
			get
			{
				return base.FrustumSphereRadius * 2.0;
			}
		}

		// Token: 0x17000410 RID: 1040
		// (get) Token: 0x06001F16 RID: 7958 RVA: 0x00115A05 File Offset: 0x00113C05
		// (set) Token: 0x06001F17 RID: 7959 RVA: 0x00115A18 File Offset: 0x00113C18
		private int CurrentStage
		{
			get
			{
				return this.WatchedAttributes.GetInt("currentStage", 0);
			}
			set
			{
				this.WatchedAttributes.SetInt("currentStage", value);
			}
		}

		// Token: 0x06001F18 RID: 7960 RVA: 0x00115A2C File Offset: 0x00113C2C
		public override void Initialize(EntityProperties properties, ICoreAPI api, long InChunkIndex3d)
		{

			this.boattype = properties.Attributes["airshiptype"].AsString("none");

			this.requirePosesOnServer = true;
			this.WatchedAttributes.RegisterModifiedListener("currentStage", new Action(this.stagedChanged));
			this.WatchedAttributes.RegisterModifiedListener("wildcards", new Action(this.loadWildcards));
			base.Initialize(properties, api, InChunkIndex3d);
			this.stages = properties.Attributes["stages"].AsArray<ConstructionStage>(null, null);
			
			this.genNextInteractionStage();
		}

		// Token: 0x06001F19 RID: 7961 RVA: 0x00115AA4 File Offset: 0x00113CA4
		private void stagedChanged()
		{
			this.MarkShapeModified();
			this.genNextInteractionStage();
		}

		// Token: 0x06001F1A RID: 7962 RVA: 0x00115AB4 File Offset: 0x00113CB4
		public override void OnTesselation(ref Shape entityShape, string shapePathForLogging)
		{
			HashSet<string> addElements = new HashSet<string>();
			int cstage = this.CurrentStage;
			for (int i = 0; i <= cstage; i++)
			{
				ConstructionStage stage = this.stages[i];
				if (stage.AddElements != null)
				{
					foreach (string addele in stage.AddElements)
					{
						addElements.Add(addele + "/*");
					}
				}
				if (stage.RemoveElements != null)
				{
					foreach (string remele in stage.RemoveElements)
					{
						addElements.Remove(remele + "/*");
					}
				}
 	//			if (stage.ActionLangCode != null 
	//			//&& stage.ActionLangCode != "Launch" 
	//			)
	//			{
					//this.StopAnimation("*");
	//				string playAnimation = stage.ActionLangCode; 
	//				this.StartAnimation(playAnimation);
	//				Api.Logger.Notification("VSAirshipmod: " + Lang.Get(" - VSAirshipmod:ATTest - ") + playAnimation +"is trying to play");
	//				foreach (string playanim in stage.ActionLangCode)
	//				{
	//					this.StartAnimation(playanim);
	//				}
	//			} 
			}
			EntityShapeRenderer esr = base.Properties.Client.Renderer as EntityShapeRenderer;
			if (esr != null)
			{
				esr.OverrideSelectiveElements = addElements.ToArray<string>();
			}
			if (this.Api is ICoreClientAPI)
			{
				this.setTexture("debarked", new AssetLocation(string.Format("block/wood/debarked/{0}", this.material)));
				this.setTexture("planks", new AssetLocation(string.Format("block/wood/planks/{0}1", this.material)));
				this.setTexture("darkwood", new AssetLocation(string.Format("block/wood/path/{0}1", this.material)));
				this.setTexture("ornatewood", new AssetLocation(string.Format("VSAirshipmod:block/zigzagbeams/{0}", this.material)));
			}
			base.OnTesselation(ref entityShape, shapePathForLogging);
		}

		// Token: 0x06001F1B RID: 7963 RVA: 0x00115BE4 File Offset: 0x00113DE4
		private void setTexture(string code, AssetLocation assetLocation)
		{
			ICoreClientAPI coreClientAPI = this.Api as ICoreClientAPI;
			CompositeTexture ctex = base.Properties.Client.Textures[code] = new CompositeTexture(assetLocation);
			int tui;
			TextureAtlasPosition textureAtlasPosition;
			coreClientAPI.EntityTextureAtlas.GetOrInsertTexture(ctex, out tui, out textureAtlasPosition, 0f);
			ctex.Baked.TextureSubId = tui;
		}

		// Token: 0x06001F1C RID: 7964 RVA: 0x00115C40 File Offset: 0x00113E40
		public override void OnInteract(EntityAgent byEntity, ItemSlot handslot, Vec3d hitPosition, EnumInteractMode mode)
		{
			base.OnInteract(byEntity, handslot, hitPosition, mode);
			if (this.Api.Side == EnumAppSide.Client)
			{
				return;
			}
			if (this.CurrentStage >= this.stages.Length - 1)
			{
				return;
			}
			if (this.CurrentStage == 0 && handslot.Empty && byEntity.Controls.ShiftKey)
			{
				byEntity.TryGiveItemStack(new ItemStack(this.Api.World.GetItem(new AssetLocation("vsairshipmod:vsairshipmodroller-"+ this.boattype)), 1));
				this.Die(EnumDespawnReason.Death, null);
				return;
			}
			if (!this.tryConsumeIngredients(byEntity, handslot))
			{
				return;
			}
			if (this.CurrentStage == 1)
			{
				StopAllAnimations();
			}
			if (this.CurrentStage < this.stages.Length - 1)
			{
				int currentStage = this.CurrentStage;
				this.CurrentStage = currentStage + 1;
				this.MarkShapeModified();
			}
/* 	if (this.CurrentStage.ActionLangCode != null && this.CurrentStage.ActionLangCode != "Launch" )
            {
                	string playAnimation = CurrentStage.ActionLangCode; 
					string stageAnimation = stack.Collectible.Attributes["stage/"+CurrentStage+"/Animation"]?.AsString("");
					this.StartAnimation(playAnimation);
					Api.Logger.Notification("VSAirshipmod: " + Lang.Get(" - VSAirshipmod:ATTest - ") + playAnimation +"is trying to play");
                {
                    {
                        this.genNextInteractionStage();
                    }
                };
            }  */
if (this.CurrentStage == this.stages.Length - 4 && !this.AnimManager.IsAnimationActive(new string[] { "moveboat" }))
            {
                this.StartAnimation("moveboat");
                {
                 // if ((double)((this.AnimManager.Animator != null) ? this.AnimManager.Animator.GetAnimationState("moveboat").AnimProgress : 0f) >= 0.99)
				//	{
                        this.genNextInteractionStage();
                 //   }
                };
            }
else if (this.CurrentStage >= this.stages.Length - 2 && !this.AnimManager.IsAnimationActive(new string[] { "launch" }))
{
    this.launchingEntity = byEntity;
    this.launchStartPos = this.getCenterPos();
    this.StartAnimation("launch");
    {
        this.genNextInteractionStage();
    };
}

		}

		// Token: 0x06001F1D RID: 7965 RVA: 0x00115D4C File Offset: 0x00113F4C
		private Vec3f getCenterPos()
		{
			IAnimator animator = this.AnimManager.Animator;
			AttachmentPointAndPose apap = (animator != null) ? animator.GetAttachmentPointPose("Center") : null;
			if (apap != null)
			{
				Matrixf mat = new Matrixf();
				mat.RotateY(this.ServerPos.Yaw + 1.5707964f);
				apap.Mul(mat);
				return mat.TransformVector(new Vec4f(0f, 0f, 0f, 1f)).XYZ;
			}
			return null;
		}

		// Token: 0x06001F1E RID: 7966 RVA: 0x00115DC8 File Offset: 0x00113FC8
		private void genNextInteractionStage()
		{
			if (this.CurrentStage + 1 >= this.stages.Length)
			{
				this.nextConstructWis = null;
				return;
			}
			ConstructionStage stage = this.stages[this.CurrentStage + 1];
			if (stage.RequireStacks == null)
			{
				this.nextConstructWis = null;
				return;
			}
			List<WorldInteraction> wis = new List<WorldInteraction>();
			int i = 0;
			ConstructionIgredient[] requireStacks = stage.RequireStacks;
			for (int j = 0; j < requireStacks.Length; j++)
			{
				ConstructionIgredient ingred = requireStacks[j];
				List<ItemStack> stacksl = new List<ItemStack>();
				foreach (KeyValuePair<string, string> val in this.storedWildCards)
				{
					ingred.FillPlaceHolder(val.Key, val.Value);
				}
				if (!ingred.Resolve(this.Api.World, "Require stack for construction stage " + (this.CurrentStage + 1).ToString() + " on entity " + this.Code))
				{
					return;
				}
				i++;
				foreach (CollectibleObject collectible in this.Api.World.Collectibles)
				{
					ItemStack stack = new ItemStack(collectible, 1);
					if (ingred.SatisfiesAsIngredient(stack, false))
					{
						stack.StackSize = ingred.Quantity;
						stacksl.Add(stack);
					}
				}
				ItemStack[] stacks = stacksl.ToArray();
				wis.Add(new WorldInteraction
				{
					ActionLangCode = stage.ActionLangCode,
					Itemstacks = stacks,
					GetMatchingStacks = ((WorldInteraction wi, BlockSelection bs, EntitySelection es) => stacks),
					MouseButton = EnumMouseButton.Right
				});
			}
			if (stage.RequireStacks.Length == 0)
			{
				wis.Add(new WorldInteraction
				{
					ActionLangCode = stage.ActionLangCode,
					MouseButton = EnumMouseButton.Right
				});
			}
			this.nextConstructWis = wis.ToArray();
		}

		// Token: 0x06001F1F RID: 7967 RVA: 0x00115FD4 File Offset: 0x001141D4
		private bool tryConsumeIngredients(EntityAgent byEntity, ItemSlot handslot)
		{
			ICoreAPI api = this.Api;
			IServerPlayer plr = (byEntity as EntityPlayer).Player as IServerPlayer;
			ConstructionStage stage = this.stages[this.CurrentStage + 1];
			IInventory hotbarinv = plr.InventoryManager.GetHotbarInventory();
			List<KeyValuePair<ItemSlot, int>> takeFrom = new List<KeyValuePair<ItemSlot, int>>();
			List<ConstructionIgredient> requireIngreds = new List<ConstructionIgredient>();
			if (stage.RequireStacks == null)
			{
				return true;
			}
			for (int i = 0; i < stage.RequireStacks.Length; i++)
			{
				requireIngreds.Add(stage.RequireStacks[i].Clone());
			}
			Dictionary<string, string> storeWildCard = new Dictionary<string, string>();
			bool skipMatCost = plr != null && plr.WorldData.CurrentGameMode == EnumGameMode.Creative && byEntity.Controls.CtrlKey;
			foreach (ItemSlot slot in hotbarinv)
			{
				if (!slot.Empty)
				{
					if (requireIngreds.Count == 0)
					{
						break;
					}
					for (int j = 0; j < requireIngreds.Count; j++)
					{
						ConstructionIgredient ingred = requireIngreds[j];
						foreach (KeyValuePair<string, string> val in this.storedWildCards)
						{
							ingred.FillPlaceHolder(val.Key, val.Value);
						}
						ingred.Resolve(this.Api.World, "Require stack for construction stage " + j.ToString() + " on entity " + this.Code);
						if (!skipMatCost && ingred.SatisfiesAsIngredient(slot.Itemstack, false))
						{
							int amountToTake = Math.Min(ingred.Quantity, slot.Itemstack.StackSize);
							takeFrom.Add(new KeyValuePair<ItemSlot, int>(slot, amountToTake));
							ingred.Quantity -= amountToTake;
							if (ingred.Quantity <= 0)
							{
								requireIngreds.RemoveAt(j);
								j--;
								if (ingred.StoreWildCard != null)
								{
									storeWildCard[ingred.StoreWildCard] = slot.Itemstack.Collectible.Variant[ingred.StoreWildCard];
								}
							}
						}
						else if (skipMatCost && ingred.StoreWildCard != null)
						{
							storeWildCard[ingred.StoreWildCard] = slot.Itemstack.Collectible.Variant[ingred.StoreWildCard];
						}
					}
				}
			}
			if (!skipMatCost && requireIngreds.Count > 0)
			{
				ConstructionIgredient ingred2 = requireIngreds[0];
				string langCode = plr.LanguageCode;
				plr.SendIngameError("missingstack", null, new object[]
				{
					ingred2.Quantity,
					ingred2.IsWildCard ? Lang.GetL(langCode, ingred2.Name ?? "", Array.Empty<object>()) : ingred2.ResolvedItemstack.GetName()
				});
				return false;
			}
			foreach (KeyValuePair<string, string> val2 in storeWildCard)
			{
				this.storedWildCards[val2.Key] = val2.Value;
			}
			if (!skipMatCost)
			{
				bool soundPlayed = false;
				foreach (KeyValuePair<ItemSlot, int> kvp in takeFrom)
				{
					if (!soundPlayed)
					{
						AssetLocation soundLoc = null;
						ItemStack stack = kvp.Key.Itemstack;
						if (stack.Block != null)
						{
							BlockSounds sounds = stack.Block.Sounds;
							soundLoc = ((sounds != null) ? sounds.Place : null);
						}
						if (soundLoc == null)
						{
							CollectibleBehaviorGroundStorable behavior = stack.Collectible.GetBehavior<CollectibleBehaviorGroundStorable>();
							AssetLocation assetLocation;
							if (behavior == null)
							{
								assetLocation = null;
							}
							else
							{
								GroundStorageProperties storageProps = behavior.StorageProps;
								assetLocation = ((storageProps != null) ? storageProps.PlaceRemoveSound : null);
							}
							soundLoc = assetLocation;
						}
						if (soundLoc != null)
						{
							soundPlayed = true;
							this.Api.World.PlaySoundAt(soundLoc, this, null, true, 32f, 1f);
						}
					}
					kvp.Key.TakeOut(kvp.Value);
					kvp.Key.MarkDirty();
				}
			}
			this.storeWildcards();
			this.WatchedAttributes.MarkPathDirty("wildcards");
			return true;
		}

		// Token: 0x06001F20 RID: 7968 RVA: 0x00116474 File Offset: 0x00114674
		private ItemSlot tryTakeFrom(CraftingRecipeIngredient requireStack, List<ItemSlot> skipSlots, IReadOnlyCollection<ItemSlot> fromSlots)
		{
			foreach (ItemSlot slot in fromSlots)
			{
				if (!slot.Empty && !skipSlots.Contains(slot) && requireStack.SatisfiesAsIngredient(slot.Itemstack, true))
				{
					return slot;
				}
			}
			return null;
		}

		// Token: 0x06001F21 RID: 7969 RVA: 0x001164DC File Offset: 0x001146DC
		public override void OnGameTick(float dt)
		{
			base.OnGameTick(dt);
			IAnimator animator = this.AnimManager.Animator;
			if ((double)((animator != null) ? animator.GetAnimationState("launch").AnimProgress : 0f) >= 0.99)
			{
				this.AnimManager.StopAnimation("launch");
				this.CurrentStage = 0;
				this.MarkShapeModified();
				if (this.World.Side == EnumAppSide.Server)
				{
					this.Spawn();
				}
			}
		}

		// Token: 0x06001F22 RID: 7970 RVA: 0x00116554 File Offset: 0x00114754
		private void Spawn()
		{

			Vec3f nowOff = this.getCenterPos();
			Vec3f offset = (nowOff == null) ? new Vec3f() : (nowOff - this.launchStartPos);
			EntityProperties type = this.World.GetEntityType(new AssetLocation("vsairshipmod:airship-"+this.boattype+"-"+this.material));
			Entity entity = this.World.ClassRegistry.CreateEntity(type);
			if ((int)Math.Abs(this.ServerPos.Yaw * 57.295776f) == 90 || (int)Math.Abs(this.ServerPos.Yaw * 57.295776f) == 270)
			{
				offset.X *= 1.1f;
			}
			offset.Y = 0.5f;
			entity.ServerPos.SetFrom(this.ServerPos).Add(offset);
			entity.ServerPos.Motion.Add((double)offset.X / 50.0, 0.0, (double)offset.Z / 50.0);
			EntityPlayer entityPlayer = this.launchingEntity as EntityPlayer;
			IPlayer plr = (entityPlayer != null) ? entityPlayer.Player : null;
			if (plr != null)
			{
				entity.WatchedAttributes.SetString("createdByPlayername", plr.PlayerName);
				entity.WatchedAttributes.SetString("createdByPlayerUID", plr.PlayerUID);
			}
			entity.Pos.SetFrom(entity.ServerPos);
			this.World.SpawnEntity(entity);
		}

		// Token: 0x06001F23 RID: 7971 RVA: 0x001166CC File Offset: 0x001148CC
		public override WorldInteraction[] GetInteractionHelp(IClientWorldAccessor world, EntitySelection es, IClientPlayer player)
		{
			WorldInteraction[] wis = base.GetInteractionHelp(world, es, player);
			if (this.nextConstructWis == null)
			{
				return wis;
			}
			wis = wis.Append(this.nextConstructWis);
			if (this.CurrentStage == 0)
			{
				wis = wis.Append(new WorldInteraction
				{
					HotKeyCode = "sneak",
					RequireFreeHand = true,
					MouseButton = EnumMouseButton.Right,
					ActionLangCode = "rollers-deconstruct"
				});
			}
			return wis;
		}

		// Token: 0x06001F24 RID: 7972 RVA: 0x00116733 File Offset: 0x00114933
		public override void ToBytes(BinaryWriter writer, bool forClient)
		{
			this.storeWildcards();
			base.ToBytes(writer, forClient);
		}

		// Token: 0x06001F25 RID: 7973 RVA: 0x00116744 File Offset: 0x00114944
		private void storeWildcards()
		{
			TreeAttribute tree = new TreeAttribute();
			foreach (KeyValuePair<string, string> val in this.storedWildCards)
			{
				tree[val.Key] = new StringAttribute(val.Value);
			}
			this.WatchedAttributes["wildcards"] = tree;
		}

		// Token: 0x06001F26 RID: 7974 RVA: 0x001167C0 File Offset: 0x001149C0
		public override void FromBytes(BinaryReader reader, bool isSync)
		{
			base.FromBytes(reader, isSync);
			this.loadWildcards();
		}

		// Token: 0x06001F27 RID: 7975 RVA: 0x001167D0 File Offset: 0x001149D0
		public override string GetInfoText()
		{
			return base.GetInfoText() + "\n" + Lang.Get("Material: {0}", new object[]
			{
				Lang.Get("material-" + this.material, Array.Empty<object>())
			});
		}

		// Token: 0x06001F28 RID: 7976 RVA: 0x00116810 File Offset: 0x00114A10
		private void loadWildcards()
		{
			this.storedWildCards.Clear();
			TreeAttribute tree = this.WatchedAttributes["wildcards"] as TreeAttribute;
			if (tree != null)
			{
				foreach (KeyValuePair<string, IAttribute> val in tree)
				{
					this.storedWildCards[val.Key] = (val.Value as StringAttribute).value;
				}
			}
			string wood;
			if (this.storedWildCards.TryGetValue("wood", out wood))
			{
				this.material = wood;
				if (this.material == null || this.material.Length == 0)
				{
					this.storedWildCards["wood"] = (this.material = "oak");
				}
			}
		}
 		public void StopAllAnimations()
{
    var activeAnims = this.AnimManager?.ActiveAnimationsByAnimCode;
    if (activeAnims == null || activeAnims.Count == 0) return;

    foreach (var anim in activeAnims.Keys.ToList())
    {
        this.AnimManager.StopAnimation(anim);
    }
}


		// Token: 0x04001012 RID: 4114
		private ConstructionStage[] stages;

		// Token: 0x04001013 RID: 4115
		private string material = "oak";
		private string boattype = "none";

		// Token: 0x04001014 RID: 4116
		private Vec3f launchStartPos = new Vec3f();

		// Token: 0x04001015 RID: 4117
		private Dictionary<string, string> storedWildCards = new Dictionary<string, string>();

		// Token: 0x04001016 RID: 4118
		private WorldInteraction[] nextConstructWis;

		// Token: 0x04001017 RID: 4119
		private EntityAgent launchingEntity;
	}
}
