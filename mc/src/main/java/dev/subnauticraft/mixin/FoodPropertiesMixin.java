package dev.subnauticraft.mixin;

import dev.subnauticraft.SnPickups;
import dev.subnauticraft.SubnautiCraft;
import java.util.Locale;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.world.entity.LivingEntity;
import net.minecraft.world.entity.player.Player;
import net.minecraft.world.food.FoodProperties;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.item.component.Consumable;
import net.minecraft.world.level.Level;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

/**
 * Steve ate something: in Steve mode Subnautica's food (and water) bars are Steve's too, so the diver gets it as well
 * ({"t":"ate","item":id,"n":nutrition,"s":saturation}); otherwise Subnautica would still starve him.
 */
@Mixin(FoodProperties.class)
abstract class FoodPropertiesMixin {
	@Inject(method = "onConsume", at = @At("HEAD"))
	private void snc$ate(final Level level, final LivingEntity user, final ItemStack stack, final Consumable consumable, final CallbackInfo ci) {
		if (level.isClientSide() || !SubnautiCraft.steve || !(user instanceof Player)) {
			return;
		}

		FoodProperties food = (FoodProperties) (Object) this;
		// one of Subnautica's own foods or drinks (SnPickups): Subnautica gives the diver what that kind gives
		String tech = SnPickups.tech(stack);
		String exact = tech == null ? "" : ",\"tech\":\"" + tech.replaceAll("[^A-Za-z0-9_]", "") + "\"";

		SubnautiCraft.events.accept(String.format(Locale.ROOT, "{\"t\":\"ate\",\"item\":\"%s\",\"n\":%d,\"s\":%.2f%s}",
			BuiltInRegistries.ITEM.getKey(stack.getItem()), food.nutrition(), food.saturation(), exact));
	}
}
