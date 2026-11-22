using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.ValueProps;

namespace STS2_Things.Monsters;

public sealed class ThingsMycorrhizalBulwark : MycorrhizalTwin
{
    public override bool IsVanguard => false;

    private async Task Bash(bool furious)
    {
        await Strike(furious ? Damage(12, 14) : Damage(10, 12));
        await CreatureCmd.GainBlock(Creature, Guard, ValueProp.Move, null);
    }

    private async Task ArmorStrike(bool furious)
    {
        await Strike(furious ? Damage(8, 10) : Damage(5, 6));
        await Armor(solo: furious);
    }

    protected override MonsterMoveStateMachine GenerateMoveStateMachine()
    {
        var shelter = new MoveState("SHELTER_MOVE", Shelter, new DefendIntent(), new DebuffIntent());
        var armor = new MoveState("ARMOR_MOVE", _ => ArmorStrike(false), new SingleAttackIntent(Damage(5, 6)), new BuffIntent());
        var bash = new MoveState("BASH_MOVE", _ => Bash(false), new SingleAttackIntent(Damage(10, 12)), new DefendIntent());
        var fury = new MoveState("RAMPART_MOVE", _ => Bash(true), new SingleAttackIntent(Damage(12, 14)), new DefendIntent());
        var fortify = new MoveState("FORTIFY_MOVE", _ => ArmorStrike(true), new SingleAttackIntent(Damage(8, 10)), new DefendIntent(), new BuffIntent());
        fury.FollowUpState = fortify; fortify.FollowUpState = fury;
        var a = After("AFTER_SHELTER", armor, fury, () => IsFurious);
        var b = After("AFTER_ARMOR", bash, fury, () => IsFurious);
        var c = After("AFTER_BASH", shelter, fury, () => IsFurious);
        shelter.FollowUpState = a; armor.FollowUpState = b; bash.FollowUpState = c;
        return new MonsterMoveStateMachine([shelter, armor, bash, fury, fortify, a, b, c], shelter);
    }
}
