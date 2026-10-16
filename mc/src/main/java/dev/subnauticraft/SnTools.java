package dev.subnauticraft;

import java.util.HashMap;
import java.util.Map;
import java.util.UUID;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.server.MinecraftServer;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.world.effect.MobEffectInstance;
import net.minecraft.world.effect.MobEffects;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.phys.Vec3;

/**
 * Subnautica's tools in Steve's hand. Each runs on a battery: the item's durability is its charge, it never breaks,
 * and crafting it with a battery recharges it. The flashlight shines (Subnautica draws its light) and drains while
 * held; the Seaglide pulls Steve through the water (Minecraft's Dolphin's Grace) and drains while it does; the scanner
 * drains while it scans (Subnautica says when, see SnBlueprints).
 */
public final class SnTools {
	private static final Map<UUID, Vec3> lastPosition = new HashMap<>();
	private static int ticks;

	private SnTools() {
	}

	/** Which of the tools this is ("flashlight", "seaglide"), if it still has charge; null otherwise. */
	public static String working(final ItemStack stack) {
		String tech = SnPickups.tech(stack);
		if ("Welder".equals(tech)) {
			return "welder"; // Subnautica's repair tool (no battery of its own in Minecraft)
		}

		if (tech != null) {
			return "sn:" + tech; // any other: if it's a tool, the diver holds the real one (SteveTools)
		}

		if (stack.isEmpty() || stack.getMaxDamage() <= 0 || stack.getDamageValue() >= stack.getMaxDamage() - 1) {
			return null;
		}

		var id = BuiltInRegistries.ITEM.getKey(stack.getItem());
		if (!SubnautiCraft.ID.equals(id.getNamespace())) {
			return null;
		}

		return switch (id.getPath()) {
			case "flashlight", "seaglide", "scanner" -> id.getPath();
			default -> null;
		};
	}

	/** Every server tick (only while Subnautica is connected). */
	static void tick(final MinecraftServer server) {
		if (!SubnautiCraft.active) {
			lastPosition.clear();
			return;
		}

		boolean second = ++ticks % 20 == 0;
		for (ServerPlayer player : server.getPlayerList().getPlayers()) {
			Vec3 now = player.position();
			Vec3 before = lastPosition.put(player.getUUID(), now);
			ItemStack held = player.getMainHandItem();
			String tool = working(held);
			if (tool == null) {
				continue;
			}

			boolean drain = false;
			if (tool.equals("flashlight")) {
				drain = true;
			} else if (tool.equals("seaglide") && player.isInWater()) {
				// the Seaglide's motor: much faster swimming, as long as there's charge
				player.addEffect(new MobEffectInstance(MobEffects.DOLPHINS_GRACE, 15, 0, true, false, false));
				drain = before != null && now.distanceToSqr(before) > 0.0025;
			}

			if (drain && second && !player.getAbilities().instabuild) {
				held.setDamageValue(held.getDamageValue() + 1); // one second of the battery
			}
		}
	}
}
