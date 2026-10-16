package dev.subnauticraft;

import java.util.function.Consumer;
import net.fabricmc.api.ModInitializer;
import net.fabricmc.fabric.api.entity.event.v1.ServerLivingEntityEvents;
import net.fabricmc.fabric.api.event.lifecycle.v1.ServerLifecycleEvents;
import net.fabricmc.fabric.api.event.lifecycle.v1.ServerTickEvents;
import net.fabricmc.fabric.api.event.player.PlayerBlockBreakEvents;
import net.fabricmc.fabric.api.networking.v1.ServerPlayConnectionEvents;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

public class SubnautiCraft implements ModInitializer {
	public static final String ID = "subnauticraft";
	public static final Logger LOG = LoggerFactory.getLogger(ID);
	/** True while Subnautica is driving the camera. The integrated server shares this JVM, so both sides read it. */
	public static volatile boolean active;
	/**
	 * Steve mode: Minecraft owns the player (movement, physics, look, inventory) and Subnautica's diver is a puppet
	 * that follows him, like SkyCraft's Skyrim player. Off: Ryley mode, Subnautica's camera drives Minecraft's.
	 */
	public static volatile boolean steve;
	/** Where events for Subnautica go (JSON lines); the client's HostLink sets it. */
	public static volatile Consumer<String> events = message -> {};

	@Override
	public void onInitialize() {
		SnItems.register();
		SnTerrainBlocks.register();
		SnCreatures.register();
		ServerLifecycleEvents.SERVER_STARTED.register(WorldBridge::attach);
		ServerLifecycleEvents.SERVER_STOPPING.register(server -> WorldBridge.detach());
		ServerTickEvents.END_SERVER_TICK.register(WorldBridge::tick);
		ServerTickEvents.END_SERVER_TICK.register(SnTools::tick);
		ServerTickEvents.END_SERVER_TICK.register(SnCreatures::tick);
		ServerTickEvents.END_SERVER_TICK.register(SnDig::tick);
		// a block standing for Subnautica's terrain was mined: the terrain is cut there too
		PlayerBlockBreakEvents.AFTER.register((level, player, pos, state, blockEntity) -> SnDig.broken(level, player, pos));
		// Subnautica's fabricator and blueprints, in whichever world Steve joins (they may have come while it opened)
		ServerPlayConnectionEvents.JOIN.register((handler, sender, server) -> server.execute(() -> {
			if (!SnFabricator.apply(server)) {
				SnBlueprints.reapply(server);
			}
		}));
		ServerLifecycleEvents.END_DATA_PACK_RELOAD.register((server, resources, success) -> {
			if (success) {
				LOG.info("data packs reloaded");
				SnBlueprints.reapply(server);
			}
		});
		// Steve died (with immediate respawn the client never even shows it): in Steve mode the diver dies too
		ServerLivingEntityEvents.AFTER_DEATH.register((entity, source) -> {
			if (steve && entity instanceof net.minecraft.server.level.ServerPlayer) {
				events.accept("{\"t\":\"died\"}");
			}
		});
		LOG.info("subnauticraft loaded");
	}
}
