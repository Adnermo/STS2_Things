using Godot;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using static STS2_Things.Platform.Sts2VersionSeam;

namespace STS2_Things.Visuals;

/// <summary>
/// Lets each whip recover into the following windup while native damage hooks
/// finish. A long multiplayer power application cannot play the next hit early.
/// Uncontrolled animation previews play the complete clip without waiting.
/// </summary>
[GlobalClass]
public partial class NThingsScaleBeetleMotion : Node
{
    private MegaSprite? _sprite;
    private Callable _animStartedCallable;
    private Callable _animEventCallable;
    private bool _signalsConnected;
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

    public void AllowBeat(int beat)
    {
        _controlled = true;
        _allowedBeat = beat;
        SetWhipSpeed(1f);
    }

    public void FinishCombo()
    {
        _controlled = false;
        SetWhipSpeed(1f);
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
        int nextBeat = name switch { "whip_ready_second" => 2, "whip_ready_third" => 3, _ => 0 };
        if (nextBeat > _allowedBeat) SetWhipSpeed(0f);
    }

    private void SetWhipSpeed(float speed)
    {
        MegaAnimationState? animState = _sprite?.TryGetAnimationState();
        if (animState == null) return;
        using (TrackEntryScope(animState.GetCurrent(0), out MegaTrackEntry? track))
        {
            // A hit reaction or death takes precedence over the interrupted combo.
            if (track != null && track.GetAnimationName() == "whip") track.SetTimeScale(speed);
        }
    }
}
