package dev.subnauticraft.client;

import com.google.gson.JsonArray;
import com.google.gson.JsonObject;
import dev.subnauticraft.SubnautiCraft;
import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.List;
import java.util.Map;
import java.util.concurrent.ConcurrentHashMap;
import java.util.stream.Stream;
import net.minecraft.SharedConstants;
import net.minecraft.client.Minecraft;
import net.minecraft.server.packs.PackType;
import net.minecraft.server.packs.repository.PackRepository;

/**
 * Subnautica's own item icons and names for SubnautiCraft's Subnautica items. The plugin draws each icon from the
 * player's copy of Subnautica and sends it here ({"t":"icon"}); they're kept as a resource pack in Minecraft's folder
 * (resourcepacks/SubnautiCraft Icons), switched on, and reloaded only when something changed. Until then the items
 * show the mod's own simple drawings. Nothing of Subnautica's leaves the player's computer.
 */
public final class IconPack {
	static final String FOLDER = "SubnautiCraft Icons";
	private static final Map<String, byte[]> icons = new ConcurrentHashMap<>();

	private IconPack() {
	}

	/** Any thread: one icon (PNG) for item subnauticraft:{name}. */
	public static void put(final String name, final byte[] png) {
		if (name.matches("[a-z0-9_]+")) {
			icons.put(name, png);
		}
	}

	/** Render thread: write what changed, then switch the pack on (or reload it) so Minecraft draws the icons. */
	public static void finish(final Minecraft minecraft, final Map<String, String> names) {
		Path root = minecraft.gameDirectory.toPath().resolve("resourcepacks").resolve(FOLDER);
		int changed = 0;
		try {
			int format = SharedConstants.getCurrentVersion().packVersion(PackType.CLIENT_RESOURCES).major();
			JsonObject pack = new JsonObject();
			pack.addProperty("description", "Subnautica's item icons, drawn from your own game by SubnautiCraft");
			pack.addProperty("min_format", format);
			pack.addProperty("max_format", format);
			JsonObject meta = new JsonObject();
			meta.add("pack", pack);
			changed += writeIfChanged(root.resolve("pack.mcmeta"), meta.toString().getBytes(StandardCharsets.UTF_8));
			for (Map.Entry<String, byte[]> e : icons.entrySet()) {
				changed += writeIfChanged(root.resolve("assets/subnauticraft/textures/item/" + e.getKey() + ".png"), e.getValue());
			}

			changed += pictures(root);

			if (!names.isEmpty()) {
				// in the language Minecraft is set to: the names as Subnautica shows them
				JsonObject lang = new JsonObject();
				names.forEach((item, name) -> lang.addProperty("item.subnauticraft." + item, name));
				changed += writeIfChanged(root.resolve("assets/subnauticraft/lang/" + minecraft.options.languageCode + ".json"),
					lang.toString().getBytes(StandardCharsets.UTF_8));
			}
		} catch (IOException | RuntimeException e) {
			SubnautiCraft.LOG.warn("icons: couldn't write the resource pack: {}", e.toString());
			return;
		}

		int count = icons.size();
		icons.clear();
		PackRepository repo = minecraft.getResourcePackRepository();
		repo.reload();
		String id = "file/" + FOLDER;
		if (!repo.getSelectedIds().contains(id)) {
			if (repo.addPack(id)) {
				SubnautiCraft.LOG.info("icons: {} from Subnautica, resource pack switched on", count);
				minecraft.options.updateResourcePacks(repo);
			} else {
				SubnautiCraft.LOG.warn("icons: written, but Minecraft doesn't list the pack");
			}
		} else if (changed > 0) {
			SubnautiCraft.LOG.info("icons: {} files changed, reloading resources", changed);
			minecraft.reloadResourcePacks();
		} else {
			SubnautiCraft.LOG.info("icons: {} from Subnautica, all up to date", count);
		}
	}

	/**
	 * The "Subnautica item" (SnPickups) shows its own kind's icon: one model per icon "tech_*" kept so far, picked by
	 * the stack's custom model data.
	 */
	private static int pictures(final Path root) throws IOException {
		Path textures = root.resolve("assets/subnauticraft/textures/item");
		List<String> kinds = new ArrayList<>();
		if (Files.isDirectory(textures)) {
			try (Stream<Path> files = Files.list(textures)) {
				files.map(f -> f.getFileName().toString())
					.filter(f -> f.startsWith("tech_") && f.endsWith(".png"))
					.map(f -> f.substring(0, f.length() - 4))
					.sorted()
					.forEach(kinds::add);
			}
		}

		int changed = 0;
		JsonArray cases = new JsonArray();
		for (String kind : kinds) {
			JsonObject model = new JsonObject();
			model.addProperty("parent", "minecraft:item/generated");
			JsonObject layers = new JsonObject();
			layers.addProperty("layer0", "subnauticraft:item/" + kind);
			model.add("textures", layers);
			changed += writeIfChanged(root.resolve("assets/subnauticraft/models/item/" + kind + ".json"), model.toString().getBytes(StandardCharsets.UTF_8));
			JsonObject entry = new JsonObject();
			entry.addProperty("when", kind);
			entry.add("model", plainModel("subnauticraft:item/" + kind));
			cases.add(entry);
		}

		JsonObject item = new JsonObject();
		if (kinds.isEmpty()) {
			// no pictures yet: Minecraft refuses a choice among none
			item.add("model", plainModel("subnauticraft:item/subnautica_item"));
			return changed + writeIfChanged(root.resolve("assets/subnauticraft/items/subnautica_item.json"), item.toString().getBytes(StandardCharsets.UTF_8));
		}

		JsonObject select = new JsonObject();
		select.addProperty("type", "minecraft:select");
		select.addProperty("property", "minecraft:custom_model_data");
		select.addProperty("index", 0);
		select.add("cases", cases);
		select.add("fallback", plainModel("subnauticraft:item/subnautica_item"));
		item.add("model", select);
		return changed + writeIfChanged(root.resolve("assets/subnauticraft/items/subnautica_item.json"), item.toString().getBytes(StandardCharsets.UTF_8));
	}

	private static JsonObject plainModel(final String model) {
		JsonObject o = new JsonObject();
		o.addProperty("type", "minecraft:model");
		o.addProperty("model", model);
		return o;
	}

	private static int writeIfChanged(final Path path, final byte[] data) throws IOException {
		if (Files.exists(path) && Arrays.equals(Files.readAllBytes(path), data)) {
			return 0;
		}

		Files.createDirectories(path.getParent());
		Files.write(path, data);
		return 1;
	}
}
