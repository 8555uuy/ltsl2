namespace Itsl2.Core.Models;

public sealed record ServerBookmark(string Name, string Host, int Port = 25565)
{
    public string Address => Port == 25565 ? Host : $"{Host}:{Port}";
}

public sealed record MinecraftServerStatus(
    string Version,
    int OnlinePlayers,
    int MaximumPlayers,
    string Description,
    int LatencyMilliseconds);