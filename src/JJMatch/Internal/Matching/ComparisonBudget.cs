namespace JJMatch;

// Per-call state only. Limits fail the operation; they never degrade a comparison to an approximate result.
internal sealed class ComparisonBudget
{
    private readonly JJMatchSettings _settings;
    private readonly CancellationToken _cancellationToken;
    private long _remaining;
    private int _nodes;

    internal ComparisonBudget(JJMatchSettings settings, CancellationToken cancellationToken)
    {
        _settings = settings;
        _remaining = settings.MaxWork;
        _cancellationToken = cancellationToken;
    }

    internal void Spend(long work = 1)
    {
        _cancellationToken.ThrowIfCancellationRequested();
        if (work > _remaining) throw new InvalidOperationException("Comparison exceeded JJMatchSettings.MaxWork.");
        _remaining -= work;
    }

    internal void VisitNode()
    {
        Spend();
        if (++_nodes > _settings.MaxNodes) throw new InvalidOperationException("Comparison exceeded JJMatchSettings.MaxNodes.");
    }

    internal void Assignment(int left, int right)
    {
        Spend((long)left * right);
        if (Math.Max(left, right) > _settings.MaxAssignmentSize)
            throw new InvalidOperationException("Comparison exceeded JJMatchSettings.MaxAssignmentSize. Increase the limit.");
    }
}
