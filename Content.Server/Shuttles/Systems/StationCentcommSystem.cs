using Content.Server._Europa.BlockSelling;
using Content.Server.GameTicking;
using Content.Server.Maps;
using Content.Server.Shuttles.Components;
using Content.Server.Station.Systems;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Server.Shuttles.Systems;

public sealed partial class StationCentCommSystem : EntitySystem
{
    [Dependency] private readonly GameTicker _gameTicker = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly ShuttleSystem _shuttle = default!;
    [Dependency] private readonly MapSystem _map = default!;
    [Dependency] private readonly IMapManager _mapMan = default!;
    [Dependency] private readonly StationSystem _station = default!;

    private ISawmill _sawmill = default!;
    private bool _fuckMe = false;

    public override void Initialize()
    {
        _sawmill = Logger.GetSawmill("station.centcomm");
        SubscribeLocalEvent<StationCentCommComponent, ComponentShutdown>(OnCentCommShutdown);
        SubscribeLocalEvent<StationCentCommComponent, ComponentInit>(OnCentCommInit);
    }

    private void OnCentCommShutdown(EntityUid uid, StationCentCommComponent component, ComponentShutdown args)
    {
        QueueDel(component.StationEntity);
        component.StationEntity = EntityUid.Invalid;

        if (_map.MapExists(component.MapId))
            _map.DeleteMap(component.MapId);

        component.MapId = MapId.Nullspace;
        _fuckMe = false;
    }

    private void OnCentCommInit(EntityUid uid, StationCentCommComponent component, ComponentInit args)
    {
        if (_fuckMe)
            return;

        if (_map.MapExists(component.MapId) || component.StationEntity.Valid)
            return;

        AddCentComm(component);
    }

    private void AddCentComm(StationCentCommComponent component)
    {
        var query = AllEntityQuery<StationCentCommComponent>();

        while (query.MoveNext(out var otherComp))
        {
            if (otherComp == component)
                continue;

            component.MapId = otherComp.MapId;
            component.StationEntity = otherComp.StationEntity;
            return;
        }

        if (_prototypeManager.TryIndex<GameMapPrototype>(component.Station, out var gameMap))
        {
            _gameTicker.LoadGameMap(gameMap, out var mapId);

            if (_shuttle.TryAddFTLDestination(mapId, true, out var ftlDestination))
                ftlDestination.Whitelist = component.ShuttleWhitelist;

            foreach (var uid in _mapMan.GetAllGrids(mapId))
            {
                EnsureComp<BlockSellingStationComponent>(uid);
            }

            component.MapId = mapId;

            if (_station.GetStationInMap(mapId) is { } station)
                component.StationEntity = station;

            _map.InitializeMap(mapId);
            _fuckMe = true;
        }
        else
        {
            _sawmill.Warning("No CentComm map found, skipping setup.");
        }
    }
}
