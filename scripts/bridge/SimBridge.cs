using Godot;
using LoveAndHonor.Sim;

namespace LoveAndHonor.Bridge;

/// <summary>
/// Thin Godot-facing wrapper around the sim core. GDScript talks to the simulation only through
/// bridge nodes like this one; the sim library itself never references Godot.
/// </summary>
[GlobalClass]
public partial class SimBridge : Node
{
    public string GetSimDescription() => SimInfo.Describe();

    /// <summary>Saved games for the boot screen (§31), newest first.</summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> ListSaves() => GameHost.ListSaves();
}
