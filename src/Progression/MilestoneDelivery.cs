using System;
using System.Collections.Generic;

namespace DumpingGroundMap
{
    /// <summary>Do not record receipt unless every part of the gift has a place.</summary>
    internal static class MilestoneDelivery
    {
        internal static bool Give<T>(bool includeToken, Func<T> createToken, Func<T> createNote,
            Func<T, bool> place, Action<List<T>> rollback, Action markReceived) where T : class
        {
            List<T> created = new List<T>();
            bool committed = false;
            try
            {
                if (includeToken)
                {
                    T token = createToken();
                    if (token == null) return false;
                    created.Add(token);
                    if (!place(token)) return false;
                }
                T note = createNote();
                if (note == null) return false;
                created.Add(note);
                if (!place(note)) return false;
                markReceived();
                committed = true;
                return true;
            }
            finally
            {
                if (!committed && created.Count > 0) rollback(created);
            }
        }
    }
}
