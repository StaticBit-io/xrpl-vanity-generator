namespace XrplVanity.App.Core.ViewModels;

public sealed class TargetChoice
{
    public TargetChoice(string id, string name, string details, bool isCpuDevice, bool selected)
    {
        Id = id;
        Name = name;
        Details = details;
        IsCpuDevice = isCpuDevice;
        Selected = selected;
    }

    public string Id { get; }
    public string Name { get; }
    public string Details { get; }
    public bool IsCpuDevice { get; }
    public bool Selected { get; internal set; }
    public bool IsCpu => Id == "cpu";
}
