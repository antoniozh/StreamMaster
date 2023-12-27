﻿namespace StreamMaster.Domain.Common;

public class SimpleIntList
{
    private readonly HashSet<int> intSet;
    private readonly int startingValue;
    private int nextAvailableInt;

    public SimpleIntList(int startingValue)
    {
        intSet = [];
        this.startingValue = startingValue;
        nextAvailableInt = startingValue;
    }

    public void AddInt(int value)
    {
        intSet.Add(value);
    }

    public bool ContainsInt(int value)
    {
        return intSet.Contains(value);
    }

    private readonly object lockObject = new object();

    public int GetNextInt(int? value = null)
    {
        lock (lockObject)
        {
            int desiredValue = value ?? ++nextAvailableInt;

            // Ensure desiredValue is not less than startingValue
            desiredValue = Math.Max(desiredValue, startingValue);

            while (intSet.Contains(desiredValue))
            {
                desiredValue = ++nextAvailableInt;
            }

            intSet.Add(desiredValue);
            return desiredValue;
        }
    }

}
