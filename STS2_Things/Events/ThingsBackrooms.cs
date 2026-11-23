using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.ValueProps;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using STS2_Things.Core.Models;
using STS2_Things.Enchantments;
using STS2_Things.Relics;

namespace STS2_Things.Events;

public sealed class ThingsBackrooms : ThingsEventModel
{
    public const int FirstSearchDamage = 2;
    public const int RestMaxHpLoss = 5;

    // 搜索伤害/概率统一以 DynamicVars 为唯一数据源，避免与实例字段不同步
    private int SearchDamage => (int)DynamicVars.HpLoss.BaseValue;
    private int SearchChance => (int)DynamicVars["SearchChance"].BaseValue;
    private int MaxHpLoss => (int)DynamicVars["MaxHpLoss"].BaseValue;
    private bool CanRest => !EventOwner.Creature.IsDead && EventOwner.Creature.MaxHp > MaxHpLoss;

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new HpLossVar(FirstSearchDamage), // 寻找出口初始扣血
        new IntVar("SearchChance", 10),   // 初始概率10%
        new IntVar("MaxHpLoss", RestMaxHpLoss), // 原地休息失去最大生命
        new HealVar(25),                  // 原地休息恢复生命
        new CardsVar(2)                   // 附魔消散选择牌数
    ];

    public override bool IsAllowed(IRunState runState) =>
        runState.Players.Count > 0
        && runState.Players.All(player =>
            !player.Creature.IsDead
            && (player.Creature.CurrentHp > FirstSearchDamage
                || player.Creature.MaxHp > RestMaxHpLoss));

    protected override IReadOnlyList<EventOption> GenerateInitialOptions()
    {
        EventOption restOption = CanRest
            ? new EventOption(this, Rest, InitialOptionKey("REST"))
                .ThatDecreasesMaxHp(MaxHpLoss)
            : new EventOption(this, null, InitialOptionKey("REST_LOCKED"));

        return
        [
            new EventOption(this, SearchExit, InitialOptionKey("SEARCH_EXIT"))
                .ThatDoesDamage(SearchDamage),
            restOption
        ];
    }

    private async Task SearchExit()
    {
        if (IsFinished || EventOwner.Creature.IsDead)
            return;

        var damage = SearchDamage;
        var chance = SearchChance;

        // 扣除生命值
        await ThingsEventHelpers.DealUnblockableDamageAsync(EventOwner, damage);
        if (EventOwner.Creature.IsDead)
        {
            SetEventFinished(L10NLookup($"{Id.Entry}.pages.DEATH.description"));
            return;
        }

        // 判断是否找到出口
        if (Rng.NextInt(100) < chance)
        {
            // 找到出口！切换到出口页面
            ShowExitPage();
        }
        else
        {
            // 没找到出口，增加扣血和概率（唯一数据源：DynamicVars）
            DynamicVars.HpLoss.BaseValue = damage + 2;
            DynamicVars["SearchChance"].BaseValue = chance + 20;

            EventOption restOption = CanRest
                ? new EventOption(this, Rest, $"{Id.Entry}.pages.SEARCH_FAIL.options.REST")
                    .ThatDecreasesMaxHp(MaxHpLoss)
                : new EventOption(this, null, $"{Id.Entry}.pages.SEARCH_FAIL.options.REST_LOCKED");

            // 显示未找到出口的页面，可以继续寻找
            SetEventState(
                L10NLookup($"{Id.Entry}.pages.SEARCH_FAIL.description"),
                [
                    new EventOption(this, SearchExit, $"{Id.Entry}.pages.SEARCH_FAIL.options.SEARCH_EXIT")
                        .ThatDoesDamage(SearchDamage),
                    restOption
                ]
            );
        }
    }

    private void ShowExitPage()
    {
        var disperse = ModelDb.Enchantment<ThingsDisperse>();
        bool canEnchant = EventOwner.Deck.Cards.Any(c => disperse.CanEnchant(c));
        bool ownsAlmondWater = EventOwner.Relics.Any(r => r is ThingsAlmondWater);
        SetEventState(
            L10NLookup($"{Id.Entry}.pages.EXIT.description"),
            [
                new EventOption(this, canEnchant ? EnchantDissolve : null, $"{Id.Entry}.pages.EXIT.options.ENCHANT_DISSOLVE",
                    HoverTipFactory.FromEnchantment<ThingsDisperse>()),
                new EventOption(this, ownsAlmondWater ? null : TakeAlmondWater, $"{Id.Entry}.pages.EXIT.options.TAKE_ALMOND_WATER",
                    HoverTipFactory.FromRelic<ThingsAlmondWater>()),
                new EventOption(this, Leave, $"{Id.Entry}.pages.EXIT.options.LEAVE")
            ]
        );
    }

    private async Task EnchantDissolve()
    {
        var disperse = ModelDb.Enchantment<ThingsDisperse>();
        int eligibleCount = EventOwner.Deck.Cards.Count(c => disperse.CanEnchant(c));
        int selectCount = Math.Min((int)DynamicVars["Cards"].BaseValue, eligibleCount);
        if (selectCount > 0)
        {
            var selected = (await CardSelectCmd.FromDeckForEnchantment(
                EventOwner, disperse, 1,
                new CardSelectorPrefs(CardSelectorPrefs.EnchantSelectionPrompt, selectCount))).ToList();

            foreach (var card in selected)
            {
                CardCmd.Enchant<ThingsDisperse>(card, 1m);
                var vfx = NCardEnchantVfx.Create(card);
                if (vfx != null)
                {
                    NRun.Instance?.GlobalUi.CardPreviewContainer.AddChildSafely(vfx);
                }
            }
        }

        SetEventFinished(L10NLookup($"{Id.Entry}.pages.ENCHANT_DISSOLVE.description"));
    }

    private async Task TakeAlmondWater()
    {
        RelicModel relic = EventOwner.Relics.Any(r => r is ThingsAlmondWater)
            ? ModelDb.Relic<Circlet>().ToMutable()
            : ModelDb.Relic<ThingsAlmondWater>().ToMutable();
        await RelicCmd.Obtain(relic, EventOwner);
        SetEventFinished(L10NLookup($"{Id.Entry}.pages.TAKE_ALMOND_WATER.description"));
    }

    private async Task Rest()
    {
        if (IsFinished || EventOwner.Creature.IsDead || EventOwner.Creature.MaxHp <= MaxHpLoss)
            return;

        await LoseEventOwnerMaxHpAsync(
            new ThrowingPlayerChoiceContext(),
            DynamicVars["MaxHpLoss"].BaseValue);
        if (EventOwner.Creature.IsDead)
        {
            SetEventFinished(L10NLookup($"{Id.Entry}.pages.DEATH.description"));
            return;
        }

        await CreatureCmd.Heal(EventOwner.Creature, DynamicVars.Heal.BaseValue);
        SetEventFinished(L10NLookup($"{Id.Entry}.pages.REST.description"));
    }

    private Task Leave()
    {
        SetEventFinished(L10NLookup($"{Id.Entry}.pages.LEAVE.description"));
        return Task.CompletedTask;
    }
}
