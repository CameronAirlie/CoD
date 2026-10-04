using System;
using System.Collections.Generic;

namespace CoD.Scripts;

/// <summary>Reserves bounded planning slots in rotating order, independent of combat.</summary>
internal sealed class FairBotPlanner
{
    private readonly List<int> _eligible = new();
    private readonly HashSet<int> _granted = new();
    private int _cursor;

    public void BeginFrame(IEnumerable<int> participants, int budget, Func<int, bool> eligible)
    {
        _eligible.Clear();
        _granted.Clear();
        foreach (var id in participants)
            if (eligible(id)) _eligible.Add(id);
        if (_eligible.Count == 0) { _cursor = 0; return; }
        _cursor %= _eligible.Count;
        var count = Math.Clamp(budget, 1, _eligible.Count);
        for (var i = 0; i < count; i++)
            _granted.Add(_eligible[(_cursor + i) % _eligible.Count]);
        _cursor = (_cursor + count) % _eligible.Count;
    }

    public bool CanPlan(int participant) => _granted.Contains(participant);
}
