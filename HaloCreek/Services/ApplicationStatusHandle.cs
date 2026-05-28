using System;

namespace HaloCreek.Services
{
    public sealed class ApplicationStatusHandle
    {
        internal ApplicationStatusHandle(Guid id)
        {
            Id = id;
        }

        internal Guid Id { get; }
    }
}
