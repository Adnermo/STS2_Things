using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using STS2_Things.Powers;
using STS2_Things.Visuals;

namespace STS2_Things.Monsters;

/// <summary>Encounter-local shared state; no static cross-combat or local-UI state.</summary>
internal sealed class HumanFaceColumnGroup
{
    private readonly List<ThingsHumanFaceColumn> _members = [];
    private readonly HashSet<ThingsHumanFaceColumn> _broken = [];
    private readonly HashSet<ThingsHumanFaceColumn> _pendingStuns = [];
    private ThingsHumanFaceColumn[]? _livingCache;
    private int _livingCacheCount;
    private int _livingCacheAliveMask;
    private int _livingCacheLevelBits;
    private int _pendingLosses;
    private bool _resolving;
    public int Remaining { get; private set; } = ThingsHumanFaceColumn.InitialLayers;
    public int Reserve { get; private set; } = ThingsHumanFaceColumn.InitialLayers - 3;

    private void InvalidateLivingCache() => _livingCache = null;

    public ThingsHumanFaceColumn[] Living
    {
        get
        {
            int count = _members.Count;
            int aliveMask = 0;
            int levelBits = 0;
            int livingCount = 0;
            for (int i = 0; i < count; i++)
            {
                ThingsHumanFaceColumn member = _members[i];
                levelBits = (levelBits << 3) | (member.Level & 7);
                if (member.Creature.IsAlive)
                {
                    aliveMask |= 1 << i;
                    livingCount++;
                }
            }

            if (_livingCache != null &&
                _livingCacheCount == count &&
                _livingCacheAliveMask == aliveMask &&
                _livingCacheLevelBits == levelBits)
            {
                return _livingCache;
            }

            var result = livingCount == 0 ? Array.Empty<ThingsHumanFaceColumn>() : new ThingsHumanFaceColumn[livingCount];
            int idx = 0;
            for (int i = 0; i < count; i++)
            {
                if ((aliveMask & (1 << i)) != 0)
                    result[idx++] = _members[i];
            }

            for (int i = 1; i < result.Length; i++)
            {
                ThingsHumanFaceColumn key = result[i];
                int j = i - 1;
                while (j >= 0 && result[j].Level > key.Level)
                {
                    result[j + 1] = result[j];
                    j--;
                }
                result[j + 1] = key;
            }

            _livingCache = result;
            _livingCacheCount = count;
            _livingCacheAliveMask = aliveMask;
            _livingCacheLevelBits = levelBits;
            return result;
        }
    }

    public ThingsHumanFaceColumn? BottomLiving
    {
        get
        {
            for (int i = 0; i < _members.Count; i++)
            {
                ThingsHumanFaceColumn member = _members[i];
                if (member.Level == 0 && member.Creature.IsAlive)
                    return member;
            }
            return null;
        }
    }

    public static IReadOnlyList<(MonsterModel, string?)> CreateInitial()
    {
        var group = new HumanFaceColumnGroup();
        var result = new List<(MonsterModel, string?)>();
        for (int level = 2; level >= 0; level--)
        {
            var member = (ThingsHumanFaceColumn)ModelDb.Monster<ThingsHumanFaceColumn>().ToMutable();
            member.Configure(group, level, 2 - level);
            group._members.Add(member);
            result.Add((member, ThingsHumanFaceColumn.LayerSlots[level]));
        }
        return result;
    }

    public static HumanFaceColumnGroup ForStandalone(ThingsHumanFaceColumn member)
    {
        var group = new HumanFaceColumnGroup { Remaining = 1, Reserve = 0 };
        group._members.Add(member);
        return group;
    }

    public async Task BreakLayer(ThingsHumanFaceColumn broken, PlayerChoiceContext context)
    {
        if (!_broken.Add(broken)) return;
        // Capture identity before compacting the stack. In a simultaneous
        // bottom/middle kill this reference can die too; neither the old top
        // nor a new replacement should inherit its stun.
        var originalMiddle = broken.Level == 0
            ? _members.FirstOrDefault(member => member.Level == 1) : null;
        if (!_members.Remove(broken)) return;
        InvalidateLivingCache();
        if (originalMiddle != null) _pendingStuns.Add(originalMiddle);
        Remaining = Math.Max(0, Remaining - 1);
        _pendingLosses++;
        await ResolvePending(context, broken.Creature.CombatState);
    }

    public async Task ResolvePending(PlayerChoiceContext context, ICombatState? combat)
    {
        // Native AoE applies all HP loss before resolving each death. Wait for
        // those confirmations, including possible death prevention, so a living
        // disc never falls through a zero-HP layer still awaiting its death hook.
        if (_resolving || _pendingLosses == 0 || combat == null || CombatManager.Instance.IsOverOrEnding ||
            _members.Any(member => member.Creature.IsDead)) return;
        _resolving = true;
        try
        {
            int losses = _pendingLosses;
            _pendingLosses = 0;
            var stunTargets = _pendingStuns.ToArray();
            _pendingStuns.Clear();
            var survivors = Living;
            int replacements = Math.Min(Reserve, losses);
            var poses = Enumerable.Range(0, Reserve)
                .Select(index => NHumanFaceColumnVisuals.IncomingPose(survivors.LastOrDefault()?.Creature, index)).ToArray();
            Reserve -= replacements;
            for (int level = 0; level < survivors.Length; level++) survivors[level].LowerTo(level);
            InvalidateLivingCache();
            for (int i = 0; i < replacements; i++)
            {
                int level = Living.Length;
                var replacement = (ThingsHumanFaceColumn)ModelDb.Monster<ThingsHumanFaceColumn>().ToMutable();
                replacement.Configure(this, level, 0, replacement: true);
                _members.Add(replacement);
                InvalidateLivingCache();
                string slotName = ThingsHumanFaceColumn.LayerSlots[level];
                string? validSlot = combat.Encounter?.Slots.Contains(slotName) == true ? slotName : null;
                await CreatureCmd.Add(replacement, combat, CombatSide.Enemy, validSlot);
                NHumanFaceColumnVisuals.DropIn(replacement.Creature, poses[i], losses, i);
            }
            NHumanFaceColumnVisuals.RestoreReservePoses(Living.LastOrDefault()?.Creature,
                poses.Skip(replacements).ToArray());
            foreach (var member in Living)
            {
                if (member.Creature.GetPower<ThingsHumanFaceColumnPower>() is { } power)
                {
                    int difference = Remaining - power.Amount;
                    if (difference != 0)
                        await PowerCmd.ModifyAmount(context, power, difference, member.Creature, null, silent: true);
                    power.RefreshHeightDisplay();
                }
                if (stunTargets.Contains(member)) await member.StunForCollapse();
            }
            await Cmd.Wait(.32f * MathF.Sqrt(losses) + .23f);
        }
        finally { _resolving = false; }
        // Reactions to spawning or changing a power can produce another death.
        // Its confirmed loss is queued above and resolved after this pass.
        if (_pendingLosses > 0) await ResolvePending(context, combat);
    }
}
