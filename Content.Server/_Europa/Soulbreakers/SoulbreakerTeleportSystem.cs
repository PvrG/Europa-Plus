using Content.Server.Cargo.Systems;
using Content.Server.Chat.Systems;
using Content.Server.Station.Systems;
using Content.Shared._Europa.Soulbreakers;
using Content.Shared.Buckle;
using Content.Shared.Chat;
using Content.Shared.Interaction;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.Verbs;
using Robust.Server.Audio;
using Robust.Server.GameObjects;
using Robust.Shared.Audio;
using Robust.Shared.Map;
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
        [Dependency] private readonly PullingSystem _pulling = default!;
        [Dependency] private readonly TransformSystem _transform = default!;
        [Dependency] private readonly AudioSystem _audio = default!;
        [Dependency] private readonly IRobustRandom _random = default!;
        [Dependency] private readonly SharedBuckleSystem _buckle = default!;
        [Dependency] private readonly PricingSystem _pricing = default!;
        [Dependency] private readonly EntityLookupSystem _lookup = default!;
        [Dependency] private readonly MobStateSystem _mobStateSystem = default!;
        [Dependency] private readonly IGameTiming _gameTiming = default!;

        private const string FunnyEffect = "EffectFlashBluespace";

        private const string ErrorNoTargetMessage = "soulbreaker-teleport-error-no-target";
        private const string ErrorInvalidLocationMessage = "soulbreaker-teleport-error-invalid-location";
        private const string ErrorOnCooldownMessage = "soulbreaker-cooldown";
        private const string ErrorNoSlavesMessage = "soulbreaker-teleport-error-no-slaves";
        private const string ErrorNotBuckledMessage = "soulbreaker-sell-error-not-buckled";
        private const string ErrorNotOnPadMessage = "soulbreaker-sell-error-not-on-pad";
        private const string SlaveSoldMessage = "soulbreaker-sell-success";
        private const string TargetSelectedMessage = "soulbreaker-teleport-target-selected";
        private const string AlternativeVerbName = "soulbreaker-teleport-verb-next";

        private static readonly SoundSpecifier ConsoleClickSound = new SoundCollectionSpecifier("SoulbreakerConsoleInteract");
        private static readonly SoundSpecifier ConsoleTeleportSound = new SoundPathSpecifier("/Audio/_Europa/Devices/Teleporter/atar_selected.ogg");
        private static readonly SoundSpecifier SellSound = new SoundPathSpecifier("/Audio/Effects/Cargo/ping.ogg");

        public override void Initialize()
        {
            base.Initialize();
            SubscribeLocalEvent<SoulbreakerCrewTeleporterComponent, InteractHandEvent>(OnCrewTeleporterInteract);
            SubscribeLocalEvent<SoulbreakerCrewTeleporterComponent, GetVerbsEvent<AlternativeVerb>>(OnCrewTeleporterVerb);

            SubscribeLocalEvent<SoulbreakerSlaveSellerComponent, InteractHandEvent>(OnSlaveSellerInteract);

            SubscribeLocalEvent<SoulbreakerSlavesTeleporterComponent, InteractHandEvent>(OnSlavesTeleporterInteract);
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

        private (EntityUid? stationGrid, TransformComponent? xform) GetAnyStationGrid()
        {
            var query = AllEntityQuery<SoulbreakerAvailableForTeleportationComponent, TransformComponent>();
            while (query.MoveNext(out var uid, out _, out var xform))
            {
                return (uid, xform);
            }

            return (null, null);
        }

        private void Teleport(EntityUid entity, EntityCoordinates dst)
        {
            var xform = Transform(entity);
            Spawn(FunnyEffect, _transform.GetMapCoordinates(xform));
            _transform.SetCoordinates(entity, dst);
            Spawn(FunnyEffect, dst);
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

        private void OnCrewTeleporterVerb(EntityUid uid, SoulbreakerCrewTeleporterComponent comp, GetVerbsEvent<AlternativeVerb> args)
        {
            if (!args.CanAccess || !args.CanInteract)
                return;

            args.Verbs.Add(new AlternativeVerb
            {
                Text = Loc.GetString(AlternativeVerbName),
                Act = () => SwitchTeleportationSubject(uid, args.User, comp)
            });
        }

        private void OnCrewTeleporterInteract(EntityUid uid, SoulbreakerCrewTeleporterComponent comp, InteractHandEvent args)
        {
            var xform = Transform(uid);
            _audio.PlayPvs(ConsoleClickSound, xform.Coordinates);

            if (_gameTiming.CurTime < comp.NextTeleportTime)
            {
                var remain = comp.NextTeleportTime - _gameTiming.CurTime;
                SendError(uid, ErrorOnCooldownMessage, ("time", Math.Ceiling(remain.TotalSeconds)));
                return;
            }

            if (comp.TeleportationSubject == null || !Exists(comp.TeleportationSubject.Value))
            {
                SendError(uid, ErrorNoTargetMessage);
                return;
            }

            var target = comp.TeleportationSubject.Value;
            var targetX = Transform(target);
            _pulling.StopAllPulls(args.User);

            EntityCoordinates? dest = null;

            if (IsOnShuttle(targetX))
            {
                var pad = GetCrewPad();
                if (pad == null || !IsOnSameTile(target, pad.Value))
                {
                    SendError(uid, ErrorNotOnPadMessage);
                    return;
                }

                var (stationGrid, stationX) = GetAnyStationGrid();
                if (stationGrid == null)
                {
                    SendError(uid, ErrorInvalidLocationMessage);
                    return;
                }

                if (_station.GetLargestGrid(stationGrid.Value) is not {} grid)
                {
                    SendError(uid, ErrorInvalidLocationMessage);
                    return;
                }

                dest = Transform(grid).Coordinates;
            }
            else
            {
                var pad = GetCrewPad();
                if (pad == null)
                {
                    SendError(uid, ErrorInvalidLocationMessage);
                    return;
                }

                dest = Transform(pad.Value).Coordinates;
            }

            comp.NextTeleportTime = _gameTiming.CurTime + comp.Cooldown;
            Dirty(uid, comp);

            Teleport(target, dest.Value);
            _audio.PlayPvs(ConsoleTeleportSound, xform.Coordinates);
        }

        private void SwitchTeleportationSubject(EntityUid uid, EntityUid user, SoulbreakerCrewTeleporterComponent comp)
        {
            var xform = Transform(uid);
            _audio.PlayPvs(ConsoleClickSound, xform.Coordinates);

            var list = new List<EntityUid>();
            var query = AllEntityQuery<SoulbreakerTeleportableComponent>();

            while (query.MoveNext(out var ent, out _))
            {
                list.Add(ent);
            }

            if (list.Count == 0)
            {
                SendError(uid, ErrorNoTargetMessage);
                comp.TeleportationSubject = null;
                Dirty(uid, comp);
                return;
            }

            comp.TeleportationSubject = _random.Pick(list);
            Dirty(uid, comp);

            _chat.TrySendInGameICMessage(
                uid,
                Loc.GetString(TargetSelectedMessage, ("targetName", Name(comp.TeleportationSubject.Value))),
                InGameICChatType.Speak,
                false
            );
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
