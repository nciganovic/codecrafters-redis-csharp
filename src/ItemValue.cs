namespace  codecrafters_redis.src;

public record ItemValue
{
    private double GetCurrentMiliseconds() => TimeSpan.FromTicks(DateTime.UtcNow.Ticks).TotalMilliseconds;
    private double createdAt;
    private bool hasExpiration; 

    public ItemValue(string value, int timeToLive = int.MaxValue)
    {
        Value = value;
        TimeToLive = timeToLive;
        createdAt = GetCurrentMiliseconds();
        hasExpiration = (timeToLive != int.MaxValue);
    }

    public string Value { get; set; }
    public double ValidUntil { get => (hasExpiration) ? createdAt + TimeToLive : double.MaxValue; }
    public int TimeToLive { get; set; }
    public bool IsValid => ValidUntil > GetCurrentMiliseconds();
}