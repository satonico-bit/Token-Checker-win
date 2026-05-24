namespace TokenChecker.Utilities;

public enum PopupTransparency
{
    Percent0,
    Percent5,
    Percent10,
    Percent15,
    Percent20,
    Percent30,
    Percent40,
}

public static class PopupTransparencyExtensions
{
    public static byte ToAlpha(this PopupTransparency transparency) => transparency switch
    {
        PopupTransparency.Percent0  => 0xFF,
        PopupTransparency.Percent5  => 0xF2,
        PopupTransparency.Percent10 => 0xE6,
        PopupTransparency.Percent15 => 0xD9,
        PopupTransparency.Percent20 => 0xCC,
        PopupTransparency.Percent30 => 0xB3,
        PopupTransparency.Percent40 => 0x99,
        _                           => 0xE6,
    };

    public static string ToLabel(this PopupTransparency transparency) => transparency switch
    {
        PopupTransparency.Percent0  => "0% (不透明)",
        PopupTransparency.Percent5  => "5%",
        PopupTransparency.Percent10 => "10%",
        PopupTransparency.Percent15 => "15%",
        PopupTransparency.Percent20 => "20%",
        PopupTransparency.Percent30 => "30%",
        PopupTransparency.Percent40 => "40% (薄い)",
        _                           => transparency.ToString(),
    };

    public static readonly PopupTransparency Default = PopupTransparency.Percent20;

    public static readonly PopupTransparency[] All =
    [
        PopupTransparency.Percent0,
        PopupTransparency.Percent5,
        PopupTransparency.Percent10,
        PopupTransparency.Percent15,
        PopupTransparency.Percent20,
        PopupTransparency.Percent30,
        PopupTransparency.Percent40,
    ];
}
