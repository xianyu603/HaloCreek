using System;
using HaloCreek.Services.Completions;
using HaloCreek.ViewModels;
using HaloCreek.ViewModels.Components;
using HaloCreek.Views;

namespace HaloCreek.Services
{
    public sealed class FloatingPromptService : IDisposable
    {
        private readonly FloatingPromptViewModel _viewModel;
        private readonly FloatingPromptWindow _window;
        private bool _isDisposed;

        public FloatingPromptService(
            SessionLifecycleService sessionLifecycleService,
            AppCommonRuntime appCommonRuntime,
            CompletionCoordinator completionCoordinator)
        {
            _viewModel = new FloatingPromptViewModel(
                new PromptInputViewModel(
                    sessionLifecycleService,
                    appCommonRuntime,
                    completionCoordinator));
            _window = new FloatingPromptWindow
            {
                DataContext = _viewModel,
            };
        }

        public void ShowOrActivate()
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(FloatingPromptService));
            }

            _window.Show();
            _window.Activate();
            _window.FocusPromptInput();
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            _window.CloseForDispose();
            _viewModel.Dispose();
        }
    }
}
