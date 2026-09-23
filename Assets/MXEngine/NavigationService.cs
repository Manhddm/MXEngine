using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
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
        private readonly ViewCatalog _catalog;
        private readonly UIRoot _uiRoot;
        private readonly SemaphoreSlim _gate = new(1, 1);
        private readonly SemaphoreSlim _overlayGate = new(1, 1);
        private readonly Stack<OpenView> _screens = new();
        private readonly Stack<OpenView> _screenReorderBuffer = new();
        private readonly Stack<OpenView> _modals = new();
        private readonly Dictionary<ViewId, OpenView> _overlays = new();
        private readonly object _shutdownSync = new();
        private UniTaskCompletionSource _shutdownCompletion;
        private volatile bool _shutdownRequested;
        private int _activeLifecycleHooks;

        public int ScreenCount => _screens.Count;
        public int ModalCount => _modals.Count;
        public bool IsInTransition => _gate.CurrentCount == 0 || _overlayGate.CurrentCount == 0;

        public NavigationService(IViewLoader loader, ViewCatalog catalog, UIRoot uiRoot)
        {
            _loader = loader ?? throw new ArgumentNullException(nameof(loader));
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _uiRoot = uiRoot ?? throw new ArgumentNullException(nameof(uiRoot));
        }

        public async UniTask<TPresenter> ShowScreenAsync<TPresenter, TView, TState>(
            ViewId id, Func<TView, TPresenter> createPresenter, bool stack = true,
            Action<TPresenter> configurePresenter = null, CancellationToken cancellationToken = default)
            where TPresenter : ScreenPresenter<TView, TState>
            where TView : View<TState>
            where TState : ViewState, new()
        {
            ThrowIfUnavailable();
            await _gate.WaitAsync(cancellationToken);
            try
            {
                ThrowIfUnavailable();
                cancellationToken.ThrowIfCancellationRequested();
                if (stack && _screens.Count > 0 && _screens.Peek().Id == id)
                    return GetScreenPresenter<TPresenter>(_screens.Peek(), id);

                if (stack)
                {
                    OpenView existing = null;
                    foreach (var screen in _screens)
                    {
                        if (screen.Id != id)
                            continue;
                        existing = screen;
                        break;
                    }

                    if (existing != null)
                    {
                        var existingPresenter = GetScreenPresenter<TPresenter>(existing, id);
                        while (!ReferenceEquals(_screens.Peek(), existing))
                            _screenReorderBuffer.Push(_screens.Pop());
                        _screens.Pop();
                        while (_screenReorderBuffer.Count > 0)
                            _screens.Push(_screenReorderBuffer.Pop());
                        SetActive(_screens.Peek(), false);
                        _screens.Push(existing);
                        SetActive(existing, true);
                        return existingPresenter;
                    }
                }

                var (presenter, opened) = await CreateAsync<TPresenter, TView, TState>(id, ViewLayer.Screen,
                    _uiRoot.ScreenRoot, createPresenter, configurePresenter, cancellationToken);
                var committed = false;
                try
                {
                    ThrowIfShuttingDown();
                    cancellationToken.ThrowIfCancellationRequested();
                    if (stack)
                    {
                        if (_screens.Count > 0)
                            SetActive(_screens.Peek(), false);

                        SetActive(opened, true);
                        _screens.Push(opened);
                        committed = true;
                        return presenter;
                    }

                    // A replace commits the new screen before releasing old screens. Teardown
                    // failures cannot roll back an Addressables instance that was released.
                    if (_screens.Count > 0)
                        SetActive(_screens.Peek(), false);
                    SetActive(opened, true);
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

                    ThrowIfErrors("Screen replacement teardown failed.", errors);
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
            ViewId id, Func<TView, TPresenter> createPresenter,
            Action<TPresenter> configurePresenter = null, CancellationToken cancellationToken = default)
            where TPresenter : ModalPresenter<TView, TState>
            where TView : View<TState>
            where TState : ViewState, new()
        {
            ThrowIfUnavailable();
            await _gate.WaitAsync(cancellationToken);
            try
            {
                ThrowIfUnavailable();
                var (presenter, opened) = await CreateAsync<TPresenter, TView, TState>(id, ViewLayer.Modal,
                    _uiRoot.ModalRoot, createPresenter, configurePresenter, cancellationToken);
                try
                {
                    ThrowIfShuttingDown();
                    cancellationToken.ThrowIfCancellationRequested();
                    SetActive(opened, true);
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
            ViewId id, Func<TView, TPresenter> createPresenter,
            Action<TPresenter> configurePresenter = null, CancellationToken cancellationToken = default)
            where TPresenter : Presenter<TView, TState>
            where TView : View<TState>
            where TState : ViewState, new()
        {
            ThrowIfUnavailable();
            await _overlayGate.WaitAsync(cancellationToken);
            try
            {
                ThrowIfUnavailable();
                if (_overlays.ContainsKey(id))
                    throw new InvalidOperationException($"Overlay {id} is already open.");

                var (presenter, opened) = await CreateAsync<TPresenter, TView, TState>(id, ViewLayer.Overlay,
                    _uiRoot.OverlayRoot, createPresenter, configurePresenter, cancellationToken);
                try
                {
                    ThrowIfShuttingDown();
                    cancellationToken.ThrowIfCancellationRequested();
                    SetActive(opened, true);
                    _overlays.Add(id, opened);
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
        public async UniTask<TView> ShowOverlayAsync<TView>(ViewId id,
            CancellationToken cancellationToken = default) where TView : Component
        {
            ThrowIfUnavailable();
            await _overlayGate.WaitAsync(cancellationToken);
            try
            {
                ThrowIfUnavailable();
                if (_overlays.ContainsKey(id))
                    throw new InvalidOperationException($"Overlay {id} is already open.");
                if (_uiRoot.OverlayRoot == null)
                    throw new InvalidOperationException("UI root for Overlay is not assigned.");

                var entry = GetEntry(id, ViewLayer.Overlay);
                var view = await _loader.LoadAsync<TView>(entry.Reference, _uiRoot.OverlayRoot);
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ThrowIfShuttingDown();
                    if (view == null)
                        throw new InvalidOperationException($"Loader returned no View for {id}.");
                    SetActive(view, true);
                    _overlays.Add(id, new OpenView(id, view, null, null));
                    return view;
                }
                catch (Exception openException)
                {
                    try
                    {
                        await _loader.ReleaseAsync(view);
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
            await _gate.WaitAsync(cancellationToken);
            try
            {
                ThrowIfUnavailable();
                if (_screens.Count < 2)
                    return false;

                var opened = _screens.Pop();
                try
                {
                    await ReleaseAsync(opened);
                }
                finally
                {
                    SetActive(_screens.Peek(), true);
                }
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
            await _gate.WaitAsync(cancellationToken);
            try
            {
                ThrowIfUnavailable();
                if (_modals.Count == 0)
                    return false;

                await ReleaseAsync(_modals.Pop());
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
            await _gate.WaitAsync(cancellationToken);
            try
            {
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
                ThrowIfErrors("Modal teardown failed.", errors);
            }
            finally
            {
                _gate.Release();
            }
        }

        public async UniTask<bool> HideOverlayAsync(ViewId id,
            CancellationToken cancellationToken = default)
        {
            ThrowIfUnavailable();
            await _overlayGate.WaitAsync(cancellationToken);
            try
            {
                ThrowIfUnavailable();
                if (!_overlays.TryGetValue(id, out var opened))
                    return false;

                _overlays.Remove(id);
                await ReleaseAsync(opened);
                return true;
            }
            finally
            {
                _overlayGate.Release();
            }
        }

        private ViewEntry GetEntry(ViewId id, ViewLayer layer)
        {
            var entry = _catalog.Get(id);
            if (entry == null)
                throw new InvalidOperationException($"View {id} is not registered in the catalog.");
            if (entry.Layer != layer)
                throw new InvalidOperationException($"View {id} belongs to {entry.Layer}, not {layer}.");
            if (entry.Reference == null || !entry.Reference.RuntimeKeyIsValid())
                throw new InvalidOperationException($"View {id} has no valid Addressable reference.");
            return entry;
        }

        private static TPresenter GetScreenPresenter<TPresenter>(OpenView screen, ViewId id)
        {
            if (screen.Presenter is TPresenter presenter)
                return presenter;
            throw new InvalidOperationException($"Screen {id} was opened with a different presenter type.");
        }

        private async UniTask<(TPresenter presenter, OpenView opened)> CreateAsync<TPresenter, TView, TState>(
            ViewId id, ViewLayer layer, Transform root, Func<TView, TPresenter> createPresenter,
            Action<TPresenter> configurePresenter, CancellationToken cancellationToken)
            where TPresenter : Presenter<TView, TState>
            where TView : View<TState>
            where TState : ViewState, new()
        {
            if (createPresenter == null)
                throw new ArgumentNullException(nameof(createPresenter));
            if (root == null)
                throw new InvalidOperationException($"UI root for {layer} is not assigned.");

            var entry = GetEntry(id, layer);
            var view = await _loader.LoadAsync<TView>(entry.Reference, root);
            TPresenter presenter = null;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                ThrowIfShuttingDown();
                if (view == null)
                    throw new InvalidOperationException($"Loader returned no View for {id}.");
                SetActive(view, false);
                presenter = createPresenter(view) ??
                    throw new InvalidOperationException($"Presenter factory for {id} returned null.");
                configurePresenter?.Invoke(presenter);
                Interlocked.Increment(ref _activeLifecycleHooks);
                try
                {
                    await presenter.InitializeAsync();
                }
                finally
                {
                    Interlocked.Decrement(ref _activeLifecycleHooks);
                }
                cancellationToken.ThrowIfCancellationRequested();
                ThrowIfShuttingDown();
                return (presenter, new OpenView(id, view, presenter, presenter.DisposeAsync));
            }
            catch (Exception createException)
            {
                Exception cleanupException = null;
                try
                {
                    if (presenter != null)
                        await DisposePresenterAsync(presenter.DisposeAsync);
                }
                catch (Exception exception)
                {
                    cleanupException = exception;
                }
                try
                {
                    await _loader.ReleaseAsync(view);
                }
                catch (Exception exception)
                {
                    cleanupException = cleanupException == null
                        ? exception : new AggregateException(cleanupException, exception);
                }
                if (cleanupException != null)
                    throw new AggregateException("View creation and cleanup both failed.",
                        createException, cleanupException);
                throw;
            }
        }

        private async UniTask ReleaseAsync(OpenView opened)
        {
            Exception presenterException = null;
            try
            {
                if (opened.DisposePresenter != null)
                    await DisposePresenterAsync(opened.DisposePresenter);
            }
            catch (Exception exception)
            {
                presenterException = exception;
            }
            try
            {
                await _loader.ReleaseAsync(opened.View);
            }
            catch (Exception releaseException)
            {
                if (presenterException != null)
                    throw new AggregateException("Presenter and View teardown both failed.",
                        presenterException, releaseException);
                throw;
            }
            if (presenterException != null)
                ExceptionDispatchInfo.Capture(presenterException).Throw();
        }

        /// <summary>Stop accepting requests and release every View owned by this service.</summary>
        public UniTask ShutdownAsync()
        {
            lock (_shutdownSync)
            {
                if (_shutdownCompletion != null)
                    return _shutdownCompletion.Task;

                _shutdownRequested = true;
                _shutdownCompletion = new UniTaskCompletionSource();
            }

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

        private async UniTask DisposePresenterAsync(Func<UniTask> disposePresenter)
        {
            Interlocked.Increment(ref _activeLifecycleHooks);
            try
            {
                await disposePresenter();
            }
            finally
            {
                Interlocked.Decrement(ref _activeLifecycleHooks);
            }
        }

        private void SetActive(OpenView opened, bool active) => SetActive(opened.View, active);

        private void SetActive(Component view, bool active)
        {
            Interlocked.Increment(ref _activeLifecycleHooks);
            try
            {
                view.gameObject.SetActive(active);
            }
            finally
            {
                Interlocked.Decrement(ref _activeLifecycleHooks);
            }
        }

        private void ThrowIfUnavailable()
        {
            ThrowIfShuttingDown();
            if (Volatile.Read(ref _activeLifecycleHooks) != 0)
                throw new InvalidOperationException(
                    "Navigation cannot be requested from a View or Presenter lifecycle callback.");
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
            public readonly ViewId Id;
            public readonly Component View;
            public readonly object Presenter;
            public readonly Func<UniTask> DisposePresenter;

            public OpenView(ViewId id, Component view, object presenter, Func<UniTask> disposePresenter)
            {
                Id = id;
                View = view;
                Presenter = presenter;
                DisposePresenter = disposePresenter;
            }
        }
    }
}
