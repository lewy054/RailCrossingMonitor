using RailCrossingMonitor.Model;

namespace RailCrossingMonitor.Application.Crossing;

public class CrossingStore
{

    private List<RailwayCrossing> crossings = [];

    public IReadOnlyList<RailwayCrossing> GetAll()
    {

            return crossings.ToList();
        
    }

    public void Set(IEnumerable<RailwayCrossing> railwayCrossings)
    {
        crossings = railwayCrossings.ToList();
        
    }
    
}