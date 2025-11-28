using Content.Server.Atmos.EntitySystems;
using Content.Server.Cargo.Systems;
using Content.Server.Chat.Systems;
using Content.Server.Station.Systems;
using Content.Shared._Europa.Soulbreakers;
using Content.Shared.Buckle;
using Content.Shared.Chat;
using Content.Shared.Interaction;
using Content.Shared.Inventory;
using Content.Shared.Maps;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.Teleportation.Components;
using Content.Shared.Teleportation.Systems;
using Content.Shared.UserInterface;
using Robust.Server.Audio;
using Robust.Server.GameObjects;
using Robust.Shared.Audio;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Europa.Soulbreakers
{
    /// <summary>
    /// Vibecode
    /// </summary>
    public sealed partial class SoulbreakerTeleportSystem : SharedSoulbreakerTeleportSystem
    {
        [Dependency] private readonly StationSystem _station = default!;
        [Dependency] private readonly ChatSystem _chat = default!;
        [Dependency] private readonly TransformSystem _transform = default!;
        [Dependency] private readonly AudioSystem _audio = default!;
        [Dependency] private readonly IRobustRandom _random = default!;
        [Dependency] private readonly SharedBuckleSystem _buckle = default!;
        [Dependency] private readonly PricingSystem _pricing = default!;
        [Dependency] private readonly EntityLookupSystem _lookup = default!;
        [Dependency] private readonly MobStateSystem _mobStateSystem = default!;
        [Dependency] private readonly IGameTiming _gameTiming = default!;
        [Dependency] private readonly UserInterfaceSystem _userInterface = default!;
        [Dependency] private readonly LinkedEntitySystem _linkedEntity = default!;
        [Dependency] private readonly MapSystem _mapSystem = default!;
        [Dependency] private readonly TurfSystem _turf = default!;
        [Dependency] private readonly AtmosphereSystem _atmosphere = default!;
        [Dependency] private readonly InventorySystem _inventory = default!;

        private EntityQuery<PhysicsComponent> _physicsQuery;

        private const string FunnyEffect = "EffectFlashBluespace";

        private const string ErrorNoTargetMessage = "soulbreaker-teleport-error-no-target";
        private const string ErrorInvalidLocationMessage = "soulbreaker-teleport-error-invalid-location";
        private const string ErrorOnCooldownMessage = "soulbreaker-cooldown";
        private const string ErrorNoSlavesMessage = "soulbreaker-teleport-error-no-slaves";
        private const string ErrorNotBuckledMessage = "soulbreaker-sell-error-not-buckled";
        // private const string ErrorNotOnPadMessage = "soulbreaker-sell-error-not-on-pad";
        private const string SlaveSoldMessage = "soulbreaker-sell-success";
        private const string TargetSelectedMessage = "soulbreaker-teleport-target-selected";

        private static readonly SoundSpecifier ConsoleClickSound = new SoundCollectionSpecifier("SoulbreakerConsoleInteract");
        private static readonly SoundSpecifier ConsoleTeleportSound = new SoundPathSpecifier("/Audio/_Europa/Devices/Teleporter/atar_selected.ogg");
        private static readonly SoundSpecifier SellSound = new SoundPathSpecifier("/Audio/Effects/Cargo/ping.ogg");

        public override void Initialize()
        {
            base.Initialize();

            _physicsQuery = GetEntityQuery<PhysicsComponent>();

            SubscribeLocalEvent<SoulbreakerCrewTeleporterComponent, AfterActivatableUIOpenEvent>(OnToggleInterface);

            SubscribeLocalEvent<SoulbreakerCrewTeleporterComponent, ExecuteTeleportationMessage>(OnExecuteTeleportation);
            SubscribeLocalEvent<SoulbreakerCrewTeleporterComponent, ChangeTeleportCountMessage>(OnChangeCount);
            SubscribeLocalEvent<SoulbreakerCrewTeleporterComponent, SelectTeleportTargetMessage>(OnSelectTarget);

            SubscribeLocalEvent<SoulbreakerTeleportServerComponent, InteractHandEvent>(OnTeleportServerInteract);

            SubscribeLocalEvent<SoulbreakerSlaveSellerComponent, InteractHandEvent>(OnSlaveSellerInteract);

            SubscribeLocalEvent<SoulbreakerSlavesTeleporterComponent, InteractHandEvent>(OnSlavesTeleporterInteract);
        }

        private void OnExecuteTeleportation(EntityUid uid, SoulbreakerCrewTeleporterComponent component, ExecuteTeleportationMessage args)
        {
            var x = Transform(uid);
            _audio.PlayPvs(ConsoleClickSound, x.Coordinates);

            // КД
            if (_gameTiming.CurTime < component.NextTeleportTime)
            {
                var remain = component.NextTeleportTime - _gameTiming.CurTime;
                SendError(uid, ErrorOnCooldownMessage, ("time", Math.Ceiling(remain.TotalSeconds)));
                return;
            }

            // ищем пад на шаттле
            var pad = GetCrewPad();
            if (pad == null)
            {
                SendError(uid, ErrorInvalidLocationMessage);
                return;
            }

            var padCoords = Transform(pad.Value).Coordinates;

            // --- TELEPORT ALL ---
            if (component.TeleportAll)
            {
                var toTeleport = new List<EntityUid>();
                var query = AllEntityQuery<SoulbreakerTeleportableComponent>();

                while (query.MoveNext(out var ent, out _))
                {
                    // не телепортируем тех, кто УЖЕ на шаттле
                    if (!IsOnShuttle(Transform(ent)))
                        toTeleport.Add(ent);
                }

                if (toTeleport.Count == 0)
                {
                    SendError(uid, ErrorNoTargetMessage);
                    return;
                }

                foreach (var target in toTeleport)
                    Teleport(target, padCoords);

                component.NextTeleportTime = _gameTiming.CurTime + component.Cooldown;
                Dirty(uid, component);
                UpdateUserInterface(uid, component);
                return;
            }

            // --- SINGLE TARGET TELEPORT ---
            if (component.TeleportationSubject == null || !Exists(component.TeleportationSubject.Value))
            {
                SendError(uid, ErrorNoTargetMessage);
                return;
            }

            var subject = component.TeleportationSubject.Value;
            var subjectX = Transform(subject);

            if (IsOnShuttle(subjectX))
            {
                SendError(uid, ErrorNoTargetMessage);
                return;
            }

            // Телепортируем цель на шаттл
            Teleport(subject, padCoords);

            component.NextTeleportTime = _gameTiming.CurTime + component.Cooldown;
            Dirty(uid, component);
            UpdateUserInterface(uid, component);
        }

        private void OnChangeCount(EntityUid uid, SoulbreakerCrewTeleporterComponent component, ChangeTeleportCountMessage args)
        {
            component.TeleportAll = args.TeleportAll;
            Dirty(uid, component);
            UpdateUserInterface(uid, component);
        }

        private void OnSelectTarget(EntityUid uid, SoulbreakerCrewTeleporterComponent comp, SelectTeleportTargetMessage msg)
        {
            if (TryGetEntity(msg.Target, out var target)
                && HasComp<SoulbreakerTeleportableComponent>(target))
            {
                comp.TeleportationSubject = target;
                Dirty(uid, comp);
                _chat.TrySendInGameICMessage(
                    uid,
                    Loc.GetString(TargetSelectedMessage, ("targetName", Name(comp.TeleportationSubject.Value))),
                    InGameICChatType.Speak,
                    false
                );
            }

            UpdateUserInterface(uid, comp);
        }

        private void OnToggleInterface(EntityUid uid, SoulbreakerCrewTeleporterComponent component, AfterActivatableUIOpenEvent args)
        {
            UpdateUserInterface(uid, component);
        }

        private void UpdateUserInterface(EntityUid uid, SoulbreakerCrewTeleporterComponent? component = null)
        {
            if (!Resolve(uid, ref component))
                return;

            var list = new List<(NetEntity, string)>();
            var query = AllEntityQuery<SoulbreakerTeleportableComponent, MetaDataComponent>();
            while (query.MoveNext(out var ent, out _, out var meta))
            {
                list.Add((GetNetEntity(ent), meta.EntityName));
            }

            NetEntity? selected = component.TeleportationSubject != null
                ? GetNetEntity(component.TeleportationSubject.Value)
                : null;

            var state = new SoulbreakerTeleportationConsoleUiState(component.TeleportAll, list, selected);
            _userInterface.SetUiState(uid, SoulbreakerTeleportationConsoleUiKey.Key, state);
        }

        private void OnTeleportServerInteract(EntityUid uid, SoulbreakerTeleportServerComponent comp, InteractHandEvent args)
        {
            var x = Transform(uid);
            _audio.PlayPvs(ConsoleClickSound, x.Coordinates);

            if (_gameTiming.CurTime < comp.NextUseTime)
            {
                var remaining = (comp.NextUseTime - _gameTiming.CurTime).TotalSeconds;
                _chat.TrySendInGameICMessage(uid, $"Сервер на перезарядке: {remaining:0} секунд.", InGameICChatType.Speak, false);
                return;
            }

            TogglePortals(uid, comp);
        }

        private void TogglePortals(EntityUid uid, SoulbreakerTeleportServerComponent comp)
        {
            var hasAnyPortal =
                (comp.ShuttlePortal is { } sh && Exists(sh)) ||
                (comp.StationPortal is { } st && Exists(st));

            if (hasAnyPortal)
            {
                if (comp.ShuttlePortal is { } shp && Exists(shp))
                    QueueDel(shp);

                if (comp.StationPortal is { } stp && Exists(stp))
                    QueueDel(stp);

                comp.ShuttlePortal = null;
                comp.StationPortal = null;

                _chat.TrySendInGameICMessage(uid,
                    "Порталы деактивированы.",
                    InGameICChatType.Speak,
                    false);

                comp.NextUseTime = _gameTiming.CurTime + comp.Cooldown;
                Dirty(uid, comp);
                return;
            }

            EntityUid? crewPad = GetCrewPad();
            if (crewPad == null)
            {
                _chat.TrySendInGameICMessage(uid, "Ошибка: не найден телепорт пад на шаттле!", InGameICChatType.Speak, false);
                return;
            }

            var shuttleCoords = Transform(crewPad.Value).Coordinates;

            var stationCoords = GetRandomStationTile();
            if (stationCoords == EntityCoordinates.Invalid)
            {
                _chat.TrySendInGameICMessage(uid, "Ошибка: не найдено подходящее место для создания внешнего портала!", InGameICChatType.Speak, false);
                return;
            }

            // 3 — создаём порталы
            var stationPortal = SpawnAttachedTo("SoulbreakerPortal", stationCoords);
            if (stationPortal != EntityUid.Invalid && !Initializing(stationPortal) && !TerminatingOrDeleted(stationPortal))
                comp.StationPortal = stationPortal;

            var shuttlePortal = SpawnAttachedTo("SoulbreakerPortal", shuttleCoords);
            if (shuttlePortal != EntityUid.Invalid && !Initializing(shuttlePortal) && !TerminatingOrDeleted(shuttlePortal))
                comp.ShuttlePortal = shuttlePortal;

            // 4 — линковка
            LinkPortals(stationPortal, shuttlePortal);

            comp.NextUseTime = _gameTiming.CurTime + comp.Cooldown;
            Dirty(uid, comp);

            _chat.TrySendInGameICMessage(uid,
                $"Портал создан на станции: {stationCoords.Position}",
                InGameICChatType.Speak,
                false);
        }

        private void LinkPortals(EntityUid a, EntityUid b)
        {
            var linkA = EnsureComp<LinkedEntityComponent>(a);
            var linkB = EnsureComp<LinkedEntityComponent>(b);

            _linkedEntity.TryLink(a, b, deleteOnEmptyLinks: false);

            Dirty(a, linkA);
            Dirty(b, linkB);
        }

        private EntityCoordinates GetRandomStationTile()
        {
            var station = GetAnyStation();
            if (station == null)
                return EntityCoordinates.Invalid;

            var gridUid = _station.GetLargestGrid(station.Value);
            if (gridUid == null)
                return EntityCoordinates.Invalid;

            Vector2i tile;
            EntityCoordinates targetCoords;

            var grid = Comp<MapGridComponent>(gridUid.Value);
            var aabb = grid.LocalAABB;

            for (var i = 0; i < 10; i++)
            {
                var randomX = _random.Next((int) aabb.Left, (int) aabb.Right);
                var randomY = _random.Next((int) aabb.Bottom, (int) aabb.Top);

                tile = new Vector2i(randomX, randomY);

                if (!_mapSystem.TryGetTile(grid, tile, out var selectedTile) || selectedTile.IsEmpty ||
                    _turf.IsSpace(selectedTile))
                    continue;

                if (_atmosphere.IsTileSpace(gridUid.Value, Transform(gridUid.Value).MapUid, tile)
                    || _atmosphere.IsTileAirBlocked(gridUid.Value, tile, mapGridComp: grid))
                    continue;

                targetCoords = (_mapSystem).GridTileToLocal(gridUid.Value, grid, tile);
                return targetCoords;
            }

            return EntityCoordinates.Invalid;
        }


        private bool IsOnShuttle(TransformComponent xform)
        {
            if (xform.GridUid is not {} grid)
            {
                return false;
            }

            return HasComp<SoulbreakerShuttleComponent>(grid);
        }

        private EntityUid? GetCrewPad()
        {
            var query = AllEntityQuery<SoulbreakerCrewTeleporterPadComponent>();
            while (query.MoveNext(out var uid, out _))
            {
                return uid;
            }

            return null;
        }

        private EntityUid? GetSlavePad()
        {
            var query = AllEntityQuery<SoulbreakerSlavesTeleporterPadComponent>();
            while (query.MoveNext(out var uid, out _))
            {
                return uid;
            }

            return null;
        }

        private EntityUid? GetSlaveSellPad(EntityUid console)
        {
            var consoleX = Transform(console);

            var pads = AllEntityQuery<SoulbreakerSlavesSellPadComponent, TransformComponent>();
            while (pads.MoveNext(out var uid, out _, out var padX))
            {
                if (padX.GridUid == consoleX.GridUid)
                    return uid;
            }

            return null;
        }

        private EntityUid? GetAnyStation()
        {
            var query = AllEntityQuery<SoulbreakerAvailableForTeleportationComponent, TransformComponent>();
            while (query.MoveNext(out var uid, out _, out var xform))
            {
                return uid;
            }

            return null;
        }

        private void Teleport(EntityUid entity, EntityCoordinates dst)
        {
            var xform = Transform(entity);
            Spawn(FunnyEffect, _transform.GetMapCoordinates(xform));
            _audio.PlayPvs(ConsoleTeleportSound, xform.Coordinates);
            _transform.SetCoordinates(entity, dst);
            Spawn(FunnyEffect, dst);
            _audio.PlayPvs(ConsoleTeleportSound, dst);
        }

        private bool IsOnSameTile(EntityUid a, EntityUid b)
        {
            var xa = Transform(a);
            var xb = Transform(b);

            if (xa.GridUid == null || xb.GridUid == null)
                return false;

            if (!_transform.TryGetGridTilePosition((a, xa), out var tileA))
                return false;

            if (!_transform.TryGetGridTilePosition((b, xb), out var tileB))
                return false;

            return tileA == tileB;
        }

        private void OnSlavesTeleporterInteract(EntityUid uid, SoulbreakerSlavesTeleporterComponent comp, InteractHandEvent args)
        {
            var x = Transform(uid);
            _audio.PlayPvs(ConsoleClickSound, x.Coordinates);

            if (_gameTiming.CurTime < comp.NextTeleportTime)
            {
                var remain = comp.NextTeleportTime - _gameTiming.CurTime;
                SendError(uid, ErrorOnCooldownMessage, ("time", Math.Ceiling(remain.TotalSeconds)));
                return;
            }

            var pad = GetSlavePad();
            if (pad == null)
            {
                SendError(uid, ErrorInvalidLocationMessage);
                return;
            }

            var padX = Transform(pad.Value).Coordinates;

            var slaves = new List<EntityUid>();
            var enslaveds = AllEntityQuery<SoulbreakerEnslavedComponent, TransformComponent>();
            while (enslaveds.MoveNext(out var slave, out _, out var slaveX))
            {
                if (!IsOnShuttle(slaveX))
                    slaves.Add(slave);
            }

            if (slaves.Count == 0)
            {
                SendError(uid, ErrorNoSlavesMessage);
                return;
            }

            foreach (var slave in slaves)
            {
                Teleport(slave, padX);
            }

            comp.NextTeleportTime = _gameTiming.CurTime + comp.Cooldown;
            Dirty(uid, comp);

            _audio.PlayPvs(ConsoleTeleportSound, x.Coordinates);
            SendSuccess(uid, "slaves-teleported", ("count", slaves.Count));
        }

        private void OnSlaveSellerInteract(EntityUid uid, SoulbreakerSlaveSellerComponent comp, InteractHandEvent args)
        {
            var x = Transform(uid);
            _audio.PlayPvs(ConsoleClickSound, x.Coordinates);

            if (_gameTiming.CurTime < comp.NextSellTime)
            {
                var remain = comp.NextSellTime - _gameTiming.CurTime;
                SendError(uid, ErrorOnCooldownMessage, ("time", Math.Ceiling(remain.TotalSeconds)));
                return;
            }

            var pads = new List<(EntityUid pad, TransformComponent xform)>();
            var padQuery = AllEntityQuery<SoulbreakerSlavesSellPadComponent, TransformComponent>();
            while (padQuery.MoveNext(out var padUid, out _, out var padX))
            {
                pads.Add((padUid, padX));
            }

            if (pads.Count == 0)
            {
                SendError(uid, ErrorInvalidLocationMessage);
                return;
            }

            float total = 0;
            int soldCount = 0;
            bool isNotBuckled = false;

            foreach (var (pad, padX) in pads)
            {
                var nearby = _lookup.GetEntitiesInRange(
                    _transform.GetMapCoordinates(padX),
                    1.0f,
                    LookupFlags.Dynamic
                );

                foreach (var ent in nearby)
                {
                    if (!HasComp<SoulbreakerEnslavedComponent>(ent))
                        continue;

                    // if (!_inventory.TryGetSlotEntity(ent, "neck", out var collar) ||
                    //     !TryComp<SoulbreakerCollarComponent>(collar, out var collarComp) ||
                    //     collarComp.EnslavedEntity != ent)
                    // {
                    //     continue;
                    // }

                    if (!_buckle.IsBuckled(ent))
                    {
                        isNotBuckled = true;
                        continue;
                    }

                    if (!IsOnSameTile(ent, pad))
                        continue;

                    var price = CalculateSlavePrice(ent);
                    total += price;
                    soldCount++;

                    var sold = new SoulbreakerSomeoneWasSold(ent, price);
                    RaiseLocalEvent(ref sold);

                    QueueDel(ent);
                }
            }

            if (soldCount == 0)
            {
                SendError(uid, isNotBuckled ? ErrorNotBuckledMessage : ErrorNoSlavesMessage);
                return;
            }

            comp.NextSellTime = _gameTiming.CurTime + comp.Cooldown;
            Dirty(uid, comp);

            _audio.PlayPvs(SellSound, x.Coordinates);
            SendSuccess(uid, SlaveSoldMessage, ("amount", total.ToString("F2")));
        }

        private float CalculateSlavePrice(EntityUid slave)
        {
            var basePrice = _pricing.GetPrice(slave);
            var multiplier = 1f;

            if (TryComp<MobStateComponent>(slave, out var mobState))
            {
                if (!_mobStateSystem.IsAlive(slave, mobState))
                    multiplier *= 0.001f;
            }

            return (float)basePrice * multiplier;
        }

        private void SendError(EntityUid uid, string key, params (string, object)[] args)
        {
            _chat.TrySendInGameICMessage(
                uid,
                Loc.GetString(key, args),
                InGameICChatType.Speak,
                false
            );
        }

        private void SendSuccess(EntityUid uid, string key, params (string, object)[] args)
        {
            _chat.TrySendInGameICMessage(
                uid,
                Loc.GetString(key, args),
                InGameICChatType.Speak,
                false
            );
        }
    }
}
