package dev.subnauticraft.client.mixin;

import net.minecraft.client.KeyMapping;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.gen.Accessor;

@Mixin(KeyMapping.class)
public interface KeyMappingAccessor {
	@Accessor("clickCount")
	int snc$getClickCount();

	@Accessor("clickCount")
	void snc$setClickCount(int clickCount);
}
