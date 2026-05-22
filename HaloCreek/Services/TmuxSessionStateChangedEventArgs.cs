using System;
using HaloCreek.Models;

namespace HaloCreek.Services
{
    public sealed class TmuxSessionStateChangedEventArgs : EventArgs
    {
        public TmuxSessionStateChangedEventArgs(
            string identifier,
            OngoingSessionState state)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(identifier);

            Identifier = identifier;
            State = state;
        }

        public string Identifier { get; }

        public OngoingSessionState State { get; }
    }
}
