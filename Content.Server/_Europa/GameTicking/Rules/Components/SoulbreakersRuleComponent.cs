using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Server._Europa.GameTicking.Rules.Components;

[RegisterComponent, Access(typeof(SoulbreakersRuleSystem))]
public sealed partial class SoulbreakersRuleComponent : Component
{
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))]
    public TimeSpan? NextLogicTick;

    [DataField]
    public TimeSpan EndCheckDelay = TimeSpan.FromSeconds(30);

    [DataField]
    public bool RoundstartDelayEnded = false;

    [DataField]
    public TimeSpan RoundstartDelay = TimeSpan.FromSeconds(30);

    [DataField]
    public float EnslavedShuttleCallPercentage = 0.5f;

    [DataField]
    public int EnslavedCount = 0;

    [DataField]
    public float EnslavedStonks = 0;
}
