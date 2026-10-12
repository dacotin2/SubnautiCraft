package dev.subnauticraft.client.mixin;

import dev.subnauticraft.SubnautiCraft;
import dev.subnauticraft.client.HostState;
import java.util.Locale;
import net.minecraft.client.Minecraft;
import net.minecraft.client.player.LocalPlayer;
import net.minecraft.tags.ItemTags;
import net.minecraft.core.component.DataComponents;
import net.minecraft.world.entity.EquipmentSlot;
import net.minecraft.world.entity.ai.attributes.Attributes;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.item.component.ItemAttributeModifiers;
import org.jspecify.annotations.Nullable;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/** Steve swings a sword: Subnautica uses it as its survival knife on what's in front of him (it gets the sword's damage). */
@Mixin(Minecraft.class)
abstract class MinecraftAttackMixin {
	@Shadow public @Nullable LocalPlayer player;

	@Inject(method = "startAttack", at = @At("HEAD"))
	private void snc$swordIsKnife(final CallbackInfoReturnable<Boolean> cir) {
		LocalPlayer p = this.player;
		ItemStack sword = p != null ? p.getMainHandItem() : ItemStack.EMPTY;
		if (p != null && HostState.tookOver() && sword.is(ItemTags.SWORDS)) {
			// from the sword itself: the client's attribute values don't include the held item's modifiers
			double damage = sword.getOrDefault(DataComponents.ATTRIBUTE_MODIFIERS, ItemAttributeModifiers.EMPTY)
				.compute(Attributes.ATTACK_DAMAGE, p.getAttributeBaseValue(Attributes.ATTACK_DAMAGE), EquipmentSlot.MAINHAND);
			SubnautiCraft.events.accept(String.format(Locale.ROOT, "{\"t\":\"melee\",\"dmg\":%.2f}", damage));
		}
	}
}
