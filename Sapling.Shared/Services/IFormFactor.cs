namespace Sapling.Shared.Services;

public enum FormFactorKind
{
    Desktop,
    Mobile
}

/// <summary>Chosen by the host app, not by viewport width: the web head is desktop, the MAUI head is mobile.</summary>
public interface IFormFactor
{
    FormFactorKind Kind { get; }

    string Platform { get; }
}

public sealed class DesktopFormFactor : IFormFactor
{
    public FormFactorKind Kind => FormFactorKind.Desktop;

    public string Platform => "Web";
}
