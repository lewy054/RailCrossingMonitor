using RailCrossingMonitor.Application.RailwayTracks;
using RailCrossingMonitor.Application.Trains;
using RailCrossingMonitor.Model;

namespace RailCrossingMonitor.Application;

public sealed class TrainPredictionService(
    TrainStore trainStore,
    RailwayTrackStore trackStore)
{
    /*
     * Jeżeli ostatnia rzeczywiście zmieniona pozycja
     * jest starsza niż 60 sekund, przestajemy przewidywać.
     */
    private static readonly TimeSpan MaxGpsAge =
        TimeSpan.FromSeconds(60);

    /*
     * To NIE jest przewidywanie 30 sekund do przodu.
     *
     * Jest to maksymalny czas, jaki możemy wykorzystać
     * do odtworzenia pozycji "TERAZ" z opóźnionego GPS.
     */
    private const double MaxPredictionSeconds = 30;

    private const double MinSpeedKmh = 5;
    private const double MaxSpeedKmh = 300;

    public IReadOnlyCollection<PredictedTrainPosition> GetAll()
    {
        var now = DateTimeOffset.UtcNow;

        var result =
            new List<PredictedTrainPosition>();

        foreach (var train in trainStore.GetAll())
        {
            var predicted =
                Predict(train, now);

            if (predicted is not null)
            {
                result.Add(predicted);
            }
        }

        return result;
    }

    public PredictedTrainPosition? Predict(
        TrainPosition train)
    {
        return Predict(
            train,
            DateTimeOffset.UtcNow);
    }

    private PredictedTrainPosition? Predict(
        TrainPosition train,
        DateTimeOffset now)
    {
        if (train.SpeedKmh is not >= MinSpeedKmh)
            return null;

        if (train.SpeedKmh > MaxSpeedKmh)
            return null;

        /*
         * Ile czasu minęło od ostatniej rzeczywiście
         * zmienionej pozycji GPS?
         */
        var gpsAge =
            now - train.ReceivedAtUtc;

        if (gpsAge < TimeSpan.Zero)
        {
            gpsAge = TimeSpan.Zero;
        }

        if (gpsAge > MaxGpsAge)
        {
            return null;
        }

        /*
         * Dopasowanie pociągu do toru.
         */
        var match = trackStore.Match(
            train.Latitude,
            train.Longitude,
            train.HeadingDegrees,
            maxDistanceMeters: 150);

        if (match is null)
        {
            return null;
        }

        /*
         * Przesuwamy pociąg tylko o czas,
         * który faktycznie upłynął od ostatniej zmienionej
         * pozycji GPS.
         *
         * Nie dodajemy żadnych dodatkowych 10 sekund.
         */
        var predictionSeconds =
            Math.Clamp(
                gpsAge.TotalSeconds,
                0,
                MaxPredictionSeconds);

        var predictionDuration =
            TimeSpan.FromSeconds(
                predictionSeconds);

        /*
         * Pozycja GPS
         *      +
         * prędkość
         *      ×
         * czas od GPS
         *      =
         * pozycja pociągu TERAZ
         */
        var predictedDistance =
            trackStore.PredictDistanceAlongTrack(
                match,
                train.SpeedKmh.Value,
                predictionDuration,
                MaxPredictionSeconds);

        var predictedPoint =
            trackStore.GetPointAtDistanceAlongTrack(
                match.Track,
                predictedDistance);

        if (predictedPoint is null)
        {
            return null;
        }

        return new PredictedTrainPosition(
            Id: train.Id,

            Latitude:
                predictedPoint.Value.Latitude,

            Longitude:
                predictedPoint.Value.Longitude,

            DistanceAlongTrackMeters:
                predictedDistance,

            TrackId:
                match.Track.Id,

            SpeedKmh:
                train.SpeedKmh.Value,

            PredictionSeconds:
                predictionSeconds,

            BasedOnGpsAtUtc:
                train.ReceivedAtUtc);
    }
}
