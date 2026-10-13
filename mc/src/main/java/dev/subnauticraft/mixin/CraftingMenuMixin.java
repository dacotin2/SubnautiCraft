package dev.subnauticraft.mixin;

import com.llamalad7.mixinextras.injector.wrapoperation.Operation;
import com.llamalad7.mixinextras.injector.wrapoperation.WrapOperation;
import dev.subnauticraft.SnBlueprints;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.world.inventory.CraftingMenu;
import net.minecraft.world.inventory.ResultContainer;
import net.minecraft.world.item.crafting.RecipeHolder;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;

/** No result for a Subnautica recipe whose blueprint the diver doesn't know yet (crafting table and inventory grid alike). */
@Mixin(CraftingMenu.class)
abstract class CraftingMenuMixin {
	@WrapOperation(
		method = "slotChangedCraftingGrid",
		at = @At(value = "INVOKE", target = "Lnet/minecraft/world/inventory/ResultContainer;setRecipeUsed(Lnet/minecraft/server/level/ServerPlayer;Lnet/minecraft/world/item/crafting/RecipeHolder;)Z")
	)
	private static boolean snc$needsBlueprint(final ResultContainer result, final ServerPlayer player, final RecipeHolder<?> recipe, final Operation<Boolean> original) {
		return SnBlueprints.allowed(player, recipe) && original.call(result, player, recipe);
	}
}
