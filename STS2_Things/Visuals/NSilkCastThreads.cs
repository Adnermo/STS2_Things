using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Combat;
using STS2_Things.Platform;
using static STS2_Things.Platform.Sts2VersionSeam;

namespace STS2_Things.Visuals;

/// <summary>Three hand-painted-colour filaments follow the animated foreleg.</summary>
public partial class NSilkCastThreads : Node2D
{
    private const int MaxExtendedTargets = 16;

    [Export(PropertyHint.Range, "0,0.5,0.01")]
    public float ReleaseDelay { get; set; }
    private MegaSprite? _spine;
    private NCreature? _owner;
    private float _time = -1;
    private float _pixel;
    private bool _wasActive;
    private int _rawTargetCount;
    private readonly List<Vector2> _targets = [];
    private readonly List<(Vector2 Pos, bool IsLocal)> _candidateTargets = [];
    private readonly Vector2[] _strandPoints = new Vector2[33];
    public override void _Ready()
    {
        _spine = new MegaSprite(GetParent().GetParent());
        for (Node? node = GetParent(); node != null; node = node.GetParent())
            if (node is NCreature creature) { _owner = creature; break; }
    }
    public override void _Process(double delta)
    {
        _time = -1;
        using var scope = TrackEntryScope(_spine?.TryGetAnimationState()?.GetCurrent(0), out MegaTrackEntry? track);
        if (track?.GetAnimationName() == "cast") _time = track.GetTrackTime() - ReleaseDelay;
        bool active = _time >= .45f && _time < 1.10f;
        if (active)
        {
            _pixel = 1 / Mathf.Max(.01f, GlobalTransform.X.Length());
            _targets.Clear();
            _candidateTargets.Clear();
            if (_owner?.Entity is { IsAlive: true } entity)
            {
                foreach (var player in entity.CombatState?.Players ?? [])
                    if (player.Creature.IsAlive && player.Creature.GetCreatureNode() is { } target)
                        _candidateTargets.Add((ToLocal(target.VfxSpawnPosition), LocalContext.IsMe(player)));
                if (_candidateTargets.Count == 0 && _owner.GetParent() is { } parent)
                    foreach (var node in parent.FindChildren("*", "", true, false).OfType<NCreature>())
                        if (node.Entity is { IsPlayer: true, IsAlive: true })
                            _candidateTargets.Add((ToLocal(node.VfxSpawnPosition), LocalContext.IsMe(node.Entity)));
            }
            _rawTargetCount = _candidateTargets.Count;
            SelectTargetsForDrawing(_candidateTargets, _targets, MaxExtendedTargets);
            QueueRedraw();
        }
        else if (_wasActive)
        {
            _targets.Clear();
            _candidateTargets.Clear();
            _rawTargetCount = 0;
            QueueRedraw();
        }
        _wasActive = active;
    }
    public override void _Draw()
    {
        if (_time < .45f || _time >= 1.10f) return;
        float lead = Mathf.Clamp((_time - .45f) / .17f, 0, 1);
        float tail = Mathf.Clamp((_time - .75f) / .35f, 0, 1);
        if (lead <= tail) return;
        int effectiveCount = Math.Max(_rawTargetCount, _targets.Count);
        int strandCount = 3;
        float alphaScale = 1f;
        if (MultiplayerCompat.IsExtendedParty(effectiveCount))
        {
            strandCount = effectiveCount <= 10 ? 2 : 1;
            alphaScale = Mathf.Lerp(0.86f, 0.64f, Mathf.Clamp((effectiveCount - MultiplayerCompat.VanillaMaxPlayers) / 16f, 0f, 1f));
        }

        float opacity = Mathf.Min(1, (_time - .45f) / .07f) * (1 - Mathf.Clamp((_time - .9f) / .2f, 0, 1)) * alphaScale;
        foreach (var target in _targets)
            for (int strand = 0; strand < strandCount; strand++)
            {
                for (int i = 0; i < _strandPoints.Length; i++)
                {
                    float u = Mathf.Lerp(tail, lead, i / (float)(_strandPoints.Length - 1));
                    float wave = Mathf.Sin(u * Mathf.Pi);
                    _strandPoints[i] = target * u + new Vector2(0, (-25 - strand * 13) * _pixel * wave +
                        4 * _pixel * Mathf.Sin(u * 12 - _time * 14 + strand) * wave);
                }
                DrawPolyline(_strandPoints, new Color(.22f, .35f, .31f, .64f * opacity), 4 * _pixel, true);
                DrawPolyline(_strandPoints, new Color(.81f, .90f, .70f, .90f * opacity), 1.65f * _pixel, true);
            }
    }

    private static void SelectTargetsForDrawing(
        List<(Vector2 Pos, bool IsLocal)> candidates,
        List<Vector2> destination,
        int maxCount)
    {
        if (candidates.Count <= maxCount)
        {
            for (int i = 0; i < candidates.Count; i++)
                destination.Add(candidates[i].Pos);
            return;
        }

        int localIndex = -1;
        for (int i = 0; i < candidates.Count; i++)
        {
            if (candidates[i].IsLocal)
            {
                localIndex = i;
                break;
            }
        }

        if (localIndex >= 0)
            destination.Add(candidates[localIndex].Pos);

        int remaining = maxCount - destination.Count;
        int nonLocalCount = candidates.Count - (localIndex >= 0 ? 1 : 0);
        if (remaining <= 0 || nonLocalCount <= 0)
            return;

        for (int k = 0; k < remaining; k++)
        {
            int nonLocalPick = remaining == 1
                ? nonLocalCount / 2
                : (int)MathF.Round(k * (nonLocalCount - 1) / (float)(remaining - 1));
            int actualIndex = (localIndex >= 0 && nonLocalPick >= localIndex)
                ? nonLocalPick + 1
                : nonLocalPick;
            destination.Add(candidates[actualIndex].Pos);
        }
    }
}
