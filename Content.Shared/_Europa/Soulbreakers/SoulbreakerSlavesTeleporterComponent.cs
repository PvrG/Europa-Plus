using Robust.Shared.GameStates;

namespace Content.Shared._Europa.Soulbreakers;

[RegisterComponent, NetworkedComponent]
public sealed partial class SoulbreakerSlavesTeleporterComponent : Component
{
    [DataField("cooldown")]
    public TimeSpan Cooldown = TimeSpan.FromSeconds(15);

    [ViewVariables]
    public TimeSpan NextTeleportTime;
}
