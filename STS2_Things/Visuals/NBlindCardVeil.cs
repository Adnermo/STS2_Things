using Godot;
using HarmonyLib;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Rooms;
using STS2_Things.Cards;

namespace STS2_Things.Visuals;

/// <summary>Local visual clock; never reads gameplay RNG or modifies card text/models.</summary>
public partial class NBlindCardVeil : Node
{
    private const string ChildName = "LanternBlindVeil";
    private const string Glyphs = "#%&?+=/*01@$!:;";
    private NCard _card = null!;
    private CardModel? _boundModel;
    private MegaLabel? _title;
    private MegaRichTextLabel? _description;
    private readonly List<ColorRect> _portraits = [];
    private readonly List<(TextureRect Portrait, Material? SavedMaterial)> _savedPortraits = [];
    private bool _active;
    private double _clock;
    private int _frame = -1;
    private CardPreviewMode _previewMode;

    public static void RefreshOnTable(CardModel model)
    {
        if (NCard.FindOnTable(model) is { } node &&
            GodotObject.IsInstanceValid(node) &&
            (LanternBlindness.IsVeiled(model) || node.HasNode(ChildName)))
        {
            node.UpdateVisuals(node.DisplayingPile, CardPreviewMode.Normal);
        }
    }

    public static void Refresh(NCard card, CardPreviewMode mode)
    {
        if (!GodotObject.IsInstanceValid(card) || !card.IsNodeReady())
            return;

        var veil = card.GetNodeOrNull<NBlindCardVeil>(ChildName);
        bool shouldVeil = LanternBlindness.IsVeiled(card.Model);
        if (!shouldVeil)
        {
            if (veil != null)
            {
                if (veil._boundModel != card.Model)
                {
                    veil.DetachAndFree(restorePortraitMaterials: false);
                    return;
                }
                veil._previewMode = mode;
                veil.RefreshVisuals(nativeRefreshed: true);
            }
            return;
        }

        if (veil == null)
        {
            veil = new NBlindCardVeil
            {
                Name = ChildName,
                _card = card,
                _boundModel = card.Model,
                _previewMode = mode,
            };
            card.AddChild(veil);
            return;
        }

        veil._boundModel = card.Model;
        veil._previewMode = mode;
        veil.SetProcess(true);
        veil.RefreshVisuals(nativeRefreshed: true);
    }

    public static void DetachFromCard(NCard card, bool restorePortraitMaterials)
    {
        if (!GodotObject.IsInstanceValid(card))
            return;
        if (card.GetNodeOrNull<NBlindCardVeil>(ChildName) is { } veil)
            veil.DetachAndFree(restorePortraitMaterials);
    }

    public override void _EnterTree()
    {
        base._EnterTree();
        CombatManager.Instance.CombatEnded += OnCombatEnded;
        if (GodotObject.IsInstanceValid(_card))
        {
            _card.ModelChanged -= OnCardModelChanged;
            _card.ModelChanged += OnCardModelChanged;
        }
    }

    public override void _ExitTree()
    {
        CombatManager.Instance.CombatEnded -= OnCombatEnded;
        if (GodotObject.IsInstanceValid(_card))
            _card.ModelChanged -= OnCardModelChanged;
        base._ExitTree();
    }

    public override void _Ready()
    {
        if (!GodotObject.IsInstanceValid(_card))
            return;

        _title = _card.GetNodeOrNull<MegaLabel>("%TitleLabel");
        _description = _card.GetNodeOrNull<MegaRichTextLabel>("%DescriptionLabel");
        foreach (string name in new[] { "%Portrait", "%AncientPortrait" })
        {
            if (_card.GetNodeOrNull<TextureRect>(name) is not { } portrait) continue;
            _savedPortraits.Add((portrait, portrait.Material));
            var rect = new ColorRect
            {
                Name = "BlindStatic", MouseFilter = Control.MouseFilterEnum.Ignore,
                Material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/cards/lantern_blindness.gdshader") },
                Visible = false
            };
            portrait.AddChild(rect);
            rect.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            _portraits.Add(rect);
        }
        RefreshVisuals(true);
        DismissStaleHolderHoverTips();
    }

    private void DismissStaleHolderHoverTips()
    {
        if (!GodotObject.IsInstanceValid(_card))
            return;

        Node? cursor = _card.GetParent();
        for (int depth = 0; depth < 4 && cursor != null; depth++, cursor = cursor.GetParent())
        {
            if (cursor is NCardHolder holder && GodotObject.IsInstanceValid(holder))
            {
                NHoverTipSet.Remove(holder);
                break;
            }
        }
    }

    public override void _Process(double delta)
    {
        _clock += delta;
        RefreshVisuals(false);
    }

    private void OnCombatEnded(CombatRoom _)
    {
        if (!GodotObject.IsInstanceValid(this))
            return;

        bool wasActive = _active;
        DetachAndFree(restorePortraitMaterials: false);
        if (wasActive && GodotObject.IsInstanceValid(_card) && _card.IsInsideTree() && _card.Model != null)
            _card.UpdateVisuals(_card.DisplayingPile, _previewMode);
    }

    private void OnCardModelChanged(CardModel? _)
    {
        if (!GodotObject.IsInstanceValid(this) || !GodotObject.IsInstanceValid(_card))
            return;

        if (_card.Model != _boundModel && !LanternBlindness.IsVeiled(_card.Model))
        {
            // Never call UpdateVisuals with a stale _previewMode from an earlier card.
            DetachAndFree(restorePortraitMaterials: _card.Model == null);
        }
    }

    private void DetachAndFree(bool restorePortraitMaterials)
    {
        CombatManager.Instance.CombatEnded -= OnCombatEnded;
        if (GodotObject.IsInstanceValid(_card))
            _card.ModelChanged -= OnCardModelChanged;
        SetProcess(false);
        _active = false;
        _frame = -1;

        foreach (ColorRect rect in _portraits)
        {
            if (!GodotObject.IsInstanceValid(rect))
                continue;
            rect.Visible = false;
            Node? parent = rect.GetParent();
            if (parent != null && GodotObject.IsInstanceValid(parent) && NGame.IsMainThread() &&
                (parent.IsNodeReady() || !parent.IsInsideTree()))
            {
                parent.RemoveChild(rect);
            }
            rect.QueueFree();
        }
        _portraits.Clear();

        if (restorePortraitMaterials && GodotObject.IsInstanceValid(_card))
        {
            foreach ((TextureRect portrait, Material? savedMaterial) in _savedPortraits)
            {
                if (GodotObject.IsInstanceValid(portrait))
                    portrait.Material = savedMaterial;
            }
        }
        _savedPortraits.Clear();

        Node? veilParent = GetParent();
        if (veilParent != null && GodotObject.IsInstanceValid(veilParent) && NGame.IsMainThread() &&
            (veilParent.IsNodeReady() || !veilParent.IsInsideTree()))
        {
            veilParent.RemoveChild(this);
        }
        QueueFree();
    }

    private void RefreshVisuals(bool nativeRefreshed)
    {
        if (!GodotObject.IsInstanceValid(_card))
            return;

        if (_card.Model != _boundModel && !LanternBlindness.IsVeiled(_card.Model))
        {
            DetachAndFree(restorePortraitMaterials: _card.Model == null);
            return;
        }

        _boundModel = _card.Model;
        bool active = LanternBlindness.IsVeiled(_card.Model);
        bool changed = _active != active;
        _active = active;
        foreach (var rect in _portraits)
        {
            if (GodotObject.IsInstanceValid(rect))
                rect.Visible = active;
        }
        if (!active)
        {
            _frame = -1;
            SetProcess(false);
            if (changed && !nativeRefreshed && _card.Model != null)
                _card.UpdateVisuals(_card.DisplayingPile, _previewMode);
            return;
        }
        SetProcess(true);
        int frame = (int)(_clock * 8);
        if (!nativeRefreshed && frame == _frame) return;
        _frame = frame;
        // Stable lengths avoid layout jitter. BBCode delimiters are never generated.
        _title?.SetTextAutoSize(Scramble(frame, 8, 17));
        _description?.SetTextAutoSize("[center]" + Scramble(frame, 13, 71) + "\n" +
            Scramble(frame, 12, 137) + "\n" + Scramble(frame, 13, 251) + "[/center]");
    }

    private static string Scramble(int frame, int count, uint salt)
    {
        uint state = unchecked((uint)frame * 747796405u + salt);
        char[] chars = new char[count];
        for (int i = 0; i < count; i++)
        {
            state ^= state << 13; state ^= state >> 17; state ^= state << 5;
            chars[i] = Glyphs[(int)(state % Glyphs.Length)];
        }
        return new string(chars);
    }
}

[HarmonyPatch(typeof(NCard), nameof(NCard.UpdateVisuals))]
internal static class LanternBlindnessCardVisualPatch
{
    private static void Postfix(NCard __instance, CardPreviewMode previewMode) => NBlindCardVeil.Refresh(__instance, previewMode);
}

[HarmonyPatch(typeof(NCard), nameof(NCard.OnFreedToPool))]
internal static class LanternBlindnessPoolCleanupPatch
{
    private static void Postfix(NCard __instance) => NBlindCardVeil.DetachFromCard(__instance, restorePortraitMaterials: true);
}
