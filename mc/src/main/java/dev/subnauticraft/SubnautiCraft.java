package dev.subnauticraft;

import java.util.function.Consumer;
import net.fabricmc.api.ModInitializer;
import net.fabricmc.fabric.api.entity.event.v1.ServerLivingEntityEvents;
import net.fabricmc.fabric.api.event.lifecycle.v1.ServerLifecycleEvents;
import net.fabricmc.fabric.api.event.lifecycle.v1.ServerTickEvents;
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
		ServerLifecycleEvents.SERVER_STARTED.register(WorldBridge::attach);
		ServerLifecycleEvents.SERVER_STOPPING.register(server -> WorldBridge.detach());
		ServerTickEvents.END_SERVER_TICK.register(WorldBridge::tick);
		// Steve died (with immediate respawn the client never even shows it): in Steve mode the diver dies too
		ServerLivingEntityEvents.AFTER_DEATH.register((entity, source) -> {
			if (steve && entity instanceof net.minecraft.server.level.ServerPlayer) {
				events.accept("{\"t\":\"died\"}");
			}
		});
		LOG.info("subnauticraft loaded");
	}
}
