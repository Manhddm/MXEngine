using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using MXEngine.MVP;
using UnityEngine;

namespace MXEngine
{
    /// <summary>Loads views and owns their presenter lifetimes without a window framework.</summary>
    public sealed class NavigationService
    {
        private readonly IViewLoader _loader;
        private readonly UIRoot _uiRoot;
        private readonly SemaphoreSlim _gate = new(1, 1);
        private readonly SemaphoreSlim _overlayGate = new(1, 1);
        private readonly Stack<OpenView> _screens = new();
        private readonly Stack<OpenView> _screenReorderBuffer = new();
        private readonly Stack<OpenView> _modals = new();
        private readonly Dictionary<string, OpenView> _overlays = new();
        private readonly object _shutdownSync = new();
        private UniTaskCompletionSource _shutdownCompletion;
        private volatile bool _shutdownRequested;
        private readonly CancellationTokenSource _lifetime = new();
        private readonly List<OpenView> _pendingReleases = new();

        /// <summary>Post-commit cleanup errors do not turn successful navigation into failure.</summary>
        public event Action<Exception> CleanupFailed;

        public int ScreenCount => _screens.Count;
        public int ModalCount => _modals.Count;
        public int OverlayCount => _overlays.Count;
        public int PendingReleaseCount => _pendingReleases.Count;
        public bool IsInTransition => _gate.CurrentCount == 0 || _overlayGate.CurrentCount == 0;

        public NavigationService(IViewLoader loader, UIRoot uiRoot)
        {
            _loader = loader ?? throw new ArgumentNullException(nameof(loader));
            _uiRoot = uiRoot ?? throw new ArgumentNullException(nameof(uiRoot));
        }

        public async UniTask<TPresenter> ShowScreenAsync<TPresenter, TView, TState>(
            string key, Func<TView, TPresenter> createPresenter, bool stack = true,
            Action<TPresenter> configurePresenter = null, CancellationToken cancellationToken = default)
            where TPresenter : ScreenPresenter<TView, TState>
            where TView : View<TState>
            where TState : ViewState, new()
        {
            ThrowIfUnavailable();
            using var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
            cancellationToken = requestCancellation.Token;
            await _gate.WaitAsync(cancellationToken);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                ThrowIfUnavailable();
                ValidateKey(key);
                if (stack && _screens.Count > 0 && _screens.Peek().Key == key)
                    return GetScreenPresenter<TPresenter>(_screens.Peek(), key);

                if (stack)
                {
                    OpenView existing = null;
                    foreach (var screen in _screens)
                    {
                        if (screen.Key != key)
                            continue;
                        existing = screen;
                        break;
                    }

                    if (existing != null)
                    {
                        var existingPresenter = GetScreenPresenter<TPresenter>(existing, key);
                        var current = _screens.Peek();
                        SetActive(current, false);
                        try
                        {
                            SetActive(existing, true);
                            cancellationToken.ThrowIfCancellationRequested();
                        }
                        catch (Exception operationException)
                        {
                            Exception recoveryException = null;
                            try { SetActive(existing, false); SetActive(current, true); }
                            catch (Exception exception) { recoveryException = exception; }
                            if (recoveryException != null)
                                throw new AggregateException("Screen reorder recovery failed.",
                                    operationException, recoveryException);
                            throw;
                        }
                        while (!ReferenceEquals(_screens.Peek(), existing))
                            _screenReorderBuffer.Push(_screens.Pop());
                        _screens.Pop();
                        while (_screenReorderBuffer.Count > 0)
                            _screens.Push(_screenReorderBuffer.Pop());
                        _screens.Push(existing);
                        return existingPresenter;
                    }
                }

                var (presenter, opened) = await CreateAsync<TPresenter, TView, TState>(key, "Screen",
                    _uiRoot.ScreenRoot, createPresenter, configurePresenter, cancellationToken);
                var committed = false;
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ThrowIfShuttingDown();
                    if (stack)
                    {
                        if (_screens.Count > 0)
                            SetActive(_screens.Peek(), false);

                        SetActive(opened, true);
                        cancellationToken.ThrowIfCancellationRequested();
                        _screens.Push(opened);
                        committed = true;
                        return presenter;
                    }

                    // A replace commits the new screen before releasing old screens. Teardown
                    // failures cannot roll back an Addressables instance that was released.
                    if (_screens.Count > 0)
                        SetActive(_screens.Peek(), false);
                    SetActive(opened, true);
                    cancellationToken.ThrowIfCancellationRequested();
                    var oldScreens = new List<OpenView>(_screens.Count);
                    while (_screens.Count > 0)
                        oldScreens.Add(_screens.Pop());
                    _screens.Push(opened);
                    committed = true;

                    List<Exception> errors = null;
                    foreach (var oldScreen in oldScreens)
                    {
                        try
                        {
                            await ReleaseAsync(oldScreen);
                        }
                        catch (Exception exception)
                        {
                            (errors ??= new List<Exception>()).Add(exception);
                        }
                    }

                    if (errors != null)
                        ReportCleanupFailure(new AggregateException("Screen replacement teardown failed.", errors));
                    return presenter;
                }
                catch (Exception operationException)
                {
                    if (committed)
                        throw;

                    Exception recoveryException = null;
                    if (_screens.Count > 0)
                    {
                        try
                        {
                            SetActive(_screens.Peek(), true);
                        }
                        catch (Exception exception)
                        {
                            recoveryException = exception;
                        }
                    }
                    try
                    {
                        await ReleaseAsync(opened);
                    }
                    catch (Exception exception)
                    {
                        recoveryException = recoveryException == null
                            ? exception : new AggregateException(recoveryException, exception);
                    }
                    if (recoveryException != null)
                        throw new AggregateException("Screen open and recovery both failed.",
                            operationException, recoveryException);
                    throw;
                }
            }
            finally
            {
                _gate.Release();
            }
        }

        public async UniTask<TPresenter> ShowModalAsync<TPresenter, TView, TState>(
            string key, Func<TView, TPresenter> createPresenter,
            Action<TPresenter> configurePresenter = null, CancellationToken cancellationToken = default)
            where TPresenter : ModalPresenter<TView, TState>
            where TView : View<TState>
            where TState : ViewState, new()
        {
            ThrowIfUnavailable();
            using var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
            cancellationToken = requestCancellation.Token;
            await _gate.WaitAsync(cancellationToken);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                ThrowIfUnavailable();
                var (presenter, opened) = await CreateAsync<TPresenter, TView, TState>(key, "Modal",
                    _uiRoot.ModalRoot, createPresenter, configurePresenter, cancellationToken);
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ThrowIfShuttingDown();
                    SetActive(opened, true);
                    cancellationToken.ThrowIfCancellationRequested();
                    _modals.Push(opened);
                    return presenter;
                }
                catch (Exception openException)
                {
                    try
                    {
                        await ReleaseAsync(opened);
                    }
                    catch (Exception cleanupException)
                    {
                        throw new AggregateException("Modal open and cleanup both failed.",
                            openException, cleanupException);
                    }
                    throw;
                }
            }
            finally
            {
                _gate.Release();
            }
        }

        public async UniTask<TPresenter> ShowOverlayAsync<TPresenter, TView, TState>(
            string key, Func<TView, TPresenter> createPresenter,
            Action<TPresenter> configurePresenter = null, CancellationToken cancellationToken = default)
            where TPresenter : Presenter<TView, TState>
            where TView : View<TState>
            where TState : ViewState, new()
        {
            ThrowIfUnavailable();
            using var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
            cancellationToken = requestCancellation.Token;
            await _overlayGate.WaitAsync(cancellationToken);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                ThrowIfUnavailable();
                ValidateKey(key);
                if (_overlays.ContainsKey(key))
                    throw new InvalidOperationException($"Overlay {key} is already open.");

                var (presenter, opened) = await CreateAsync<TPresenter, TView, TState>(key, "Overlay",
                    _uiRoot.OverlayRoot, createPresenter, configurePresenter, cancellationToken);
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ThrowIfShuttingDown();
                    SetActive(opened, true);
                    cancellationToken.ThrowIfCancellationRequested();
                    _overlays.Add(key, opened);
                    return presenter;
                }
                catch (Exception openException)
                {
                    try
                    {
                        await ReleaseAsync(opened);
                    }
                    catch (Exception cleanupException)
                    {
                        throw new AggregateException("Overlay open and cleanup both failed.",
                            openException, cleanupException);
                    }
                    throw;
                }
            }
            finally
            {
                _overlayGate.Release();
            }
        }

        // Useful for loading overlays with no state or presenter.
        public async UniTask<TView> ShowOverlayAsync<TView>(string key,
            CancellationToken cancellationToken = default) where TView : Component
        {
            ThrowIfUnavailable();
            using var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
            cancellationToken = requestCancellation.Token;
            await _overlayGate.WaitAsync(cancellationToken);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                ThrowIfUnavailable();
                ValidateKey(key);
                if (_overlays.ContainsKey(key))
                    throw new InvalidOperationException($"Overlay {key} is already open.");
                if (_uiRoot.OverlayRoot == null)
                    throw new InvalidOperationException("UI root for Overlay is not assigned.");

                var view = await _loader.LoadAsync<TView>(key, _uiRoot.OverlayRoot, cancellationToken);
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ThrowIfShuttingDown();
                    if (view == null)
                        throw new InvalidOperationException($"Loader returned no View for {key}.");
                    SetActive(view, true);
                    cancellationToken.ThrowIfCancellationRequested();
                    _overlays.Add(key, new OpenView(key, view, null, null));
                    return view;
                }
                catch (Exception openException)
                {
                    try
                    {
                        await ReleaseAsync(new OpenView(key, view, null, null));
                    }
                    catch (Exception cleanupException)
                    {
                        throw new AggregateException("Overlay open and cleanup both failed.",
                            openException, cleanupException);
                    }
                    throw;
                }
            }
            finally
            {
                _overlayGate.Release();
            }
        }

        public async UniTask<bool> BackToPreviousScreenAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfUnavailable();
            using var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
            cancellationToken = requestCancellation.Token;
            await _gate.WaitAsync(cancellationToken);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                ThrowIfUnavailable();
                if (_screens.Count < 2)
                    return false;

                var opened = _screens.Pop();
                List<Exception> errors = null;
                AddError(ref errors, await TryReleaseAsync(opened));
                try { SetActive(_screens.Peek(), true); }
                catch (Exception exception) { AddError(ref errors, exception); }
                if (errors != null)
                    ReportCleanupFailure(new AggregateException("Back navigation cleanup failed.", errors));
                return true;
            }
            finally
            {
                _gate.Release();
            }
        }

        public async UniTask<bool> CloseModalAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfUnavailable();
            using var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
            cancellationToken = requestCancellation.Token;
            await _gate.WaitAsync(cancellationToken);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                ThrowIfUnavailable();
                if (_modals.Count == 0)
                    return false;

                var error = await TryReleaseAsync(_modals.Pop());
                if (error != null) ReportCleanupFailure(error);
                return true;
            }
            finally
            {
                _gate.Release();
            }
        }

        public async UniTask CloseAllModalsAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfUnavailable();
            using var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
            cancellationToken = requestCancellation.Token;
            await _gate.WaitAsync(cancellationToken);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                ThrowIfUnavailable();
                List<Exception> errors = null;
                while (_modals.Count > 0)
                {
                    try
                    {
                        await ReleaseAsync(_modals.Pop());
                    }
                    catch (Exception exception)
                    {
                        (errors ??= new List<Exception>()).Add(exception);
                    }
                }
                if (errors != null)
                    ReportCleanupFailure(new AggregateException("Modal teardown failed.", errors));
            }
            finally
            {
                _gate.Release();
            }
        }

        public async UniTask<bool> HideOverlayAsync(string key,
            CancellationToken cancellationToken = default)
        {
            ThrowIfUnavailable();
            using var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
            cancellationToken = requestCancellation.Token;
            await _overlayGate.WaitAsync(cancellationToken);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                ThrowIfUnavailable();
                ValidateKey(key);
                if (!_overlays.TryGetValue(key, out var opened))
                    return false;

                _overlays.Remove(key);
                var error = await TryReleaseAsync(opened);
                if (error != null) ReportCleanupFailure(error);
                return true;
            }
            finally
            {
                _overlayGate.Release();
            }
        }

        private static void ValidateKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("View key is required.", nameof(key));
        }

        private static TPresenter GetScreenPresenter<TPresenter>(OpenView screen, string key)
        {
            if (screen.Presenter is TPresenter presenter)
                return presenter;
            throw new InvalidOperationException($"Screen {key} was opened with a different presenter type.");
        }

        private async UniTask<(TPresenter presenter, OpenView opened)> CreateAsync<TPresenter, TView, TState>(
            string key, string layer, Transform root, Func<TView, TPresenter> createPresenter,
            Action<TPresenter> configurePresenter, CancellationToken cancellationToken)
            where TPresenter : Presenter<TView, TState>
            where TView : View<TState>
            where TState : ViewState, new()
        {
            if (createPresenter == null)
                throw new ArgumentNullException(nameof(createPresenter));
            ValidateKey(key);
            if (root == null)
                throw new InvalidOperationException($"UI root for {layer} is not assigned.");

            var view = await _loader.LoadAsync<TView>(key, root, cancellationToken);
            TPresenter presenter = null;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                ThrowIfShuttingDown();
                if (view == null)
                    throw new InvalidOperationException($"Loader returned no View for {key}.");
                SetActive(view, false);
                using (LifecycleContext.Enter())
                {
                    presenter = createPresenter(view) ??
                        throw new InvalidOperationException($"Presenter factory for {key} returned null.");
                    configurePresenter?.Invoke(presenter);
                }
                await presenter.InitializeAsync(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                ThrowIfShuttingDown();
                return (presenter, new OpenView(key, view, presenter, presenter.DisposeAsync));
            }
            catch (Exception createException)
            {
                try
                {
                    await ReleaseAsync(new OpenView(key, view, presenter,
                        presenter == null ? null : presenter.DisposeAsync));
                }
                catch (Exception cleanupException)
                {
                    throw new AggregateException("View creation and cleanup both failed.",
                        createException, cleanupException);
                }
                throw;
            }
        }

        private async UniTask ReleaseAsync(OpenView opened)
        {
            List<Exception> presenterErrors = null;
            try
            {
                SetActive(opened, false);
            }
            catch (Exception exception)
            {
                (presenterErrors ??= new List<Exception>()).Add(exception);
            }
            if (!opened.DisposeAttempted && opened.DisposePresenter != null)
            {
                opened.DisposeAttempted = true;
                try { await opened.DisposePresenter(); }
                catch (Exception exception) { (presenterErrors ??= new List<Exception>()).Add(exception); }
            }
            try
            {
                await _loader.ReleaseAsync(opened.View);
                _pendingReleases.Remove(opened);
            }
            catch (Exception releaseException)
            {
                if (!_pendingReleases.Contains(opened))
                    _pendingReleases.Add(opened);
                if (presenterErrors != null)
                    throw new AggregateException("Presenter and View teardown both failed.",
                        new AggregateException(presenterErrors), releaseException);
                throw;
            }
            ThrowIfErrors("View lifecycle teardown failed.", presenterErrors);
        }

        /// <summary>Stop accepting requests and release every View owned by this service.</summary>
        public UniTask ShutdownAsync()
        {
            LifecycleContext.ThrowIfActive();
            lock (_shutdownSync)
            {
                if (_shutdownCompletion != null)
                    return _shutdownCompletion.Task;

                _shutdownRequested = true;
                _shutdownCompletion = new UniTaskCompletionSource();
            }

            try { _lifetime.Cancel(); }
            catch (Exception exception) { ReportCleanupFailure(exception); }
            ShutdownCoreAsync().Forget();
            return _shutdownCompletion.Task;
        }

        private async UniTask ShutdownCoreAsync()
        {
            try
            {
                await _gate.WaitAsync();
                try
                {
                    await _overlayGate.WaitAsync();
                    try
                    {
                        List<Exception> errors = null;
                        var pending = _pendingReleases.ToArray();
                        foreach (var opened in pending)
                            AddError(ref errors, await TryReleaseAsync(opened));
                        var overlays = new List<OpenView>(_overlays.Values);
                        _overlays.Clear();
                        foreach (var opened in overlays)
                            AddError(ref errors, await TryReleaseAsync(opened));
                        while (_modals.Count > 0)
                            AddError(ref errors, await TryReleaseAsync(_modals.Pop()));
                        while (_screens.Count > 0)
                            AddError(ref errors, await TryReleaseAsync(_screens.Pop()));

                        ThrowIfErrors("Navigation shutdown failed to release some views.", errors);
                    }
                    finally
                    {
                        _overlayGate.Release();
                    }
                }
                finally
                {
                    _gate.Release();
                }

                _shutdownCompletion.TrySetResult();
            }
            catch (Exception exception)
            {
                _shutdownCompletion.TrySetException(exception);
            }
        }

        /// <summary>Retry resources whose loader release failed, including after shutdown.</summary>
        public async UniTask RetryPendingReleasesAsync()
        {
            LifecycleContext.ThrowIfActive();
            await _gate.WaitAsync();
            try
            {
                await _overlayGate.WaitAsync();
                try
                {
                    List<Exception> errors = null;
                    foreach (var opened in _pendingReleases.ToArray())
                        AddError(ref errors, await TryReleaseAsync(opened));
                    ThrowIfErrors("Some View releases are still pending.", errors);
                }
                finally { _overlayGate.Release(); }
            }
            finally { _gate.Release(); }
        }

        private void SetActive(OpenView opened, bool active) => SetActive(opened.View, active);

        private void SetActive(Component view, bool active)
        {
            using (LifecycleContext.Enter())
                if (view != null)
                view.gameObject.SetActive(active);
        }

        private void ThrowIfUnavailable()
        {
            ThrowIfShuttingDown();
            LifecycleContext.ThrowIfActive();
        }

        private void ReportCleanupFailure(Exception exception)
        {
            using var context = LifecycleContext.Enter();
            var handlers = CleanupFailed;
            if (handlers == null) { Debug.LogException(exception); return; }
            foreach (Action<Exception> handler in handlers.GetInvocationList())
            {
                try { handler(exception); }
                catch (Exception diagnosticException) { Debug.LogException(diagnosticException); }
            }
        }

        private void ThrowIfShuttingDown()
        {
            if (_shutdownRequested)
                throw new ObjectDisposedException(nameof(NavigationService));
        }

        private async UniTask<Exception> TryReleaseAsync(OpenView opened)
        {
            try
            {
                await ReleaseAsync(opened);
                return null;
            }
            catch (Exception exception)
            {
                return exception;
            }
        }

        private static void AddError(ref List<Exception> errors, Exception exception)
        {
            if (exception != null)
                (errors ??= new List<Exception>()).Add(exception);
        }

        private static void ThrowIfErrors(string message, List<Exception> errors)
        {
            if (errors != null && errors.Count > 0)
                throw new AggregateException(message, errors);
        }

        private sealed class OpenView
        {
            public readonly string Key;
            public readonly Component View;
            public readonly object Presenter;
            public readonly Func<UniTask> DisposePresenter;
            public bool DisposeAttempted;

            public OpenView(string key, Component view, object presenter, Func<UniTask> disposePresenter)
            {
                Key = key;
                View = view;
                Presenter = presenter;
                DisposePresenter = disposePresenter;
            }
        }
    }
}
