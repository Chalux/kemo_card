namespace KemoCard.Mod.Run;

public sealed class PlayerController
{
    public string PlayerId { get; }
    public string DisplayName { get; private set; }
    public bool IsOwner { get; }

    public PlayerController(string playerId, string displayName, bool isOwner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(playerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        PlayerId = playerId;
        DisplayName = displayName;
        IsOwner = isOwner;
    }

    public void SetDisplayName(string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        DisplayName = displayName;
    }

    public PlayerControllerDto ToDto()
    {
        return new PlayerControllerDto
        {
            PlayerId = PlayerId,
            DisplayName = DisplayName,
            IsOwner = IsOwner,
        };
    }
}