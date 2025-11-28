using System.Net;
using System.Net.Sockets;
using Content.Goobstation.Shared.MisandryBox.Smites;
using Content.Server.Administration;
using Content.Server.Administration.Logs;
using Content.Server.Administration.Managers;
using Content.Shared.Administration.Logs;
using Content.Shared.CCVar;
using Content.Shared.Database;
using Content.Shared.Players.PlayTimeTracking;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Player;
using Serilog;

namespace Content.Server._Europa.Chat;

public sealed class EuropaChatAnnihilator
{
    [Dependency] private readonly IConfigurationManager _configurationManager = default!;
    [Dependency] private readonly IEntitySystemManager _sysMan = default!;
    [Dependency] private readonly IBanManager _banManager = default!;
    [Dependency] private readonly IPlayerLocator _locator = default!;
    [Dependency] private readonly IPlayerManager _playerMan = default!;
    [Dependency] private readonly IAdminManager _admin = default!;
    [Dependency] private readonly IAdminLogManager _adminLog = default!;
    [Dependency] private readonly ISharedPlaytimeManager _playtime = default!;

    private ThunderstrikeSystem? _thunder;
    private bool _doAnnihilate;

    private static readonly List<string> IcShit = new()
    {
        "набег",
        "nabeg",
        "raid",
        "дискорд",
        "http",
        " gg ",
        ".gg",
        "корвакс",
        "корвукс",
        "корвах",
        "корвух",
        "pedal",
        "discord",
        "ахелп",
        "ahelp",
        "sunrise",
        "corvax",
        "squad",
        "сквад",
        "zapret",
        "админ ",
        "админу ",
        "админа ",
        "админам ",
        "админом ",
        "админов ",
        " педал ",
        " педалей ",
        " педали ",
        " педаль ",
        " педалям ",
        " модер ",
        "хелпер",
        "pvrg",
        "illuzorr",
        "卍",
        "卐",
        "\u2591",
        "\u2592",
        "\u2593",
        "\u2588",
        "\u2605",
        "\u2665",
        "\u2606",
        "\u2600",
        "\u2192",
        "\u2190",
        "рыбья станция",
        "fish",
        "reserv",
        "cyber",
        "cerber",
        "щиткур",
        " читы ",
        " чит ",
        ":clown:",
        ":fish:",
        ":earth_africa:",
        ":fire:",
        ":rotating_light:",
        "dscrd",
        "bind",
        "admin",
        "moder",
        "host",
        "nabeb",
        " рп ",
        " хрп ",
        " лрп ",
        " мрп ",
        "ютуб",
        "youtube",
        ".com",
        ".ru",
        "опг рыбное",
        "набенах",
        ":underage:",
        ":alien:",
        " аккич ",
        "ware",
        " варе "
    };

    private static readonly List<string> OocShit = new()
    {
        "nabeg",
        "http",
        "squad",
        "卍",
        "卐",
        "\u2591",
        "\u2592",
        "\u2593",
        "\u2588",
        "\u2605",
        "\u2665",
        "\u2606",
        "\u2600",
        "\u2192",
        "\u2190",
        "reserv",
        "cyber ",
        "cerber",
        " читы ",
        " чит ",
        ":clown:",
        ":fish:",
        ":earth_africa:",
        ":fire:",
        ":rotating_light:",
        "dscrd",
        "nabeb",
        ".com",
        ".ru",
        "опг рыбное",
        "набенах",
        ":underage:",
        ":alien:",
        "ware",
    };

    public bool AnnihilateChudInIc(string message, EntityUid player)
    {
        if (!_doAnnihilate)
            return false;

        if (_admin.IsAdmin(player, true))
            return false;

        foreach (var phrase in IcShit)
        {
            if (!message.ToLower().Contains(phrase))
                continue;

            if (!_playerMan.TryGetSessionByEntity(player, out var session))
                continue;

            if (_playtime.GetPlayTimes(session).TryGetValue(PlayTimeTrackingShared.TrackerOverall, out var playtime)
                && playtime >= TimeSpan.FromHours(100))
                return false;

            _thunder = _sysMan.GetEntitySystemOrNull<ThunderstrikeSystem>();

            if (_thunder != null)
                _thunder.Smite(player);

            MakeLittleBan(session, message);
            return true;
        }

        return false;
    }

    public bool AnnihilateChudInOoc(string message, ICommonSession? session)
    {
        if (!_doAnnihilate)
            return false;

        if (session == null)
            return false;

        if (_admin.IsAdmin(session, true))
            return false;

        if (_playtime.GetPlayTimes(session).TryGetValue(PlayTimeTrackingShared.TrackerOverall, out var playtime)
            && playtime >= TimeSpan.FromHours(100))
            return false;

        foreach (var phrase in OocShit)
        {
            if (!message.ToLower().Contains(phrase))
                continue;

            MakeLittleBan(session, message);
            return true;
        }

        return false;
    }

    private async void MakeLittleBan(ICommonSession player, string banReason)
    {
        (IPAddress, int)? targetIP = null;
        ImmutableTypedHwid? targetHWid = null;

        var sessionData = await _locator.LookupIdAsync(player.UserId);

        if (sessionData != null)
        {
            if (sessionData.LastAddress is not null)
            {
                targetIP = sessionData.LastAddress.AddressFamily is AddressFamily.InterNetwork
                    ? (sessionData.LastAddress, 32) // People with ipv4 addresses get a /32 address so we ban that
                    : (sessionData.LastAddress, 64); // This can only be an ipv6 address. People with ipv6 address should get /64 addresses so we ban that.
            }

            targetHWid = sessionData.LastHWId;
        }

        _adminLog.Add(LogType.EuropaAnnihilatorBrainrot, LogImpact.Extreme, $"Игрок ${player.Name} получил автоматическиую блокировку за такие вот слова: {banReason}");
        _banManager.CreateServerBan(player.UserId, player.Name, null, targetIP, targetHWid, 60, NoteSeverity.High, $"Это автоматический бан, его нельзя обжаловать. Ключевое сообщение: {banReason}");
    }

    public void Initialize()
    {
        _configurationManager.OnValueChanged(CCVars.ChatAnnihilationEnabled, x => _doAnnihilate = x, true);
    }
}
