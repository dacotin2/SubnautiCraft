package dev.subnauticraft.client;

import com.google.gson.JsonObject;
import dev.subnauticraft.SubnautiCraft;
import dev.subnauticraft.client.mixin.KeyMappingAccessor;
import net.minecraft.client.KeyMapping;
import net.minecraft.client.Minecraft;
import net.minecraft.client.player.LocalPlayer;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.world.entity.player.Inventory;
import org.lwjgl.sdl.SDLVideo;

/** Subnautica's input, applied on the client thread: Subnautica's window has the focus, so Minecraft never sees these itself. */
final class ClientInput {
	private ClientInput() {
	}

	static void handle(final Minecraft minecraft, final JsonObject m) {
		LocalPlayer player = minecraft.player;
		switch (m.get("t").getAsString()) {
			case "key" -> {
				String k = m.get("k").getAsString();
				boolean down = !m.has("down") || m.get("down").getAsBoolean();
				if (k.equals("escape")) {
					if (down && minecraft.gui.screen() != null) {
						minecraft.gui.screen().onClose();
					}

					return;
				}

				KeyMapping key = switch (k) {
					case "use" -> minecraft.options.keyUse;
					case "attack" -> minecraft.options.keyAttack;
					case "pick" -> minecraft.options.keyPickItem;
					case "inventory" -> minecraft.options.keyInventory;
					case "drop" -> minecraft.options.keyDrop;
					case "swap" -> minecraft.options.keySwapOffhand;
					default -> null;
				};
				if (k.equals("attack") && down && player != null
					&& BuiltInRegistries.ITEM.getKey(player.getMainHandItem().getItem()).getPath().endsWith("_sword")) {
					// a sword swing: Subnautica hits the creature in front of the player
					SubnautiCraft.events.accept("{\"t\":\"melee\"}");
				}

				if (key != null) {
					if (down && !key.isDown()) {
						KeyMappingAccessor access = (KeyMappingAccessor)key;
						access.snc$setClickCount(access.snc$getClickCount() + 1);
					}

					key.setDown(down);
				}
			}
			case "slot" -> {
				if (player != null) {
					player.getInventory().setSelectedSlot(Math.clamp(m.get("n").getAsInt(), 0, Inventory.getSelectionSize() - 1));
				}
			}
			case "scroll" -> {
				if (player != null) {
					Inventory inventory = player.getInventory();
					int size = Inventory.getSelectionSize();
					inventory.setSelectedSlot(Math.floorMod(inventory.getSelectedSlot() - m.get("d").getAsInt(), size));
				}
			}
			case "hud" -> {
				if (minecraft.gui.hud.isHidden() != m.get("hidden").getAsBoolean()) {
					minecraft.gui.hud.toggle();
				}
			}
			case "view" -> {
				// match Subnautica's picture exactly: un-minimize/un-maximize first (resizing a maximized window is ignored)
				int w = m.get("w").getAsInt(), h = m.get("h").getAsInt();
				long handle = minecraft.getWindow().handle();
				SDLVideo.SDL_RestoreWindow(handle);
				minecraft.getWindow().setWindowed(w, h);
				SDLVideo.SDL_SetWindowSize(handle, w, h);
				SDLVideo.SDL_SyncWindow(handle);
				if (windowHidden) {
					hideWindow(minecraft, true);
				}
			}
			case "window" -> hideWindow(minecraft, m.get("hide").getAsBoolean());
			default -> {
			}
		}
	}

	/** Off the desktop, not minimized: a minimized window stops rendering, one outside the screen keeps going. */
	private static final int OFF_SCREEN = -20000;
	private static boolean windowHidden;

	/** While Subnautica shows Minecraft, Minecraft's own window sits outside the desktop; it comes back when Subnautica leaves. */
	static void hideWindow(final Minecraft minecraft, final boolean hide) {
		long handle = minecraft.getWindow().handle();
		if (hide && !windowHidden) {
			// remember where it was (a second screen, say) to put it back there
			try (org.lwjgl.system.MemoryStack stack = org.lwjgl.system.MemoryStack.stackPush()) {
				java.nio.IntBuffer x = stack.mallocInt(1), y = stack.mallocInt(1);
				if (SDLVideo.SDL_GetWindowPosition(handle, x, y)) {
					shownX = x.get(0);
					shownY = y.get(0);
				}
			}
		}

		if (hide) {
			SDLVideo.SDL_SetWindowPosition(handle, OFF_SCREEN, OFF_SCREEN);
		} else if (windowHidden) {
			SDLVideo.SDL_SetWindowPosition(handle, shownX, shownY);
		}

		windowHidden = hide;
		SDLVideo.SDL_SyncWindow(handle);
	}

	private static int shownX = SDLVideo.SDL_WINDOWPOS_CENTERED, shownY = SDLVideo.SDL_WINDOWPOS_CENTERED;
}
