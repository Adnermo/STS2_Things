using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Rooms;
using STS2_Things.Powers;

namespace STS2_Things.Encounters;

/// <summary>Event-only pair; preserves the native merchant models and reward budget.</summary>
public sealed class RobberyFakeMerchantEncounter : EncounterModel
{
    // Together the two merchants match one Act 1 elite: a third of the native HP each,
    // and ShiftyPartnersPower scales their attacks (docs/depths-balance-20261110.md).
    private const int HealthDivisor = 3;
    private bool _prepared;
    public override RoomType RoomType => RoomType.Monster;
    public override bool HasScene => true;
    protected override bool HasCustomBackground => true;
    public override IReadOnlyList<string> Slots => ["merchant_left", "merchant_right"];
    public override IEnumerable<MonsterModel> AllPossibleMonsters => [ModelDb.Monster<FakeMerchantMonster>()];
    public override int MinGoldReward => 300;
    public override int MaxGoldReward => 300;

    protected override IReadOnlyList<(MonsterModel, string?)> GenerateMonsters() =>
    [
        (ModelDb.Monster<FakeMerchantMonster>().ToMutable(), "merchant_left"),
        (ModelDb.Monster<FakeMerchantMonster>().ToMutable(), "merchant_right"),
    ];

    internal async Task PrepareForFight(ICombatState state)
    {
        AssertMutable();
        if (_prepared) return;
        // Shared events execute one callback per player against the same arena.
        // Mark the mutable encounter before awaiting, so both merchants are
        // reduced once after native multiplayer HP scaling, never once per voter.
        _prepared = true;
        foreach (var merchant in state.Enemies.Where(c => c.Monster is FakeMerchantMonster))
        {
            await CreatureCmd.SetMaxAndCurrentHp(merchant, Math.Max(1, merchant.MaxHp / HealthDivisor));
            // The fight runs in this same layout state, so the power carries over.
            await PowerCmd.Apply<ShiftyPartnersPower>(new ThrowingPlayerChoiceContext(), merchant, 1, merchant, null);
        }
    }
}
