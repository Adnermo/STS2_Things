using System.Runtime.CompilerServices;
using Godot;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.Combat;
using STS2_Things.Monsters;

namespace STS2_Things.Visuals;

/// <summary>A painted, flexible rhizome attached to the two animated root feet.</summary>
[GlobalClass]
public partial class NMycorrhizalLink : Node2D
{
    public const string TexturePath = "res://images/vfx/mycorrhizal_root.png";
    private static readonly ConditionalWeakTable<Creature, NMycorrhizalLink> Instances = new();
    private NCreature? _host;
    private Creature? _cachedPeer;
    private NCreature? _cachedOther;
    private Node2D? _cachedFromSocket;
    private Node2D? _cachedToSocket;
    private Line2D? _root, _shadow;
    private Texture2D? _mote;
    private readonly Vector2[] _points = new Vector2[49];
    private readonly Vector2[] _shadowPoints = new Vector2[49];
    private bool _hasPoints;
    private float _age, _pulse = -1, _visibility;
    private float _appliedVisibility = -1f;
    private float _appliedWidth = -1f;
    private bool _wasPulsing;
    private bool _connected;
    public bool HasPartner => _connected;

    public static void Pulse(Creature first, Creature second)
    {
        if (Instances.TryGetValue(first, out var link) && IsInstanceValid(link)) link._pulse = 0;
    }

    public override void _Ready()
    {
        // Run after Spine and the creature's form update, not one pose behind.
        ProcessPriority = 20;
        var texture = GD.Load<Texture2D>(TexturePath);
        _mote = GD.Load<Texture2D>("res://images/vfx/dot.png");
        _shadow = MakeLine("RootShadow", texture, 27, new Color(.07f, .08f, .045f, .24f));
        _root = MakeLine("PaintedRoot", texture, 24, Colors.White);
        AddChild(_shadow); AddChild(_root);
    }

    private static Line2D MakeLine(string name, Texture2D texture, float width, Color color) => new()
    {
        Name = name, Texture = texture, TextureMode = Line2D.LineTextureMode.Stretch,
        Width = width, DefaultColor = color, Antialiased = true,
        JointMode = Line2D.LineJointMode.Round, RoundPrecision = 8,
        TextureFilter = TextureFilterEnum.Linear,
    };

    public override void _ExitTree()
    {
        if (_host?.Entity is { } entity && Instances.TryGetValue(entity, out var current) && current == this)
            Instances.Remove(entity);
        _cachedPeer = null;
        _cachedOther = null;
        _cachedFromSocket = null;
        _cachedToSocket = null;
    }

    private static NCreature? FindCreatureInParent(Node parent, Creature peer)
    {
        int count = parent.GetChildCount();
        for (int i = 0; i < count; i++)
        {
            if (parent.GetChild(i) is NCreature creature && creature.Entity == peer)
                return creature;
        }
        return null;
    }

    public override void _Process(double delta)
    {
        if (_root == null || _shadow == null) return;
        if (_host == null)
        {
            for (Node? p = GetParent(); p != null; p = p.GetParent()) if (p is NCreature n) { _host = n; break; }
            if (_host?.Entity.Monster is ThingsMycorrhizalVanguard) Instances.AddOrUpdate(_host.Entity, this);
        }
        if (_host?.Entity.Monster is not ThingsMycorrhizalVanguard twin) { Visible = false; return; }
        _age += (float)delta;
        bool isPulsing = _pulse >= 0f;
        if (isPulsing)
        {
            _pulse += (float)delta;
            if (_pulse > 1.15f)
            {
                _pulse = -1f;
                isPulsing = false;
            }
        }
        var peer = twin.Partner?.Creature;
        Node? parent = _host.GetParent();
        if (parent == null)
        {
            Visible = false;
            _connected = false;
            if (_wasPulsing)
            {
                _wasPulsing = false;
                QueueRedraw();
            }
            return;
        }

        if (_cachedFromSocket == null || !IsInstanceValid(_cachedFromSocket) || !_cachedFromSocket.IsInsideTree())
            _cachedFromSocket = _host.Visuals?.GetNodeOrNull<Node2D>("Visuals/RootSocket");

        if (peer != _cachedPeer || _cachedOther == null || !IsInstanceValid(_cachedOther) || !_cachedOther.IsInsideTree() || _cachedOther.Entity != peer)
        {
            _cachedPeer = peer;
            _cachedOther = peer == null ? null : (peer.GetCreatureNode() ?? FindCreatureInParent(parent, peer));
            _cachedToSocket = null;
        }
        if (_cachedOther != null && (_cachedToSocket == null || !IsInstanceValid(_cachedToSocket) || !_cachedToSocket.IsInsideTree()))
            _cachedToSocket = _cachedOther.Visuals?.GetNodeOrNull<Node2D>("Visuals/RootSocket");

        var from = _cachedFromSocket;
        var to = _cachedToSocket;
        _connected = from != null && to != null && _host.Entity.IsAlive && peer?.IsAlive == true;
        if (_connected)
        {
            Vector2 start = ToLocal(from!.GlobalPosition), end = ToLocal(to!.GlobalPosition);
            for (int i = 0; i < _points.Length; i++)
            {
                float t = i / (float)(_points.Length - 1), envelope = Mathf.Sin(t * Mathf.Pi);
                float breath = Mathf.Sin(_age * 1.8f - t * 4) * 1.15f;
                Vector2 point = start.Lerp(end, t) + new Vector2(0, (7 + breath) * envelope);
                _points[i] = point;
                _shadowPoints[i] = new Vector2(point.X, point.Y + 3f);
            }
            _hasPoints = true;
            _root.Points = _points;
            _shadow.Points = _shadowPoints;
        }
        _visibility = Mathf.MoveToward(_visibility, _connected ? 1 : 0, (float)delta * 3.5f);
        if (!Mathf.IsEqualApprox(_visibility, _appliedVisibility))
        {
            _appliedVisibility = _visibility;
            _root.Modulate = _shadow.Modulate = new Color(1, 1, 1, _visibility);
        }
        float surge = isPulsing ? Mathf.Sin(Mathf.Clamp(_pulse / 1.15f, 0, 1) * Mathf.Pi) : 0f;
        float desiredWidth = 24f + surge * 2.2f;
        if (!Mathf.IsEqualApprox(desiredWidth, _appliedWidth))
        {
            _appliedWidth = desiredWidth;
            _root.Width = desiredWidth;
        }
        if (isPulsing || _wasPulsing)
        {
            QueueRedraw();
        }
        _wasPulsing = isPulsing;
    }

    private Vector2 Along(float t)
    {
        float p = Mathf.Clamp(t, 0, 1) * (_points.Length - 1); int i = Math.Min((int)p, _points.Length - 2);
        return _points[i].Lerp(_points[i + 1], p - i);
    }
    public override void _Draw()
    {
        if (_pulse < 0 || !_hasPoints || _mote == null) return;
        float progress = Mathf.Clamp(_pulse / .92f, 0, 1), fade = Mathf.Sin(progress * Mathf.Pi) * _visibility;
        for (int direction = 0; direction < 2; direction++) for (int i = 0; i < 3; i++)
        {
            float t = direction == 0 ? progress - i * .018f : 1 - progress + i * .018f;
            Vector2 p = Along(t); float size = 15 - i * 3;
            Color color = new(direction == 0 ? "eed597" : "bfd9b1"); color.A = fade * (.58f - i * .12f);
            DrawTextureRect(_mote, new Rect2(p - Vector2.One * size / 2, Vector2.One * size), false, color);
        }
    }
}
