package dev.subnauticraft;

import java.util.HashMap;
import java.util.Iterator;
import java.util.Locale;
import java.util.Map;
import net.fabricmc.fabric.api.object.builder.v1.entity.FabricDefaultAttributeRegistry;
import net.minecraft.core.Registry;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.core.registries.Registries;
import net.minecraft.resources.Identifier;
import net.minecraft.resources.ResourceKey;
import net.minecraft.server.MinecraftServer;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.world.damagesource.DamageSource;
import net.minecraft.world.entity.Entity;
import net.minecraft.world.entity.EntityDimensions;
import net.minecraft.world.entity.EntityType;
import net.minecraft.world.entity.LivingEntity;
import net.minecraft.world.entity.Mob;
import net.minecraft.world.entity.MobCategory;
import net.minecraft.world.entity.Pose;
import net.minecraft.world.entity.monster.Enemy;
import net.minecraft.world.entity.player.Player;
import net.minecraft.world.level.Level;
import net.minecraft.world.phys.AABB;

/**
 * Minecraft's mobs and Subnautica's creatures can fight each other. Each Subnautica creature near the player has an
 * invisible stand-in here (Minecraft's monsters hunt and hit it; the hits go to the creature in Subnautica), and every
 * Minecraft mob near the player is reported to Subnautica, which gives it an invisible body there (its creatures bite
 * it; the bites come back here as damage).
 */
public final class SnCreatures {
	public static EntityType<Creature> TYPE;
	private static final Map<Integer, Creature> standIns = new HashMap<>();
	private static int ticks;

	private SnCreatures() {
	}

	static void register() {
		ResourceKey<EntityType<?>> key = ResourceKey.create(Registries.ENTITY_TYPE, Identifier.fromNamespaceAndPath(SubnautiCraft.ID, "subnautica_creature"));
		TYPE = Registry.register(BuiltInRegistries.ENTITY_TYPE, key,
			EntityType.Builder.<Creature>of(Creature::new, MobCategory.MISC).sized(1.0F, 1.0F).noSave().noSummon().clientTrackingRange(4).build(key));
		FabricDefaultAttributeRegistry.register(TYPE, Mob.createMobAttributes());
	}

	/**
	 * {"t":"creatures","c":[[id,x,y,z,size],...]}: the Subnautica creatures near the player, centres in Minecraft
	 * coordinates. Stand-ins appear, follow and go away with them.
	 */
	public static void update(final MinecraftServer server, final double[][] creatures) {
		ServerLevel level = server.overworld();
		Map<Integer, double[]> seen = new HashMap<>();
		for (double[] c : creatures) {
			seen.put((int) c[0], c);
		}

		for (Iterator<Map.Entry<Integer, Creature>> it = standIns.entrySet().iterator(); it.hasNext(); ) {
			Map.Entry<Integer, Creature> e = it.next();
			if (!seen.containsKey(e.getKey()) || e.getValue().isRemoved()) {
				e.getValue().discard();
				it.remove();
			}
		}

		for (double[] c : seen.values()) {
			int id = (int) c[0];
			float size = (float) Math.max(0.3, Math.min(8.0, c[4]));
			Creature standIn = standIns.get(id);
			if (standIn == null) {
				standIn = new Creature(TYPE, level);
				standIn.subnauticaId = id;
				standIn.setSize(size);
				standIn.setPos(c[1], c[2] - size / 2.0, c[3]);
				level.addFreshEntity(standIn);
				standIns.put(id, standIn);
			} else {
				standIn.setSize(size);
				standIn.setPos(c[1], c[2] - size / 2.0, c[3]);
			}
		}
	}

	/** Subnautica is gone: no stand-ins left behind. */
	public static void clear() {
		standIns.values().forEach(Entity::discard);
		standIns.clear();
	}

	/** Every 2 ticks: {"t":"mobs","m":[[id,x,y,z,width,height,hostile],...]}, Minecraft's mobs near the player (feet). */
	static void tick(final MinecraftServer server) {
		if (!SubnautiCraft.active || ++ticks % 2 != 0 || server.getPlayerList().getPlayers().isEmpty()) {
			return;
		}

		ServerPlayer player = server.getPlayerList().getPlayers().get(0);
		StringBuilder b = new StringBuilder("{\"t\":\"mobs\",\"m\":[");
		boolean first = true;
		for (Mob mob : player.level().getEntitiesOfClass(Mob.class, new AABB(player.blockPosition()).inflate(48.0), m -> !(m instanceof Creature) && m.isAlive())) {
			b.append(first ? "" : ",").append(String.format(Locale.ROOT, "[%d,%.3f,%.3f,%.3f,%.2f,%.2f,%d]",
				mob.getId(), mob.getX(), mob.getY(), mob.getZ(), mob.getBbWidth(), mob.getBbHeight(), mob instanceof Enemy ? 1 : 0));
			first = false;
		}

		SubnautiCraft.events.accept(b.append("]}").toString());
	}

	/** {"t":"mobhit","id":entity,"dmg":hp}: a Subnautica creature bit this Minecraft mob. */
	public static void mobHit(final MinecraftServer server, final int id, final float damage) {
		ServerLevel level = server.overworld();
		Entity entity = level.getEntity(id);
		if (entity instanceof LivingEntity living && !(entity instanceof Creature) && living.isAlive()) {
			living.hurtServer(level, level.damageSources().generic(), damage);
		}
	}

	/**
	 * A Subnautica creature's stand-in: invisible, silent, no AI, not solid, not hit by the player (his sword and arrows
	 * already reach the creature in Subnautica itself), never hurt itself; a mob's hit goes to the creature.
	 */
	public static final class Creature extends Mob {
		int subnauticaId;
		private float size = 1.0F;

		public Creature(final EntityType<? extends Mob> type, final Level level) {
			super(type, level);
			this.noPhysics = true;
			this.setNoGravity(true);
			this.setNoAi(true);
			// not "invisible" (monsters would hardly notice it): it simply has nothing to draw (NoopRenderer)
			this.setSilent(true);
			this.setPersistenceRequired();
		}

		void setSize(final float size) {
			if (Math.abs(size - this.size) > 0.05F) {
				this.size = size;
				this.refreshDimensions();
			}
		}

		@Override
		protected EntityDimensions getDefaultDimensions(final Pose pose) {
			return EntityDimensions.scalable(this.size, this.size);
		}

		@Override
		public boolean hurtServer(final ServerLevel level, final DamageSource source, final float damage) {
			if (source.getEntity() instanceof Mob attacker && !(attacker instanceof Creature) && damage > 0.0F) {
				SubnautiCraft.events.accept(String.format(Locale.ROOT, "{\"t\":\"creaturehit\",\"id\":%d,\"dmg\":%.2f}", this.subnauticaId, damage));
				attacker.setLastHurtMob(this);
				return true;
			}

			return false;
		}

		/** Below Subnautica's surface it's in the water (it has no physics to find out): drowned hunt it there by day too. */
		@Override
		public boolean isInWater() {
			return super.isInWater() || dev.subnauticraft.world.SnWater.depthIn(this.blockPosition()) > 0.0F;
		}

		@Override
		public boolean isPickable() {
			return false;
		}

		@Override
		public boolean isPushable() {
			return false;
		}

		@Override
		public boolean canBeCollidedWith(final Entity other) {
			return false;
		}

		@Override
		public boolean isAttackable() {
			return true;
		}

		@Override
		public boolean removeWhenFarAway(final double distanceSqr) {
			return false;
		}

		@Override
		public boolean shouldShowName() {
			return false;
		}

		@Override
		public boolean canBeHitByProjectile() {
			return false;
		}

		@Override
		public boolean isInvulnerableTo(final ServerLevel level, final DamageSource source) {
			return !(source.getEntity() instanceof Mob) || source.getEntity() instanceof Player;
		}
	}
}
