package dev.subnauticraft;

import com.google.gson.Gson;
import com.google.gson.GsonBuilder;
import com.google.gson.JsonArray;
import com.google.gson.JsonElement;
import com.google.gson.JsonObject;
import com.mojang.serialization.JsonOps;
import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.HashSet;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.Set;
import java.util.stream.Stream;
import net.minecraft.SharedConstants;
import net.minecraft.core.HolderLookup;
import net.minecraft.resources.RegistryOps;
import net.minecraft.server.MinecraftServer;
import net.minecraft.server.packs.PackType;
import net.minecraft.server.packs.repository.PackRepository;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.item.ItemStackTemplate;
import net.minecraft.world.level.storage.LevelResource;

/**
 * Subnautica's whole fabricator in the crafting table. The mod ships the recipes made only of materials with their own
 * Minecraft items; Subnautica sends the rest of its fabricator ({"t":"recipes"}: the mobile vehicle bay, the knife,
 * filtered water...), whose results or ingredients are "Subnautica items" (SnPickups), and they're written as a data
 * pack in this world ("SubnautiCraft Fabricator", recipes named "<item>_fabricator") and loaded. The blueprint rules
 * are SnBlueprints': a recipe crafts once the diver knows it, and shows in the "Subnautica" advancements tab.
 */
public final class SnFabricator {
	static final String FOLDER = "SubnautiCraft Fabricator";
	static final String SUFFIX = "_fabricator";
	private static final Gson GSON = new GsonBuilder().setPrettyPrinting().disableHtmlEscaping().create();
	/** The last recipes Subnautica sent: written into whichever world Steve is in. */
	private static volatile JsonArray recipes;

	private SnFabricator() {
	}

	/** Any thread: Subnautica's recipes (written now if a world is open, otherwise when Steve joins one). */
	public static void received(final JsonArray list) {
		recipes = list;
		WorldBridge.withServer(SnFabricator::apply);
	}

	/**
	 * Server thread: the world's data pack matches Subnautica's recipes; if it changed (or isn't loaded yet), the data
	 * packs are reloaded, and the blueprints known are applied again after (SnBlueprints.reapply). Returns whether a
	 * reload started.
	 */
	public static boolean apply(final MinecraftServer server) {
		JsonArray list = recipes;
		if (list == null) {
			return false;
		}

		Path root = server.getWorldPath(LevelResource.DATAPACK_DIR).resolve(FOLDER);
		Map<String, String> files = new LinkedHashMap<>();
		RegistryOps<JsonElement> ops = server.registryAccess().createSerializationContext(JsonOps.INSTANCE);
		int format = SharedConstants.getCurrentVersion().packVersion(PackType.SERVER_DATA).major();
		JsonObject pack = new JsonObject();
		pack.addProperty("description", "Subnautica's fabricator recipes, from your own game (SubnautiCraft)");
		pack.addProperty("min_format", format);
		pack.addProperty("max_format", format);
		JsonObject meta = new JsonObject();
		meta.add("pack", pack);
		files.put("pack.mcmeta", GSON.toJson(meta));

		int written = 0;
		for (JsonElement e : list) {
			try {
				JsonObject r = e.getAsJsonObject();
				JsonObject out = r.getAsJsonObject("out");
				String name = name(out);
				ItemStack result = stack(server.registryAccess(), out, r.has("n") ? r.get("n").getAsInt() : 1);
				if (result.isEmpty()) {
					continue;
				}

				JsonArray ingredients = new JsonArray();
				for (JsonElement i : r.getAsJsonArray("in")) {
					JsonObject in = i.getAsJsonObject();
					for (int k = 0; k < (in.has("n") ? in.get("n").getAsInt() : 1); k++) {
						ingredients.add(ingredient(in));
					}
				}

				JsonObject recipe = new JsonObject();
				recipe.addProperty("type", "minecraft:crafting_shapeless");
				recipe.addProperty("category", "misc");
				recipe.add("ingredients", ingredients);
				recipe.add("result", ItemStackTemplate.CODEC.encodeStart(ops, ItemStackTemplate.fromNonEmptyStack(result)).getOrThrow());
				files.put("data/subnauticraft/recipe/" + name + SUFFIX + ".json", GSON.toJson(recipe));
				if (out.has("tech")) {
					files.put("data/subnauticraft/advancement/subnautica/" + name + ".json", GSON.toJson(advancement(ops, result, out)));
				}

				written++;
			} catch (RuntimeException ex) {
				SubnautiCraft.LOG.warn("fabricator: a recipe from Subnautica couldn't be written ({}): {}", ex.toString(), e);
			}
		}

		int changed;
		try {
			changed = sync(root, files);
		} catch (IOException ex) {
			SubnautiCraft.LOG.warn("fabricator: couldn't write the data pack: {}", ex.toString());
			return false;
		}

		PackRepository repo = server.getPackRepository();
		String id = "file/" + FOLDER;
		if (changed == 0 && repo.getSelectedIds().contains(id)) {
			SubnautiCraft.LOG.info("fabricator: {} recipes from Subnautica, all up to date", written);
			return false;
		}

		repo.reload();
		List<String> selected = new ArrayList<>(repo.getSelectedIds());
		if (!selected.contains(id) && repo.isAvailable(id)) {
			selected.add(id);
		}

		SubnautiCraft.LOG.info("fabricator: {} recipes from Subnautica ({} files changed), reloading data packs", written, changed);
		server.reloadResources(selected).exceptionally(ex -> {
			SubnautiCraft.LOG.warn("fabricator: data pack reload failed: {}", ex.toString());
			return null;
		});
		return true;
	}

	/** Our recipes and advancements are named after the item: "constructor" for a Subnautica item is "tech_constructor". */
	static String name(final JsonObject o) {
		return o.has("item") ? o.get("item").getAsString() : SnPickups.picture(o.get("tech").getAsString());
	}

	private static ItemStack stack(final HolderLookup.Provider registries, final JsonObject o, final int count) {
		return SnPickups.stack(
			registries,
			o.has("item") ? o.get("item").getAsString() : null,
			o.has("tech") ? o.get("tech").getAsString() : null,
			o.has("name") ? o.get("name").getAsString() : null,
			count,
			o.has("food") ? o.get("food").getAsFloat() : null,
			o.has("water") ? o.get("water").getAsFloat() : null,
			o.has("charge") ? o.get("charge").getAsFloat() : null);
	}

	/** One of ours by id, or a "Subnautica item" of that kind (whatever else its custom data says). */
	private static JsonElement ingredient(final JsonObject o) {
		if (o.has("item")) {
			return new com.google.gson.JsonPrimitive(SubnautiCraft.ID + ":" + o.get("item").getAsString());
		}

		JsonObject custom = new JsonObject();
		custom.addProperty("fabric:type", "fabric:custom_data");
		custom.addProperty("base", SubnautiCraft.ID + ":subnautica_item");
		custom.addProperty("nbt", "{tech:\"" + o.get("tech").getAsString().replaceAll("[^A-Za-z0-9_]", "") + "\"}");
		return custom;
	}

	private static JsonObject advancement(final RegistryOps<JsonElement> ops, final ItemStack result, final JsonObject out) {
		JsonObject title = new JsonObject();
		title.addProperty("text", out.has("name") ? out.get("name").getAsString() : out.get("tech").getAsString());
		JsonObject description = new JsonObject();
		description.addProperty("translate", "advancements.subnauticraft." + ("Constructor".equals(out.get("tech").getAsString()) ? "vehicle" : "blueprint"));
		JsonObject display = new JsonObject();
		display.add("icon", ItemStackTemplate.CODEC.encodeStart(ops, ItemStackTemplate.fromNonEmptyStack(result, 1)).getOrThrow());
		display.add("title", title);
		display.add("description", description);
		display.addProperty("frame", "task");
		display.addProperty("show_toast", false);
		display.addProperty("announce_to_chat", false);
		JsonObject learned = new JsonObject();
		learned.addProperty("trigger", "minecraft:impossible");
		JsonObject criteria = new JsonObject();
		criteria.add("learned", learned);
		JsonArray requirement = new JsonArray();
		requirement.add("learned");
		JsonArray requirements = new JsonArray();
		requirements.add(requirement);
		JsonObject advancement = new JsonObject();
		advancement.addProperty("parent", SubnautiCraft.ID + ":subnautica/root");
		advancement.add("criteria", criteria);
		advancement.add("display", display);
		advancement.add("requirements", requirements);
		return advancement;
	}

	/** The folder holds exactly these files: write what differs, delete what's no longer there. */
	private static int sync(final Path root, final Map<String, String> files) throws IOException {
		int changed = 0;
		Set<Path> keep = new HashSet<>();
		for (Map.Entry<String, String> f : files.entrySet()) {
			Path path = root.resolve(f.getKey());
			keep.add(path.toAbsolutePath().normalize());
			byte[] data = f.getValue().getBytes(StandardCharsets.UTF_8);
			if (Files.exists(path) && Arrays.equals(Files.readAllBytes(path), data)) {
				continue;
			}

			Files.createDirectories(path.getParent());
			Files.write(path, data);
			changed++;
		}

		if (Files.isDirectory(root)) {
			List<Path> stale;
			try (Stream<Path> walk = Files.walk(root)) {
				stale = walk.filter(Files::isRegularFile).filter(p -> !keep.contains(p.toAbsolutePath().normalize())).toList();
			}

			for (Path p : stale) {
				Files.delete(p);
				changed++;
			}
		}

		return changed;
	}
}
