using Content.Shared._Europa.Soulbreakers;
using JetBrains.Annotations;

namespace Content.Client._Europa.Soulbreakers.UI;

[UsedImplicitly]
public sealed class SoulbreakerTeleportationConsoleBoundUi : BoundUserInterface
{
    [ViewVariables]
    private SoulbreakerTeleportationConsoleWindow? _window;

    public SoulbreakerTeleportationConsoleBoundUi(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
        Open();

        _window = new SoulbreakerTeleportationConsoleWindow();
        _window.OpenCentered();

        _window.OnClose += Close;

        _window.ExecuteTeleportButtonPressed += () =>
        {
            SendMessage(new ExecuteTeleportationMessage());
        };

        _window.OneModeButtonPressed += () =>
        {
            SendMessage(new ChangeTeleportCountMessage(false));
        };

        _window.AllModeButtonPressed += () =>
        {
            SendMessage(new ChangeTeleportCountMessage(true));
        };

        _window.OnSelectedTarget += ent =>
        {
            SendMessage(new SelectTeleportTargetMessage(ent));
        };
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (_window == null || state is not SoulbreakerTeleportationConsoleUiState cast)
            return;

        _window.UpdateState(cast);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (!disposing || _window == null)
            return;

        _window.OnClose -= Close;

        _window?.Close();
        _window = null;
    }
}
