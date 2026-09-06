namespace XrplVanity.App.Core.Components;

public partial class DevicePicker : ViewModelComponent
{
    private bool Busy => Vm.State != SearchState.Idle;
}
