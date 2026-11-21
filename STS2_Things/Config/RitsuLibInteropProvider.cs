using System.Reflection;

// RitsuLib（STS2-RitsuLib）的互操作镜像通过程序集级 AssemblyMetadata 自动发现配置页
// 提供器（见 RuntimeInteropMirrorSource.ReadProviderTypeNames），无需任何编译期引用。
// 若 RitsuLib 在模组之后才加载，镜像会在程序集集合变化时重新扫描，仍可发现本提供器。
[assembly: AssemblyMetadata(
    "RitsuLib.ModSettingsInterop.ProviderType",
    "STS2_Things.Config.RitsuLibInteropProvider")]

namespace STS2_Things.Config;

/// <summary>
/// RitsuLib 配置页互操作提供器（零编译依赖）。
///
/// 契约（RitsuLib v0.5.12，RuntimeInteropMirrorSource）：
///   - 静态 <c>CreateRitsuLibSettingsSchema()</c> 返回 schema（字典 / JSON 字符串 / 文件路径），
///     这里使用字典，旧式单页格式：pageId/title/description/sortOrder/sections[]；
///   - 静态值访问器：Get/SetRitsuLibSettingValue(string[, object])（必需）、
///     Get/SetRitsuLibSettingBool（可选类型化）、SaveRitsuLibSettings；
///   - visibleWhenMethod 允许 <c>Foo()</c> 或 <c>Foo(string)</c> 返回 bool，用于
///     同槽位强制冲突时的条件可见。
///
/// 所有键读写都委托给 <see cref="ThingsModConfig"/>（同一份
/// user://mod_configs/STS2_Things.cfg，与 BaseLib 桥、独立模式共用）。
/// </summary>
public static class RitsuLibInteropProvider
{
    public static IDictionary<string, object?> CreateRitsuLibSettingsSchema()
    {
        return new Dictionary<string, object?>
        {
            ["modId"] = "STS2_Things",
            ["pageId"] = "things",
            ["title"] = Text("STS2_Things Settings", "尖塔：琐事 设置"),
            ["description"] = Text(
                "In multiplayer, the host controls settings. Gameplay settings lock when the run starts; only the host can change music during a run. Your local preferences are preserved.",
                "多人时由房主决定配置；玩法设置开局后锁定，音乐仍可由房主调整。退出房间恢复自己的设置。"),
            ["sortOrder"] = 10040,
            ["sections"] = new object[]
            {
                AudioSection(),
                BossSection(),
                EncounterSection(),
                EventSection(),
                CardSection(),
                MerchantSection(),
                NeowRelicSection(),
                StabilitySection(),
            },
        };
    }

    // ---- 值访问器（RitsuLib 经反射定位；全部委托 ThingsModConfig）----

    public static object? GetRitsuLibSettingValue(string key) => ThingsModConfig.GetValue(key);

    public static void SetRitsuLibSettingValue(string key, object? value) => ThingsModConfig.SetValue(key, value);

    public static bool GetRitsuLibSettingBool(string key) => ThingsModConfig.GetBool(key);

    public static void SetRitsuLibSettingBool(string key, bool value) => ThingsModConfig.SetValue(key, value);

    public static int GetRitsuLibSettingInt(string key) => ThingsModConfig.GetInt(key);

    public static void SetRitsuLibSettingInt(string key, int value) => ThingsModConfig.SetValue(key, value);

    public static void SaveRitsuLibSettings() => ThingsModConfig.Save();

    // The mirror schema exposes conditional visibility, but no enabled predicate.
    public static bool IsSettingVisible(string key) => ThingsModConfig.CanEdit(key) &&
        (!key.EndsWith("Forced", StringComparison.Ordinal) || ThingsModConfig.CanForce(key)) &&
        (!ThingsModConfig.IsIndividualCardKey(key) || ThingsModConfig.IsEnabled(ThingsModConfig.CardsEnabled));

    /// <summary>强制开关的可见条件：同槽位没有其他强制 Boss（或强制者正是本键）。</summary>
    public static bool IsBossForceVisible(string key)
    {
        return ThingsModConfig.CanForce(key);
    }

    // ---- schema 构建 ----

    private static Dictionary<string, object?> AudioSection() => Section("audio", "Audio", "音频",
        Entry("custom_bgm", ThingsModConfig.FeatureCustomBgmEnabled, "Mod custom music", "启用模组自定义音乐",
            "Disable to restore the game's original music. Sound effects remain enabled.",
            "关闭后恢复原版音乐，保留战斗音效。"));

    private static Dictionary<string, object?> BossSection()
    {
        // 名称与模组本地化一致（monsters.json / encounters.json 的显示名）。
        return Section("bosses", "Boss Encounters", "遭遇战·Boss",
            Entry("only_mod_bosses", ThingsModConfig.BossOnlyModBosses, "Only this mod's bosses", "仅出现本模组 Boss",
                "Use enabled mod bosses in their original acts. If none are available, keep the original boss pool. Forced bosses take priority.",
                "在对应地图只抽取已启用的本模组 Boss；没有可用者时回退原有 Boss 池。指定 Boss 优先。"),
            BossEntry("origin_fogmog_enabled", ThingsModConfig.BossOriginFogmogEnabled, "Origin Fogmog", "始源雾菇"),
            BossForceEntry("origin_fogmog_forced", ThingsModConfig.BossOriginFogmogForced, "Origin Fogmog", "始源雾菇"),
            WeightEntry("origin_fogmog_weight", ThingsModConfig.BossOriginFogmogWeightPercent, "Origin Fogmog", "始源雾菇"),
            BossEntry("scale_beetle_enabled", ThingsModConfig.BossScaleBeetleEnabled, "Scale Beetle", "放缩巨甲虫"),
            BossForceEntry("scale_beetle_forced", ThingsModConfig.BossScaleBeetleForced, "Scale Beetle", "放缩巨甲虫"),
            WeightEntry("scale_beetle_weight", ThingsModConfig.BossScaleBeetleWeightPercent, "Scale Beetle", "放缩巨甲虫"),
            BossEntry("gravetide_slug_enabled", ThingsModConfig.BossGravetideSlugEnabled, "Gravetide Slug", "墓潮蛞蝓"),
            BossForceEntry("gravetide_slug_forced", ThingsModConfig.BossGravetideSlugForced, "Gravetide Slug", "墓潮蛞蝓"),
            WeightEntry("gravetide_slug_weight", ThingsModConfig.BossGravetideSlugWeightPercent, "Gravetide Slug", "墓潮蛞蝓"),
            BossEntry("the_legacy_enabled", ThingsModConfig.BossTheLegacyEnabled, "The Legacy", "腐化之遗"),
            BossForceEntry("the_legacy_forced", ThingsModConfig.BossTheLegacyForced, "The Legacy", "腐化之遗"),
            WeightEntry("the_legacy_weight", ThingsModConfig.BossTheLegacyWeightPercent, "The Legacy", "腐化之遗"),
            BossEntry("bowlbug_progenitor_enabled", ThingsModConfig.BossBowlbugProgenitorEnabled, "Bowlbug Progenitor", "盛碗虫族母"),
            BossForceEntry("bowlbug_progenitor_forced", ThingsModConfig.BossBowlbugProgenitorForced, "Bowlbug Progenitor", "盛碗虫族母"),
            WeightEntry("bowlbug_progenitor_weight", ThingsModConfig.BossBowlbugProgenitorWeightPercent, "Bowlbug Progenitor", "盛碗虫族母"),
            BossEntry("cave_god_enabled", ThingsModConfig.BossCaveGodEnabled, "Living Megalith", "活体巨岩"),
            BossForceEntry("cave_god_forced", ThingsModConfig.BossCaveGodForced, "Living Megalith", "活体巨岩"),
            WeightEntry("cave_god_weight", ThingsModConfig.BossCaveGodWeightPercent, "Living Megalith", "活体巨岩"),
            BossEntry("human_face_tower_enabled", ThingsModConfig.BossHumanFaceTowerEnabled, "The Visage Tower", "人面塔"),
            BossForceEntry("human_face_tower_forced", ThingsModConfig.BossHumanFaceTowerForced, "The Visage Tower", "人面塔"),
            WeightEntry("human_face_tower_weight", ThingsModConfig.BossHumanFaceTowerWeightPercent, "The Visage Tower", "人面塔"),
            BossEntry("abyssal_inkmaw_enabled", ThingsModConfig.BossAbyssalInkmawEnabled, "Abyssal Inkmaw", "渊墨巨蛸"),
            BossForceEntry("abyssal_inkmaw_forced", ThingsModConfig.BossAbyssalInkmawForced, "Abyssal Inkmaw", "渊墨巨蛸"),
            WeightEntry("abyssal_inkmaw_weight", ThingsModConfig.BossAbyssalInkmawWeightPercent, "Abyssal Inkmaw", "渊墨巨蛸"));
    }

    private static Dictionary<string, object?> EncounterSection()
    {
        return Section("encounters", "Other Encounters", "遭遇战·其他",
            Entry("soul_roes_enabled", ThingsModConfig.EncounterSoulRoesEnabled, "Soul Roes", "灵魂鱼子团"),
            WeightEntry("soul_roes_weight", ThingsModConfig.EncounterSoulRoesWeightPercent, "Soul Roes (elite)", "灵魂鱼子团（精英）"),
            Entry("quirky_hopper_enabled", ThingsModConfig.EncounterQuirkyHopperEnabled, "Quirky Hopper", "怪癖草蜢"));
    }

    private static Dictionary<string, object?> EventSection()
    {
        // 名称与模组本地化一致（events.json 的 .title 显示名）。
        return Section("events", "Events", "事件",
            Entry("robbery_fake_merchant_enabled", ThingsModConfig.EventRobberyFakeMerchantEnabled,
                "Shifty Shop", "诡谲之店"),
            Entry("backrooms_enabled", ThingsModConfig.EventBackroomsEnabled, "Stumble", "跌倒"),
            Entry("medusa_enabled", ThingsModConfig.EventMedusaEnabled, "Medusa", "蛇发女妖"),
            Entry("cutting_it_close_enabled", ThingsModConfig.EventCuttingItCloseEnabled,
                "Cutting It Close", "命悬一线"),
            Entry("reality_aligned_houses_enabled", ThingsModConfig.EventRealityAlignedHousesEnabled,
                "Reality Aligned Houses", "对齐之屋"),
            Entry("shadow_cloakroom_enabled", ThingsModConfig.EventShadowCloakroomEnabled,
                "Shadow Cloakroom", "影子寄存处"),
            Entry("echoing_well_enabled", ThingsModConfig.EventEchoingWellEnabled,
                "Echoing Well", "收音井"),
            Entry("polite_maw_enabled", ThingsModConfig.EventPoliteMawEnabled,
                "Polite Maw", "礼貌的洞胃"),
            Entry("mycelial_bank_enabled", ThingsModConfig.EventMycelialBankEnabled,
                "Mycelial Bank", "菌根借贷所"),
            Entry("unlit_fire_enabled", ThingsModConfig.EventUnlitFireEnabled,
                "Unlit Fire", "还没烧起来的火"),
            Entry("relic_workshop_enabled", ThingsModConfig.EventRelicWorkshopEnabled,
                "Relic Workshop", "遗物修补摊"),
            Entry("potion_tasting_enabled", ThingsModConfig.EventPotionTastingEnabled,
                "Potion Tasting", "药水试饮会"),
            Entry("narrow_gate_enabled", ThingsModConfig.EventNarrowGateEnabled,
                "Narrow Gate", "窄门"),
            Entry("biting_chest_enabled", ThingsModConfig.EventBitingChestEnabled,
                "Biting Chest", "会咬人的宝箱"),
            Entry("crowded_ward_enabled", ThingsModConfig.EventCrowdedWardEnabled,
                "Crowded Ward", "满员的病房"),
            Entry("returns_counter_enabled", ThingsModConfig.EventReturnsCounterEnabled,
                "Returns Counter", "退货柜台"),
            Entry("hot_wax_box_enabled", ThingsModConfig.EventHotWaxBoxEnabled,
                "Hot Wax Box", "发烫的蜡封匣"),
            Entry("warm_royal_cell_enabled", ThingsModConfig.EventWarmRoyalCellEnabled,
                "Warm Royal Cell", "温热的育王巢房"),
            Entry("splice_booth_enabled", ThingsModConfig.EventSpliceBoothEnabled,
                "The Splice Booth", "过载接线亭"),
            Entry("gilded_press_enabled", ThingsModConfig.EventGildedPressEnabled,
                "The Gilded Press", "漏墨的镀金印台"),
            Entry("crooked_counterweight_enabled", ThingsModConfig.EventCrookedCounterweightEnabled,
                "Crooked Counterweight", "歪掉的砝码秤"),
            Entry("ant_sorting_table_enabled", ThingsModConfig.EventAntSortingTableEnabled,
                "Ant Sorting Table", "兵蚁分拣台"),
            Entry("grudge_vending_machine_enabled", ThingsModConfig.EventGrudgeVendingMachineEnabled,
                "Grudge Vending Machine", "记仇的贩卖机"),
            Entry("blind_crab_inkwell_enabled", ThingsModConfig.EventBlindCrabInkwellEnabled,
                "Blind Crab Inkwell", "打翻的盲蟹墨缸"),
            Entry("chronometer_rig_enabled", ThingsModConfig.EventChronometerRigEnabled,
                "Chronometer Rig", "发条调速仪"),
            Entry("silvered_gallery_enabled", ThingsModConfig.EventSilveredGalleryEnabled,
                "Silvered Gallery", "镀银试衣镜"),
            Entry("cracked_brazier_enabled", ThingsModConfig.EventCrackedBrazierEnabled,
                "Cracked Brazier", "裂纹琉璃火盆"),
            Entry("silt_winch_enabled", ThingsModConfig.EventSiltWinchEnabled,
                "Silt Winch", "淤泥绞盘台"),
            Entry("bilge_mines_enabled", ThingsModConfig.EventBilgeMinesEnabled,
                "Bilge Grate", "暗渠货栅"),
            Entry("moss_rune_loom_enabled", ThingsModConfig.EventMossRuneLoomEnabled,
                "Overgrown Rune Loom", "古藤刻痕盘"),
            Entry("clam_claw_enabled", ThingsModConfig.EventClamClawEnabled,
                "Clam Claw Crane", "蚌口夹珍"),
            Entry("forest_plinko_enabled", ThingsModConfig.EventForestPlinkoEnabled,
                "Overgrown Plinko", "古林落籽盘"),
            Entry("bronze_cauldron_enabled", ThingsModConfig.EventBronzeCauldronEnabled,
                "Bronze Tripod Scramble (Co-op)", "铜鼎争宝（多人）"),
            Entry("lending_vine_enabled", ThingsModConfig.EventLendingVineEnabled,
                "The Lending Vine", "借牌老藤"),
            Entry("overstuffed_brood_enabled", ThingsModConfig.EventOverstuffedBroodEnabled,
                "Overstuffed Brood", "过饱育婴室"),
            Entry("scales_of_vanity_enabled", ThingsModConfig.EventScalesOfVanityEnabled,
                "Scales of Vanity", "虚荣天平"),
            Entry("wall_crack_ledger_enabled", ThingsModConfig.EventWallCrackLedgerEnabled,
                "The Wall-Crack Ledger", "墙缝账房"),
            Entry("spiked_door_enabled", ThingsModConfig.EventSpikedDoorEnabled,
                "The Spiked Door (Co-op)", "谁去推门（多人）"));
    }

    private static Dictionary<string, object?> CardSection()
    {
        // 名称与模组本地化一致（cards.json 的 .title 显示名）。
        return Section("cards", "Cards", "卡牌",
            Entry("cards_enabled", ThingsModConfig.CardsEnabled,
                "Mod custom cards", "启用模组自制卡牌",
                "Disable to exclude all custom class cards from this mod out of rewards, shops, transforms, and random card generation. Card Library and existing saves remain unaffected.",
                "关闭后，所有本模组自制职业牌不再出现在奖励、商店、变化或随机生成池中；图鉴与已有存档不受影响。"),
            Entry("card_collision_enabled", ThingsModConfig.CardCollisionEnabled,
                "Collision (Ironclad)", "对撞（铁甲战士）"),
            Entry("card_reuse_enabled", ThingsModConfig.CardReuseEnabled,
                "Reuse (Defect)", "再利用（故障机器人）"),
            Entry("card_soulfysh_disease_enabled", ThingsModConfig.CardSoulfyshDiseaseEnabled,
                "Soulfysh Disease (Silent)", "异鱼症（静默猎手）"),
            Entry("card_recall_enabled", ThingsModConfig.CardRecallEnabled,
                "Reminisce (Silent)", "追忆（静默猎手）"),
            Entry("card_pack_up_enabled", ThingsModConfig.CardPackUpEnabled,
                "Pack Up (Silent)", "整顿（静默猎手）"));
    }

    private static Dictionary<string, object?> MerchantSection()
    {
        return Section("merchant", "Merchant Bargain", "商人猜拳",
            Entry("merchant_bargain_enabled", ThingsModConfig.FeatureMerchantBargainEnabled,
                "Merchant Rock-Paper-Scissors", "商人猜拳（讨价还价）",
                "Toggle the merchant bargaining mini-game.", "开关商人处的猜拳讨价还价小游戏。"));
    }

    private static Dictionary<string, object?> NeowRelicSection()
    {
        // 名称与模组本地化一致（relics.json 的 .title 显示名）。
        return Section("neow", "Neow Starting Relics", "涅奥起始遗物",
            Entry("neow_curse_remover_enabled", ThingsModConfig.NeowRelicCurseRemoverEnabled,
                "Yellow Talisman (Neow)", "黄符（涅奥）"),
            Entry("neow_white_flag_enabled", ThingsModConfig.NeowRelicWhiteFlagEnabled,
                "White Flag (Neow)", "白旗（涅奥）"),
            Entry("neow_magic_glove_enabled", ThingsModConfig.NeowRelicMagicGloveEnabled,
                "Severed Sleeve (Neow)", "断袖（涅奥）"));
    }

    private static Dictionary<string, object?> StabilitySection()
    {
        return Section("stability", "Engine Safety", "引擎防护",
            Entry("defensive_engine_guards_enabled", ThingsModConfig.FeatureDefensiveEngineGuardsEnabled,
                "Defensive UI & lifecycle crash guards", "防御性 UI 与生命周期防崩保护",
                "Protects against hover-tip viewport clipping, pooled card texture leaks, and combat teardown race conditions without changing any vanilla gameplay rules.",
                "防护悬停提示越界裁剪、对象池卡面贴图残留与战斗退出竞态异常，不改变任何原版玩法规则。"));
    }

    private static Dictionary<string, object?> Section(
        string id,
        string titleEn,
        string titleZh,
        params Dictionary<string, object?>[] entries)
    {
        return new Dictionary<string, object?>
        {
            ["id"] = id,
            ["title"] = Text(titleEn, titleZh),
            ["entries"] = entries,
        };
    }

    private static Dictionary<string, object?> BossEntry(
        string id, string key, string nameEn, string nameZh)
    {
        return Entry(id, key, $"{nameEn} — enabled", $"{nameZh} — 启用",
            "Can this boss appear as the act boss or in the act pool?",
            "该 Boss 是否能出现在本幕的遭遇池/Boss 候选中？");
    }

    private static Dictionary<string, object?> BossForceEntry(
        string id, string key, string nameEn, string nameZh)
    {
        return Entry(id, key, $"{nameEn} — force", $"{nameZh} — 强制",
            "Always fight this boss at the act end (one forced boss per act).",
            "本幕结尾必定遭遇该 Boss（每幕最多一个强制）。");
    }

    private static Dictionary<string, object?> WeightEntry(string id, string key, string nameEn, string nameZh)
    {
        Dictionary<string, object?> entry = Entry(id, key, $"{nameEn} — spawn weight (%)", $"{nameZh} — 出现权重（%）",
            "0: never; 100: default; 200: double weight. Relative to other eligible encounters, not a fixed chance. Takes effect when the next act is generated.",
            "0 不出现，100 为默认，200 为两倍权重；实际概率由候选共同决定，并非固定百分比。下次生成幕时生效。");
        entry["type"] = "int-slider";
        entry["min"] = 0;
        entry["max"] = 1000;
        entry["step"] = 10;
        return entry;
    }

    private static Dictionary<string, object?> Entry(
        string id,
        string key,
        string labelEn,
        string labelZh,
        string? descriptionEn = null,
        string? descriptionZh = null)
    {
        var entry = new Dictionary<string, object?>
        {
            ["id"] = id,
            ["type"] = "toggle",
            ["key"] = key,
            ["label"] = Text(labelEn, labelZh),
            ["defaultValue"] = ThingsModConfig.Entries.Single(entry => entry.Key == key).Default,
            ["visibleWhenMethod"] = nameof(IsSettingVisible),
        };
        if (descriptionEn is not null)
            entry["description"] = Text(descriptionEn, descriptionZh ?? descriptionEn);
        return entry;
    }

    /// <summary>
    /// 语言映射文本：键为游戏本地化语言码（LocManager：eng/zhs/zht…）。
    /// RitsuLib 的 ResolveLangMap 按当前语言码精确/前缀匹配，最后回退 "en"；
    /// 简体与繁体中文均显示中文，其余语言回退英文。
    /// </summary>
    private static Dictionary<string, object?> Text(string en, string zhCn)
    {
        return new Dictionary<string, object?>
        {
            ["en"] = en,
            ["zhs"] = zhCn,
            ["zht"] = zhCn,
        };
    }
}
