package dev.subnauticraft;

import java.util.Set;
import net.minecraft.world.InteractionHand;
import net.minecraft.world.InteractionResult;
import net.minecraft.world.entity.player.Player;
import net.minecraft.world.item.Item;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.level.Level;

/**
 * The "Subnautica item" (SnPickups): any of Subnautica's things without a Minecraft item of its own. The simple
 * deployables are placed with right click in Steve mode, as the diver would throw them: the mobile vehicle bay
 * (it floats up and deploys), a beacon, a waterproof locker, a flare... They come out in Subnautica's world.
 */
public final class SnGenericItem extends Item {
	private static final Set<String> PLACEABLE = Set.of("Constructor", "Beacon", "Gravsphere", "SmallStorage", "LEDLight", "Flare", "PipeSurfaceFloater");

	public SnGenericItem(final Item.Properties properties) {
		super(properties);
	}

	@Override
	public InteractionResult use(final Level level, final Player player, final InteractionHand hand) {
		ItemStack stack = player.getItemInHand(hand);
		String tech = SnPickups.tech(stack);
		if (!SubnautiCraft.steve || tech == null || !PLACEABLE.contains(tech)) {
			return super.use(level, player, hand);
		}

		if (!level.isClientSide()) {
			SubnautiCraft.events.accept(SnPickups.dropEvent(stack.copyWithCount(1), true));
			stack.consume(1, player);
		}

		return InteractionResult.SUCCESS;
	}
}
