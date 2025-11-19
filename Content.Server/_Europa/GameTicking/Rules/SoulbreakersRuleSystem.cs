using System.Linq;
using Content.Server.Antag;
using Content.Server.Chat.Systems;
using Content.Server.Roles;
using Content.Server.RoundEnd;
using Content.Server.Station.Components;
using Content.Server.Station.Systems;
using Content.Shared.GameTicking.Components;
using Content.Shared.Humanoid;
using Content.Shared.Mind;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;
using Robust.Shared.Timing;
using Content.Server._Europa.GameTicking.Rules.Components;
using Content.Server._Europa.Roles;
using Content.Server.Communications;
using Content.Server.GameTicking;
using Content.Server.GameTicking.Rules;
using Content.Server.Shuttles.Events;
using Content.Shared._Europa.Soulbreakers;
using Content.Shared.Zombies;

namespace Content.Server._Europa.GameTicking.Rules;

public sealed class SoulbreakersRuleSystem : GameRuleSystem<SoulbreakersRuleComponent>
{
    [Dependency] private readonly AntagSelectionSystem _antag = default!;
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly ISharedPlayerManager _player = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly RoundEndSystem _roundEnd = default!;
    [Dependency] private readonly SharedMindSystem _mindSystem = default!;
    [Dependency] private readonly StationSystem _station = default!;
    [Dependency] private readonly GameTicker _gameTicker = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SoulbreakerSomeoneWasSold>(OnEnslavedSold);
        SubscribeLocalEvent<SoulbreakerRoleComponent, GetBriefingEvent>(OnSoulbreakerBriefing);
        SubscribeLocalEvent<SoulbreakerAssistantRoleComponent, GetBriefingEvent>(OnAssistantBriefing);
        SubscribeLocalEvent<SoulbreakersRuleComponent, RuleLoadedGridsEvent>(OnRuleLoadedGrids);
        SubscribeLocalEvent<SoulbreakerRoleComponent, EntityZombifiedEvent>(OnSoulbreakerZombified);
        SubscribeLocalEvent<SoulbreakerAssistantRoleComponent, EntityZombifiedEvent>(OnSoulbreakerZombified);
        SubscribeLocalEvent<CommunicationConsoleCallShuttleAttemptEvent>(OnShuttleCallAttempt);
        SubscribeLocalEvent<ConsoleFTLAttemptEvent>(OnShuttleFTLAttempt);
    }

    #region --- Briefings ---


    // This is for the roundstart i think so it is usless
    private void OnSoulbreakerBriefing(Entity<SoulbreakerRoleComponent> _, ref GetBriefingEvent args)
    {
        args.Append(Loc.GetString("soulbreakers-soulbreaker-role-greeting"));
        _audio.PlayGlobal("/Audio/_Europa/Announcements/azan.ogg", Filter.Entities(args.Mind.Owner), false);
    }

    private void OnAssistantBriefing(Entity<SoulbreakerAssistantRoleComponent> _, ref GetBriefingEvent args)
    {
        args.Append(Loc.GetString("soulbreakers-soulbreaker-assistant-role-greeting"));
        _audio.PlayGlobal("/Audio/_Europa/Announcements/azan.ogg", Filter.Entities(args.Mind.Owner), false);
    }

    #endregion

    #region --- Round End Summary ---

    protected override void AppendRoundEndText(EntityUid uid, SoulbreakersRuleComponent comp, GameRuleComponent gameRule, ref RoundEndTextAppendEvent args)
    {
        base.AppendRoundEndText(uid, comp, gameRule, ref args);

        var soulbreakers = GetSoulbreakerEntries(uid);
        var enslavedFraction = GetEnslavedFraction(comp.EnslavedCount);

        AppendEnslavedSummary(args, enslavedFraction, comp);
        AppendSoulbreakerList(args, soulbreakers);
        AppendCrewStatus(args);
    }

    private List<string> GetSoulbreakerEntries(EntityUid uid)
    {
        var entries = new List<string>();
        var antagIdentifiers = _antag.GetAntagIdentifiers(uid);

        foreach (var (antagUid, data, name) in antagIdentifiers)
        {
            if (!Exists(antagUid) || Terminating(antagUid))
                continue;

            var status = GetHealthStatus(antagUid);

            string? text = null;

            if (HasComp<SoulbreakerRoleComponent>(antagUid))
            {
                text = Loc.GetString("soulbreakers-round-end-user-was-soulbreaker",
                    ("name", name),
                    ("username", data.UserName));
            }
            else if (HasComp<SoulbreakerAssistantRoleComponent>(antagUid))
            {
                text = Loc.GetString("soulbreakers-round-end-user-was-soulbreaker-assistant",
                    ("name", name),
                    ("username", data.UserName));
            }

            if (text != null)
                entries.Add($"{text} {status}");
        }

        return entries;
    }

    private void AppendEnslavedSummary(RoundEndTextAppendEvent args, float fraction, SoulbreakersRuleComponent comp)
    {
        var text = fraction switch
        {
            <= 0 => Loc.GetString("soulbreakers-round-end-enslaved-amount-none"),
            <= 0.25f => Loc.GetString("soulbreakers-round-end-enslaved-amount-low", ("amount", comp.EnslavedCount)),
            <= 0.5f => Loc.GetString("soulbreakers-round-end-enslaved-amount-medium", ("amount", comp.EnslavedCount)),
            < 1f => Loc.GetString("soulbreakers-round-end-enslaved-amount-high", ("amount", comp.EnslavedCount)),
            _ => Loc.GetString("soulbreakers-round-end-enslaved-amount-all")
        };

        args.AddLine(text);

        if (fraction <= 0)
            args.AddLine(Loc.GetString("soulbreakers-round-end-sum", ("sum", comp.EnslavedStonks.ToString("F2"))));
    }

    private void AppendSoulbreakerList(RoundEndTextAppendEvent args, IEnumerable<string> soulbreakers)
    {
        args.AddLine(Loc.GetString("soulbreakers-round-end-soulbreakers-list"));
        foreach (var sb in soulbreakers)
        {
            args.AddLine(sb);
        }
    }

    private void AppendCrewStatus(RoundEndTextAppendEvent args)
    {
        var enslavedCrew = new List<string>();
        var freeCrew = new List<string>();

        var players = AllEntityQuery<HumanoidAppearanceComponent, ActorComponent>();
        while (players.MoveNext(out var uid, out _, out _))
        {
            if (!Exists(uid) || Terminating(uid))
                continue;

            if (HasComp<SoulbreakerRoleComponent>(uid)
                || HasComp<SoulbreakerAssistantRoleComponent>(uid))
                continue;

            var name = MetaData(uid).EntityName;
            var username = TryGetUsername(uid);
            var status = GetHealthStatus(uid);

            var enslaved = HasComp<SoulbreakerEnslavedComponent>(uid);
            var text = Loc.GetString(
                enslaved ? "soulbreakers-round-end-user-was-enslaved" : "soulbreakers-round-end-user-remained-free",
                ("name", name),
                ("username", username));

            (enslaved ? enslavedCrew : freeCrew).Add($"{text} {status}");
        }

        args.AddLine(Loc.GetString("soulbreakers-round-end-enslaved-result"));
        foreach (var e in enslavedCrew)
        {
            args.AddLine(e);
        }

        foreach (var e in freeCrew)
        {
            args.AddLine(e);
        }
    }

    private string GetHealthStatus(EntityUid uid)
    {
        return _mobState.IsAlive(uid)
            ? Loc.GetString("soulbreakers-health-status-alive")
            : Loc.GetString("soulbreakers-health-status-dead");
    }

    private string TryGetUsername(EntityUid uid)
    {
        if (_mindSystem.TryGetMind(uid, out _, out var mind) &&
            _player.TryGetSessionById(mind.UserId, out var session))
            return session.Name;

        return string.Empty;
    }

    #endregion

    #region --- Round End Logic ---

    private void CheckRoundEnd(SoulbreakersRuleComponent comp)
    {
        if (!comp.RoundstartDelayEnded)
            return;

        var healthy = GetHealthyHumans();
        var healthySoulbreakers = GetHealthySoulbreakers();
        var enslavedFraction = GetEnslavedFraction(comp.EnslavedCount);

        var shouldCallShuttle =
            !_roundEnd.IsRoundEndRequested() &&
            (enslavedFraction > comp.EnslavedShuttleCallPercentage ||
             healthySoulbreakers.Count < 1 ||
             healthy.Count <= healthySoulbreakers.Count);

        if (shouldCallShuttle)
        {
            foreach (var station in _station.GetStations())
            {
                if (!Exists(station) || Terminating(station))
                    continue;
                _chat.DispatchStationAnnouncement(station,
                    Loc.GetString("soulbreakers-shuttle-call", ("stationName", Name(station))));
            }

            // _audio.PlayGlobal("/Audio/_Europa/Announcements/azan.ogg", Filter.Broadcast(), true);
            _roundEnd.RequestRoundEnd(null, false);
        }

        if (enslavedFraction >= 0.8f)
            _roundEnd.EndRound();
    }

    #endregion

    #region --- Specific ---

    private void CheckRoundstartDelay(SoulbreakersRuleComponent comp, GameRuleComponent gameRule)
    {
        if (comp.RoundstartDelayEnded
            || gameRule.ActivatedAt + comp.RoundstartDelay > _timing.CurTime)
            return;
        comp.RoundstartDelayEnded = true;
        AnnounceSoulbreakersArrival();
    }

    private void AnnounceSoulbreakersArrival()
    {
        foreach (var station in _station.GetStations())
        {
            if (!Exists(station) || Terminating(station))
                continue;

            _chat.DispatchStationAnnouncement(station,
                Loc.GetString("soulbreakers-start-announcement", ("stationName", Name(station))));
        }
    }

    #endregion

    #region --- Game Rule Events ---

    private void OnEnslavedSold(ref SoulbreakerSomeoneWasSold ev)
    {
        var query = QueryActiveRules();
        while (query.MoveNext(out _, out _, out var soulbreakersRule, out _))
        {
            soulbreakersRule.EnslavedStonks += ev.price;
            soulbreakersRule.EnslavedCount += 1;
        }
    }
    private void OnShuttleFTLAttempt(ref ConsoleFTLAttemptEvent ev)
    {
        var query = QueryActiveRules();
        while (query.MoveNext(out var uid, out _, out var soulbreakersRule, out var gameRule))
        {
            if (ev.Uid != GetShuttle(uid))
                continue;

            var timeRemaining = Timing.CurTime.Subtract(gameRule.ActivatedAt + soulbreakersRule.RoundstartDelay);
            if (timeRemaining <= TimeSpan.Zero)
                continue;

            ev.Cancelled = true;
            ev.Reason = Loc.GetString("soulbreakers-soulbreakers-shuttle-unavailable",
                ("timeRemaining", timeRemaining.ToString("mm\\:ss")));
            _audio.PlayGlobal("/Audio/_Goobstation/Weapons/Effects/energy_error.ogg", Filter.Entities(ev.Uid), true);
        }
    }
    private void OnShuttleCallAttempt(ref CommunicationConsoleCallShuttleAttemptEvent ev)
    {
        var healthy = GetHealthyHumans();
        var healthySoulbreakers = GetHealthySoulbreakers();
        var query1 = QueryActiveRules();
        var enslavedCount = 0;
        while (query1.MoveNext(out var _, out var ruleComponent, out var _))
        {
            enslavedCount = enslavedCount < ruleComponent.EnslavedCount ? ruleComponent.EnslavedCount : enslavedCount;
        }
        var enslavedFraction = GetEnslavedFraction(enslavedCount);

        var shouldRecallShuttle =
            (healthySoulbreakers.Count < 1 ||
             healthy.Count <= healthySoulbreakers.Count);

        var query = QueryActiveRules();
        while (query.MoveNext(out _, out _, out var soulbreakersRule, out _))
        {
            if (!soulbreakersRule.RoundstartDelayEnded)
                continue;

            if (!shouldRecallShuttle || enslavedFraction < soulbreakersRule.EnslavedShuttleCallPercentage)
                continue;

            ev.Cancelled = true;
            ev.Reason = Loc.GetString("soulbreakers-shuttle-call-unavailable");
            // TODO: What the fuck? My mind stop working at this point... Fix that shit, pls... 4:18
            var sender = new List<EntityUid>();
            if (ev.Sender != null)
                sender.Add(ev.Sender.Value);
            _audio.PlayGlobal("/Audio/_Europa/Announcements/trubi.ogg", Filter.Entities(sender.First()), true);
        }
    }
    private void OnRuleLoadedGrids(Entity<SoulbreakersRuleComponent> ent, ref RuleLoadedGridsEvent args)
    {
        var query = AllEntityQuery<SoulbreakerShuttleComponent>();
        while (query.MoveNext(out var uid, out var shuttle))
        {
            if (!Exists(uid) || Terminating(uid))
                continue;

            if (Transform(uid).MapID == args.Map)
            {
                shuttle.AssociatedRule = ent;
            }
        }
    }

    private void OnSoulbreakerZombified(EntityUid uid, SoulbreakerRoleComponent component, ref EntityZombifiedEvent args)
    {
        RemCompDeferred(uid, component);
    }
    private void OnSoulbreakerZombified(EntityUid uid, SoulbreakerAssistantRoleComponent component, ref EntityZombifiedEvent args)
    {
        RemCompDeferred(uid, component);
    }
    protected override void Started(EntityUid uid, SoulbreakersRuleComponent comp, GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        base.Started(uid, comp, gameRule, args);
        comp.NextLogicTick = _timing.CurTime + comp.EndCheckDelay;
    }
    protected override void ActiveTick(EntityUid uid, SoulbreakersRuleComponent comp, GameRuleComponent gameRule, float frameTime)
    {
        base.ActiveTick(uid, comp, gameRule, frameTime);
        if (comp.NextLogicTick is not { } nextCheck || nextCheck > _timing.CurTime)
            return;

        CheckRoundstartDelay(comp, gameRule);
        CheckRoundEnd(comp);
        comp.NextLogicTick = _timing.CurTime + comp.EndCheckDelay;
    }

    #endregion

    #region --- Helpers ---

    private EntityUid? GetShuttle(EntityUid ruleOwner)
    {
        var query = AllEntityQuery<SoulbreakerShuttleComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.AssociatedRule == ruleOwner)
                return uid;
        }

        return null;
    }

    private List<EntityUid> GetHealthyHumans(
        bool includeOffStation = false,
        bool includeEnslaved = false,
        bool includeSoulbreakers = false)
    {
        return GetFilteredEntities(uid =>
        {
            // Только живые
            if (!_mobState.IsAlive(uid))
                return false;

            // Исключаем душеломов, если не хотим их включать
            if (!includeSoulbreakers &&
                (HasComp<SoulbreakerRoleComponent>(uid) || HasComp<SoulbreakerAssistantRoleComponent>(uid)))
                return false;

            // Исключаем порабощённых, если не хотим их включать
            if (!includeEnslaved && HasComp<SoulbreakerEnslavedComponent>(uid))
                return false;

            return true;
        },
            includeOffStation);
    }


    private List<EntityUid> GetHealthySoulbreakers(bool includeOffStation = false)
    {
        return GetFilteredEntities(uid =>
        {
            // Только живые
            if (!_mobState.IsAlive(uid))
                return false;

            // Только душеломы
            if (!HasComp<SoulbreakerRoleComponent>(uid) && !HasComp<SoulbreakerAssistantRoleComponent>(uid))
                return false;

            // Исключаем рабов
            if (HasComp<SoulbreakerEnslavedComponent>(uid))
                return false;

            return true;
        },
            includeOffStation);
    }


    private List<EntityUid> GetFilteredEntities(Func<EntityUid, bool> predicate, bool includeOffStation)
    {
        var healthy = new List<EntityUid>();
        var stationGrids = includeOffStation ? null : GetStationGrids();

        var players = AllEntityQuery<HumanoidAppearanceComponent, ActorComponent, MobStateComponent, TransformComponent>();
        while (players.MoveNext(out var uid, out _, out _, out _, out var xform))
        {
            if (!Exists(uid) || Terminating(uid))
                continue;

            if (!predicate(uid))
                continue;

            if (!includeOffStation && stationGrids != null && !stationGrids.Contains(xform.GridUid ?? EntityUid.Invalid))
                continue;

            healthy.Add(uid);
        }

        return healthy;
    }

    private HashSet<EntityUid> GetStationGrids()
    {
        var grids = new HashSet<EntityUid>();
        foreach (var station in _gameTicker.GetSpawnableStations())
        {
            if (!Exists(station) || Terminating(station))
                continue;

            if (TryComp<StationDataComponent>(station, out _) && _station.GetLargestGrid(station) is { } grid)
                grids.Add(grid);
        }
        return grids;
    }

    private float GetEnslavedFraction(int enslavedCount)
    {
        var playerCount = 0;

        var players = AllEntityQuery<HumanoidAppearanceComponent, ActorComponent>();
        while (players.MoveNext(out var uid, out _, out _))
        {
            if (HasComp<SoulbreakerRoleComponent>(uid) || HasComp<SoulbreakerAssistantRoleComponent>(uid))
                continue;
            playerCount++;
        }

        return playerCount == 0 ? 0 : enslavedCount / (float)playerCount;
    }

    #endregion
}
