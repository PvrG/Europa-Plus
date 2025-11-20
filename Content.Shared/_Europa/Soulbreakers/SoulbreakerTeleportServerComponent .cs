using Robust.Shared.GameStates;

namespace Content.Shared._Europa.Soulbreakers;

[RegisterComponent, NetworkedComponent]
public sealed partial class SoulbreakerTeleportServerComponent : Component
{
    [DataField]
    public TimeSpan Cooldown = TimeSpan.FromSeconds(3);

    [DataField]
    public TimeSpan NextUseTime = TimeSpan.Zero;

    [ViewVariables]
    public EntityUid? StationPortal;

    [ViewVariables]
    public EntityUid? ShuttlePortal;
}
