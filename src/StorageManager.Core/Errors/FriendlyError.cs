namespace StorageManager.Errors;

/// <summary>
/// A plain-language interpretation of an sshfs/ssh failure. <see cref="Raw"/>
/// keeps the original diagnostic text for a "details" region and support logs.
/// </summary>
public sealed record FriendlyError(string Headline, string Guidance, string Raw);
