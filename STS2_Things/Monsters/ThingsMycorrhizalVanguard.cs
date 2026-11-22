using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;

namespace STS2_Things.Monsters;

public sealed class ThingsMycorrhizalVanguard : MycorrhizalTwin
{
    public override bool IsVanguard => true;

    private async Task WarSpores(bool furious)
    {
        await Strike(furious ? Damage(10, 12) : Damage(6, 8));
        await Strengthen(solo: furious);
    }

    protected override MonsterMoveStateMachine GenerateMoveStateMachine()
    {
        var lash = new MoveState("LASH_MOVE", _ => Strike(Damage(14, 16)), new SingleAttackIntent(Damage(14, 16)));
        var doubleHit = new MoveState("DOUBLE_MOVE", _ => Strike(Damage(7, 8), 2), new MultiAttackIntent(Damage(7, 8), 2));
        var spores = new MoveState("WAR_SPORES_MOVE", _ => WarSpores(false), new SingleAttackIntent(Damage(6, 8)), new BuffIntent());
        var fury = new MoveState("FURY_LASH_MOVE", _ => Strike(Damage(7, 8), 2), new MultiAttackIntent(Damage(7, 8), 2));
        var rageSpores = new MoveState("RAGE_SPORES_MOVE", _ => WarSpores(true), new SingleAttackIntent(Damage(10, 12)), new BuffIntent());
        fury.FollowUpState = rageSpores;
        rageSpores.FollowUpState = fury;
        var a = After("AFTER_LASH", doubleHit, fury, () => IsFurious);
        var b = After("AFTER_DOUBLE", spores, fury, () => IsFurious);
        var c = After("AFTER_SPORES", lash, fury, () => IsFurious);
        lash.FollowUpState = a; doubleHit.FollowUpState = b; spores.FollowUpState = c;
        return new MonsterMoveStateMachine([lash, doubleHit, spores, fury, rageSpores, a, b, c], lash);
    }
}
