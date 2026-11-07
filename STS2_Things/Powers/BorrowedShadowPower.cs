using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using STS2_Things.Visuals;

namespace STS2_Things.Powers;

public sealed class BorrowedShadowPower : PowerModel
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.None;
    protected override IEnumerable<IHoverTip> ExtraHoverTips => [HoverTipFactory.Static(StaticHoverTip.Block)];
    protected override object InitInternalData() => new PoisonSources();

    public override async Task AfterDamageGiven(PlayerChoiceContext choiceContext, Creature? dealer,
        DamageResult result, ValueProp props, Creature target, CardModel? cardSource)
    {
        // Unlike AfterDamageReceived, the native damage-given hook also runs
        // for lethal hits. Native GainBlock decides whether combat has ended.
        if (target != Owner || result.UnblockedDamage <= 0 || Owner.CombatState is not { } combat) return;
        if (PlayerCreature(dealer) is { } recipient)
            await GiveBlock(recipient, result.UnblockedDamage, combat);
    }

    public override Task AfterPowerAmountChanged(PlayerChoiceContext choiceContext, PowerModel power,
        decimal amount, Creature? applier, CardModel? cardSource)
    {
        if (power is PoisonPower poison && poison.Owner == Owner)
            GetInternalData<PoisonSources>().Update(poison, (int)amount, PlayerCreature(applier));
        return Task.CompletedTask;
    }

    internal async Task AfterPoisonDamage(PoisonPower poison, int hpLost, ICombatState combat)
    {
        if (hpLost <= 0 || CombatManager.Instance.IsOverOrEnding) return;
        var sources = GetInternalData<PoisonSources>();
        sources.Update(poison);
        var shares = sources.Split(hpLost);
        foreach (var player in combat.Players)
            if (shares.TryGetValue(player.Creature, out int block))
                await GiveBlock(player.Creature, block, combat);
    }

    private static Creature? PlayerCreature(Creature? source) => (source?.Player ?? source?.PetOwner)?.Creature;

    private async Task GiveBlock(Creature recipient, int amount, ICombatState combat)
    {
        if (!recipient.IsAlive || recipient.CombatState != combat || CombatManager.Instance.IsOverOrEnding) return;
        Flash();
        NFleetingShadowVfx.Play(Owner, recipient);
        await CreatureCmd.GainBlock(recipient, amount, ValueProp.Unpowered, null, fast: true);
    }

    private sealed class PoisonSources
    {
        private sealed class Batch(Creature? recipient, int stacks)
        {
            public Creature? Recipient { get; } = recipient;
            public int Stacks { get; set; } = stacks;
        }

        private readonly Queue<Batch> _batches = new();
        private PoisonPower? _power;
        private int _total;

        public void Update(PoisonPower poison, int applied = 0, Creature? recipient = null)
        {
            // The native counter is shared. Replacing the power (e.g. after a cleanse)
            // must discard old credit; decay consumes the oldest stacks first.
            if (_power != poison)
            {
                _power = poison;
                _total = 0;
                _batches.Clear();
            }

            int next = Math.Max(0, poison.Amount);
            int delta = next - _total;
            if (delta > 0)
            {
                int tracked = Math.Clamp(applied, 0, delta);
                if (delta > tracked) _batches.Enqueue(new Batch(null, delta - tracked));
                if (tracked > 0) _batches.Enqueue(new Batch(recipient, tracked));
            }
            else
            {
                int remove = -delta;
                while (remove > 0 && _batches.TryPeek(out var batch))
                {
                    int consumed = Math.Min(remove, batch.Stacks);
                    batch.Stacks -= consumed;
                    remove -= consumed;
                    if (batch.Stacks == 0) _batches.Dequeue();
                }
            }
            _total = next;
        }

        public Dictionary<Creature, int> Split(int hpLost)
        {
            var shares = new Dictionary<Creature, int>();
            int seen = 0;
            int previous = 0;
            foreach (var batch in _batches)
            {
                seen += batch.Stacks;
                // Cumulative rounding keeps the total at most the actual HP lost,
                // including when modifiers or overkill make it less than the stack count.
                int through = (int)((long)hpLost * seen / _total);
                int share = through - previous;
                previous = through;
                if (share > 0 && batch.Recipient is { } player)
                    shares[player] = shares.GetValueOrDefault(player) + share;
            }
            return shares;
        }
    }
}
