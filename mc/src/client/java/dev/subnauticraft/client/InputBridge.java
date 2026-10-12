package dev.subnauticraft.client;

import com.google.gson.JsonArray;
import com.google.gson.JsonElement;
import com.google.gson.JsonObject;
import net.minecraft.client.Minecraft;
import net.minecraft.client.input.KeyEvent;
import net.minecraft.client.input.MouseButtonInfo;
import org.lwjgl.sdl.SDLKeyboard;

/**
 * Replays Subnautica-captured input into Minecraft's own input handlers, as if Minecraft's (hidden) window had
 * focus, so movement, mouse look, the hotbar, the inventory and every screen work exactly as in Minecraft. Keeps a
 * virtual keyboard so InputConstants.isKeyDown() still works. Adapted from SkyCraft's InputBridge (MIT).
 *
 * <p>{"t":"in","k":[[scancode,down],...],"b":[[button,down],...],"m":[dx,dy],"c":[x,y],"w":wheel,"txt":"..."}:
 * SDL scancodes, SDL mouse buttons (1 left, 2 middle, 3 right), mouse motion in pixels, the absolute cursor (in
 * Minecraft window pixels, while a screen is open), wheel notches, typed text.
 */
public final class InputBridge {
	private static final boolean[] KEYS = new boolean[512];
	private static final boolean[] BUTTONS = new boolean[8];
	private static double cursorX, cursorY;
	private static int modifiers;

	private InputBridge() {
	}

	public static boolean isKeyDown(final int scancode) {
		return scancode >= 0 && scancode < KEYS.length && KEYS[scancode];
	}

	/** Client thread. */
	static void apply(final Minecraft minecraft, final JsonObject m) {
		long handle = minecraft.getWindow().handle();
		if (m.has("k")) {
			for (JsonElement e : m.getAsJsonArray("k")) {
				JsonArray k = e.getAsJsonArray();
				key(minecraft, handle, k.get(0).getAsInt(), k.get(1).getAsBoolean());
			}
		}

		if (m.has("c")) {
			JsonArray c = m.getAsJsonArray("c");
			double x = c.get(0).getAsDouble(), y = c.get(1).getAsDouble();
			double dx = x - cursorX, dy = y - cursorY;
			cursorX = x;
			cursorY = y;
			minecraft.mouseHandler.onMove(handle, x, y, dx, dy);
		} else if (m.has("m")) {
			JsonArray d = m.getAsJsonArray("m");
			double dx = d.get(0).getAsDouble(), dy = d.get(1).getAsDouble();
			cursorX += dx;
			cursorY += dy;
			minecraft.mouseHandler.onMove(handle, cursorX, cursorY, dx, dy);
		}

		if (m.has("b")) {
			for (JsonElement e : m.getAsJsonArray("b")) {
				JsonArray b = e.getAsJsonArray();
				int button = b.get(0).getAsInt();
				boolean down = b.get(1).getAsBoolean();
				if (button > 0 && button < BUTTONS.length) {
					BUTTONS[button] = down;
				}

				minecraft.mouseHandler.onButton(handle, new MouseButtonInfo(button, modifiers), down ? 1 : 0);
			}
		}

		if (m.has("w")) {
			minecraft.mouseHandler.onScroll(handle, 0.0, m.get("w").getAsDouble());
		}

		if (m.has("txt") && minecraft.gui.screen() != null) {
			m.get("txt").getAsString().codePoints().forEach(cp -> minecraft.keyboardHandler.textInput(handle, new String(Character.toChars(cp))));
		}
	}

	private static void key(final Minecraft minecraft, final long handle, final int scancode, final boolean down) {
		if (scancode <= 0 || scancode >= KEYS.length) {
			return;
		}

		if (KEYS[scancode] == down) {
			return; // Subnautica sends changes only; a repeat would need Minecraft's own repeat code
		}

		KEYS[scancode] = down;
		updateModifiers();
		int keycode = SDLKeyboard.SDL_GetKeyFromScancode(scancode, (short) modifiers, true);
		minecraft.keyboardHandler.keyPress(handle, down ? 1 : 0, new KeyEvent(scancode, keycode, modifiers));
	}

	private static void updateModifiers() {
		int m = 0;
		if (KEYS[225]) m |= 0x0001; // SDL_KMOD_LSHIFT
		if (KEYS[229]) m |= 0x0002; // SDL_KMOD_RSHIFT
		if (KEYS[224]) m |= 0x0040; // SDL_KMOD_LCTRL
		if (KEYS[228]) m |= 0x0080; // SDL_KMOD_RCTRL
		if (KEYS[226]) m |= 0x0100; // SDL_KMOD_LALT
		if (KEYS[230]) m |= 0x0200; // SDL_KMOD_RALT
		modifiers = m;
	}

	/** Lift every key and button we think is held (Subnautica opened its PDA or menu, the link dropped, ...). Client thread. */
	public static void releaseAll() {
		Minecraft minecraft = Minecraft.getInstance();
		long handle = minecraft.getWindow().handle();
		for (int sc = 0; sc < KEYS.length; sc++) {
			if (KEYS[sc]) {
				KEYS[sc] = false;
				updateModifiers();
				minecraft.keyboardHandler.keyPress(handle, 0, new KeyEvent(sc, SDLKeyboard.SDL_GetKeyFromScancode(sc, (short) 0, true), modifiers));
			}
		}

		for (int button = 1; button < BUTTONS.length; button++) {
			if (BUTTONS[button]) {
				BUTTONS[button] = false;
				minecraft.mouseHandler.onButton(handle, new MouseButtonInfo(button, 0), 0);
			}
		}
	}
}
