using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using STS2_Things.Platform;

namespace STS2_Things.Visuals;

public partial class NRadioWaveVfx : Node2D
{
    private const int MaxExtendedAttackWaves = 8;

    private Vector2 _origin;
    private Vector2 _target;
    private bool _shield;
    private bool _finale;
    private float _opacityScale = 1f;
    private float _age;

    public static void Attack(Creature owner, bool finale)
    {
        var room = NCombatRoom.Instance;
        var node = owner.GetCreatureNode();
        if (room == null || node == null) return;
        Vector2 origin = node.Visuals.GetNodeOrNull<Node2D>("Visuals/Voice")?.GlobalPosition ?? node.VfxSpawnPosition;
        List<(NCreature Target, bool IsLocal)> targets = [];
        foreach (var player in owner.CombatState?.Players ?? [])
            if (player.Creature.IsAlive && player.Creature.GetCreatureNode() is { } target)
                targets.Add((target, LocalContext.IsMe(player)));

        if (targets.Count == 0) return;

        float opacityScale = MultiplayerCompat.IsExtendedParty(targets.Count)
            ? Mathf.Lerp(0.85f, 0.58f, Mathf.Clamp((targets.Count - MultiplayerCompat.VanillaMaxPlayers) / 20f, 0f, 1f))
            : 1f;

        foreach (NCreature target in SelectAttackTargets(targets, MaxExtendedAttackWaves))
            room.CombatVfxContainer.AddChildSafely(new NRadioWaveVfx
            {
                _origin = origin,
                _target = target.VfxSpawnPosition,
                _finale = finale,
                _opacityScale = opacityScale
            });
    }

    private static IReadOnlyList<NCreature> SelectAttackTargets(
        List<(NCreature Target, bool IsLocal)> candidates,
        int maxCount)
    {
        if (candidates.Count <= maxCount)
            return candidates.Select(c => c.Target).ToList();

        var selected = new List<NCreature>(maxCount);
        int localIndex = candidates.FindIndex(c => c.IsLocal);
        if (localIndex >= 0)
            selected.Add(candidates[localIndex].Target);

        int remaining = maxCount - selected.Count;
        int nonLocalCount = candidates.Count - (localIndex >= 0 ? 1 : 0);
        if (remaining <= 0 || nonLocalCount <= 0)
            return selected;

        for (int k = 0; k < remaining; k++)
        {
            int nonLocalPick = remaining == 1
                ? nonLocalCount / 2
                : (int)MathF.Round(k * (nonLocalCount - 1) / (float)(remaining - 1));
            int actualIndex = (localIndex >= 0 && nonLocalPick >= localIndex)
                ? nonLocalPick + 1
                : nonLocalPick;
            selected.Add(candidates[actualIndex].Target);
        }

        return selected;
    }

    public static void Shield(Creature owner)
    {
        var room = NCombatRoom.Instance;
        var node = owner.GetCreatureNode();
        if (room == null || node == null) return;
        room.CombatVfxContainer.AddChildSafely(new NRadioWaveVfx
        { _origin = node.VfxSpawnPosition, _target = node.VfxSpawnPosition, _shield = true });
    }

    public override void _Process(double delta)
    {
        _age += (float)delta;
        if (_age >= .60f) { this.QueueFreeSafely(); return; }
        QueueRedraw();
    }

    public override void _Draw()
    {
        float t = Mathf.Clamp(_age / .60f, 0, 1);
        float opacity = Mathf.Sin(t * Mathf.Pi) * .68f * _opacityScale;
        Vector2 origin = ToLocal(_origin), target = ToLocal(_target);
        Color color = RadioColors.ForMask(_shield ? 2 : _finale ? 3 : 1);
        if (_shield)
        {
            for (int ring = 0; ring < 3; ring++)
            {
                color.A = opacity * (1 - ring * .2f);
                float radius = 130 + t * 48 + ring * 11;
                var points = Enumerable.Range(0, 65).Select(i =>
                    origin + new Vector2(Mathf.Cos(i * Mathf.Tau / 64) * radius, Mathf.Sin(i * Mathf.Tau / 64) * radius * .72f)).ToArray();
                DrawPolyline(points, color, ring == 0 ? 3.4f : 1.7f, true);
            }
            return;
        }
        float angle = (target - origin).Angle();
        for (int ring = 0; ring < 3; ring++)
        {
            float progress = Mathf.Clamp(t * 1.65f - ring * .075f, 0, 1);
            Vector2 at = origin.Lerp(target, progress);
            color.A = opacity * (1 - ring * .22f);
            float radius = (_finale ? 28 : 19) + progress * 18;
            DrawArc(at, radius, angle - .8f, angle + .8f, 22, color, ring == 0 ? 3.5f : 2, true);
        }
    }
}
