namespace  codecrafters_redis.src;

public record ItemValue
{
    private double GetCurrentMiliseconds() => TimeSpan.FromTicks(DateTime.UtcNow.Ticks).TotalMilliseconds;
    private double createdAt;
    private bool hasExpiration; 

    public ItemValue(string value, double timeToLive = double.MaxValue)
    {
        Value = value;
        TimeToLive = timeToLive;
        createdAt = GetCurrentMiliseconds();
        hasExpiration = (timeToLive != double.MaxValue);
    }

    public string Value { get; set; }
    public double ValidUntil { get => (hasExpiration) ? createdAt + TimeToLive : double.MaxValue; }
    public double TimeToLive { get; set; }
    public bool IsValid => ValidUntil > GetCurrentMiliseconds();
}