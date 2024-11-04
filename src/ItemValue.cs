namespace  codecrafters_redis.src;

public record ItemValue
{
    public string Value { get; set; }
    public double ValidUntil { get; set; }
}