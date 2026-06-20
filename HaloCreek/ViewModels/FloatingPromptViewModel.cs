using System;
using HaloCreek.ViewModels.Components;

namespace HaloCreek.ViewModels
{
    public sealed class FloatingPromptViewModel : ViewModelBase, IDisposable
    {
        private bool _isDisposed;

        public FloatingPromptViewModel(PromptInputViewModel promptInput)
        {
            PromptInput = promptInput ?? throw new ArgumentNullException(nameof(promptInput));
        }

        public PromptInputViewModel PromptInput { get; }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            PromptInput.Dispose();
        }
    }
}
