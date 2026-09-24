using System.Text.Json;

namespace Itsl2.Core.Models;

public sealed record PreparedMinecraft(
    string VersionId,
    string GameDirectory,
    string MainClass,
    string Classpath,
    string AssetsDirectory,
    string AssetsIndex,
    string NativesDirectory,
    JsonElement[] JvmArguments,
    JsonElement[] GameArguments,
    string ClientJarPath);