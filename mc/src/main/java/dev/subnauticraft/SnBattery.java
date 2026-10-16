package dev.subnauticraft;

import java.util.List;
import java.util.Set;
import net.minecraft.ChatFormatting;
import net.minecraft.core.component.DataComponents;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.network.chat.Component;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.sounds.SoundEvents;
import net.minecraft.sounds.SoundSource;
import net.minecraft.world.SimpleContainer;
import net.minecraft.world.SimpleMenuProvider;
import net.minecraft.world.entity.player.Inventory;
import net.minecraft.world.entity.player.Player;
import net.minecraft.world.inventory.ChestMenu;
import net.minecraft.world.inventory.ContainerInput;
import net.minecraft.world.inventory.MenuType;
import net.minecraft.world.inventory.Slot;
import net.minecraft.world.item.DyeColor;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.item.Items;
import net.minecraft.world.item.component.ItemLore;

/**
 * Subnautica's tools run on a battery: its charge is the item's durability bar (the flashlight, Seaglide and scanner
 * spend it here; any other tool's comes from the real one in Subnautica, SteveTools). R with a tool in hand opens the
 * battery screen: the tool, its charge, and a slot to put a fresh battery in, which swaps it, as Subnautica's R does.
 */
public final class SnBattery {
	private static final int MAX = 1000;
	private static final int SLOT_TOOL = 0;
	private static final int SLOT_CHARGE = 2;
	private static final int SLOT_BATTERY = 4;
	private static final Set<String> MAPPED = Set.of("flashlight", "seaglide", "scanner");
	/** Subnautica's tools on a battery, known by kind (one picked up before batteries has no bar yet). */
	private static final Set<String> TOOLS = Set.of("StasisRifle", "PropulsionCannon", "RepulsionCannon", "LaserCutter", "Builder", "Welder",
		"AirBladder", "DiveReel", "Terraformer", "Flashlight", "Seaglide", "Scanner");

	private SnBattery() {
	}

	/** One of Subnautica's tools that runs on a battery. */
	public static boolean isTool(final ItemStack stack) {
		String path = BuiltInRegistries.ITEM.getKey(stack.getItem()).getPath();
		boolean ours = SubnautiCraft.ID.equals(BuiltInRegistries.ITEM.getKey(stack.getItem()).getNamespace());
		String tech = SnPickups.tech(stack);
		return ours && (MAPPED.contains(path) || tech != null && (stack.isDamageableItem() || TOOLS.contains(tech)));
	}

	/** A fresh battery: Subnautica's battery, or the ion battery. */
	private static boolean isBattery(final ItemStack stack) {
		return stack.is(BuiltInRegistries.ITEM.getValue(net.minecraft.resources.Identifier.fromNamespaceAndPath(SubnautiCraft.ID, "battery")))
			|| "PrecursorIonBattery".equals(SnPickups.tech(stack));
	}

	/** A "Subnautica item" tool gets its battery bar (one from before batteries, or one never charged). */
	static void makeChargeable(final ItemStack stack) {
		if (!stack.isDamageableItem()) {
			stack.set(DataComponents.MAX_STACK_SIZE, 1);
			stack.set(DataComponents.MAX_DAMAGE, MAX);
			stack.set(DataComponents.DAMAGE, 0);
		}
	}

	/** {"t":"toolcharge","c":0..1}: the real tool in the diver's hand spent (or got) charge: so does the item. */
	public static void charge(final ServerPlayer player, final float charge) {
		ItemStack held = player.getMainHandItem();
		if (SnPickups.tech(held) == null || held.getCount() != 1) {
			return;
		}

		makeChargeable(held);
		held.setDamageValue(Math.round((1F - Math.max(0F, Math.min(1F, charge))) * held.getMaxDamage()));
	}

	/** R: the battery screen for the tool in Steve's hand. */
	public static void open(final ServerPlayer player) {
		int slot = player.getInventory().getSelectedSlot();
		ItemStack tool = player.getInventory().getItem(slot);
		if (!isTool(tool)) {
			player.sendSystemMessage(Component.translatable("subnauticraft.battery.none").withStyle(ChatFormatting.GRAY), true);
			return;
		}

		if (SnPickups.tech(tool) != null) {
			makeChargeable(tool);
		}

		SimpleContainer shown = new SimpleContainer(9);
		show(shown, tool);
		player.openMenu(new SimpleMenuProvider((id, inventory, p) -> new BatteryMenu(id, inventory, shown, slot),
			Component.translatable("subnauticraft.battery.title")));
	}

	private static void show(final SimpleContainer shown, final ItemStack tool) {
		ItemStack glass = new ItemStack(Items.STAINED_GLASS_PANE.pick(DyeColor.GRAY));
		glass.set(DataComponents.CUSTOM_NAME, Component.literal(" "));
		for (int i = 0; i < 9; i++) {
			shown.setItem(i, glass.copy());
		}

		shown.setItem(SLOT_TOOL, tool.copyWithCount(1));
		int percent = tool.isDamageableItem() ? Math.round(100F * (tool.getMaxDamage() - tool.getDamageValue()) / tool.getMaxDamage()) : 100;
		ItemStack charge = new ItemStack(Items.DYE.pick(percent > 50 ? DyeColor.LIME : percent > 15 ? DyeColor.YELLOW : DyeColor.RED));
		charge.set(DataComponents.CUSTOM_NAME, Component.translatable("subnauticraft.battery.charge", percent)
			.withStyle(percent > 50 ? ChatFormatting.GREEN : percent > 15 ? ChatFormatting.YELLOW : ChatFormatting.RED));
		shown.setItem(SLOT_CHARGE, charge);
		ItemStack put = new ItemStack(Items.STAINED_GLASS_PANE.pick(DyeColor.LIGHT_BLUE));
		put.set(DataComponents.CUSTOM_NAME, Component.translatable("subnauticraft.battery.put").withStyle(ChatFormatting.AQUA));
		put.set(DataComponents.LORE, new ItemLore(List.of(Component.translatable("subnauticraft.battery.how").withStyle(ChatFormatting.GRAY))));
		shown.setItem(SLOT_BATTERY, put);
	}

	/** One row: the tool, its charge, and where a battery goes. Nothing in it can be taken. */
	private static final class BatteryMenu extends ChestMenu {
		private final SimpleContainer shown;
		private final int toolSlot;

		BatteryMenu(final int id, final Inventory inventory, final SimpleContainer shown, final int toolSlot) {
			super(MenuType.GENERIC_9x1, id, inventory, shown, 1);
			this.shown = shown;
			this.toolSlot = toolSlot;
		}

		/** A battery goes in: the tool is full again (the old battery stays with Subnautica's recycling). */
		private boolean swap(final Player player, final ItemStack battery) {
			ItemStack tool = player.getInventory().getItem(this.toolSlot);
			if (!isBattery(battery) || !isTool(tool)) {
				return false;
			}

			tool.setDamageValue(0);
			battery.shrink(1);
			player.level().playSound(null, player.getX(), player.getY(), player.getZ(), SoundEvents.ARMOR_EQUIP_IRON.value(), SoundSource.PLAYERS, 0.8F, 1.4F);
			show(this.shown, tool);
			return true;
		}

		@Override
		public void clicked(final int slot, final int button, final ContainerInput input, final Player player) {
			if (slot >= 0 && slot < 9) {
				if (slot == SLOT_BATTERY && !this.getCarried().isEmpty()) {
					swap(player, this.getCarried());
				}

				return;
			}

			super.clicked(slot, button, input, player);
		}

		@Override
		public ItemStack quickMoveStack(final Player player, final int slot) {
			// shift-click on a battery in the inventory: it goes in
			if (slot >= 9) {
				Slot from = this.slots.get(slot);
				if (from.hasItem() && swap(player, from.getItem())) {
					from.setChanged();
				}
			}

			return ItemStack.EMPTY;
		}

		@Override
		public boolean canTakeItemForPickAll(final ItemStack carried, final Slot slot) {
			return slot.container != this.shown && super.canTakeItemForPickAll(carried, slot);
		}

		@Override
		public boolean stillValid(final Player player) {
			return true;
		}
	}
}
