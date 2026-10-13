package dev.subnauticraft;

import net.minecraft.server.level.ServerLevel;
import net.minecraft.sounds.SoundEvents;
import net.minecraft.sounds.SoundSource;
import net.minecraft.world.InteractionHand;
import net.minecraft.world.InteractionResult;
import net.minecraft.world.entity.player.Player;
import net.minecraft.world.item.Item;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.level.Level;

/** Subnautica's first aid kit: right click heals (its 50 health is 10 of Minecraft's) and uses it up. */
public final class SnHealItem extends Item {
	private final float health;

	public SnHealItem(final Item.Properties properties, final float health) {
		super(properties);
		this.health = health;
	}

	@Override
	public InteractionResult use(final Level level, final Player player, final InteractionHand hand) {
		if (player.getHealth() >= player.getMaxHealth()) {
			return InteractionResult.FAIL;
		}

		if (level instanceof ServerLevel) {
			player.heal(this.health);
			level.playSound(null, player.getX(), player.getY(), player.getZ(), SoundEvents.BOTTLE_EMPTY, SoundSource.PLAYERS, 1.0F, 1.2F);
			ItemStack stack = player.getItemInHand(hand);
			player.getCooldowns().addCooldown(stack, 20);  // holding right click doesn't spend the whole stack
			stack.consume(1, player);
		}

		return InteractionResult.SUCCESS;
	}
}
