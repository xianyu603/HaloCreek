using System;
using System.Collections.Generic;
using System.Linq;

namespace HaloCreek.Services
{
    public sealed class DragDropService
    {
        public string? GetPrimaryDroppedPath(IEnumerable<string>? paths)
        {
            // MVP1 DragDrop uniformly selects the first dropped object's path and returns it.
            // Future stages can expand this boundary for multiple items and richer drag payloads.
            if (paths is null)
            {
                return null;
            }

            return paths
                .FirstOrDefault(path => !string.IsNullOrWhiteSpace(path))
                ?.Trim();
        }
    }
}
