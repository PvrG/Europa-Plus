using Content.Server.Shuttles.Systems;
using Content.Shared.Parallax.Biomes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype.List;
using Robust.Shared.Utility;

namespace Content.Server.Shuttles.Components;

[RegisterComponent, Access( typeof(EmergencyShuttleSystem))]
public sealed partial class StationTransitHubComponent : Component
{
    [DataField]
    public ResPath Map = new("/Maps/transit_hub.yml");

    [DataField]
    public EntityUid? Entity;

    [DataField]
    public EntityUid? MapEntity;

    [ViewVariables(VVAccess.ReadOnly),
     DataField(customTypeSerializer: typeof(PrototypeIdListSerializer<BiomeTemplatePrototype>))]
    public List<string> Biomes = new();
}
