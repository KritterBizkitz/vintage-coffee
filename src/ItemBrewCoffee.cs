using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace CoffeeMod
{
    public class CollectibleBehaviorCoffeeBuff : CollectibleBehavior
    {
        public CollectibleBehaviorCoffeeBuff(CollectibleObject coll) : base(coll) { }

        static object GetBehaviorReflect(EntityAgent e, string typeA, string typeB)
        {
            var behType = Type.GetType(typeA) ?? Type.GetType(typeB);
            if (behType == null) return null;

            var m = typeof(EntityAgent).GetMethods(BindingFlags.Instance | BindingFlags.Public)
                                       .FirstOrDefault(x => x.Name == "GetBehavior" && x.IsGenericMethodDefinition && x.GetParameters().Length == 0);
            if (m == null) return null;

            var gm = m.MakeGenericMethod(behType);
            return gm.Invoke(e, null);
        }

        // Your API: includes secondsUsed and ref EnumHandling
        public override void OnHeldInteractStop(float secondsUsed, ItemSlot slot, EntityAgent byEntity,
            BlockSelection blockSel, EntitySelection entitySel, ref EnumHandling handling)
        {
            base.OnHeldInteractStop(secondsUsed, slot, byEntity, blockSel, entitySel, ref handling);
            if (byEntity.World.Side != EnumAppSide.Server) return;
            if (secondsUsed < 0.95f) return;

            var eplr = byEntity as EntityPlayer;
            if (eplr == null) return;

            var a = slot.Itemstack?.Collectible?.Attributes;

            float instantDegrees = a?["coffeeInstantDegrees"].AsFloat(2.5f) ?? 2.5f;
            float durationSec = a?["coffeeDurationSec"].AsFloat(480f) ?? 480f;
            float boostPerSec = a?["coffeeBoostPerSec"].AsFloat(0.03f) ?? 0.03f;

            float hungerMul = a?["coffeeHungerMul"].AsFloat(0.9f) ?? 0.9f;
            float hours = a?["coffeeHungerHours"].AsFloat(3f) ?? 3f;
            float baseSatPerHr = a?["coffeeHungerSatPerHourBase"].AsFloat(60f) ?? 60f;

            // Instant warmth
            var tempBeh = GetBehaviorReflect(
                eplr,
                "Vintagestory.GameContent.EntityBehaviorBodyTemperature, VSSurvivalMod",
                "Vintagestory.GameContent.EntityBehaviorBodyTemperature, Vintagestory.GameContent"
            );
            if (tempBeh != null)
            {
                var prop = tempBeh.GetType().GetProperty("CurBodyTemperature");
                if (prop != null)
                {
                    float cur = (float)(prop.GetValue(tempBeh) ?? 0f);
                    float next = GameMath.Clamp(cur + instantDegrees, -20, 40);
                    prop.SetValue(tempBeh, next);
                }
            }

            double now = byEntity.World.Calendar.TotalHours * 3600.0;

            var wattr = eplr.WatchedAttributes;
            double until = Math.Max(wattr.GetDouble("coffeeWarmthUntil", 0), now + durationSec);
            wattr.SetDouble("coffeeWarmthUntil", until);
            wattr.SetFloat("coffeeBoostPerSec", boostPerSec);

            wattr.SetDouble("coffeeHungerUntil", now + hours * 3600f);
            wattr.SetFloat("coffeeHungerMul", hungerMul);
            wattr.SetFloat("coffeeHungerBaseSatPerHr", baseSatPerHr);
        }
    }
}
