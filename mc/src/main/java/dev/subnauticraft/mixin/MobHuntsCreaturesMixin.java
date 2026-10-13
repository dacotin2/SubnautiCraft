package dev.subnauticraft.mixin;

import dev.subnauticraft.SnCreatures;
import net.minecraft.world.entity.EntityType;
import net.minecraft.world.entity.Mob;
import net.minecraft.world.entity.ai.goal.GoalSelector;
import net.minecraft.world.entity.ai.goal.target.NearestAttackableTargetGoal;
import net.minecraft.world.entity.monster.Enemy;
import net.minecraft.world.level.Level;
import org.spongepowered.asm.mixin.Final;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

/** Minecraft's monsters hunt Subnautica's creatures too (their stand-ins, see SnCreatures), after the player. */
@Mixin(Mob.class)
abstract class MobHuntsCreaturesMixin {
	@Shadow
	@Final
	protected GoalSelector targetSelector;

	@Inject(method = "<init>", at = @At("TAIL"))
	private void snc$huntCreatures(final EntityType<? extends Mob> type, final Level level, final CallbackInfo ci) {
		Mob self = (Mob) (Object) this;
		if (self instanceof Enemy && !(self instanceof SnCreatures.Creature) && level != null && !level.isClientSide() && SnCreatures.TYPE != null) {
			this.targetSelector.addGoal(3, new NearestAttackableTargetGoal<>(self, SnCreatures.Creature.class, true));
		}
	}
}
