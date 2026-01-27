using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;
using Newtonsoft.Json.Linq;
using System.Linq.Expressions;

namespace VSAirshipmod
{
	// Token: 0x0200038B RID: 907

	 public class ModSystemVSAirshipmodConstructionSitePreview : ModSystem 
		{
				ICoreClientAPI capi;
				public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;

				public override void StartClientSide(ICoreClientAPI api)
				{
						capi = api;
						api.Event.RegisterGameTickListener(onTick, 100);
				}

				private void onTick(float dt)
				{
						var slot = capi.World.Player.InventoryManager.ActiveHotbarSlot;

						if (slot.Itemstack?.Collectible is ItemVSAirshipmodRoller roller)
						{
							roller.UpdateListsFromStack(slot.Itemstack);

								int orient = roller.GetOrient(capi.World.Player);
								var siteList = roller.siteListByFacing[orient];
								var waterEdgeList = roller.waterEdgeByFacing[orient];

								for (int i = 0; i < siteList.Count; i++)
								{
										var pos = siteList[i];
										//capi.Logger.Notification($"siteList[{i}] = X:{pos.X}, Y:{pos.Y}, Z:{pos.Z}");
								}

								var c = roller.HasWaterLaunch
									? ColorUtil.ColorFromRgba(0, 50, 150, 50)   // blue when water-launchable
									: ColorUtil.ColorFromRgba(0, 150, 50, 50);  // green otherwise
								capi.World.HighlightBlocks(capi.World.Player, 1192, siteList, EnumHighlightBlocksMode.AttachedToSelectedBlock, EnumHighlightShape.Cube);
								capi.World.HighlightBlocks(capi.World.Player, 1193, waterEdgeList, new List<int>() { c }, EnumHighlightBlocksMode.AttachedToSelectedBlock, EnumHighlightShape.Cube);
						} else
						{
								capi.World.HighlightBlocks(capi.World.Player, 1192, ItemVSAirshipmodRoller.emptyList, EnumHighlightBlocksMode.AttachedToSelectedBlock, EnumHighlightShape.Cube);
								capi.World.HighlightBlocks(capi.World.Player, 1193, ItemVSAirshipmodRoller.emptyList, EnumHighlightBlocksMode.AttachedToSelectedBlock, EnumHighlightShape.Cube);
						}
				}
		}

	public class ItemVSAirshipmodRoller : Item
	{
		public bool HasWaterLaunch { get; private set; } = true;
		public override void OnLoaded(ICoreAPI api)
		{
			base.OnLoaded(api);
						siteListByFacing = new List<List<BlockPos>>();
			waterEdgeByFacing = new List<List<BlockPos>>();


						siteListByFacing.Add(siteListN);
						waterEdgeByFacing.Add(waterEdgeListN);
						for (int i = 1; i < 4; i++) {
								siteListByFacing.Add(rotateList(siteListN, i));
								waterEdgeByFacing.Add(rotateList(waterEdgeListN, i));
						}

			this.skillItems = new SkillItem[]
			{
				new SkillItem
				{
					Code = new AssetLocation("east"),
					Name = Lang.Get("facing-west", Array.Empty<object>()) //rivv here, switching these around 180degrees. idk what i'm doing but this worked.
				},
				new SkillItem
				{
					Code = new AssetLocation("north"),
					Name = Lang.Get("facing-south", Array.Empty<object>())
				},
				new SkillItem
				{
					Code = new AssetLocation("west"),
					Name = Lang.Get("facing-east", Array.Empty<object>())
				},
				new SkillItem
				{
					Code = new AssetLocation("south"),
					Name = Lang.Get("facing-north", Array.Empty<object>())
				}
			};
			ICoreClientAPI capi = api as ICoreClientAPI;
			if (capi != null)
			{
				this.skillItems[0].WithIcon(capi, capi.Gui.LoadSvgWithPadding(new AssetLocation("textures/icons/pointwest.svg"), 48, 48, 5, new int?(-1)));
				this.skillItems[1].WithIcon(capi, capi.Gui.LoadSvgWithPadding(new AssetLocation("textures/icons/pointsouth.svg"), 48, 48, 5, new int?(-1)));
				this.skillItems[2].WithIcon(capi, capi.Gui.LoadSvgWithPadding(new AssetLocation("textures/icons/pointeast.svg"), 48, 48, 5, new int?(-1)));
				this.skillItems[3].WithIcon(capi, capi.Gui.LoadSvgWithPadding(new AssetLocation("textures/icons/pointnorth.svg"), 48, 48, 5, new int?(-1)));
			}
		}

		// Token: 0x06001F4F RID: 8015 RVA: 0x001170FC File Offset: 0x001152FC
		public override void OnUnloaded(ICoreAPI api)
		{
			if (this.skillItems != null)
			{
				SkillItem[] array = this.skillItems;
				for (int i = 0; i < array.Length; i++)
				{
					array[i].Dispose();
				}
			}
		}

		// Token: 0x06001F50 RID: 8016 RVA: 0x00117130 File Offset: 0x00115330
		private static List<BlockPos> rotateList(List<BlockPos> startlist, int i)
		{
			Matrixf matrixf = new Matrixf();
			matrixf.RotateY((float)i * 1.5707964f);
			switch (i)
			{
                case 1:
                    matrixf.Translate(-1f, 0f, 0f);
                    break;
                case 2:
                    matrixf.Translate(-1f, 0f, -1f);
                    break;
                case 3:
                    matrixf.Translate(0f, 0f, -1f);
                    break;
                default:
                    break;
            }
			List<BlockPos> list = new List<BlockPos>();
			Vec4f vec = matrixf.TransformVector(new Vec4f((float)startlist[0].X, (float)startlist[0].Y, (float)startlist[0].Z, 1f));
			Vec4f vec2 = matrixf.TransformVector(new Vec4f((float)startlist[1].X, (float)startlist[1].Y, (float)startlist[1].Z, 1f));
			BlockPos minpos = new BlockPos((int)Math.Round((double)Math.Min(vec.X, vec2.X)), (int)Math.Round((double)Math.Min(vec.Y, vec2.Y)), (int)Math.Round((double)Math.Min(vec.Z, vec2.Z)));
			BlockPos maxpos = new BlockPos((int)Math.Round((double)Math.Max(vec.X, vec2.X)), (int)Math.Round((double)Math.Max(vec.Y, vec2.Y)), (int)Math.Round((double)Math.Max(vec.Z, vec2.Z)));
			list.Add(minpos);
			list.Add(maxpos);
			return list;
		}

		// Token: 0x06001F51 RID: 8017 RVA: 0x001172A7 File Offset: 0x001154A7
		public override int GetToolMode(ItemSlot slot, IPlayer byPlayer, BlockSelection blockSelection)
		{
			return GetOrient(byPlayer);
		}

        public new int GetOrient(IPlayer byPlayer)
        {
            return ObjectCacheUtil.GetOrCreate<int>(byPlayer.Entity.Api, "rollerOrient-" + byPlayer.PlayerUID, () => 0);
        }
		public void UpdateListsFromStack(ItemStack stack)
		{
				if (stack == null) return;

				try
				{
						var attrs = stack.Collectible?.Attributes;
						if (attrs == null) return;

						var groundAttr = attrs["constructionBoxGround"];
						var waterAttr  = attrs["constructionBoxWater"];

						if (groundAttr == null || waterAttr == null) return;

						JArray gArr = JArray.Parse(groundAttr.ToString());
						JArray wArr = JArray.Parse(waterAttr.ToString());

						if (gArr.Count < 2 || wArr.Count < 2) return;

						BlockPos ParsePosFromArray(JToken token)
						{
							JArray arr = (JArray)token;
							return new BlockPos((int)arr[0], (int)arr[1], (int)arr[2]);
						}

						siteListN = new List<BlockPos>
						{
							ParsePosFromArray(gArr[0]),
							ParsePosFromArray(gArr[1])
						};

						waterEdgeListN = new List<BlockPos>
						{
							ParsePosFromArray(wArr[0]),
							ParsePosFromArray(wArr[1])
						};

						siteListByFacing.Clear();
						waterEdgeByFacing.Clear();

						try
						{
						    HasWaterLaunch = attrs["hasWaterLaunch"]?.AsBool(true) ?? true;
						}
						catch
						{
						    HasWaterLaunch = true;
						}


						siteListByFacing.Add(siteListN);
						waterEdgeByFacing.Add(waterEdgeListN);

						for (int i = 1; i < 4; i++)
						{
							siteListByFacing.Add(rotateList(siteListN, i));
							waterEdgeByFacing.Add(rotateList(waterEdgeListN, i));
						}
				}
				catch (Exception e)
				{
						this.api?.Logger?.Warning("ItemVSAirshipmodRoller: failed to read preview boxes data: " + e.Message);
				}
		}






		// Token: 0x06001F53 RID: 8019 RVA: 0x001173B1 File Offset: 0x001155B1
		public override SkillItem[] GetToolModes(ItemSlot slot, IClientPlayer forPlayer, BlockSelection blockSel)
		{
			return this.skillItems;
		}

		// Token: 0x06001F54 RID: 8020 RVA: 0x001173B9 File Offset: 0x001155B9
		public override void SetToolMode(ItemSlot slot, IPlayer byPlayer, BlockSelection blockSelection, int toolMode)
		{
			this.api.ObjectCache["rollerOrient-" + byPlayer.PlayerUID] = toolMode;
		}

		// Token: 0x06001F55 RID: 8021 RVA: 0x001173E4 File Offset: 0x001155E4
		public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handling)
		{
			if (blockSel == null)
			{
				return;
			}

		ItemStack stack = slot.Itemstack;
			if (stack == null)
			{
					return;
			}
			UpdateListsFromStack(stack);

			string boattype = stack.Collectible.Attributes["airshiptype"]?.AsString("none");
			bool waterLaunch = stack.Collectible.Attributes["hasWaterLaunch"]?.AsBool(false) ?? false;

			EntityPlayer entityPlayer = byEntity as EntityPlayer;
			IPlayer player = (entityPlayer != null) ? entityPlayer.Player : null;
			if (slot.StackSize < 1)
			{
				ICoreClientAPI coreClientAPI = this.api as ICoreClientAPI;
				if (coreClientAPI == null)
				{
					return;
				}
				coreClientAPI.TriggerIngameError(this, "need5", Lang.Get("Need 5 rolles to place a boat construction site", Array.Empty<object>()));
				return;
			}
			else
			{
				if (this.suitableLocation(player, blockSel, waterLaunch))
				{
					slot.TakeOut(5);
					slot.MarkDirty();
					string material = "oak";
					int orient = GetOrient(player);
					EntityProperties type = byEntity.World.GetEntityType(new AssetLocation("vsairshipmod:vsairshipmodconstruction-" + boattype + "-" + material));
					Entity entity = byEntity.World.ClassRegistry.CreateEntity(type);
					entity.ServerPos.SetPos(blockSel.Position.ToVec3d().AddCopy(0.5, 1.0, 0.5));
					entity.ServerPos.Yaw = -1.5707964f + (float)orient * 1.5707964f;

					switch(orient)
					{ 
						case 0:
							entity.ServerPos.X += 1.0;
							break;
						case 1:
							entity.ServerPos.Z -= 1.0;
                            break;
                        case 2:
							entity.ServerPos.X -= 1.0;
                            break;
                        case 3:
							entity.ServerPos.Z += 1.0;
                            break;
						default:
                            break;
                    }
					entity.Pos.SetFrom(entity.ServerPos);
					byEntity.World.SpawnEntity(entity);
					this.api.World.PlaySoundAt(new AssetLocation("sounds/block/planks"), byEntity, player, true, 32f, 1f);
					handling = EnumHandHandling.PreventDefault;
					return;
				}
				ICoreClientAPI coreClientAPI2 = this.api as ICoreClientAPI;
				if (coreClientAPI2 == null)
				{
					return;
				}
				coreClientAPI2.TriggerIngameError(this, "unsuitableLocation", Lang.Get("Requires a suitable location to place a Airship construction site. Use tool mode to rotate", Array.Empty<object>()));
				return;
			}
		}

		// Token: 0x06001F56 RID: 8022 RVA: 0x001175B8 File Offset: 0x001157B8
		private bool suitableLocation(IPlayer forPlayer, BlockSelection blockSel, bool waterLaunch)
		{
			int orient = GetOrient(forPlayer);
			List<BlockPos> siteList = siteListByFacing[orient];
			List<BlockPos> waterEdgeList = waterEdgeByFacing[orient];
			IBlockAccessor ba = this.api.World.BlockAccessor;
			bool placeable = true;
			//bool waterLauch;
			BlockPos cpos = blockSel.Position;
			BlockPos mingPos = siteList[0].AddCopy(0, 1, 0).Add(cpos);
			BlockPos maxgPos = siteList[1].AddCopy(-1, 0, -1).Add(cpos);
			maxgPos.Y = mingPos.Y;
			this.api.World.BlockAccessor.WalkBlocks(mingPos, maxgPos, delegate(Block block, int x, int y, int z)
			{
				if (!block.SideIsSolid(new BlockPos(x, y, z), BlockFacing.UP.Index))
				{
					placeable = false;
				}
			}, false);
			if (!placeable)
			{
				return false;
			}
			BlockPos minPos = siteList[0].AddCopy(0, 2, 0).Add(cpos);
			BlockPos maxPos = siteList[1].AddCopy(-1, 1, -1).Add(cpos);
			this.api.World.BlockAccessor.WalkBlocks(minPos, maxPos, delegate(Block block, int x, int y, int z)
			{
				Cuboidf[] cboxes = block.GetCollisionBoxes(ba, new BlockPos(x, y, z));
				if (cboxes != null && cboxes.Length != 0)
				{
					placeable = false;
				}
			}, false);
			//BlockPos minlPos = waterEdgeList[0].AddCopy(0, 1, 0).Add(cpos);
			//BlockPos maxlPos = waterEdgeList[1].AddCopy(-1, 0, -1).Add(cpos);
			//this.WalkBlocks(minlPos, maxlPos, delegate(Block block, int x, int y, int z)
			//{
			//	if (waterLaunch == true && !block.IsLiquid())
			//	{
			//		placeable = false;
			//	}
			//}, 2);
			return placeable;
		}

		// Token: 0x06001F57 RID: 8023 RVA: 0x00117720 File Offset: 0x00115920
		public void WalkBlocks(BlockPos minPos, BlockPos maxPos, Action<Block, int, int, int> onBlock, int layer)
		{
			IBlockAccessor ba = this.api.World.BlockAccessor;
			int x2 = minPos.X;
			int miny = minPos.InternalY;
			int minz = minPos.Z;
			int maxx = maxPos.X;
			int maxy = maxPos.InternalY;
			int maxz = maxPos.Z;
			for (int x = x2; x <= maxx; x++)
			{
				for (int y = miny; y <= maxy; y++)
				{
					for (int z = minz; z <= maxz; z++)
					{
						Block block = ba.GetBlock(x, y, z);
						onBlock(block, x, y, z);
					}
				}
			}
		}

		// Token: 0x04001021 RID: 4129
		public static List<BlockPos> emptyList = new List<BlockPos>();

		// Token: 0x04001022 RID: 4130
		public List<List<BlockPos>> siteListByFacing = new List<List<BlockPos>>();

		// Token: 0x04001023 RID: 4131
		public List<List<BlockPos>> waterEdgeByFacing = new List<List<BlockPos>>();

		// Token: 0x04001024 RID: 4132
		public List<BlockPos> siteListN = new List<BlockPos>
		{
			new BlockPos(-5, -1, -2),
			new BlockPos(3, 2, 2)
		};

		// Token: 0x04001025 RID: 4133
		public List<BlockPos> waterEdgeListN = new List<BlockPos>
		{
			new BlockPos(3, -1, -2),
			new BlockPos(6, 0, 2)
		};

		// Token: 0x04001026 RID: 4134
		public SkillItem[] skillItems;
	}
}
