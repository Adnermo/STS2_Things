using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
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
    private const string ShaderPath = "res://shaders/cards/lantern_blindness.gdshader";
    private const string Glyphs = "#%&?+=/*01@$!:;";
    private const int ScrambleTableSize = 16;
    private const double ScrambleHz = 4.5;

    private static readonly string[] PortraitNodeNames = ["%Portrait", "%AncientPortrait"];
    private static readonly string[] ScrambledTitles = BuildScrambledTitles();
    private static readonly string[] ScrambledDescriptions = BuildScrambledDescriptions();

    private static readonly HashSet<NBlindCardVeil> ActiveVeils = [];
    private static readonly ConditionalWeakTable<NCard, NBlindCardVeil> VeilsByCard = new();

    private static Shader? _cachedShader;
    private static ShaderMaterial? _sharedMaterial;

    private NCard _card = null!;
    private CardModel? _boundModel;
    private MegaLabel? _title;
    private MegaRichTextLabel? _description;
    private readonly List<ColorRect> _portraits = [];
    private readonly List<(TextureRect Portrait, Material? SavedMaterial)> _savedPortraits = [];
    private bool _active;
    private bool _detaching;
    private bool _textSized;
    private double _clock;
    private int _frame = -1;
    private int _scrambleOffset;
    private CardPreviewMode _previewMode;

    internal static bool HasActiveVeils => ActiveVeils.Count > 0;

    private static string[] BuildScrambledTitles()
    {
        var table = new string[ScrambleTableSize];
        for (int i = 0; i < ScrambleTableSize; i++)
            table[i] = Scramble(i, 8, 17);
        return table;
    }

    private static string[] BuildScrambledDescriptions()
    {
        var table = new string[ScrambleTableSize];
        for (int i = 0; i < ScrambleTableSize; i++)
        {
            table[i] = "[center]" + Scramble(i, 13, 71) + "\n" +
                       Scramble(i, 12, 137) + "\n" + Scramble(i, 13, 251) + "[/center]";
        }
        return table;
    }

    private static ShaderMaterial? GetSharedMaterial()
    {
        if (_sharedMaterial != null && GodotObject.IsInstanceValid(_sharedMaterial) &&
            _sharedMaterial.Shader != null && GodotObject.IsInstanceValid(_sharedMaterial.Shader))
        {
            return _sharedMaterial;
        }

        if (_cachedShader == null || !GodotObject.IsInstanceValid(_cachedShader))
            _cachedShader = GD.Load<Shader>(ShaderPath);

        if (_cachedShader == null || !GodotObject.IsInstanceValid(_cachedShader))
            return null;

        _sharedMaterial = new ShaderMaterial { Shader = _cachedShader };
        return _sharedMaterial;
    }

    private static NBlindCardVeil? FindVeil(NCard card)
    {
        if (ActiveVeils.Count == 0)
            return null;

        if (VeilsByCard.TryGetValue(card, out NBlindCardVeil? veil))
        {
            if (GodotObject.IsInstanceValid(veil) && !veil._detaching && !veil.IsQueuedForDeletion())
                return veil;
            VeilsByCard.Remove(card);
        }

        return null;
    }

    public static void RefreshOnTable(CardModel model)
    {
        if (!LanternBlindness.HasPotentialState && ActiveVeils.Count == 0)
            return;

        if (NCard.FindOnTable(model) is { } node &&
            GodotObject.IsInstanceValid(node) &&
            (LanternBlindness.IsVeiled(model) || FindVeil(node) != null))
        {
            node.UpdateVisuals(node.DisplayingPile, CardPreviewMode.Normal);
        }
    }

    public static void Refresh(NCard card, CardPreviewMode mode)
    {
        if (!LanternBlindness.HasPotentialState && ActiveVeils.Count == 0)
            return;

        if (!GodotObject.IsInstanceValid(card) || !card.IsNodeReady())
            return;

        NBlindCardVeil? veil = FindVeil(card);
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
                _scrambleOffset = card.GetHashCode() & (ScrambleTableSize - 1),
            };
            ActiveVeils.Add(veil);
            VeilsByCard.AddOrUpdate(card, veil);
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

        if (VeilsByCard.TryGetValue(card, out NBlindCardVeil? mapped))
        {
            VeilsByCard.Remove(card);
            if (GodotObject.IsInstanceValid(mapped) && !mapped._detaching)
                mapped.DetachAndFree(restorePortraitMaterials);
        }

        // Always scan direct children without relying on ActiveVeils/VeilsByCard:
        // _ExitTree() clears both registries before deferred NCard.OnFreedToPool() executes.
        for (int i = card.GetChildCount() - 1; i >= 0; i--)
        {
            Node child = card.GetChild(i);
            if (child is not NBlindCardVeil veil || !GodotObject.IsInstanceValid(veil))
                continue;

            if (!veil._detaching)
            {
                veil.DetachAndFree(restorePortraitMaterials);
            }
            else
            {
                ActiveVeils.Remove(veil);
                if (veil._card != null && VeilsByCard.TryGetValue(veil._card, out NBlindCardVeil? current) && ReferenceEquals(current, veil))
                    VeilsByCard.Remove(veil._card);
                if (ReferenceEquals(veil.GetParent(), card))
                {
                    if (NGame.IsMainThread() && (card.IsNodeReady() || !card.IsInsideTree()))
                        card.RemoveChild(veil);
                    else
                        card.CallDeferred(Node.MethodName.RemoveChild, veil);
                }
                if (!veil.IsQueuedForDeletion())
                    veil.QueueFree();
            }
        }

        // Scrub any stray BlindStatic overlays attached under %Portrait / %AncientPortrait.
        foreach (string portraitName in PortraitNodeNames)
        {
            if (card.GetNodeOrNull<TextureRect>(portraitName) is not { } portrait ||
                !GodotObject.IsInstanceValid(portrait))
            {
                continue;
            }

            for (int i = portrait.GetChildCount() - 1; i >= 0; i--)
            {
                Node child = portrait.GetChild(i);
                if (child is ColorRect rect && GodotObject.IsInstanceValid(rect) &&
                    rect.Name.ToString().StartsWith("BlindStatic", StringComparison.Ordinal))
                {
                    rect.Visible = false;
                    if (NGame.IsMainThread() && (portrait.IsNodeReady() || !portrait.IsInsideTree()))
                        portrait.RemoveChild(rect);
                    else
                        portrait.CallDeferred(Node.MethodName.RemoveChild, rect);
                    if (!rect.IsQueuedForDeletion())
                        rect.QueueFree();
                }
            }
        }
    }

    public override void _EnterTree()
    {
        base._EnterTree();
        if (_detaching ||
            !GodotObject.IsInstanceValid(_card) ||
            _card.Model == null ||
            !ReferenceEquals(_card.Model, _boundModel) ||
            !LanternBlindness.IsVeiled(_card.Model))
        {
            DetachAndFree(restorePortraitMaterials: false, deferRemoveChild: true);
            return;
        }

        ActiveVeils.Add(this);
        CombatManager.Instance.CombatEnded += OnCombatEnded;
        VeilsByCard.AddOrUpdate(_card, this);
        _card.ModelChanged -= OnCardModelChanged;
        _card.ModelChanged += OnCardModelChanged;
    }

    public override void _ExitTree()
    {
        ActiveVeils.Remove(this);
        if (_card != null && VeilsByCard.TryGetValue(_card, out NBlindCardVeil? mapped) && ReferenceEquals(mapped, this))
            VeilsByCard.Remove(_card);
        CombatManager.Instance.CombatEnded -= OnCombatEnded;
        if (GodotObject.IsInstanceValid(_card))
            _card.ModelChanged -= OnCardModelChanged;
        base._ExitTree();
    }

    public override void _Ready()
    {
        if (_detaching || !GodotObject.IsInstanceValid(_card))
            return;

        _title = _card.GetNodeOrNull<MegaLabel>("%TitleLabel");
        _description = _card.GetNodeOrNull<MegaRichTextLabel>("%DescriptionLabel");
        ShaderMaterial? sharedMaterial = GetSharedMaterial();
        foreach (string name in PortraitNodeNames)
        {
            if (_card.GetNodeOrNull<TextureRect>(name) is not { } portrait) continue;
            _savedPortraits.Add((portrait, portrait.Material));
            var rect = new ColorRect
            {
                Name = "BlindStatic",
                MouseFilter = Control.MouseFilterEnum.Ignore,
                Material = sharedMaterial,
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

    private void DetachAndFree(bool restorePortraitMaterials, bool deferRemoveChild = false)
    {
        if (_detaching)
            return;
        _detaching = true;

        ActiveVeils.Remove(this);
        if (_card != null && VeilsByCard.TryGetValue(_card, out NBlindCardVeil? mapped) && ReferenceEquals(mapped, this))
            VeilsByCard.Remove(_card);
        CombatManager.Instance.CombatEnded -= OnCombatEnded;
        if (GodotObject.IsInstanceValid(_card))
            _card.ModelChanged -= OnCardModelChanged;
        SetProcess(false);
        _active = false;
        _textSized = false;
        _frame = -1;

        foreach (ColorRect rect in _portraits)
        {
            if (!GodotObject.IsInstanceValid(rect))
                continue;
            rect.Visible = false;
            Node? parent = rect.GetParent();
            if (parent != null && GodotObject.IsInstanceValid(parent))
            {
                if (!deferRemoveChild && NGame.IsMainThread() && (parent.IsNodeReady() || !parent.IsInsideTree()))
                    parent.RemoveChild(rect);
                else
                    parent.CallDeferred(Node.MethodName.RemoveChild, rect);
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
        if (veilParent != null && GodotObject.IsInstanceValid(veilParent))
        {
            if (!deferRemoveChild && NGame.IsMainThread() && (veilParent.IsNodeReady() || !veilParent.IsInsideTree()))
                veilParent.RemoveChild(this);
            else
                veilParent.CallDeferred(Node.MethodName.RemoveChild, this);
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
            _textSized = false;
            SetProcess(false);
            if (changed && !nativeRefreshed && _card.Model != null)
                _card.UpdateVisuals(_card.DisplayingPile, _previewMode);
            return;
        }
        SetProcess(true);
        int frame = (int)(_clock * ScrambleHz);
        if (!nativeRefreshed && frame == _frame) return;
        _frame = frame;
        int slot = (frame + _scrambleOffset) & (ScrambleTableSize - 1);
        // Stable lengths avoid layout jitter. BBCode delimiters are never generated.
        if (nativeRefreshed || !_textSized)
        {
            _title?.SetTextAutoSize(ScrambledTitles[slot]);
            _description?.SetTextAutoSize(ScrambledDescriptions[slot]);
            _textSized = true;
        }
        else
        {
            if (_title != null && GodotObject.IsInstanceValid(_title))
                ((Label)_title).Text = ScrambledTitles[slot];
            if (_description != null && GodotObject.IsInstanceValid(_description))
                ((RichTextLabel)_description).Text = ScrambledDescriptions[slot];
        }
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
    private static void Postfix(NCard __instance, CardPreviewMode previewMode)
    {
        if (LanternBlindness.HasPotentialState || NBlindCardVeil.HasActiveVeils)
            NBlindCardVeil.Refresh(__instance, previewMode);
    }
}

[HarmonyPatch(typeof(NCard), nameof(NCard.OnFreedToPool))]
internal static class LanternBlindnessPoolCleanupPatch
{
    private static void Postfix(NCard __instance)
    {
        NBlindCardVeil.DetachFromCard(__instance, restorePortraitMaterials: true);
    }
}
