using MegaCrit.Sts2.Core.Models;

namespace STS2_Things.Core.Models;

/// <summary>
/// Shared v2.0 base class for internal <c>STS2_Things</c> run-state policy modifiers
/// registered via <c>ModHelper.SubscribeForRunStateHooks</c>.
/// Disables combat-level hook dispatch by default since run-state policies only observe
/// room rewards, rest site options, or merchant pricing.
/// </summary>
public abstract class ThingsModifierModel : ModifierModel
{
    public override bool ShouldReceiveCombatHooks => false;

    protected override string IconPath => "res://images/packed/common_ui/locked_model.png";
}
