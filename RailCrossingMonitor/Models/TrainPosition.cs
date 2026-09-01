namespace RailCrossingMonitor.Models;

public sealed record TrainPosition(
    long Id,
    double Latitude,
    double Longitude,
    int Status,
    int Info,
    string Carrier,
    string Number,
    object? Code,
    double Angle,
    DateTimeOffset ReceivedAtUtc
);