using Godot;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using static STS2_Things.Platform.Sts2VersionSeam;

namespace STS2_Things.Visuals;

/// <summary>
/// Gates multi-hit roe-fling beats on ThingsSoulRoes (`multi_bite_6` and
/// `multi_bite_4_intangible`) so slow multiplayer or power hooks never play
/// subsequent strikes early, while uncontrolled animation previews play through.
/// </summary>
[GlobalClass]
public partial class NThingsSoulRoesMotion : Node
{
    private MegaSprite? _sprite;
    private Callable _animStartedCallable;
    private Callable _animEventCallable;
    private bool _signalsConnected;
    private string _activeAnim = string.Empty;
    private int _allowedBeat;
    private bool _controlled;

    public override void _Ready()
    {
        if (GetParent() is not Node2D sprite) return;
        _sprite = new MegaSprite(sprite);
        _animStartedCallable = Callable.From<Variant, Variant, Variant>((_, _, _) =>
        {
            if (!GodotObject.IsInstanceValid(this) || !IsInsideTree()) return;
            _controlled = false;
            _allowedBeat = 0;
        });
        _animEventCallable = Callable.From<Variant, Variant, Variant, Variant>(OnEvent);
        _sprite.ConnectAnimationStarted(_animStartedCallable);
        _sprite.ConnectAnimationEvent(_animEventCallable);
        _signalsConnected = true;
    }

    public void AllowBeat(string animName, int beat)
    {
        _activeAnim = animName;
        _controlled = true;
        _allowedBeat = beat;
        SetComboSpeed(1f);
    }

    public void FinishCombo()
    {
        _controlled = false;
        SetComboSpeed(1f);
    }

    public override void _ExitTree()
    {
        if (_signalsConnected && _sprite != null && GodotObject.IsInstanceValid(_sprite.BoundObject))
        {
            if (_sprite.BoundObject.IsConnected("animation_started", _animStartedCallable))
                _sprite.DisconnectAnimationStarted(_animStartedCallable);
            if (_sprite.BoundObject.IsConnected("animation_event", _animEventCallable))
                _sprite.DisconnectAnimationEvent(_animEventCallable);
        }
        _signalsConnected = false;
        _controlled = false;
        _sprite = null;
    }

    private void OnEvent(Variant _, Variant __, Variant ___, Variant spineEvent)
    {
        if (!_controlled || !GodotObject.IsInstanceValid(this) || !IsInsideTree() || spineEvent.VariantType != Variant.Type.Object)
            return;
        string name = new MegaEvent(spineEvent).GetData().GetEventName();
        int nextBeat = name switch
        {
            "bite6_ready_2" or "bite4_ready_2" => 2,
            "bite6_ready_3" or "bite4_ready_3" => 3,
            "bite6_ready_4" or "bite4_ready_4" => 4,
            "bite6_ready_5" => 5,
            "bite6_ready_6" => 6,
            _ => 0
        };
        if (nextBeat > _allowedBeat) SetComboSpeed(0f);
    }

    private void SetComboSpeed(float speed)
    {
        MegaAnimationState? animState = _sprite?.TryGetAnimationState();
        if (animState == null) return;
        using (TrackEntryScope(animState.GetCurrent(0), out MegaTrackEntry? track))
        {
            if (track != null && track.GetAnimationName() == _activeAnim)
            {
                track.SetTimeScale(speed);
            }
        }
    }
}
