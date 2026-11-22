using System;
using System.Collections.Generic;
using Godot;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using STS2_Things.Platform;

namespace STS2_Things.Visuals;

/// <summary>
/// Crystalline cyan armor-shard siphon stream and plate shatter burst VFX for Scale-Peeler Wyrm.
/// </summary>
public partial class NArmorSiphonVfx : Node2D
{
    private const float SiphonDuration = 0.68f;
    private const float BurstDuration = 0.52f;
    private const int VanillaShardsPerSource = 6;
    private const int MaxTotalSiphonShards = 48;
    private const int MaxExtendedSiphonSources = 24;
    private const int MaxBurstShards = 48;
    private readonly List<Vector2> _sources = [];
    private readonly List<BurstShard> _burstShards = [];
    private Creature? _owner;
    private Vector2 _burstOrigin;
    private int _rawSourceCount;
    private float _elapsed;
    private bool _isBurst;

    private readonly record struct BurstShard(Vector2 Velocity, float Size, float Rotation, float Spin);

    /// <summary>Spawns convergent crystal-shard streams pulled from blocked players into the Wyrm's gullet.</summary>
    public static void PlaySiphonStream(Creature owner, IReadOnlyList<Creature> sources)
    {
        NCreature? ownerNode = Sts2VersionSeam.GetCreatureNode(owner);
        Node? host = NCombatRoom.Instance?.CombatVfxContainer ?? ownerNode?.GetParent();
        if (host == null)
            return;

        List<(Vector2 Pos, bool IsLocal)> resolved = [];
        foreach (Creature source in sources)
        {
            if (Sts2VersionSeam.GetCreatureNode(source) is { } srcNode)
                resolved.Add((srcNode.VfxSpawnPosition, LocalContext.IsMe(source)));
        }
        if (resolved.Count == 0)
            return;

        NArmorSiphonVfx vfx = new()
        {
            _owner = owner,
            _rawSourceCount = resolved.Count,
            ZIndex = 35,
        };
        SelectSiphonSources(resolved, vfx._sources, MaxExtendedSiphonSources);

        host.AddChildSafely(vfx);
    }

    private static void SelectSiphonSources(
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

        int localIndex = candidates.FindIndex(c => c.IsLocal);
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

    /// <summary>Spawns faceted cyan crystal fragments bursting away when dorsal armor plates shatter.</summary>
    public static void SpawnShatterBurst(Creature owner, int shardCount = 6)
    {
        NCreature? ownerNode = Sts2VersionSeam.GetCreatureNode(owner);
        Node? host = NCombatRoom.Instance?.CombatVfxContainer ?? ownerNode?.GetParent();
        if (host == null || ownerNode == null || shardCount <= 0)
            return;

        int clampedShards = Math.Min(shardCount, MaxBurstShards);
        Marker2D? center = ownerNode.Visuals?.GetNodeOrNull<Marker2D>("%CenterPos");
        Vector2 origin = center?.GlobalPosition ?? ownerNode.VfxSpawnPosition;
        NArmorSiphonVfx vfx = new()
        {
            _isBurst = true,
            _burstOrigin = origin,
            ZIndex = 35,
        };

        const float golden = 2.3999632f;
        for (int i = 0; i < clampedShards; i++)
        {
            float angle = -2.65f + i * golden % 4.6f;
            float speed = 175f + (i % 4) * 58f;
            Vector2 vel = new(Mathf.Cos(angle) * speed, Mathf.Sin(angle) * speed - 95f);
            float size = 9f + (i % 3) * 3.5f;
            float spin = ((i % 2 == 0) ? 1f : -1f) * (5.5f + i * 0.7f);
            vfx._burstShards.Add(new BurstShard(vel, size, i * 0.6f, spin));
        }

        host.AddChildSafely(vfx);
    }

    public override void _Process(double delta)
    {
        _elapsed += (float)delta;
        float limit = _isBurst ? BurstDuration : SiphonDuration;
        if (_elapsed >= limit)
        {
            QueueFree();
            return;
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_isBurst)
        {
            DrawBurst();
            return;
        }
        DrawSiphon();
    }

    private void DrawSiphon()
    {
        Vector2 mouthPos = ToLocal(ResolveMouthGlobalPosition());
        float progress = Mathf.Clamp(_elapsed / SiphonDuration, 0f, 1f);
        float alphaEnvelope = Mathf.SmoothStep(0f, 0.12f, progress) * (1f - Mathf.SmoothStep(0.76f, 1f, progress));
        if (alphaEnvelope <= 0.01f)
            return;

        int effectiveSources = Math.Max(_rawSourceCount, _sources.Count);
        bool isExtended = MultiplayerCompat.IsExtendedParty(effectiveSources);
        int shardsPerSource = !isExtended
            ? VanillaShardsPerSource
            : (effectiveSources <= 12 ? 3 : 2);
        float shardAlpha = !isExtended
            ? alphaEnvelope
            : alphaEnvelope * (effectiveSources <= 12 ? 0.94f : 0.86f);
        int totalShardsDrawn = 0;

        foreach (Vector2 startGlobal in _sources)
        {
            Vector2 start = ToLocal(startGlobal);
            Vector2 delta = mouthPos - start;
            Vector2 normal = delta.LengthSquared() > 1f ? delta.Normalized().Orthogonal() : Vector2.Up;

            for (int i = 0; i < shardsPerSource; i++)
            {
                if (isExtended && totalShardsDrawn >= MaxTotalSiphonShards)
                    break;

                float tShard = Mathf.Clamp((progress - i * 0.06f) / 0.56f, 0f, 1f);
                if (tShard <= 0f || tShard >= 1f)
                    continue;
                float eased = tShard * tShard;
                float sway = Mathf.Sin(tShard * Mathf.Pi) * ((i % 2 == 0 ? 1f : -1f) * (24f + i * 5f));
                Vector2 pos = start.Lerp(mouthPos, eased) + normal * sway;
                float radius = (11f - i * 0.8f) * (1f - 0.35f * tShard);
                DrawFacetedShard(pos, delta.Angle(), radius, shardAlpha);
                totalShardsDrawn++;
            }

            if (isExtended && totalShardsDrawn >= MaxTotalSiphonShards)
                break;
        }

        float flash = Mathf.Sin(Mathf.Clamp((progress - 0.36f) / 0.42f, 0f, 1f) * Mathf.Pi);
        if (flash > 0.01f)
        {
            DrawCircle(mouthPos, 28f * flash, new Color(0.55f, 0.92f, 1.0f, 0.28f * flash));
            DrawCircle(mouthPos, 13f * flash, new Color(0.86f, 0.98f, 1.0f, 0.52f * flash));
        }
    }

    private void DrawBurst()
    {
        Vector2 localBurstOrigin = ToLocal(_burstOrigin);
        float p = Mathf.Clamp(_elapsed / BurstDuration, 0f, 1f);
        float alpha = 1f - Mathf.SmoothStep(0.45f, 1f, p);
        if (alpha <= 0.01f)
            return;

        foreach (BurstShard shard in _burstShards)
        {
            Vector2 pos = localBurstOrigin + shard.Velocity * _elapsed + new Vector2(0f, 380f * _elapsed * _elapsed);
            DrawFacetedShard(pos, shard.Rotation + shard.Spin * _elapsed, shard.Size * (1f - 0.35f * p), alpha);
        }
    }

    private void DrawFacetedShard(Vector2 center, float angle, float size, float alpha)
    {
        Vector2 dir = new(Mathf.Cos(angle), Mathf.Sin(angle));
        Vector2 perp = dir.Orthogonal();
        Vector2[] poly =
        [
            center + dir * (size * 1.45f),
            center + perp * (size * 0.62f),
            center - dir * (size * 1.05f),
            center - perp * (size * 0.62f),
        ];
        DrawColoredPolygon(poly, new Color(0.46f, 0.88f, 0.96f, 0.92f * alpha));

        Vector2[] highlight =
        [
            center + dir * (size * 1.25f),
            center + perp * (size * 0.42f),
            center - dir * (size * 0.25f),
        ];
        DrawColoredPolygon(highlight, new Color(0.86f, 0.98f, 1.0f, 0.78f * alpha));
    }

    private Vector2 ResolveMouthGlobalPosition()
    {
        if (_owner == null)
            return GlobalPosition;

        NCreature? node = Sts2VersionSeam.GetCreatureNode(_owner);
        if (node == null || !GodotObject.IsInstanceValid(node))
            return GlobalPosition;

        Node2D? mouth = node.Visuals?.GetNodeOrNull<Node2D>("Visuals/MouthSocket");
        return mouth != null && GodotObject.IsInstanceValid(mouth) ? mouth.GlobalPosition : node.VfxSpawnPosition;
    }
}
