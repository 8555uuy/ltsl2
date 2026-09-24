namespace Itsl2.Core.Models;

public sealed record ModSearchResult(
    string ProjectId,
    string Title,
    string Slug,
    string Description,
    string IconUrl,
    long Downloads,
    string? LatestVersion);