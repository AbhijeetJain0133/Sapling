using Sapling.Shared.Services;

namespace Sapling.Services;

public sealed class MobileFormFactor : IFormFactor
{
    public FormFactorKind Kind => FormFactorKind.Mobile;

    public string Platform => DeviceInfo.Platform.ToString();
}
