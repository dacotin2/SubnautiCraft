package dev.subnauticraft;

import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.Set;
import net.minecraft.core.HolderLookup;
import net.minecraft.core.component.DataComponents;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.core.registries.Registries;
import net.minecraft.nbt.CompoundTag;
import net.minecraft.network.chat.Component;
import net.minecraft.resources.Identifier;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.sounds.SoundEvents;
import net.minecraft.sounds.SoundSource;
import net.minecraft.world.entity.item.ItemEntity;
import net.minecraft.world.food.FoodProperties;
import net.minecraft.world.item.Item;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.item.Items;
import net.minecraft.world.item.component.Consumables;
import net.minecraft.world.item.component.CustomData;
import net.minecraft.world.item.component.CustomModelData;
import net.minecraft.world.item.enchantment.Enchantments;

/**
 * What Steve picks up, cuts or is handed in Subnautica's world goes into his Minecraft inventory (the diver's own
 * inventory is hidden). Subnautica's items with a Minecraft item of their own come as that; any other comes as
 * subnauticraft:subnautica_item carrying its kind (custom data "tech"), its name as Subnautica shows it, and its
 * picture (custom model data: the icon pack has one per kind), so nothing is ever lost.
 */
public final class SnPickups {
	private SnPickups() {
	}

	/**
	 * {"t":"give","item":id,"n":count} or {"t":"give","tech":TechType,"name":name,"n":count}, plus "food" and "water"
	 * for Subnautica's food and drink (null: not edible).
	 */
	public static void give(final ServerPlayer player, final String item, final String tech, final String name, final int count,
			final Float food, final Float water, final Float charge) {
		ItemStack stack = stack(player.registryAccess(), item, tech, name, count, food, water, charge);
		if (stack.isEmpty()) {
			SubnautiCraft.LOG.warn("pickup: nothing to give for item {} tech {}", item, tech);
			return;
		}

		String shown = stack.getHoverName().getString();
		if (!player.getInventory().add(stack) && !stack.isEmpty()) {
			// a full inventory: at Steve's feet for him to pick up, as /give does
			ItemEntity drop = player.createItemStackToDrop(stack, false, false);
			if (drop != null) {
				drop.setNoPickUpDelay();
				drop.setTarget(player.getUUID());
				drop.level().addFreshEntity(drop);
			}
		}

		player.level().playSound(null, player.getX(), player.getY(), player.getZ(), SoundEvents.ITEM_PICKUP, SoundSource.PLAYERS, 0.2F, 1.4F);
		SubnautiCraft.LOG.info("pickup: {} x{}", shown, count);
	}

	static ItemStack stack(final HolderLookup.Provider registries, final String item, final String tech, final String name, final int count,
			final Float food, final Float water, final Float charge) {
		if (item != null && !item.isEmpty()) {
			Item mapped = BuiltInRegistries.ITEM.getValue(Identifier.fromNamespaceAndPath(SubnautiCraft.ID, item));
			return mapped == Items.AIR ? ItemStack.EMPTY : new ItemStack(mapped, count);
		}

		if (tech == null || tech.isEmpty() || SnItems.GENERIC == null) {
			return ItemStack.EMPTY;
		}

		Item vanilla = VANILLA.get(tech);
		if (vanilla != null) {
			return new ItemStack(vanilla, count);
		}

		if ("HeatBlade".equals(tech)) {
			// the thermoblade: an iron sword (swords act as the knife) that burns, with Fire Aspect
			ItemStack sword = new ItemStack(Items.IRON_SWORD, count);
			registries.lookupOrThrow(Registries.ENCHANTMENT).get(Enchantments.FIRE_ASPECT).ifPresent(fire -> sword.enchant(fire, 1));
			return sword;
		}

		ItemStack stack = new ItemStack(SnItems.GENERIC, count);
		CompoundTag tag = new CompoundTag();
		tag.putString("tech", tech);
		if (food != null) {
			// Subnautica's food and drink: eaten (or drunk) in Minecraft too, and the diver gets exactly what it gives
			// (FoodPropertiesMixin tells Subnautica which kind). A drink can always be had; Minecraft's hunger gets a
			// sixth of its food. The kind alone is the custom data, so picked up and crafted ones stack.
			float drinks = water == null ? 0F : water;
			boolean drink = drinks > food;
			stack.set(DataComponents.FOOD, new FoodProperties(Math.max(0, Math.round(food / 6F)), 0.6F, drink || food <= 0F));
			stack.set(DataComponents.CONSUMABLE, drink ? Consumables.DEFAULT_DRINK : Consumables.DEFAULT_FOOD);
		}

		if (charge != null) {
			// a tool on a battery: its charge is the durability bar (SnBattery)
			SnBattery.makeChargeable(stack);
			stack.setDamageValue(Math.round((1F - Math.max(0F, Math.min(1F, charge))) * stack.getMaxDamage()));
		}

		stack.set(DataComponents.CUSTOM_DATA, CustomData.of(tag));
		stack.set(DataComponents.ITEM_NAME, Component.literal(name == null || name.isEmpty() ? tech : name));
		stack.set(DataComponents.CUSTOM_MODEL_DATA, new CustomModelData(List.of(), List.of(), List.of(picture(tech)), List.of()));
		return stack;
	}

	/** Subnautica's things that are simply a Minecraft item: the survival knife is an iron sword (swords act as the knife). */
	private static final Map<String, Item> VANILLA = Map.of("Knife", Items.IRON_SWORD);

	/** The vehicles' items are only pictures for the vehicle bay's screen. */
	private static final Set<String> PICTURES_ONLY = Set.of("seamoth", "prawn_suit", "cyclops", "rocket_base");

	/** Subnautica's kind of a "Subnautica item" (null for anything else). */
	public static String tech(final ItemStack stack) {
		if (!stack.is(SnItems.GENERIC)) {
			return null;
		}

		CustomData data = stack.get(DataComponents.CUSTOM_DATA);
		String tech = data == null ? "" : data.copyTag().getStringOr("tech", "");
		return tech.isEmpty() ? null : tech;
	}

	/**
	 * Steve threw this out of his inventory in Steve mode: if it's one of Subnautica's things, the message that makes it
	 * come out in Subnautica's world ({"t":"drop"} or, placing, {"t":"place"}); null for anything else.
	 */
	public static String dropEvent(final ItemStack stack, final boolean place) {
		if (stack.isEmpty()) {
			return null;
		}

		Identifier id = BuiltInRegistries.ITEM.getKey(stack.getItem());
		String kind;
		String tech = tech(stack);
		if (tech != null) {
			kind = "\"tech\":\"" + tech.replaceAll("[^A-Za-z0-9_]", "") + "\"";
		} else if (SubnautiCraft.ID.equals(id.getNamespace()) && !stack.is(SnItems.GENERIC) && !PICTURES_ONLY.contains(id.getPath())) {
			kind = "\"item\":\"" + id.getPath() + "\"";
		} else {
			return null;
		}

		String charge = tech != null && stack.isDamageableItem()
			? String.format(Locale.ROOT, ",\"charge\":%.4f", 1F - (float) stack.getDamageValue() / stack.getMaxDamage()) : "";
		return "{\"t\":\"" + (place ? "place" : "drop") + "\"," + kind + ",\"n\":" + stack.getCount() + charge + "}";
	}

	/** The icon pack's name for a kind's picture: "tech_crashpowder". */
	public static String picture(final String tech) {
		return "tech_" + tech.toLowerCase(Locale.ROOT).replaceAll("[^a-z0-9_]", "_");
	}
}
