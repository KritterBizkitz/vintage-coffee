using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace CoffeeMod
{
    public class CoffeeWarmthSystem : ModSystem
    {
        private ICoreServerAPI sapi = null!;
        private long tickId;

        // cache reflection targets exactly once
        private static MethodInfo? getBehaviorBodyTempMI;
        private static MethodInfo? getBehaviorHungerMI;
        private static PropertyInfo? curBodyTempProp;
        private static MethodInfo? satiate1MI;
        private static MethodInfo? satiate3MI;

        public override void Start(ICoreAPI api)
        {
            api.RegisterCollectibleBehaviorClass("CoffeeBuff", typeof(CollectibleBehaviorCoffeeBuff));
            


        }

        public override void StartServerSide(ICoreServerAPI api)
        {
            sapi = api;

            // do not tick during world boot
            sapi.Event.SaveGameLoaded += () =>
            {
                ResolveReflectionTargets();
                // light cadence while we test
                tickId = sapi.World.RegisterGameTickListener(OnServerTick, 1000); // once per second
            };
        }

        public override void Dispose()
        {
            if (tickId != 0 && sapi?.World != null)
            {
                sapi.World.UnregisterGameTickListener(tickId);
                tickId = 0;
            }
        }

        private void ResolveReflectionTargets()
        {
            // EntityAgent.GetBehavior<T>() generic
            var getBehGeneric = typeof(EntityAgent)
                .GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .FirstOrDefault(m => m.Name == "GetBehavior" && m.IsGenericMethodDefinition && m.GetParameters().Length == 0);

            // Body temperature type can live in either assembly name depending on game build
            var bodyTempType =
                Type.GetType("Vintagestory.GameContent.EntityBehaviorBodyTemperature, VSSurvivalMod") ??
                Type.GetType("Vintagestory.GameContent.EntityBehaviorBodyTemperature, Vintagestory.GameContent");

            var hungerType =
                Type.GetType("Vintagestory.GameContent.EntityBehaviorHunger, VSSurvivalMod") ??
                Type.GetType("Vintagestory.GameContent.EntityBehaviorHunger, Vintagestory.GameContent");

            if (getBehGeneric != null && bodyTempType != null)
            {
                getBehaviorBodyTempMI = getBehGeneric.MakeGenericMethod(bodyTempType);
                curBodyTempProp = bodyTempType.GetProperty("CurBodyTemperature", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            }

            if (getBehGeneric != null && hungerType != null)
            {
                getBehaviorHungerMI = getBehGeneric.MakeGenericMethod(hungerType);

                // try Satiate(float) first
                satiate1MI = hungerType.GetMethod("Satiate",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null, new Type[] { typeof(float) }, null);

                // then Satiate(float, EnumFoodCategory, float)
                if (satiate1MI == null)
                {
                    var all = hungerType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                                        .Where(m => m.Name == "Satiate").ToArray();
                    satiate3MI = all.FirstOrDefault(m => m.GetParameters().Length == 3);
                }
            }
        }

        private void OnServerTick(float dt)
        {
            // light guards
            if (sapi.World == null) return;
            var players = sapi.World.AllOnlinePlayers;
            if (players == null || players.Length == 0) return;

            // if no one has an active effect, do nothing
            double now = sapi.World.Calendar.TotalHours * 3600.0;
            bool anyActive = players.Any(plr =>
            {
                var a = plr.Entity.WatchedAttributes;
                return a.GetDouble("coffeeWarmthUntil", 0) > now || a.GetDouble("coffeeHungerUntil", 0) > now;
            });
            if (!anyActive) return;

            foreach (var plr in players)
            {
                var eplr = plr.Entity;
                var attrs = eplr.WatchedAttributes;

                double warmUntil = attrs.GetDouble("coffeeWarmthUntil", 0);
                if (warmUntil > now)
                {
                    float boostPerSec = attrs.GetFloat("coffeeBoostPerSec", 0f);
                    if (boostPerSec > 0f && getBehaviorBodyTempMI != null && curBodyTempProp != null)
                    {
                        var tempBeh = getBehaviorBodyTempMI.Invoke(eplr, null);
                        if (tempBeh != null)
                        {
                            float cur = (float)(curBodyTempProp.GetValue(tempBeh) ?? 0f);
                            float next = GameMath.Clamp(cur + boostPerSec * dt, -20, 40);
                            curBodyTempProp.SetValue(tempBeh, next);
                        }
                    }
                }

                double hungUntil = attrs.GetDouble("coffeeHungerUntil", 0);
                if (hungUntil > now)
                {
                    float mul = GameMath.Clamp(attrs.GetFloat("coffeeHungerMul", 0.9f), 0.1f, 2f);
                    float basePerHr = attrs.GetFloat("coffeeHungerBaseSatPerHr", 60f);
                    float restorePerSec = (1f - mul) * basePerHr / 3600f;

                    if (restorePerSec > 0f && getBehaviorHungerMI != null)
                    {
                        var hungerBeh = getBehaviorHungerMI.Invoke(eplr, null);
                        if (hungerBeh != null)
                        {
                            if (satiate1MI != null)
                            {
                                satiate1MI.Invoke(hungerBeh, new object[] { restorePerSec * dt });
                            }
                            else if (satiate3MI != null)
                            {
                                // 0 for EnumFoodCategory, 1f intensity
                                satiate3MI.Invoke(hungerBeh, new object[] { restorePerSec * dt, 0, 1f });
                            }
                        }
                    }
                }
            }
        }
    }
}
