using Godot;
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
    private readonly List<Vector2> _sources = [];
    private readonly List<BurstShard> _burstShards = [];
    private Creature? _owner;
    private Vector2 _burstOrigin;
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

        NArmorSiphonVfx vfx = new()
        {
            _owner = owner,
            ZIndex = 35,
        };
        foreach (Creature source in sources)
        {
            if (Sts2VersionSeam.GetCreatureNode(source) is { } srcNode)
                vfx._sources.Add(srcNode.VfxSpawnPosition);
        }
        if (vfx._sources.Count == 0)
            return;

        host.AddChildSafely(vfx);
    }

    /// <summary>Spawns faceted cyan crystal fragments bursting away when dorsal armor plates shatter.</summary>
    public static void SpawnShatterBurst(Creature owner, int shardCount = 6)
    {
        NCreature? ownerNode = Sts2VersionSeam.GetCreatureNode(owner);
        Node? host = NCombatRoom.Instance?.CombatVfxContainer ?? ownerNode?.GetParent();
        if (host == null || ownerNode == null || shardCount <= 0)
            return;

        Marker2D? center = ownerNode.Visuals?.GetNodeOrNull<Marker2D>("%CenterPos");
        Vector2 origin = center?.GlobalPosition ?? ownerNode.VfxSpawnPosition;
        NArmorSiphonVfx vfx = new()
        {
            _isBurst = true,
            _burstOrigin = origin,
            ZIndex = 35,
        };

        const float golden = 2.3999632f;
        for (int i = 0; i < shardCount; i++)
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

        foreach (Vector2 startGlobal in _sources)
        {
            Vector2 start = ToLocal(startGlobal);
            Vector2 delta = mouthPos - start;
            Vector2 normal = delta.LengthSquared() > 1f ? delta.Normalized().Orthogonal() : Vector2.Up;

            for (int i = 0; i < 6; i++)
            {
                float tShard = Mathf.Clamp((progress - i * 0.06f) / 0.56f, 0f, 1f);
                if (tShard <= 0f || tShard >= 1f)
                    continue;
                float eased = tShard * tShard;
                float sway = Mathf.Sin(tShard * Mathf.Pi) * ((i % 2 == 0 ? 1f : -1f) * (24f + i * 5f));
                Vector2 pos = start.Lerp(mouthPos, eased) + normal * sway;
                float radius = (11f - i * 0.8f) * (1f - 0.35f * tShard);
                DrawFacetedShard(pos, delta.Angle(), radius, alphaEnvelope);
            }
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
