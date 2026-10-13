package dev.subnauticraft;

import com.mojang.serialization.Codec;
import java.util.ArrayList;
import java.util.HashMap;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.UUID;
import net.minecraft.ChatFormatting;
import net.minecraft.core.component.DataComponents;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.network.chat.Component;
import net.minecraft.resources.Identifier;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.util.datafix.DataFixTypes;
import net.minecraft.world.SimpleContainer;
import net.minecraft.world.SimpleMenuProvider;
import net.minecraft.world.entity.player.Inventory;
import net.minecraft.world.entity.player.Player;
import net.minecraft.world.inventory.ChestMenu;
import net.minecraft.world.inventory.ContainerInput;
import net.minecraft.world.inventory.ContainerLevelAccess;
import net.minecraft.world.inventory.CraftingMenu;
import net.minecraft.world.inventory.MenuType;
import net.minecraft.world.inventory.Slot;
import net.minecraft.world.item.Item;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.item.Items;
import net.minecraft.world.item.component.ItemLore;
import net.minecraft.world.level.saveddata.SavedData;
import net.minecraft.world.level.saveddata.SavedDataType;

/**
 * Subnautica's machines as Minecraft screens (the plugin asks for them when Steve uses one): a locker is a chest whose
 * contents are kept in this world (one world per Subnautica save), the fabricator is a crafting table, and the mobile
 * vehicle bay shows its vehicles with what they cost; clicking one with the materials in the inventory has the bay
 * build it in Subnautica, and the materials are taken once it starts.
 */
public final class SnMenus {
	private static final Map<UUID, Pending> pending = new HashMap<>();

	private SnMenus() {
	}

	/** What a Subnautica vehicle costs: Minecraft item ids ("?Name" when the material isn't a Minecraft item). */
	public record Need(String item, int count, String name) {
	}

	public record Option(String tech, String item, String name, List<Need> needs) {
	}

	private record Pending(String bay, Option option) {
	}

	/** Every locker's contents in this world, by the Subnautica locker's id. */
	public static final class Lockers extends SavedData {
		static final Codec<Lockers> CODEC = Codec.unboundedMap(Codec.STRING, ItemStack.OPTIONAL_CODEC.listOf()).xmap(Lockers::new, l -> l.contents);
		static final SavedDataType<Lockers> TYPE = new SavedDataType<>(
			Identifier.fromNamespaceAndPath(SubnautiCraft.ID, "lockers"), Lockers::new, CODEC, DataFixTypes.SAVED_DATA_COMMAND_STORAGE
		);
		private final Map<String, List<ItemStack>> contents;

		Lockers() {
			this(Map.of());
		}

		Lockers(final Map<String, List<ItemStack>> saved) {
			this.contents = new HashMap<>(saved);
		}
	}

	/** A Subnautica locker: a Minecraft chest with its own contents. */
	public static void openLocker(final ServerPlayer player, final String id, final int rows, final String title) {
		Lockers lockers = player.level().getServer().overworld().getDataStorage().computeIfAbsent(Lockers.TYPE);
		List<ItemStack> saved = lockers.contents.getOrDefault(id, List.of());
		int size = Math.max(rows * 9, Math.min(54, saved.size()));
		int shownRows = Math.min(6, (size + 8) / 9);
		// every change is written back to the world's saved lockers
		SimpleContainer box = new SimpleContainer(shownRows * 9) {
			@Override
			public void setChanged() {
				List<ItemStack> now = new ArrayList<>(this.getContainerSize());
				for (int i = 0; i < this.getContainerSize(); i++) {
					now.add(this.getItem(i).copy());
				}

				lockers.contents.put(id, now);
				lockers.setDirty();
			}
		};
		for (int i = 0; i < Math.min(box.getContainerSize(), saved.size()); i++) {
			box.setItem(i, saved.get(i).copy());
		}
		MenuType<ChestMenu> type = switch (shownRows) {
			case 1 -> MenuType.GENERIC_9x1;
			case 2 -> MenuType.GENERIC_9x2;
			case 3 -> MenuType.GENERIC_9x3;
			case 4 -> MenuType.GENERIC_9x4;
			case 5 -> MenuType.GENERIC_9x5;
			default -> MenuType.GENERIC_9x6;
		};
		var storage = player.level().getServer().overworld().getDataStorage();
		player.openMenu(new SimpleMenuProvider((id2, inventory, p) -> new ChestMenu(type, id2, inventory, box, shownRows) {
			@Override
			public void removed(final Player who) {
				super.removed(who);
				storage.scheduleSave(); // closed: onto disk now, not at the next autosave
			}
		}, Component.literal(title)));
	}

	/** The fabricator: a crafting table (Subnautica's recipes are Minecraft recipes); what's left in the grid comes back. */
	public static void openCrafting(final ServerPlayer player, final String title) {
		player.openMenu(new SimpleMenuProvider((id, inventory, p) -> new CraftingMenu(id, inventory, ContainerLevelAccess.create(p.level(), p.blockPosition())) {
			@Override
			public boolean stillValid(final Player who) {
				return true;
			}
		}, Component.literal(title)));
	}

	/** The mobile vehicle bay: a row of the vehicles it knows, each with its cost. */
	public static void openVehicles(final ServerPlayer player, final String bay, final String title, final List<Option> options) {
		SimpleContainer cards = new SimpleContainer(9);
		for (int i = 0; i < options.size() && i < 9; i++) {
			cards.setItem(i, card(player, options.get(i)));
		}

		player.openMenu(new SimpleMenuProvider((id, inventory, p) -> new VehicleBayMenu(id, inventory, cards, bay, options), Component.literal(title)));
	}

	private static ItemStack card(final ServerPlayer player, final Option option) {
		Item item = option.item().isEmpty() ? Items.BARRIER : BuiltInRegistries.ITEM.getValue(Identifier.fromNamespaceAndPath(SubnautiCraft.ID, option.item()));
		ItemStack stack = new ItemStack(item);
		stack.set(DataComponents.CUSTOM_NAME, Component.literal(option.name()).withStyle(ChatFormatting.AQUA));
		List<Component> lore = new ArrayList<>();
		lore.add(Component.translatable("subnauticraft.bay.needs").withStyle(ChatFormatting.GRAY));
		for (Need need : option.needs()) {
			int have = count(player, need.item());
			lore.add(Component.literal(String.format(Locale.ROOT, "%d/%d  ", Math.min(have, need.count()), need.count()))
				.append(Component.literal(need.name()))
				.withStyle(have >= need.count() ? ChatFormatting.GREEN : ChatFormatting.RED));
		}

		lore.add(Component.translatable("subnauticraft.bay.click").withStyle(ChatFormatting.YELLOW));
		stack.set(DataComponents.LORE, new ItemLore(lore));
		return stack;
	}

	private static Item item(final String id) {
		return id.startsWith("?") ? null : BuiltInRegistries.ITEM.getValue(Identifier.fromNamespaceAndPath(SubnautiCraft.ID, id));
	}

	private static int count(final ServerPlayer player, final String id) {
		Item item = item(id);
		if (item == null) {
			return 0;
		}

		int n = 0;
		Inventory inventory = player.getInventory();
		for (int i = 0; i < inventory.getContainerSize(); i++) {
			ItemStack stack = inventory.getItem(i);
			if (stack.is(item)) {
				n += stack.getCount();
			}
		}

		return n;
	}

	private static void take(final ServerPlayer player, final String id, final int count) {
		Item item = item(id);
		int left = count;
		Inventory inventory = player.getInventory();
		for (int i = 0; item != null && i < inventory.getContainerSize() && left > 0; i++) {
			ItemStack stack = inventory.getItem(i);
			if (stack.is(item)) {
				int n = Math.min(left, stack.getCount());
				stack.shrink(n);
				left -= n;
			}
		}

		inventory.setChanged();
	}

	private static boolean affords(final ServerPlayer player, final Option option) {
		if (player.getAbilities().instabuild) {
			return true;
		}

		for (Need need : option.needs()) {
			if (count(player, need.item()) < need.count()) {
				return false;
			}
		}

		return true;
	}

	private static void request(final ServerPlayer player, final String bay, final Option option) {
		if (!affords(player, option)) {
			player.sendSystemMessage(Component.translatable("subnauticraft.bay.missing").withStyle(ChatFormatting.RED), true);
			return;
		}

		pending.put(player.getUUID(), new Pending(bay, option));
		player.closeContainer();
		SubnautiCraft.events.accept("{\"t\":\"build\",\"id\":\"" + bay + "\",\"tech\":\"" + option.tech() + "\"}");
	}

	/** Subnautica's answer: the bay started building (the materials are taken now) or had no room for it. */
	public static void built(final ServerPlayer player, final boolean ok) {
		Pending what = pending.remove(player.getUUID());
		if (what == null) {
			return;
		}

		if (!ok) {
			player.sendSystemMessage(Component.translatable("subnauticraft.bay.blocked").withStyle(ChatFormatting.RED), true);
			return;
		}

		if (!player.getAbilities().instabuild) {
			for (Need need : what.option().needs()) {
				take(player, need.item(), need.count());
			}
		}

		player.sendSystemMessage(Component.translatable("subnauticraft.bay.building", what.option().name()).withStyle(ChatFormatting.AQUA), true);
	}

	/** One row of vehicle cards: clicking one asks the bay to build it; the cards can't be taken. */
	private static final class VehicleBayMenu extends ChestMenu {
		private final SimpleContainer cards;
		private final String bay;
		private final List<Option> options;

		VehicleBayMenu(final int id, final Inventory inventory, final SimpleContainer cards, final String bay, final List<Option> options) {
			super(MenuType.GENERIC_9x1, id, inventory, cards, 1);
			this.cards = cards;
			this.bay = bay;
			this.options = options;
		}

		@Override
		public void clicked(final int slot, final int button, final ContainerInput input, final Player player) {
			if (slot >= 0 && slot < 9) {
				if (slot < this.options.size() && player instanceof ServerPlayer serverPlayer) {
					request(serverPlayer, this.bay, this.options.get(slot));
				}

				return;
			}

			super.clicked(slot, button, input, player);
		}

		@Override
		public ItemStack quickMoveStack(final Player player, final int slot) {
			return ItemStack.EMPTY;
		}

		@Override
		public boolean canTakeItemForPickAll(final ItemStack carried, final Slot slot) {
			return slot.container != this.cards && super.canTakeItemForPickAll(carried, slot);
		}

		@Override
		public boolean stillValid(final Player player) {
			return true;
		}
	}
}
