package dev.subnauticraft.client;

import dev.subnauticraft.SubnautiCraft;
import dev.subnauticraft.WorldBridge;
import java.util.List;
import java.util.Optional;
import net.fabricmc.api.ClientModInitializer;
import net.fabricmc.fabric.api.client.event.lifecycle.v1.ClientTickEvents;
import net.fabricmc.fabric.api.event.lifecycle.v1.ServerTickEvents;
import net.fabricmc.fabric.api.networking.v1.ServerPlayConnectionEvents;
import net.minecraft.client.CloudStatus;
import net.minecraft.client.InactivityFpsLimit;
import net.minecraft.client.Minecraft;
import net.minecraft.client.Options;
import net.minecraft.client.gui.screens.DeathScreen;
import net.minecraft.client.gui.screens.TitleScreen;
import net.minecraft.client.tutorial.TutorialSteps;
import net.minecraft.core.HolderLookup;
import net.minecraft.core.HolderSet;
import net.minecraft.core.registries.Registries;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.world.level.GameType;
import net.minecraft.world.level.LevelSettings;
import net.minecraft.world.level.WorldDataConfiguration;
import net.minecraft.world.level.biome.Biomes;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.levelgen.FlatLevelSource;
import net.minecraft.world.level.levelgen.WorldDimensions;
import net.minecraft.world.level.levelgen.WorldOptions;
import net.minecraft.world.level.levelgen.flat.FlatLayerInfo;
import net.minecraft.world.level.levelgen.flat.FlatLevelGeneratorSettings;
import net.minecraft.world.level.levelgen.presets.WorldPresets;

public class SubnautiCraftClient implements ClientModInitializer {
	/** The world Subnautica plays in: an empty (void) creative world, so only what the player builds is Minecraft. */
	private static final String WORLD = "subnauticraft";
	/** Run on the server every time the player joins: world settings only (the starter items go once). */
	private static final List<String> SETUP = List.of(
		"gamerule advance_time false",
		"gamerule advance_weather false",
		"gamerule spawn_mobs false",
		"gamerule spawn_monsters false",
		"gamerule spawn_patrols false",
		"gamerule spawn_phantoms false",
		"gamerule spawn_wandering_traders false",
		"gamerule send_command_feedback false",
		"gamerule log_admin_commands false",
		"gamerule keep_inventory true",
		"setworldspawn 0 0 0",
		"gamerule respawn_radius 0",
		"gamerule immediate_respawn true",
		"difficulty normal",
		"gamerule show_advancement_messages false",
		"gamerule player_movement_check false",
		"time set noon",
		"weather clear",
		// the starter hotbar, once per player per world (the tag remembers it): never touch an inventory again
		"item replace entity @a[tag=!snc_started] hotbar.0 with minecraft:grass_block 64",
		"item replace entity @a[tag=!snc_started] hotbar.1 with minecraft:oak_planks 64",
		"item replace entity @a[tag=!snc_started] hotbar.2 with minecraft:glass 64",
		"item replace entity @a[tag=!snc_started] hotbar.3 with minecraft:sea_lantern 64",
		"item replace entity @a[tag=!snc_started] hotbar.4 with minecraft:stone_bricks 64",
		"item replace entity @a[tag=!snc_started] hotbar.5 with minecraft:tnt 64",
		"item replace entity @a[tag=!snc_started] hotbar.6 with minecraft:flint_and_steel",
		"item replace entity @a[tag=!snc_started] hotbar.7 with minecraft:diamond_sword",
		"item replace entity @a[tag=!snc_started] hotbar.8 with minecraft:creeper_spawn_egg 64",
		"tag @a add snc_started"
	);
	private static boolean configured;
	private static boolean worldRequested;
	/** Server ticks until the setup commands run (the player isn't in the player list yet when JOIN fires). */
	private static int setupIn = -1;
	private static int respawnIn;

	@Override
	public void onInitializeClient() {
		HostLink.launch();
		ClientTickEvents.END_CLIENT_TICK.register(SubnautiCraftClient::tick);
		ServerPlayConnectionEvents.JOIN.register((handler, sender, server) -> {
			ServerPlayer player = handler.player;
			player.getAbilities().flying = true;
			player.onUpdateAbilities();
			setupIn = 10;
		});
		ServerTickEvents.END_SERVER_TICK.register(server -> {
			if (setupIn > 0 && --setupIn == 0) {
				SETUP.forEach(WorldBridge::command);
			}
		});
	}

	private static Object lastPlayer;

	private static void tick(final Minecraft minecraft) {
		// a new player object (respawn, rejoin) starts at the world spawn: in Steve mode he belongs where the diver is
		if (minecraft.player != lastPlayer) {
			lastPlayer = minecraft.player;
			if (minecraft.player != null && HostState.tookOver()) {
				SubnautiCraft.events.accept("{\"t\":\"needtp\"}");
			}
		}

		// a death (the void) would leave the death screen over the host's picture: respawn straight away
		if (minecraft.player != null && minecraft.gui.screen() instanceof DeathScreen && --respawnIn <= 0) {
			respawnIn = 20;
			minecraft.player.respawn();
		}

		if (!configured) {
			configured = true;
			configure(minecraft.options);
		}

		if (!worldRequested && minecraft.level == null && minecraft.gui.screen() instanceof TitleScreen && !Boolean.getBoolean("subnauticraft.noAutoWorld")) {
			worldRequested = true;
			openWorld(minecraft);
		}
	}

	/** Settings for sitting behind Subnautica: keep running unfocused, and no sky/cloud/bobbing effects in the picture. */
	private static void configure(final Options options) {
		options.pauseOnLostFocus = false;
		options.onboardAccessibility = false;
		options.tutorialStep = TutorialSteps.NONE;
		options.cloudStatus().set(CloudStatus.OFF);
		options.bobView().set(false);
		options.vignette().set(false);
		options.improvedTransparency().set(false);
		options.inactivityFpsLimit().set(InactivityFpsLimit.MINIMIZED);
		options.fovEffectScale().set(0.0);
		options.damageTiltStrength().set(0.0);
		options.menuBackgroundBlurriness().set(0);
		options.enableVsync().set(false);
		// Subnautica shows ~60 fps: rendering faster only takes the graphics card away from it
		options.framerateLimit().set(60);
		// the world is 2288 blocks tall (Subnautica's whole ocean): at 16 chunks Minecraft walks some 155,000 sections
		// every time the camera enters a new one and stalls for up to a second; 8 chunks (128 m) is more than anyone
		// sees underwater, and a seventh of the work
		if (options.renderDistance().get() > 8) {
			options.renderDistance().set(8);
		}

		if (options.simulationDistance().get() > 8) {
			options.simulationDistance().set(8);
		}

		options.save();
	}

	private static void openWorld(final Minecraft minecraft) {
		if (minecraft.getLevelSource().levelExists(WORLD)) {
			SubnautiCraft.LOG.info("opening world {}", WORLD);
			minecraft.createWorldOpenFlows().openWorld(WORLD, () -> minecraft.gui.setScreen(new TitleScreen()));
		} else {
			SubnautiCraft.LOG.info("creating world {}", WORLD);
			LevelSettings settings = new LevelSettings("SubnautiCraft", GameType.CREATIVE, LevelSettings.DifficultySettings.DEFAULT, true, WorldDataConfiguration.DEFAULT);
			minecraft.createWorldOpenFlows().createFreshLevel(WORLD, settings, new WorldOptions(0L, false, false), SubnautiCraftClient::voidWorld, minecraft.gui.screen());
		}
	}

	/** A flat world with a single layer of air: nothing but what gets built (Subnautica's seafloor arrives as barriers). */
	private static WorldDimensions voidWorld(final HolderLookup.Provider registries) {
		FlatLevelGeneratorSettings flat = new FlatLevelGeneratorSettings(
			Optional.of(HolderSet.direct()), registries.lookupOrThrow(Registries.BIOME).getOrThrow(Biomes.PLAINS), List.of()
		);
		flat.getLayersInfo().add(new FlatLayerInfo(1, Blocks.AIR));
		flat.updateLayers();
		return WorldPresets.createNormalWorldDimensions(registries).replaceOverworldGenerator(registries, new FlatLevelSource(flat));
	}
}
