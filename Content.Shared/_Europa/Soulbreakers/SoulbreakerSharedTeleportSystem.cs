namespace Content.Shared._Europa.Soulbreakers
{
    public abstract partial class SharedSoulbreakerTeleportSystem : EntitySystem
    {
    }

    [ByRefEvent]
    public record struct SoulbreakerSomeoneWasSold(EntityUid Slave, float price)
    {
        public readonly EntityUid Slave = Slave;
    }
}
