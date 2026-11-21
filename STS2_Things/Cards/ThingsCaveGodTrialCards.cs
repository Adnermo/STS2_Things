using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.Monsters;
using STS2_Things.Core.Models;
using STS2_Things.Powers;

namespace STS2_Things.Cards;

/// <summary>
/// 【断刃】(Broken Blade) 试炼状态卡（二选一）：选择后本回合无法打出攻击牌。
/// </summary>
public sealed class ThingsCaveGodBrokenBladeTrial : ThingsCardModel, KnowledgeDemon.IChoosable
{
    public ThingsCaveGodBrokenBladeTrial()
        : base(-1, CardType.Status, CardRarity.Status, TargetType.None)
    {
    }

    public override int MaxUpgradeLevel => 0;
    public override bool CanBeGeneratedInCombat => false;
    public override CardPoolModel Pool => ModelDb.CardPool<TokenCardPool>();

    public async Task OnChosen()
    {
        await PowerCmd.Apply<ThingsCaveGodBrokenBladePower>(
            new ThrowingPlayerChoiceContext(), Owner.Creature, 1m, Owner.Creature, this);
    }
}

/// <summary>
/// 【碎盾】(Shattered Shield) 试炼状态卡（二选一）：选择后本回合无法打出技能牌。
/// </summary>
public sealed class ThingsCaveGodShatteredShieldTrial : ThingsCardModel, KnowledgeDemon.IChoosable
{
    public ThingsCaveGodShatteredShieldTrial()
        : base(-1, CardType.Status, CardRarity.Status, TargetType.None)
    {
    }

    public override int MaxUpgradeLevel => 0;
    public override bool CanBeGeneratedInCombat => false;
    public override CardPoolModel Pool => ModelDb.CardPool<TokenCardPool>();

    public async Task OnChosen()
    {
        await PowerCmd.Apply<ThingsCaveGodShatteredShieldPower>(
            new ThrowingPlayerChoiceContext(), Owner.Creature, 1m, Owner.Creature, this);
    }
}
