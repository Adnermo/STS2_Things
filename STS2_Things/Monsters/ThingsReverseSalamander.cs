using MegaCrit.Sts2.Core.Animation;
using MegaCrit.Sts2.Core.Audio;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
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
using MegaCrit.Sts2.Core.Nodes.Vfx;
using MegaCrit.Sts2.Core.ValueProps;
using STS2_Things.Afflictions;
using STS2_Things.Powers;
using static STS2_Things.Platform.Sts2VersionSeam;

namespace STS2_Things.Monsters;

public sealed class ThingsReverseSalamander : ThingsSpineMonster
{
    public override int MinInitialHp => AscensionHelper.GetValueIfAscension(AscensionLevel.ToughEnemies,173,155);
    public override int MaxInitialHp => MinInitialHp;
    public override float HpBarSizeReduction => 10f;
    public int GrowthAmount => AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies,3,2);
    public int UnplayedRecallBlock => AscensionHelper.GetValueIfAscension(AscensionLevel.ToughEnemies,14,10);
    private int TideDamage => AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies,19,16);
    private int TailDamage => AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies,23,21);
    private int RollDamage => AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies,8,7);
    private int GatherDamage => AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies,17,14);
    private int GatherBlock => AscensionHelper.GetValueIfAscension(AscensionLevel.ToughEnemies,13,10);
    public override DamageSfxType TakeDamageSfxType => DamageSfxType.Magic;
    protected override string AttackSfx => "event:/sfx/enemy/enemy_attacks/gravetide_slug/gravetide_slug_attack_light";
    public override string DeathSfx => "event:/sfx/enemy/enemy_attacks/gravetide_slug/gravetide_slug_die";
    public override IEnumerable<string> AssetPaths => base.AssetPaths.Concat([
        ModelDb.Power<ThingsReverseCurrentPower>().ResolvedBigIconPath,
        ModelDb.Affliction<ThingsUpstreamRecall>().OverlayPath,NSplashVfx.scenePath,
        "res://sfx/gravetide_slug/gravetide_slug_attack_light-01.wav",
        "res://sfx/gravetide_slug/gravetide_slug_attack_light-02.wav",
        "res://sfx/gravetide_slug/gravetide_slug_attack_light-03.wav",
        "res://sfx/gravetide_slug/gravetide_slug_die-01.wav",
        "res://sfx/gravetide_slug/gravetide_slug_die-02.wav"]);

    public override async Task BeforeCombatStart()
    {
        await PowerCmd.Apply<ThingsReverseCurrentPower>(new ThrowingPlayerChoiceContext(),Creature,GrowthAmount,Creature,null);
    }

    protected override void AddExtraAnimationStates(CreatureAnimator animator,AnimState idle)
    {
        foreach(var(trigger,name) in new[]{("Tide","tide"),("Roll","roll"),("Gather","gather"),("Recall","recall")})
            animator.AddAnyState(trigger,new AnimState(name){NextState=idle});
    }

    protected override MonsterMoveStateMachine GenerateMoveStateMachine()
    {
        var opening=new MoveState("UPSTREAM_MOVE",_=>Strike(TideDamage,"Tide","tide",[.56f],1.55f),new SingleAttackIntent(TideDamage));
        var tail=new MoveState("TAIL_WAVE_MOVE",_=>Strike(TailDamage,"Attack","attack",[.50f],1.40f),new SingleAttackIntent(TailDamage));
        var roll=new MoveState("BACKWASH_MOVE",_=>Strike(RollDamage,"Roll","roll",[.46f,.74f,1.02f],1.65f),new MultiAttackIntent(RollDamage,3));
        var gather=new MoveState("GATHER_MOVE",Gather,new SingleAttackIntent(GatherDamage),new DefendIntent(),new DebuffIntent());
        opening.FollowUpState=tail;tail.FollowUpState=roll;roll.FollowUpState=gather;gather.FollowUpState=tail;
        return new MonsterMoveStateMachine([opening,tail,roll,gather],opening);
    }

    private async Task Strike(int damage,string trigger,string animation,float[] contacts,float duration)
    {
        int hit=0;
        await DamageCmd.Attack(damage).WithHitCount(contacts.Length).FromMonster(this)
            .WithNoAttackerAnim().AfterAttackerAnim(async()=>
            {
                if(hit==0)await CreatureCmd.TriggerAnim(Creature,trigger,0);
                float moment=contacts[Math.Min(hit,contacts.Length-1)];
                await WaitForPose(animation,moment,hit==0?moment:.28f);hit++;
            })
            .WithAttackerFx(null,AttackSfx)
            .WithHitVfxNode(target=>target.GetCreatureNode() is { } node
                ? NSplashVfx.Create(node.VfxSpawnPosition,new Godot.Color("a1d2ce")):null)
            .Execute(null);
        await WaitForPose(animation,duration,0);
    }

    private async Task Gather(IReadOnlyList<Creature> targets)
    {
        await Strike(GatherDamage,"Gather","gather",[.56f],1.20f);
        if(!Creature.IsAlive)return;
        await CreatureCmd.GainBlock(Creature,GatherBlock,ValueProp.Move,null);
        Creature.GetPower<ThingsReverseCurrentPower>()?.FlashSurge();
        foreach(var target in targets.Where(t=>t.IsAlive&&t.Player!=null).ToArray())
        {
            if(!Creature.IsAlive||CombatManager.Instance.IsOverOrEnding)return;
            await PowerCmd.Apply<ThingsReverseSurgePower>(new ThrowingPlayerChoiceContext(),target,1,Creature,null);
        }
        await WaitForPose("gather",1.55f,0);
    }

    public async Task RecallPose()
    {
        if(!Creature.IsAlive)return;
        await CreatureCmd.TriggerAnim(Creature,"Recall",0);
        await WaitForPose("recall",.54f,.54f);
    }

    private Task WaitForPose(string animation,float moment,float fallback)
        => WaitForSpineMomentAsync(animation, moment, fallback);
}
