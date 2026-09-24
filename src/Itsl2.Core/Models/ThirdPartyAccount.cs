namespace Itsl2.Core.Models;

public sealed record ThirdPartyAccount(
    string ServerUrl,
    string Username,
    string ProfileName,
    string ProfileId,
    string AccessToken);