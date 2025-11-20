using Robust.Shared.GameStates;

namespace Content.Shared._Europa.Soulbreakers;

[RegisterComponent, NetworkedComponent]
public sealed partial class SoulbreakerSlaveSellerComponent : Component
{
    [ViewVariables]
    public TimeSpan NextSellTime;

    [DataField("cooldown")]
    public TimeSpan Cooldown = TimeSpan.FromSeconds(3);
}
