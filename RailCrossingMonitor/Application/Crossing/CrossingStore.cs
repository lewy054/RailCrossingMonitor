using RailCrossingMonitor.Model;

namespace RailCrossingMonitor.Application.Crossing;

public class CrossingStore
{
    private readonly object _lock = new();

    private List<RailwayCrossing> _crossings = [];

    public IReadOnlyList<RailwayCrossing> GetAll()
    {
        lock (_lock)
        {
            return _crossings.ToList();
        }
    }

    public void Set(IEnumerable<RailwayCrossing> crossings)
    {
        lock (_lock)
        {
            _crossings = crossings.ToList();
        }
    }

    public bool IsLoaded
    {
        get
        {
            lock (_lock)
            {
                return _crossings.Count > 0;
            }
        }
    }
}