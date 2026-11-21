using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Rooms;
using STS2_Things.Cards;
using STS2_Things.Powers;

namespace STS2_Things.Visuals;

/// <summary>
/// Zero-leak unified custom abyssal-ink card veil for 【墨浊覆牌】 (Ink Blackout).
/// Covers EVERY identifiable card clue across all rarities, card types, Ancient frames/flames,
/// upgrades, Star costs, Enchantments, Afflictions, playable/gold/red highlights, flashes,
/// hover tips, pile inspection screens, and right-click card inspect screens:
///   1. Non-destructively masks every visual child inside <c>NCard.Body</c> except <c>Shadow</c>.
///   2. Renders ONE unified canonical vanilla-STS2-proportioned custom abyssal-ink card frame
///      (<c>abyssal_ink_card_frame.png</c>, <c>327x422</c> matching banner+frame bounds) driven
///      by flowing abyssal ink currents + seafoam wave-wash dissolve shader.
///   3. Renders ONE unified custom abyssal-ink energy orb (<c>abyssal_ink_energy_orb.png</c>)
///      displaying the randomized 0–3 energy cost (or X / ?) in a single uniform neutral pale-cyan
///      tint so original cost changes (green/blue) never leak.
///   4. Suppresses <see cref="CardModel.HoverTips"/> for both <see cref="AbyssalInkBlackout"/>
///      and <see cref="LanternBlindness"/> (fixing side-keyword tooltip leaks everywhere!).
/// </summary>
public partial class NInkBlackoutCardVeil : Control
{
    private const string ChildName = "AbyssalInkBlackoutVeil";
    private const string ShaderPath = "res://shaders/cards/abyssal_ink_blackout.gdshader";
    private const string FrameTexturePath = "res://images/ui/cards/abyssal_ink_card_frame.png";
    private const string OrbTexturePath = "res://images/ui/cards/abyssal_ink_energy_orb.png";

    // Canonical vanilla card bounds including the iconic sweeping TitleBanner wings (-163..+164, -211..+211).
    private static readonly Vector2 CanonicalFramePosition = new(-163.5f, -211f);
    private static readonly Vector2 CanonicalFrameSize = new(327f, 422f);

    // Canonical vanilla %EnergyIcon center (-134, -195) with crisp 70x70 custom abyssal ink gem orb.
    private static readonly Vector2 CanonicalOrbPosition = new(-169f, -230f);
    private static readonly Vector2 CanonicalOrbSize = new(70f, 70f);

    private static readonly Color NeutralCostColor = new(0.88f, 0.96f, 0.95f, 0.99f);
    private static readonly Color NeutralCostOutlineColor = new(0.03f, 0.045f, 0.09f, 1.0f);

    private static Shader? _cachedShader;
    private static Texture2D? _cachedFrameTexture;
    private static Texture2D? _cachedOrbTexture;

    // Live registry so RefreshAllForModel / WashAllForModel also reach open Draw/Discard/Exhaust
    // pile screens and grid selection screens (where NCard.FindOnTable returns null!).
    private static readonly HashSet<NInkBlackoutCardVeil> ActiveVeils = [];

    // Re-entrancy guard preventing NCard.UpdateVisuals Harmony postfixes from recursing back into
    // Refresh/DetachAndFree/StartWaveWash while a veil itself is unveiling or detaching from a card.
    private static readonly HashSet<NCard> RefreshingCards = [];

    private NCard _card = null!;
    private Control? _body;
    private CardModel? _boundModel;
    private TextureRect? _inkFrame;
    private ShaderMaterial? _inkMaterial;
    private TextureRect? _costOrb;
    private Label? _costLabel;
    private Tween? _washTween;
    private Tween? _drenchTween;
    private bool _active;
    private bool _washing;
    private bool _detaching;
    private readonly Dictionary<CanvasItem, Color> _maskedBodyChildren = [];

    public static void RefreshOnTable(CardModel model) => RefreshAllForModel(model);

    public static void WashOnTable(CardModel model) => WashAllForModel(model);

    public static void RefreshAllForModel(CardModel model)
    {
        if (NCard.FindOnTable(model) is { } tableCard && GodotObject.IsInstanceValid(tableCard))
        {
            if (AbyssalInkBlackout.IsVeiled(model) || FindVeil(tableCard) != null)
            {
                Refresh(tableCard);
            }
        }

        foreach (NInkBlackoutCardVeil veil in ActiveVeils.ToList())
        {
            if (!GodotObject.IsInstanceValid(veil) || !GodotObject.IsInstanceValid(veil._card))
            {
                ActiveVeils.Remove(veil);
                continue;
            }

            if (ReferenceEquals(veil._card.Model, model) || ReferenceEquals(veil._boundModel, model))
            {
                Refresh(veil._card);
            }
        }
    }

    public static void WashAllForModel(CardModel model)
    {
        bool startedAny = false;
        if (NCard.FindOnTable(model) is { } tableCard &&
            GodotObject.IsInstanceValid(tableCard) &&
            FindVeil(tableCard) is { } tableVeil)
        {
            tableVeil.StartWaveWash();
            startedAny = true;
        }

        foreach (NInkBlackoutCardVeil veil in ActiveVeils.ToList())
        {
            if (!GodotObject.IsInstanceValid(veil) || !GodotObject.IsInstanceValid(veil._card))
            {
                ActiveVeils.Remove(veil);
                continue;
            }

            if (ReferenceEquals(veil._card.Model, model) || ReferenceEquals(veil._boundModel, model))
            {
                veil.StartWaveWash();
                startedAny = true;
            }
        }

        if (!startedAny)
        {
            RefreshAllForModel(model);
        }
    }

    public static void Refresh(NCard card)
    {
        if (!GodotObject.IsInstanceValid(card) || !card.IsNodeReady() || RefreshingCards.Contains(card))
            return;

        NInkBlackoutCardVeil? veil = FindVeil(card);
        bool shouldVeil = AbyssalInkBlackout.IsVeiled(card.Model);

        if (!shouldVeil)
        {
            if (veil != null)
            {
                if (veil._boundModel != card.Model || !veil._washing)
                {
                    veil.DetachAndFree(refreshCardVisuals: true);
                }
            }
            return;
        }

        if (veil == null)
        {
            Control parent = ResolveHostBody(card);
            veil = new NInkBlackoutCardVeil
            {
                Name = ChildName,
                MouseFilter = MouseFilterEnum.Ignore,
                ZAsRelative = true,
                ZIndex = 6,
                _card = card,
                _body = parent,
                _boundModel = card.Model,
            };
            parent.AddChild(veil);
            return;
        }

        veil._boundModel = card.Model;
        veil.CancelWashAndReactivate();
        veil.SyncLayoutAndMask();
    }

    public static void DetachFromCard(NCard card, bool refreshCardVisuals = true)
    {
        if (!GodotObject.IsInstanceValid(card))
            return;
        if (FindVeil(card) is { } veil)
            veil.DetachAndFree(refreshCardVisuals);
    }

    private static Control ResolveHostBody(NCard card)
    {
        if (card.Body != null && GodotObject.IsInstanceValid(card.Body))
            return card.Body;
        return card.GetNodeOrNull<Control>("%CardContainer") ?? card;
    }

    private static NInkBlackoutCardVeil? FindVeil(NCard card)
    {
        if (card.Body != null && GodotObject.IsInstanceValid(card.Body) &&
            card.Body.GetNodeOrNull<NInkBlackoutCardVeil>(ChildName) is { } bodyVeil &&
            !bodyVeil._detaching && !bodyVeil.IsQueuedForDeletion())
        {
            return bodyVeil;
        }

        if (card.GetNodeOrNull<NInkBlackoutCardVeil>(ChildName) is { } rootVeil &&
            !rootVeil._detaching && !rootVeil.IsQueuedForDeletion())
        {
            return rootVeil;
        }

        return null;
    }

    public override void _EnterTree()
    {
        base._EnterTree();
        ActiveVeils.Add(this);
        CombatManager.Instance.CombatEnded += OnCombatEnded;
        if (GodotObject.IsInstanceValid(_card))
        {
            _card.ModelChanged -= OnCardModelChanged;
            _card.ModelChanged += OnCardModelChanged;
        }
    }

    public override void _ExitTree()
    {
        ActiveVeils.Remove(this);
        CombatManager.Instance.CombatEnded -= OnCombatEnded;
        if (GodotObject.IsInstanceValid(_card))
            _card.ModelChanged -= OnCardModelChanged;
        KillTweens();
        RestoreMaskedBodyChildren();
        base._ExitTree();
    }

    public override void _Ready()
    {
        if (!GodotObject.IsInstanceValid(_card))
            return;

        _cachedShader ??= GD.Load<Shader>(ShaderPath);
        _cachedFrameTexture ??= GD.Load<Texture2D>(FrameTexturePath);
        _cachedOrbTexture ??= GD.Load<Texture2D>(OrbTexturePath);

        _inkMaterial = new ShaderMaterial
        {
            Shader = _cachedShader,
            ResourceLocalToScene = true,
        };
        float seed = ((_boundModel?.GetHashCode() ?? GetHashCode()) & 0xFFFF) / 65535f;
        _inkMaterial.SetShaderParameter("card_seed", seed);
        _inkMaterial.SetShaderParameter("wash_progress", 0f);
        _inkMaterial.SetShaderParameter("drench_progress", 1f);

        _inkFrame = new TextureRect
        {
            Name = "AbyssalInkFrame",
            Texture = _cachedFrameTexture,
            Material = _inkMaterial,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = MouseFilterEnum.Ignore,
            Position = CanonicalFramePosition,
            Size = CanonicalFrameSize,
        };
        AddChild(_inkFrame);

        _costOrb = new TextureRect
        {
            Name = "AbyssalInkEnergyOrb",
            Texture = _cachedOrbTexture,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore,
            Position = CanonicalOrbPosition,
            Size = CanonicalOrbSize,
        };

        _costLabel = new Label
        {
            Name = "NeutralCostLabel",
            Text = ResolveNeutralCostText(_card.Model),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        ApplyCostLabelTheme(_costLabel);
        _costOrb.AddChild(_costLabel);
        _costLabel.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _costLabel.OffsetTop = -2f;
        AddChild(_costOrb);

        _active = true;
        SyncLayoutAndMask();
        DismissStaleHolderHoverTips();
        PlaySubtleDrenchIn();
    }

    private void ApplyCostLabelTheme(Label label)
    {
        if (_card.GetNodeOrNull<Control>("%EnergyLabel") is { } nativeEnergyLabel &&
            GodotObject.IsInstanceValid(nativeEnergyLabel))
        {
            Font? nativeFont = nativeEnergyLabel.GetThemeFont("font");
            if (nativeFont != null)
            {
                label.AddThemeFontOverride("font", nativeFont);
            }
        }

        label.AddThemeFontSizeOverride("font_size", 36);
        label.AddThemeColorOverride("font_color", NeutralCostColor);
        label.AddThemeColorOverride("font_outline_color", NeutralCostOutlineColor);
        label.AddThemeConstantOverride("outline_size", 7);
    }

    private void PlaySubtleDrenchIn()
    {
        if (_inkMaterial == null)
            return;

        if (_drenchTween != null && GodotObject.IsInstanceValid(_drenchTween))
            _drenchTween.Kill();

        _inkMaterial.SetShaderParameter("drench_progress", 0.25f);
        _drenchTween = CreateTween();
        _drenchTween.TweenMethod(
                Callable.From<float>(v => _inkMaterial?.SetShaderParameter("drench_progress", v)),
                0.25f,
                1.0f,
                0.24f)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.Out);
    }

    private void SyncLayoutAndMask()
    {
        if (!GodotObject.IsInstanceValid(_card) || _inkFrame == null || _costOrb == null || _costLabel == null)
            return;

        if (_card.Model != _boundModel && !AbyssalInkBlackout.IsVeiled(_card.Model))
        {
            DetachAndFree();
            return;
        }

        // Fixed canonical vanilla silhouette regardless of Attack/Skill/Power/Rare/Ancient/Star/Enchantment!
        _inkFrame.Position = CanonicalFramePosition;
        _inkFrame.Size = CanonicalFrameSize;
        _costOrb.Position = CanonicalOrbPosition;
        _costOrb.Size = CanonicalOrbSize;

        _costLabel.Text = ResolveNeutralCostText(_card.Model);
        _costLabel.AddThemeColorOverride("font_color", NeutralCostColor);

        _inkFrame.Visible = _active || _washing;
        _costOrb.Visible = _active && !_washing;

        if (_active && !_washing)
        {
            ApplyBodyChildrenMask();
            SuppressCardHighlight();
        }
    }

    private static string ResolveNeutralCostText(CardModel? model)
    {
        if (model == null)
            return "?";

        if (model.EnergyCost.CostsX)
            return "X";

        if (model.EnergyCost.GetWithModifiers(CostModifiers.None) < 0 ||
            model.Keywords.Contains(CardKeyword.Unplayable))
        {
            return "?";
        }

        return Math.Max(0, model.EnergyCost.GetWithModifiers(CostModifiers.All)).ToString();
    }

    private void ApplyBodyChildrenMask()
    {
        Control? body = _body != null && GodotObject.IsInstanceValid(_body) ? _body : ResolveHostBody(_card);
        if (body == null || !GodotObject.IsInstanceValid(body))
            return;

        foreach (Node child in body.GetChildren())
        {
            if (ReferenceEquals(child, this))
                continue;
            if (child is not CanvasItem item || !GodotObject.IsInstanceValid(item))
                continue;
            // Keep canonical fixed Shadow visible so cards stay grounded in hand & piles!
            if (string.Equals(child.Name, "Shadow", StringComparison.Ordinal))
                continue;

            if (!_maskedBodyChildren.ContainsKey(item))
            {
                _maskedBodyChildren[item] = item.Modulate;
            }

            if (item.Modulate.A > 0f)
            {
                Color c = item.Modulate;
                item.Modulate = new Color(c.R, c.G, c.B, 0f);
            }
        }
    }

    private void SuppressCardHighlight()
    {
        if (!GodotObject.IsInstanceValid(_card))
            return;

        if (_card.CardHighlight is { } highlight && GodotObject.IsInstanceValid(highlight))
        {
            if (!_maskedBodyChildren.ContainsKey(highlight))
            {
                _maskedBodyChildren[highlight] = highlight.Modulate;
            }
            highlight.AnimHideInstantly();
            Color c = highlight.Modulate;
            if (c.A > 0f)
            {
                highlight.Modulate = new Color(c.R, c.G, c.B, 0f);
            }
        }
    }

    private void RestoreMaskedBodyChildren()
    {
        if (_maskedBodyChildren.Count == 0)
            return;

        foreach ((CanvasItem item, Color savedModulate) in _maskedBodyChildren)
        {
            if (GodotObject.IsInstanceValid(item))
            {
                item.Modulate = savedModulate;
            }
        }
        _maskedBodyChildren.Clear();
    }

    private void StartWaveWash()
    {
        if (_detaching || !GodotObject.IsInstanceValid(this) || IsQueuedForDeletion())
            return;

        if (_inkMaterial == null || _inkFrame == null)
        {
            DetachAndFree();
            return;
        }

        if (_washing)
            return;

        _active = false;
        _washing = true;
        KillTweens();

        // Immediately unveil the real card underneath so the seafoam wave washes away the ink to reveal it!
        RestoreMaskedBodyChildren();
        RefreshCardVisualsGuarded(_card);

        if (_costOrb != null)
            _costOrb.Visible = false;

        _inkFrame.Visible = true;
        _inkMaterial.SetShaderParameter("drench_progress", 1f);
        _inkMaterial.SetShaderParameter("wash_progress", 0f);

        _washTween = CreateTween();
        _washTween.TweenMethod(
                Callable.From<float>(v => _inkMaterial?.SetShaderParameter("wash_progress", v)),
                0f,
                1f,
                0.62f)
            .SetTrans(Tween.TransitionType.Cubic)
            .SetEase(Tween.EaseType.InOut);
        _washTween.TweenCallback(Callable.From(() => DetachAndFree(refreshCardVisuals: true)));
    }

    private void CancelWashAndReactivate()
    {
        if (!_washing && _active)
            return;

        KillTweens();
        _washing = false;
        _active = true;
        _inkMaterial?.SetShaderParameter("wash_progress", 0f);
        _inkMaterial?.SetShaderParameter("drench_progress", 1f);
    }

    private void KillTweens()
    {
        if (_washTween != null && GodotObject.IsInstanceValid(_washTween))
            _washTween.Kill();
        _washTween = null;

        if (_drenchTween != null && GodotObject.IsInstanceValid(_drenchTween))
            _drenchTween.Kill();
        _drenchTween = null;
    }

    private void OnCombatEnded(CombatRoom _)
    {
        if (!GodotObject.IsInstanceValid(this))
            return;
        DetachAndFree();
    }

    private void OnCardModelChanged(CardModel? _)
    {
        if (!GodotObject.IsInstanceValid(this) || !GodotObject.IsInstanceValid(_card))
            return;

        if (_card.Model != _boundModel && !AbyssalInkBlackout.IsVeiled(_card.Model))
        {
            DetachAndFree(refreshCardVisuals: false);
        }
        else if (AbyssalInkBlackout.IsVeiled(_card.Model))
        {
            _boundModel = _card.Model;
            SyncLayoutAndMask();
        }
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

    private static void RefreshCardVisualsGuarded(NCard? card)
    {
        if (card == null || !GodotObject.IsInstanceValid(card) || !card.IsNodeReady() ||
            !card.IsInsideTree() || card.Model == null)
        {
            return;
        }

        if (!RefreshingCards.Add(card))
            return;

        try
        {
            card.UpdateVisuals(card.DisplayingPile, CardPreviewMode.Normal);
        }
        finally
        {
            RefreshingCards.Remove(card);
        }
    }

    private void DetachAndFree(bool refreshCardVisuals = true)
    {
        if (_detaching)
            return;
        _detaching = true;

        NCard? card = _card;
        ActiveVeils.Remove(this);
        CombatManager.Instance.CombatEnded -= OnCombatEnded;
        if (card != null && GodotObject.IsInstanceValid(card))
            card.ModelChanged -= OnCardModelChanged;

        KillTweens();
        _active = false;
        _washing = false;
        RestoreMaskedBodyChildren();

        // Detach from the scene tree BEFORE any NCard.UpdateVisuals refresh so Harmony postfixes
        // can never rediscover this veil and recurse (prevents STATUS_STACK_OVERFLOW / 0xc00000fd).
        Node? parent = GetParent();
        if (parent != null && GodotObject.IsInstanceValid(parent) && NGame.IsMainThread() &&
            (parent.IsNodeReady() || !parent.IsInsideTree()))
        {
            parent.RemoveChild(this);
        }

        QueueFree();

        if (refreshCardVisuals)
        {
            RefreshCardVisualsGuarded(card);
        }
    }
}

[HarmonyPatch(typeof(NCard), nameof(NCard.UpdateVisuals))]
internal static class AbyssalInkBlackoutCardVisualPatch
{
    private static void Postfix(NCard __instance) => NInkBlackoutCardVeil.Refresh(__instance);
}

[HarmonyPatch(typeof(NCard), "Reload")]
internal static class AbyssalInkBlackoutCardReloadPatch
{
    private static void Postfix(NCard __instance) => NInkBlackoutCardVeil.Refresh(__instance);
}

[HarmonyPatch(typeof(NCard), "UpdateEnergyCostVisuals")]
internal static class AbyssalInkBlackoutEnergyCostPatch
{
    private static void Postfix(NCard __instance) => NInkBlackoutCardVeil.Refresh(__instance);
}

[HarmonyPatch(typeof(NCard), "UpdateStarCostVisuals")]
internal static class AbyssalInkBlackoutStarCostPatch
{
    private static void Postfix(NCard __instance) => NInkBlackoutCardVeil.Refresh(__instance);
}

[HarmonyPatch(typeof(NCard), "UpdateEnchantmentVisuals")]
internal static class AbyssalInkBlackoutEnchantmentPatch
{
    private static void Postfix(NCard __instance) => NInkBlackoutCardVeil.Refresh(__instance);
}

[HarmonyPatch(typeof(NCard), "ReloadOverlay")]
internal static class AbyssalInkBlackoutOverlayPatch
{
    private static void Postfix(NCard __instance) => NInkBlackoutCardVeil.Refresh(__instance);
}

[HarmonyPatch(typeof(NCard), nameof(NCard.PlayRandomizeCostAnim))]
internal static class AbyssalInkBlackoutRandomizeAnimPatch
{
    private static void Postfix(NCard __instance) => NInkBlackoutCardVeil.Refresh(__instance);
}

[HarmonyPatch]
internal static class AbyssalInkBlackoutSetPortraitTexturePatch
{
    private static MethodBase? TargetMethod() =>
        AccessTools.Method(typeof(NCard), "SetPortraitTexture", [typeof(Texture2D)]);

    private static bool Prepare() => TargetMethod() != null;

    private static void Postfix(NCard __instance) => NInkBlackoutCardVeil.Refresh(__instance);
}

[HarmonyPatch(typeof(NCard), nameof(NCard.OnFreedToPool))]
internal static class AbyssalInkBlackoutPoolCleanupPatch
{
    private static void Postfix(NCard __instance) => NInkBlackoutCardVeil.DetachFromCard(__instance, refreshCardVisuals: false);
}

[HarmonyPatch(typeof(NHandCardHolder), nameof(NHandCardHolder.UpdateCard))]
internal static class AbyssalInkBlackoutHandHolderUpdatePatch
{
    private static void Postfix(NHandCardHolder __instance)
    {
        if (__instance.CardNode is { } card && AbyssalInkBlackout.IsVeiled(card.Model))
        {
            NInkBlackoutCardVeil.Refresh(card);
        }
    }
}

[HarmonyPatch(typeof(NHandCardHolder), nameof(NHandCardHolder.Flash))]
internal static class AbyssalInkBlackoutHandHolderFlashPatch
{
    private static bool Prefix(NHandCardHolder __instance)
    {
        return !(__instance.CardNode is { } card && AbyssalInkBlackout.IsVeiled(card.Model));
    }
}

/// <summary>
/// Unified zero-leak HoverTip interceptor for BOTH <see cref="AbyssalInkBlackout"/> AND
/// <see cref="LanternBlindness"/>. Replaces <see cref="CardModel.HoverTips"/> (UI-only metadata,
/// never hashed in multiplayer checksums) so hand hover, Draw/Discard/Exhaust pile hover,
/// preview holders, right-click card inspect screens, and co-op teammate hover NEVER leak
/// card keywords, Block, Evoke, Enchantments, Afflictions, or linked mini-cards!
/// </summary>
[HarmonyPatch(typeof(CardModel), nameof(CardModel.HoverTips), MethodType.Getter)]
internal static class VeiledCardHoverTipsSuppressionPatch
{
    private static void Postfix(CardModel __instance, ref IEnumerable<IHoverTip> __result)
    {
        if (AbyssalInkBlackout.IsVeiled(__instance))
        {
            __result = [HoverTipFactory.FromPower<ThingsInkBlackoutPower>()];
        }
        else if (LanternBlindness.IsVeiled(__instance))
        {
            __result = [HoverTipFactory.FromPower<ThingsLanternBlindnessPower>()];
        }
    }
}

[HarmonyPatch(typeof(NInspectCardScreen), "UpdateCardDisplay")]
internal static class VeiledCardInspectScreenPatch
{
    private static readonly AccessTools.FieldRef<NInspectCardScreen, NCard> CardFieldRef =
        AccessTools.FieldRefAccess<NInspectCardScreen, NCard>("_card");
    private static readonly AccessTools.FieldRef<NInspectCardScreen, NTickbox> UpgradeTickboxRef =
        AccessTools.FieldRefAccess<NInspectCardScreen, NTickbox>("_upgradeTickbox");

    private static void Postfix(NInspectCardScreen __instance)
    {
        NCard? cardNode = CardFieldRef(__instance);
        CardModel? model = cardNode?.Model;
        if (model == null)
            return;

        if (AbyssalInkBlackout.IsVeiled(model) || LanternBlindness.IsVeiled(model))
        {
            NTickbox? tickbox = UpgradeTickboxRef(__instance);
            if (tickbox != null && GodotObject.IsInstanceValid(tickbox))
            {
                tickbox.Visible = false;
                tickbox.MouseFilter = Control.MouseFilterEnum.Ignore;
            }

            if (AbyssalInkBlackout.IsVeiled(model) && cardNode != null)
            {
                NInkBlackoutCardVeil.Refresh(cardNode);
            }
        }
    }
}
