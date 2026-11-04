#nullable enable
using System;
using System.Collections.Generic;

namespace PersistentDebris;

public sealed class DebrisGrindingProgress
{
    private static readonly TimeSpan RequiredContact = TimeSpan.FromSeconds(0.5);

    private readonly Dictionary<int, TimeSpan> _contactTimes = new();
    private readonly HashSet<int> _completed = new();

    public bool Advance(int debrisId, TimeSpan elapsed)
    {
        if (_completed.Contains(debrisId))
        {
            return false;
        }

        if (!_contactTimes.TryGetValue(debrisId, out TimeSpan previous))
        {
            _contactTimes[debrisId] = TimeSpan.Zero;
            return false;
        }

        TimeSpan contactTime = previous + elapsed;
        _contactTimes[debrisId] = contactTime;
        if (contactTime < RequiredContact)
        {
            return false;
        }

        _completed.Add(debrisId);
        return true;
    }

    public void Retain(ISet<int> activeDebrisIds)
    {
        var inactiveDebrisIds = new List<int>();
        foreach (int debrisId in _contactTimes.Keys)
        {
            if (!activeDebrisIds.Contains(debrisId))
            {
                inactiveDebrisIds.Add(debrisId);
            }
        }

        foreach (int debrisId in inactiveDebrisIds)
        {
            _contactTimes.Remove(debrisId);
            _completed.Remove(debrisId);
        }
    }

    public void Clear()
    {
        _contactTimes.Clear();
        _completed.Clear();
    }
}
