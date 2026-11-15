using System;

namespace DumpingGroundMap
{
    /// <summary>Per-invocation, idempotent restoration for Harmony postfix/finalizer pairs.</summary>
    internal sealed class RestoreScope
    {
        private Action restore;
        internal RestoreScope(Action restore) { this.restore = restore; }
        internal void Restore()
        {
            Action action = restore;
            restore = null;
            if (action != null) action();
        }
    }

    internal sealed class RunIdentity
    {
        private readonly object owner;
        private readonly string runId;
        private readonly int slot;

        internal RunIdentity(object owner, string runId, int slot)
        {
            this.owner = owner;
            this.runId = runId;
            this.slot = slot;
        }

        internal bool Matches(object currentOwner, string currentRunId, int currentSlot)
        {
            return ReferenceEquals(owner, currentOwner) && runId == currentRunId && slot == currentSlot;
        }
    }
}
