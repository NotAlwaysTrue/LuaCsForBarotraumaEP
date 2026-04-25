#nullable enable
namespace Barotrauma;

internal sealed partial class CustomMission : Mission
{
    public override bool DisplayAsCompleted => State == SuccessState;
    public override bool DisplayAsFailed => State == FailureState;
}
