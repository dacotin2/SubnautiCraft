package dev.subnauticraft;

import java.util.HashMap;
import java.util.Map;
import net.minecraft.core.BlockPos;
import net.minecraft.core.Registry;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.core.registries.Registries;
import net.minecraft.resources.Identifier;
import net.minecraft.resources.ResourceKey;
import net.minecraft.world.level.BlockGetter;
import net.minecraft.world.level.block.Block;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.block.RenderShape;
import net.minecraft.world.level.block.state.BlockBehaviour;
import net.minecraft.world.level.block.state.BlockState;

/**
 * The blocks that stand for Subnautica's terrain while Steve digs it (SnDig): invisible (Subnautica draws the terrain
 * itself), but mined, heard and dropped exactly like the block they stand for (sand like sand, stone with a pickaxe,
 * giving cobblestone...).
 */
public final class SnTerrainBlocks {
	/** Subnautica's answer (a vanilla block id) to the block standing for it. */
	private static final Map<String, Block> FOR = new HashMap<>();

	private SnTerrainBlocks() {
	}

	static void register() {
		add("terrain_sand", Blocks.SAND);
		add("terrain_gravel", Blocks.GRAVEL);
		add("terrain_stone", Blocks.STONE);
		add("terrain_deepslate", Blocks.DEEPSLATE);
		add("terrain_blackstone", Blocks.BLACKSTONE);
	}

	/** The invisible terrain block for Subnautica's "minecraft:sand"; stone for anything unknown. */
	public static Block standingFor(final String vanilla) {
		return FOR.getOrDefault(vanilla, FOR.get("minecraft:stone"));
	}

	private static void add(final String name, final Block like) {
		ResourceKey<Block> key = ResourceKey.create(Registries.BLOCK, Identifier.fromNamespaceAndPath(SubnautiCraft.ID, name));
		Block block = Registry.register(BuiltInRegistries.BLOCK, key, new Invisible(BlockBehaviour.Properties.ofFullCopy(like).noOcclusion().setId(key)));
		FOR.put(BuiltInRegistries.BLOCK.getKey(like).toString(), block);
	}

	/** Not drawn, and lets light through: it's inside Subnautica's terrain, which is drawn instead. */
	static final class Invisible extends Block {
		Invisible(final BlockBehaviour.Properties properties) {
			super(properties);
		}

		@Override
		protected RenderShape getRenderShape(final BlockState state) {
			return RenderShape.INVISIBLE;
		}

		@Override
		protected boolean propagatesSkylightDown(final BlockState state) {
			return true;
		}

		@Override
		protected float getShadeBrightness(final BlockState state, final BlockGetter level, final BlockPos pos) {
			return 1.0F;
		}
	}
}
