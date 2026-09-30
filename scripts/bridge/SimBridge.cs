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
}
