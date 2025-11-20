using Robust.Shared.Serialization;

namespace Content.Shared._Europa.Soulbreakers
{
    public abstract partial class SharedSoulbreakerTeleportSystem : EntitySystem
    {
    }

    [ByRefEvent]
    public record struct SoulbreakerSomeoneWasSold(EntityUid Slave, float Price)
    {

    }

    [Serializable, NetSerializable]
    public enum SoulbreakerTeleportationConsoleUiKey : byte
    {
        Key
    }

    [Serializable, NetSerializable]
    public sealed class SoulbreakerTeleportationConsoleUiState : BoundUserInterfaceState
    {
        public bool TeleportAll;

        public List<(NetEntity Entity, string Name)> Targets;
        public NetEntity? Selected;

        public SoulbreakerTeleportationConsoleUiState(
            bool teleportAll,
            List<(NetEntity, string)> targets,
            NetEntity? selected)
        {
            TeleportAll = teleportAll;
            Targets = targets;
            Selected = selected;
        }
    }

    [Serializable, NetSerializable]
    public sealed class ExecuteTeleportationMessage : BoundUserInterfaceMessage
    {
        public ExecuteTeleportationMessage() { }
    }

    [Serializable, NetSerializable]
    public sealed class ChangeTeleportCountMessage : BoundUserInterfaceMessage
    {
        public bool TeleportAll;

        public ChangeTeleportCountMessage(bool teleportAll)
        {
            TeleportAll = teleportAll;
        }
    }

    [Serializable, NetSerializable]
    public sealed class SelectTeleportTargetMessage : BoundUserInterfaceMessage
    {
        public NetEntity Target;
        public SelectTeleportTargetMessage(NetEntity target)
        {
            Target = target;
        }
    }
}
