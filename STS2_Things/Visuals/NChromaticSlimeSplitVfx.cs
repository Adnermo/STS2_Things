using Godot;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using STS2_Things.Platform;

namespace STS2_Things.Visuals;

/// <summary>
/// Renders Chromatic Fission rupture splashes and arcing gelatinous glob trails flying from
/// a defeated slime into each newly spawned child slot.
/// </summary>
public partial class NChromaticSlimeSplitVfx : Node2D
{
    private const float Duration = 0.56f;

    private readonly List<Vector2> _targets = [];
    private readonly List<BurstDroplet> _droplets = [];
    private Vector2 _origin;
    private Color _coreColor;
    private Color _glowColor;
    private float _elapsed;

    private readonly record struct BurstDroplet(Vector2 Velocity, float Radius, float Gravity);

    /// <summary>Spawns colored gelatinous arc streams and splash droplets between parent and children.</summary>
    public static void PlaySplitArcs(Vector2 origin, IReadOnlyList<Creature> children, string strainKey)
    {
        Node? host = NCombatRoom.Instance?.CombatVfxContainer;
        if (host == null)
        {
            foreach (Creature child in children)
            {
                host = Sts2VersionSeam.GetCreatureNode(child)?.GetParent();
                if (host != null)
                    break;
            }
        }
        if (host == null)
            return;

        (Color core, Color glow) = ResolvePalette(strainKey);
        NChromaticSlimeSplitVfx vfx = new()
        {
            _origin = origin,
            _coreColor = core,
            _glowColor = glow,
            ZIndex = 36,
        };

        foreach (Creature child in children)
        {
            if (Sts2VersionSeam.GetCreatureNode(child) is { } childNode)
            {
                Vector2 dst = childNode.Visuals?.GetNodeOrNull<Marker2D>("%CenterPos")?.GlobalPosition
                              ?? childNode.VfxSpawnPosition;
                vfx._targets.Add(dst);
            }
        }

        const int dropletCount = 14;
        for (int i = 0; i < dropletCount; i++)
        {
            float angle = -Mathf.Pi * 0.92f + Mathf.Pi * 0.84f * i / (dropletCount - 1);
            float speed = 180f + (i % 4) * 72f;
            float radius = 6f + (i % 3) * 3.5f;
            vfx._droplets.Add(new BurstDroplet(
                new Vector2(Mathf.Cos(angle) * speed, Mathf.Sin(angle) * speed - 65f),
                radius,
                520f));
        }

        host.AddChildSafely(vfx);
    }

    public override void _Process(double delta)
    {
        _elapsed += (float)delta;
        if (_elapsed >= Duration)
        {
            QueueFree();
            return;
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        Vector2 localOrigin = ToLocal(_origin);
        float t = Mathf.Clamp(_elapsed / Duration, 0f, 1f);
        float invT = 1f - t;

        // Expanding translucent chromatic shockwave ring at rupture site
        float ringProgress = Mathf.Clamp(_elapsed / 0.34f, 0f, 1f);
        if (ringProgress < 1f)
        {
            float radius = Mathf.Lerp(26f, 148f, 1f - Mathf.Pow(1f - ringProgress, 2.2f));
            float ringAlpha = (1f - ringProgress) * 0.65f;
            DrawArc(localOrigin, radius, 0f, Mathf.Tau, 40, new Color(_glowColor, ringAlpha), 7f * (1f - ringProgress) + 2f);
        }

        // Arcing split blobs + trailing wake flying into each newly spawned slime slot
        float arcT = Mathf.Clamp(_elapsed / 0.44f, 0f, 1f);
        if (arcT < 1f)
        {
            float smooth = arcT * arcT * (3f - 2f * arcT);
            foreach (Vector2 dst in _targets)
            {
                Vector2 localDst = ToLocal(dst);
                Vector2 control = (localOrigin + localDst) * 0.5f + new Vector2(0f, -155f);
                const int samples = 7;
                for (int s = samples - 1; s >= 0; s--)
                {
                    float u = Mathf.Clamp(smooth - s * 0.045f, 0f, 1f);
                    Vector2 pos = QuadraticBezier(localOrigin, control, localDst, u);
                    float tailFade = (1f - s / (float)samples) * (1f - Mathf.Pow(arcT, 3f));
                    float r = Mathf.Lerp(20f, 5f, s / (float)samples);
                    DrawCircle(pos, r * 1.45f, new Color(_glowColor, tailFade * 0.38f));
                    DrawCircle(pos, r, new Color(_coreColor, tailFade * 0.88f));
                    DrawCircle(pos - new Vector2(r * 0.25f, r * 0.25f), r * 0.35f, new Color(Colors.White, tailFade * 0.65f));
                }
            }
        }

        // Radial gelatin splatter droplets
        float dropFade = Mathf.Clamp(invT * 1.35f, 0f, 1f);
        foreach (BurstDroplet d in _droplets)
        {
            Vector2 pos = localOrigin + d.Velocity * _elapsed + new Vector2(0f, 0.5f * d.Gravity * _elapsed * _elapsed);
            DrawCircle(pos, d.Radius * 1.35f, new Color(_glowColor, dropFade * 0.32f));
            DrawCircle(pos, d.Radius, new Color(_coreColor, dropFade * 0.85f));
        }
    }

    private static Vector2 QuadraticBezier(Vector2 p0, Vector2 p1, Vector2 p2, float u)
    {
        float o = 1f - u;
        return o * o * p0 + 2f * o * u * p1 + u * u * p2;
    }

    private static (Color Core, Color Glow) ResolvePalette(string strainKey) => strainKey switch
    {
        "crimson" => (new Color(0.95f, 0.16f, 0.34f), new Color(1.0f, 0.55f, 0.66f)),
        "amber" => (new Color(0.96f, 0.62f, 0.12f), new Color(1.0f, 0.86f, 0.42f)),
        _ => (new Color(0.10f, 0.84f, 0.72f), new Color(0.58f, 1.0f, 0.92f)),
    };
}
