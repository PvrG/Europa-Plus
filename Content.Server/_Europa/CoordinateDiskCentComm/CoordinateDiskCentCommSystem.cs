using Content.Server.Cargo.Components;
using Content.Server.Shuttles.Components;
using Content.Shared.Shuttles.Components;

namespace Content.Shared._Europa.CoordinateDiskCentComm;

public sealed partial class CoordinateDiskCentCommSystem : EntitySystem
{
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly MetaDataSystem _metaData = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<CoordinateDiskCentCommComponent, ComponentStartup>(OnCentCommDiskStartup);
    }

    private void OnCentCommDiskStartup(EntityUid uid, CoordinateDiskCentCommComponent component, ComponentStartup args)
    {
        if (!TryComp(uid, out ShuttleDestinationCoordinatesComponent? comp))
            return;

        var query = AllEntityQuery<StationCentCommComponent>();

        while (query.MoveNext(out var centCommComp))
        {
            if (!centCommComp.StationEntity.Valid)
                continue;

            if (_map.TryGetMap(centCommComp.MapId, out var mapUid))
            {
                comp.Destination = mapUid;
                Dirty(uid, comp);
                return;
            }
        }

        Log.Warning("There was no central command map to create a link for the CentComm coordinate disk!");
        _metaData.SetEntityName(uid, Loc.GetString("cds-centcomm-expired-name"));
        _metaData.SetEntityDescription(uid, Loc.GetString("cds-centcomm-expired-description"));
        if (TryComp(uid, out StaticPriceComponent? priceComponent))
            priceComponent.Price = 5;
    }
}
