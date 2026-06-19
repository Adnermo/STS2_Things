using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Acts;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Models.RelicPools;
using STS2_Things.Cards;
using STS2_Things.Encounters;
using STS2_Things.Events;
using STS2_Things.Monsters;
using STS2_Things.Relics;

[ModInitializer(nameof(Initialize))]
public static class STS2_ThingsInit
{
    public static void Initialize()
    {
        try
        {
            // ---- 卡牌 & 遗物 模型池注册 ----
            ModHelper.AddModelToPool(typeof(DefectCardPool), typeof(Reuse));
            ModHelper.AddModelToPool(typeof(EventCardPool), typeof(Surrender));
            ModHelper.AddModelToPool(typeof(SilentCardPool), typeof(SoulfyshDisease));
            ModHelper.AddModelToPool(typeof(SilentCardPool), typeof(Recall));
            ModHelper.AddModelToPool(typeof(SilentCardPool), typeof(PackUp));
            ModHelper.AddModelToPool(typeof(EventRelicPool), typeof(WhiteFlag));
            ModHelper.AddModelToPool(typeof(EventRelicPool), typeof(CurseRemover));
            ModHelper.AddModelToPool(typeof(EventRelicPool), typeof(MagicGlove));
            ModHelper.AddModelToPool(typeof(EventRelicPool), typeof(AlmondWater));
            ModHelper.AddModelToPool(typeof(EventRelicPool), typeof(MedusaHair));

            // ---- Harmony 初始化 ----
            var harmony = new Harmony("STS2_ThingsInit");

            // ---- 怪物注册中心（视觉/遭遇/背景全自动） ----
            MonsterRegistrar.Initialize(harmony);

            // 每只怪物只需 1 行：贴图路径/边界框/锚点全部自动推断
            //   贴图 = res://images/monsters/{snake_case}.png
            //   背景 = res://images/backgrounds/{snake_case}_bg.png
            //   边界/中心/意图 = 贴图实际长宽 × scale 自动居中

            MonsterRegistrar.AddBoss<OriginFogmog>(
                typeof(OriginFogmogBossEncounter),
                0.52f, hasBackground: true, forceSpawn: true,
                acts: MonsterRegistrar.ActOvergrowth
            );

            MonsterRegistrar.AddMinion<OriginEyeWithTeeth>(
                colorMod: new Color(0.7f, 0.2f, 1f)
            );

            MonsterRegistrar.AddMinion<SoulRoe>();

            MonsterRegistrar.AddMinion<ThiefRaider>(0.45f);

            MonsterRegistrar.AddElite<SoulRoes>(
                typeof(SoulRoesEncounter),
                acts: MonsterRegistrar.ActUnderdocks
            );

            MonsterRegistrar.AddBoss<TheLegacy>(
                typeof(TheLegacyBossEncounter),
                0.52f, hasBackground: true, forceSpawn: true,
                acts: MonsterRegistrar.ActUnderdocks
            );

            MonsterRegistrar.AddBoss<ScaleBeetle>(
                typeof(ScaleBeetleBossEncounter),
                0.52f, hasBackground: true,
                acts: MonsterRegistrar.ActOvergrowth
            );

            MonsterRegistrar.AddBoss<BowlbugProgenitor>(
                typeof(BowlbugProgenitorBossEncounter),
                0.52f, hasBackground: true, forceSpawn: true,
                acts: MonsterRegistrar.ActHive
            );

            MonsterRegistrar.AddEncounter(typeof(RaidParty),
                MonsterRegistrar.ActOvergrowth);

            harmony.PatchAll();

            Log.Info("STS2_Things - 加载成功!");
        }
        catch (Exception e)
        {
            Log.Error("STS2_Things - 加载失败");
            Log.Error(e.Message);
        }
    }
}

// ==================== 事件注册（非怪物，保持原样） ====================

[HarmonyPatch(typeof(Overgrowth), "get_AllEvents")]
public static class OvergrowthAllEventsPatch
{
    private static void Postfix(ref IEnumerable<EventModel> __result)
    {
        __result = __result.Concat([ModelDb.Event<RobberyFakeMerchant>(), ModelDb.Event<Backrooms>()]).Distinct();
    }
}

[HarmonyPatch(typeof(Underdocks), "get_AllEvents")]
public static class UnderdocksAllEventsPatch
{
    private static void Postfix(ref IEnumerable<EventModel> __result)
    {
        __result = __result.Concat([ModelDb.Event<RobberyFakeMerchant>(), ModelDb.Event<Backrooms>()]).Distinct();
    }
}

[HarmonyPatch(typeof(Hive), "get_AllEvents")]
public static class HiveAllEventsPatch
{
    private static void Postfix(ref IEnumerable<EventModel> __result)
    {
        __result = __result.Concat([ModelDb.Event<Medusa>()]).Distinct();
    }
}

// ==================== 涅奥遗物 ====================

[HarmonyPatch]
public static class NeowCurseOptionsPatch
{
    [HarmonyTargetMethod]
    public static MethodBase Target(HarmonyPatchType p, Harmony instance)
    {
        return AccessTools.PropertyGetter(typeof(Neow), "CurseOptions");
    }

    private static void Postfix(Neow __instance, ref IEnumerable<EventOption> __result)
    {
        var list = __result.ToList();
        AddRelicToList<CurseRemover>(__instance, list);
        AddRelicToList<WhiteFlag>(__instance, list);
        AddRelicToList<MagicGlove>(__instance, list);
        __result = list;
    }

    private static void AddRelicToList<T>(Neow neow, List<EventOption> list) where T : RelicModel
    {
        var relic = ModelDb.Relic<T>().ToMutable();
        relic.Owner = neow.Owner;
        var option = new EventOption(
            neow,
            async () =>
            {
                await RelicCmd.Obtain(relic, neow.Owner);
                var doneMethod = typeof(AncientEventModel)
                    .GetMethod("Done", BindingFlags.NonPublic | BindingFlags.Instance);
                doneMethod?.Invoke(neow, null);
            },
            relic.Title,
            relic.DynamicEventDescription,
            "INITIAL",
            relic.HoverTipsExcludingRelic
        ).WithRelic(relic);
        list.Add(option);
    }
}

// ==================== 涅奥遗物互斥 ====================

[HarmonyPatch(typeof(RelicSelectCmd), nameof(RelicSelectCmd.FromChooseARelicScreen))]
public static class RelicExclusionPatch
{
    private static void Prefix(ref IReadOnlyList<RelicModel> relics)
    {
        var hasMagicGlove = relics.Any(r => r is MagicGlove);
        var hasShears = relics.Any(r => r.Id.Entry == "PRECARIOUS_SHEARS");

        if (!hasMagicGlove || !hasShears) return;

        relics = relics
            .Where(r => r.Id.Entry != "PRECARIOUS_SHEARS")
            .ToList();
        Log.Info("[Things] Removed PrecariousShears (conflicts with MagicGlove).");
    }
}