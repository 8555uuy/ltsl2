namespace Itsl2.Core.Models;

public sealed record GameInstance(
    string Name,
    string Version,
    string GameDirectory,
    string? JavaPath = null,
    string? IconPath = null)
{
    public string DisplayPath => string.IsNullOrWhiteSpace(GameDirectory) ? "尚未设置游戏目录" : GameDirectory;
}