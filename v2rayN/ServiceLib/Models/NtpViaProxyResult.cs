namespace ServiceLib.Models;
public class NtpViaProxyResult
{
    public bool Success { get; set; }
    public TimeSpan? RoundTripTime { get; set; }
    public DateTime? ServerTimeUtc { get; set; }
    public string? StatusMessage { get; set; }
    public string? ErrorDetail { get; set; }
}
