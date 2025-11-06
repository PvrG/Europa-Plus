using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

public sealed partial class CCVars
{
    /*
     * Europa Chat Annihilator
     */

    /// <summary>
    /// Annihilate this chud. Now.
    /// </summary>
    public static readonly CVarDef<bool> ChatAnnihilationEnabled =
        CVarDef.Create("chat.annihilator_enabled", true, CVar.SERVERONLY);
}
