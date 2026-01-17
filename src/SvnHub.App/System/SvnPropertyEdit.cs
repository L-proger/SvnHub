namespace SvnHub.App.System;

public sealed record SvnPropertyEdit(string Name, string? Value, bool IsDelete)
{
    public static SvnPropertyEdit Set(string name, string value) => new(name, value, IsDelete: false);

    public static SvnPropertyEdit Delete(string name) => new(name, null, IsDelete: true);
}

