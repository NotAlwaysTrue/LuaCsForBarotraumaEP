#nullable enable
namespace Barotrauma;

/// <summary>
/// Defines a mission where the success and failure are determined solely by its state.
/// Intended to be used alongside <see cref="MissionStateAction"/>.
/// </summary>
internal sealed partial class CustomMission(MissionPrefab prefab, Location[] locations, Submarine sub) : Mission(prefab, locations, sub)
{
    public readonly int SuccessState = prefab.ConfigElement.GetAttributeInt(nameof(SuccessState), +1);
    public readonly int FailureState = prefab.ConfigElement.GetAttributeInt(nameof(FailureState), -1);

    public bool RequireDestinationReached = prefab.ConfigElement.GetAttributeBool(nameof(RequireDestinationReached), false);

    protected override bool DetermineCompleted(CampaignMode.TransitionType transitionType) => 
        State == SuccessState && 
        (!RequireDestinationReached || transitionType is CampaignMode.TransitionType.ProgressToNextLocation or CampaignMode.TransitionType.ProgressToNextEmptyLocation);
}
