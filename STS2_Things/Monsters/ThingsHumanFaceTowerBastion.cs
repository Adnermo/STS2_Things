using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Ascension;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.ValueProps;
using STS2_Things.Platform;
using STS2_Things.Powers;
using STS2_Things.Visuals;

namespace STS2_Things.Monsters;

/// <summary>
/// 人面塔 Slot 2 — 缄默面 (Bastion Visage).
/// Stone-bolt fortress &amp; burst check: enters center bolted with 14(18) Block and retains Block while bolted.
/// Breaking Block triggers 破栓 (Unbolted): suffers 2 Vulnerable and drops Colossus Drop from 18(21) to 10(12).
/// </summary>
public sealed class ThingsHumanFaceTowerBastion : ThingsHumanFaceTowerFace
{
    public bool IsBolted { get; private set; } = true;

    public override int FaceSlotIndex => 2;
    public override string IdleAnimationName => IsBolted ? "bastion_idle_bolted" : "bastion_idle_unbolted";
    public override string StarterAttackAnimName => "bastion_attack_colossus";

    public override int MinInitialHp => AscensionHelper.GetValueIfAscension(AscensionLevel.ToughEnemies, 121, 112) + (Sts2VersionSeam.IsDread() ? 14 : 0);
    public override int MaxInitialHp => MinInitialHp;

    public int EntrySealBlock => AscensionHelper.GetValueIfAscension(AscensionLevel.ToughEnemies, 18, 14) + (Sts2VersionSeam.IsDread() ? 4 : 0);
    public int BulwarkPressDamage => AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies, 12, 10) + (Sts2VersionSeam.IsDread() ? 2 : 0);
    public int BulwarkPressBlock => AscensionHelper.GetValueIfAscension(AscensionLevel.ToughEnemies, 10, 8);
    public int BoltedColossusDamage => AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies, 24, 21) + (Sts2VersionSeam.IsDread() ? 2 : 0);
    public int UnboltedColossusDamage => AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies, 14, 12) + (Sts2VersionSeam.IsDread() ? 2 : 0);
    public int ColossusDropDamage => IsBolted ? BoltedColossusDamage : UnboltedColossusDamage;

    public override int ComboStarterDamage => IsBolted
        ? AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies, 15, 13) + (Sts2VersionSeam.IsDread() ? 2 : 0)
        : AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies, 10, 9) + (Sts2VersionSeam.IsDread() ? 2 : 0);

    public override int ComboFollowUpDamage => AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies, 13, 11) + (Sts2VersionSeam.IsDread() ? 2 : 0);

    public override IEnumerable<string> AssetPaths => base.AssetPaths.Concat([
        ModelDb.Power<ThingsBastionSealPower>().ResolvedBigIconPath,
        ModelDb.Power<VulnerablePower>().ResolvedBigIconPath
    ]);

    protected override void AfterCloned()
    {
        base.AfterCloned();
        IsBolted = true;
    }

    internal void SetBolted(bool bolted, bool playUnboltAnim = false)
    {
        IsBolted = bolted;
        NHumanFaceTowerBossBackground.Current?.SetBastionBolted(bolted);
        if (IsActiveFace)
        {
            if (playUnboltAnim && !bolted)
                NHumanFaceTowerBossBackground.Current?.PlayFaceOneShot("bastion_unbolt", IdleAnimationName, mixDuration: 0f);
            else
                NHumanFaceTowerBossBackground.Current?.SetFaceIdle(IdleAnimationName);
        }

        if (IsRegisteredInCombat && Creature.IsAlive)
        {
            Creature.GetPower<ThingsBastionSealPower>()?.RefreshSealDisplay();
            Sts2VersionSeam.RefreshMonsterIntentsUi(Creature);
        }
    }

    protected override MonsterMoveStateMachine GenerateMoveStateMachine()
    {
        var bulwarkPress = new MoveState(
            "BULWARK_PRESS_MOVE",
            BulwarkPressMoveAsync,
            new SingleAttackIntent(() => BulwarkPressDamage),
            new DefendIntent());
        var colossusDrop = new MoveState(
            "COLOSSUS_DROP_MOVE",
            ColossusDropMoveAsync,
            new SingleAttackIntent(() => ColossusDropDamage));

        return BuildTowerStateMachine(
            bulwarkPress,
            colossusDrop,
            "HIDDEN_MOVE",
            "EMERGENCY_STUN_MOVE",
            "CYCLE_CLIMAX_MOVE",
            "COMBO_ROTATE_MOVE");
    }

    internal override async Task ApplyUniquePassivePowerAsync()
    {
        if (!Creature.HasPower<ThingsBastionSealPower>())
        {
            await PowerCmd.Apply<ThingsBastionSealPower>(
                new ThrowingPlayerChoiceContext(),
                Creature,
                1,
                Creature,
                null,
                silent: true);
        }
    }

    internal override async Task OnEnteredCenterAsync(bool isInitialCombatStart)
    {
        _ = isInitialCombatStart;
        if (!Creature.IsAlive || CombatManager.Instance.IsOverOrEnding)
            return;

        SetBolted(true);
        await CreatureCmd.GainBlock(Creature, EntrySealBlock, ValueProp.Move, null);
    }

    internal override async Task ExecuteComboFollowUpAsync(IReadOnlyList<Creature> _)
    {
        bool wasUnbolted = !IsBolted;
        SetBolted(true);
        string anim = wasUnbolted ? "bastion_rebolt_attack" : "bastion_fortify";
        await ExecuteStoneSingleAttackAsync(anim, 0.40f, ComboFollowUpDamage, strongShake: true);
        if (Creature.IsAlive && !CombatManager.Instance.IsOverOrEnding)
        {
            await CreatureCmd.GainBlock(Creature, BulwarkPressBlock, ValueProp.Move, null);
        }
    }

    private async Task BulwarkPressMoveAsync(IReadOnlyList<Creature> _)
    {
        bool wasUnbolted = !IsBolted;
        SetBolted(true);
        string anim = wasUnbolted ? "bastion_rebolt_attack" : "bastion_fortify";
        await ExecuteStoneSingleAttackAsync(anim, 0.42f, BulwarkPressDamage);
        if (Creature.IsAlive && !CombatManager.Instance.IsOverOrEnding)
        {
            await CreatureCmd.GainBlock(Creature, BulwarkPressBlock, ValueProp.Move, null);
        }
    }

    private Task ColossusDropMoveAsync(IReadOnlyList<Creature> _)
        => ExecuteStoneSingleAttackAsync("bastion_attack_colossus", 0.48f, ColossusDropDamage, strongShake: IsBolted);
}
