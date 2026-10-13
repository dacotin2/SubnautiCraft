package dev.subnauticraft;

import java.util.ArrayList;
import java.util.Arrays;
import java.util.List;
import java.util.Set;
import java.util.concurrent.ConcurrentHashMap;
import net.minecraft.advancements.AdvancementHolder;
import net.minecraft.resources.Identifier;
import net.minecraft.server.MinecraftServer;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.item.crafting.CraftingRecipe;
import net.minecraft.world.item.crafting.Recipe;
import net.minecraft.world.item.crafting.RecipeHolder;

/**
 * Subnautica's blueprints as Minecraft recipes. What the diver knows (from the start, by scanning, from data boxes)
 * is unlocked in the recipe book and shows in the "Subnautica" advancements tab; a Subnautica recipe whose blueprint
 * isn't known can't be crafted (the crafting table shows no result), as the fabricator wouldn't make it.
 */
public final class SnBlueprints {
	/**
	 * Every blueprint Subnautica said the diver knows in this save: applied again when Steve joins the world, or after
	 * its data packs reload (the fabricator's recipes, SnFabricator), so none is missed while a world opens.
	 */
	private static final Set<String> KNOWN = ConcurrentHashMap.newKeySet();

	private SnBlueprints() {
	}

	/** Any thread: {"t":"known","items":[...]} arrived (applied at once if Steve is in a world). */
	public static void received(final List<String> items) {
		KNOWN.addAll(items);
		WorldBridge.withServer(server -> known(server, items));
	}

	/** Another Subnautica save: its blueprints come fresh. */
	public static void forget() {
		KNOWN.clear();
	}

	/** Server thread: every blueprint known so far, again (Steve joined, or the recipes were reloaded). */
	public static void reapply(final MinecraftServer server) {
		known(server, new ArrayList<>(KNOWN));
	}

	/** {"t":"known","items":[item ids],"new":bool}: these blueprints are known (new: just learned). */
	public static void known(final MinecraftServer server, final List<String> items) {
		if (server.getPlayerList().getPlayers().isEmpty() || items.isEmpty()) {
			return;
		}

		ServerPlayer player = server.getPlayerList().getPlayers().get(0);
		List<RecipeHolder<?>> unlock = new ArrayList<>();
		for (RecipeHolder<?> recipe : server.getRecipeManager().getRecipes()) {
			Identifier id = recipe.id().identifier();
			if (SubnautiCraft.ID.equals(id.getNamespace()) && (makes(id.getPath(), items) || Arrays.asList(SnItems.ALWAYS).contains(id.getPath()))) {
				unlock.add(recipe);
			}
		}

		int recipes = player.awardRecipes(unlock);
		int learned = 0;
		for (String item : items) {
			AdvancementHolder advancement = server.getAdvancements().get(Identifier.fromNamespaceAndPath(SubnautiCraft.ID, "subnautica/" + item));
			if (advancement != null && !player.getAdvancements().getOrStartProgress(advancement).isDone()) {
				player.getAdvancements().award(advancement, "learned");
				learned++;
			}
		}

		if (recipes > 0 || learned > 0) {
			SubnautiCraft.LOG.info("blueprints: {} recipes unlocked, {} advancements", recipes, learned);
		}
	}

	/**
	 * Our recipes are named after what they make: "copper_wire", "flashlight_recharge", "cooked_peeper_from_smelting",
	 * and Subnautica's other fabricator recipes "tech_constructor_fabricator".
	 */
	private static boolean makes(final String recipe, final List<String> items) {
		for (String item : items) {
			if (recipe.equals(item) || recipe.equals(item + "_recharge") || recipe.startsWith(item + "_from_") || recipe.equals(item + SnFabricator.SUFFIX)) {
				return true;
			}
		}

		return false;
	}

	/**
	 * A Subnautica crafting recipe needs its blueprint: unlocked in the player's recipe book. Not salvage into titanium
	 * (never a blueprint in Subnautica) nor a fresh battery into a tool (not a Subnautica recipe at all).
	 */
	public static boolean allowed(final ServerPlayer player, final RecipeHolder<?> recipe) {
		Recipe<?> value = recipe.value();
		Identifier id = recipe.id().identifier();
		if (!(value instanceof CraftingRecipe) || !SubnautiCraft.ID.equals(id.getNamespace())) {
			return true;
		}

		if (id.getPath().endsWith("_recharge") || Arrays.asList(SnItems.ALWAYS).contains(id.getPath())) {
			return true;
		}

		return player.getRecipeBook().contains(recipe.id());
	}

	/** A second of scanning: the scanner in hand spends a second of its battery. */
	public static void scanned(final MinecraftServer server) {
		if (server.getPlayerList().getPlayers().isEmpty()) {
			return;
		}

		ServerPlayer player = server.getPlayerList().getPlayers().get(0);
		ItemStack held = player.getMainHandItem();
		if ("scanner".equals(SnTools.working(held)) && !player.getAbilities().instabuild) {
			held.setDamageValue(held.getDamageValue() + 1);
		}
	}
}
