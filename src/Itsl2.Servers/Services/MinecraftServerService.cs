using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Itsl2.Core.Models;

namespace Itsl2.Core.Services;

public sealed class MinecraftServerService
{
    public async Task<MinecraftServerStatus> QueryStatusAsync(
        string host,
        int port = 25565,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(host)) throw new ArgumentException("服务器地址不能为空", nameof(host));
        if (port is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));

        using var client = new TcpClient();
        var stopwatch = Stopwatch.StartNew();
        await client.ConnectAsync(host.Trim(), port, cancellationToken);
        await using var stream = client.GetStream();

        await WritePacketAsync(stream, BuildHandshakePacket(host.Trim(), port), cancellationToken);
        await WritePacketAsync(stream, [1, 0], cancellationToken);

        var packet = await ReadPacketAsync(stream, cancellationToken);
        stopwatch.Stop();
        if (packet.Length == 0 || packet[0] != 0) throw new InvalidOperationException("服务器返回了无效的状态响应");

        var jsonLengthOffset = ReadVarInt(packet, 1, out var jsonLength);
        if (jsonLength < 0 || jsonLengthOffset + jsonLength > packet.Length)
            throw new InvalidOperationException("服务器状态数据长度无效");

        var status = JsonSerializer.Deserialize<StatusResponse>(packet.AsSpan(jsonLengthOffset, jsonLength),
                 new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                     ?? throw new InvalidOperationException("服务器状态为空");
        return new MinecraftServerStatus(
            status.Version?.Name ?? "未知版本",
            status.Players?.Online ?? 0,
            status.Players?.Max ?? 0,
            ReadDescription(status.Description),
            (int)stopwatch.ElapsedMilliseconds);
    }

    private static byte[] BuildHandshakePacket(string host, int port)
    {
        using var payload = new MemoryStream();
        payload.WriteByte(0);
        WriteVarInt(payload, 763);
        WriteString(payload, host);
        payload.WriteByte((byte)(port >> 8));
        payload.WriteByte((byte)port);
        WriteVarInt(payload, 1);

        using var packet = new MemoryStream();
        WriteVarInt(packet, checked((int)payload.Length));
        payload.Position = 0;
        payload.CopyTo(packet);
        return packet.ToArray();
    }

    private static async Task WritePacketAsync(Stream stream, byte[] packet, CancellationToken cancellationToken)
    {
        await stream.WriteAsync(packet, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private static async Task<byte[]> ReadPacketAsync(Stream stream, CancellationToken cancellationToken)
    {
        var packetLength = await ReadVarIntAsync(stream, cancellationToken);
        if (packetLength is < 1 or > 2_000_000) throw new InvalidOperationException("服务器数据包长度无效");
        var packet = new byte[packetLength];
        var offset = 0;
        while (offset < packet.Length)
        {
            var read = await stream.ReadAsync(packet.AsMemory(offset), cancellationToken);
            if (read == 0) throw new EndOfStreamException("服务器提前关闭了连接");
            offset += read;
        }

        return packet;
    }

    private static async Task<int> ReadVarIntAsync(Stream stream, CancellationToken cancellationToken)
    {
        var value = 0;
        var shift = 0;
        while (shift < 35)
        {
            var buffer = new byte[1];
            if (await stream.ReadAsync(buffer, cancellationToken) == 0) throw new EndOfStreamException();
            value |= (buffer[0] & 0x7F) << shift;
            if ((buffer[0] & 0x80) == 0) return value;
            shift += 7;
        }

        throw new InvalidOperationException("服务器返回了无效的 VarInt");
    }

    private static int ReadVarInt(ReadOnlySpan<byte> data, int offset, out int value)
    {
        value = 0;
        var shift = 0;
        while (offset < data.Length && shift < 35)
        {
            var current = data[offset++];
            value |= (current & 0x7F) << shift;
            if ((current & 0x80) == 0) return offset;
            shift += 7;
        }

        throw new InvalidOperationException("服务器返回了无效的 VarInt");
    }

    private static void WriteVarInt(Stream stream, int value)
    {
        while ((value & ~0x7F) != 0)
        {
            stream.WriteByte((byte)((value & 0x7F) | 0x80));
            value >>>= 7;
        }
        stream.WriteByte((byte)value);
    }

    private static void WriteString(Stream stream, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        WriteVarInt(stream, bytes.Length);
        stream.Write(bytes);
    }

    private static string ReadDescription(JsonElement description)
    {
        if (description.ValueKind == JsonValueKind.String) return description.GetString() ?? "";
        if (description.ValueKind == JsonValueKind.Object && description.TryGetProperty("text", out var text))
            return text.GetString() ?? "";
        return "";
    }

    private sealed record StatusResponse(
        StatusVersion? Version,
        StatusPlayers? Players,
        JsonElement Description);

    private sealed record StatusVersion(string? Name);
    private sealed record StatusPlayers(int Online, int Max);
}