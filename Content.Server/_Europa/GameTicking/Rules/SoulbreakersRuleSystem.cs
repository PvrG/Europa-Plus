using Content.Server.Chat.Systems;
using Content.Server.RoundEnd;
using Content.Server.Station.Systems;
using Content.Shared.Mind;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Player;
using Robust.Shared.Timing;
using Content.Server._Europa.GameTicking.Rules.Components;
using Content.Server._Europa.Roles;
using Content.Server.GameTicking;
using Content.Server.GameTicking.Rules;
using Content.Shared._Europa.Soulbreakers;
using Content.Shared.GameTicking.Components;
using Content.Shared.Mind.Components;
using Robust.Shared.Audio.Systems;

namespace Content.Server._Europa.GameTicking.Rules;

public sealed class SoulbreakersRuleSystem : GameRuleSystem<SoulbreakersRuleComponent>
{
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly ISharedPlayerManager _player = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly RoundEndSystem _roundEnd = default!;
    [Dependency] private readonly SharedMindSystem _mindSystem = default!;
    [Dependency] private readonly StationSystem _station = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;

    public override void Initialize()
    {
        base.Initialize();

       SubscribeLocalEvent<SoulbreakerSomeoneWasSold>(OnEnslavedSold);
    }

    #region --- Round End Summary ---

    protected override void AppendRoundEndText(EntityUid uid, SoulbreakersRuleComponent comp, GameRuleComponent gameRule, ref RoundEndTextAppendEvent args)
    {
        base.AppendRoundEndText(uid, comp, gameRule, ref args);

        args.AddLine(Loc.GetString("soulbreakers-round-end-sum", ("sum", comp.EnslavedStonks.ToString("F2"))));

        AppendSoldSlaves(args, comp);

        AppendAllSoulbreakers(args);

        AppendOtherCrew(args);
    }

    private void AppendSoldSlaves(RoundEndTextAppendEvent args, SoulbreakersRuleComponent comp)
    {
        if (comp.SoldSlaves.Count > 0)
        {
            args.AddLine(Loc.GetString("soulbreakers-round-end-sold-slaves-list"));
            foreach (var slave in comp.SoldSlaves)
            {
                args.AddLine(Loc.GetString("soulbreakers-round-end-sold-slave-entry",
                    ("name", slave.Name),
                    ("price", slave.Price.ToString("F2"))));
            }
            args.AddLine(Loc.GetString("soulbreakers-round-end-total-slaves-sold",
                ("count", comp.SoldSlaves.Count)));

            var totalPlayers = GetPlayerCount() + comp.EnslavedCount;
            var enslavedFraction = totalPlayers == 0 ? 0f : comp.EnslavedCount / (float)totalPlayers;
            args.AddLine(Loc.GetString(
                "soulbreakers-round-end-enslaved-fraction",
                ("fraction", (enslavedFraction * 100f).ToString("F2") + "%")
            ));
        }
        else
        {
            args.AddLine(Loc.GetString("soulbreakers-round-end-no-slaves-sold"));
        }
    }

    private void AppendAllSoulbreakers(RoundEndTextAppendEvent args)
    {
        var soulbreakers = new List<string>();

        var query = AllEntityQuery<SoulbreakerRoleComponent, MindContainerComponent>();
        while (query.MoveNext(out var uid, out _, out _))
        {
            if (!_mindSystem.TryGetMind(uid, out _, out _))
                continue;

            var icName = GetPlayerICName(uid);
            var oocName = GetPlayerOOCName(uid);
            var status = GetHealthStatus(uid);

            var text = Loc.GetString("soulbreakers-round-end-user-was-soulbreaker",
                ("name", icName),
                ("username", oocName));

            soulbreakers.Add($"{text} {status}");
        }

        var assistantQuery = AllEntityQuery<SoulbreakerAssistantRoleComponent, MindContainerComponent>();
        while (assistantQuery.MoveNext(out var uid, out _, out _))
        {
            if (!_mindSystem.TryGetMind(uid, out _, out _))
                continue;

            var icName = GetPlayerICName(uid);
            var oocName = GetPlayerOOCName(uid);
            var status = GetHealthStatus(uid);

            var text = Loc.GetString("soulbreakers-round-end-user-was-soulbreaker-assistant",
                ("name", icName),
                ("username", oocName));

            soulbreakers.Add($"{text} {status}");
        }

        if (soulbreakers.Count > 0)
        {
            args.AddLine(Loc.GetString("soulbreakers-round-end-soulbreakers-list"));
            foreach (var sb in soulbreakers)
            {
                args.AddLine(sb);
            }
        }
        else
        {
            args.AddLine(Loc.GetString("soulbreakers-round-end-no-soulbreakers"));
        }
    }

    private void AppendOtherCrew(RoundEndTextAppendEvent args)
    {
        var otherCrew = new List<string>();

        var query = AllEntityQuery<MindContainerComponent>();
        while (query.MoveNext(out var uid, out var mindContainer))
        {
            if (HasComp<SoulbreakerRoleComponent>(uid) || HasComp<SoulbreakerAssistantRoleComponent>(uid))
                continue;

            if (!_mindSystem.TryGetMind(uid, out var mindId, out _))
                continue;

            var name = Name(uid);
            var username = GetUsername(mindId);
            var status = GetHealthStatus(uid);
            var enslaved = HasComp<SoulbreakerEnslavedComponent>(uid);

            var text = Loc.GetString(
                enslaved ? "soulbreakers-round-end-user-was-enslaved" : "soulbreakers-round-end-user-remained-free",
                ("name", name),
                ("username", username));

            otherCrew.Add($"{text} {status}");
        }

        if (otherCrew.Count > 0)
        {
            args.AddLine(Loc.GetString("soulbreakers-round-end-crew-status"));
            foreach (var crew in otherCrew)
            {
                args.AddLine(crew);
            }
        }
    }

    private string GetUsername(EntityUid mindId)
    {
        if (_player.TryGetSessionByEntity(mindId, out var session))
            return session.Name;

        if (TryComp<MindComponent>(mindId, out var mind) && mind.CharacterName != null)
            return mind.CharacterName;

        return "Неизвестный";
    }

    private string GetPlayerOOCName(EntityUid uid)
    {
        // OOC имя - это всегда имя сессии
        return _player.TryGetSessionByEntity(uid, out var session)
            ? session.Name
            : "Unknown";
    }

    private string GetPlayerICName(EntityUid uid)
    {
        // IC имя - это имя entity
        return Name(uid);
    }

    private string GetHealthStatus(EntityUid uid)
    {
        return _mobState.IsAlive(uid)
            ? Loc.GetString("soulbreakers-health-status-alive")
            : Loc.GetString("soulbreakers-health-status-dead");
    }

    #endregion

    #region --- Game Rule Events ---

    private void OnEnslavedSold(ref SoulbreakerSomeoneWasSold ev)
    {
        var query = QueryActiveRules();
        while (query.MoveNext(out _, out _, out var soulbreakersRule, out _))
        {
            soulbreakersRule.EnslavedStonks += ev.Price;
            soulbreakersRule.EnslavedCount += 1;

            // Сохраняем информацию о проданном рабе
            soulbreakersRule.SoldSlaves.Add(new SoldSlaveInfo
            {
                Name = Name(ev.Slave),
                Price = ev.Price
            });
        }
    }

    protected override void Started(EntityUid uid, SoulbreakersRuleComponent comp, GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        base.Started(uid, comp, gameRule, args);

        // Инициализируем список проданных рабов
        comp.SoldSlaves = new List<SoldSlaveInfo>();

        // Установим нормальное время проверки
        comp.NextLogicTick = _timing.CurTime + TimeSpan.FromMinutes(5);
    }

    protected override void ActiveTick(EntityUid uid, SoulbreakersRuleComponent comp, GameRuleComponent gameRule, float frameTime)
    {
        base.ActiveTick(uid, comp, gameRule, frameTime);

        if (comp.NextLogicTick is not { } nextCheck || nextCheck > _timing.CurTime)
            return;

        CheckRoundEnd(comp);
        comp.NextLogicTick = _timing.CurTime + comp.EndCheckDelay;
    }

    private void CheckRoundEnd(SoulbreakersRuleComponent comp)
    {
        if (!comp.RoundstartDelayEnded)
        {
            if (_timing.CurTime > comp.RoundstartDelay)
            {
                comp.RoundstartDelayEnded = true;
                AnnounceSoulbreakersArrival();
            }
            return;
        }

        var playerCount = GetPlayerCount() + comp.EnslavedCount;
        var enslavedFraction = playerCount == 0 ? 0 : comp.EnslavedCount / (float)playerCount;

        if (enslavedFraction >= 0.8f && !comp.PlayedSoulbreakersWin)
        {
            _audio.PlayGlobal("/Audio/_Europa/Jukebox/RendySandy/soulbreaker_win.ogg", Filter.Broadcast(), true); // Why the fuck rEndy??
            _roundEnd.EndRound();
            comp.PlayedSoulbreakersWin = true;
        }
        else if (enslavedFraction >= 0.5f && !_roundEnd.IsRoundEndRequested() && !comp.PlayedIslamicTrance)
        {
            _audio.PlayGlobal("/Audio/_Europa/Jukebox/RendySandy/08_Islamic_Trance.ogg", Filter.Broadcast(), true);
            _roundEnd.RequestRoundEnd(null, false);
            comp.PlayedIslamicTrance = true;
        }
    }

    private int GetPlayerCount()
    {
        var count = 0;
        var query = AllEntityQuery<MindContainerComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            if (HasComp<SoulbreakerRoleComponent>(uid) || HasComp<SoulbreakerAssistantRoleComponent>(uid))
                continue;
            count++;
        }
        return count;
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
}
