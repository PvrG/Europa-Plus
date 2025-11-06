using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

public sealed partial class CCVars
{
    /*
     * Boosty
     */

    /// <summary>
    /// Url to our donate platform
    /// </summary>
    public static readonly CVarDef<string> BoostyUrl =
        CVarDef.Create("tts.boosty_url", "https://boosty.to/europa14/", CVar.SERVER | CVar.REPLICATED);
}
