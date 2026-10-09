using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace MXEngine
{
    /// <summary>Loads Addressable UI components and owns their navigation lifetime.</summary>
    public sealed class NavigationService
    {
        private readonly IViewLoader _loader;
        private readonly UIRoot _uiRoot;
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _overlayGate = new SemaphoreSlim(1, 1);
        private readonly Stack<OpenView> _screens = new Stack<OpenView>();
        private readonly Stack<OpenView> _reorderBuffer = new Stack<OpenView>();
        private readonly Stack<OpenView> _modals = new Stack<OpenView>();
        private readonly Dictionary<string, OpenView> _overlays = new Dictionary<string, OpenView>();
        private readonly List<OpenView> _pendingReleases = new List<OpenView>();
        private readonly object _shutdownSync = new object();
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private UniTaskCompletionSource _shutdownCompletion;
        private volatile bool _shutdownRequested;

        /// <summary>Raised when committed navigation cleanup needs a later retry.</summary>
        public event Action<Exception> CleanupFailed;

        public int ScreenCount { get { return _screens.Count; } }
        public int ModalCount { get { return _modals.Count; } }
        public int OverlayCount { get { return _overlays.Count; } }
        public int PendingReleaseCount { get { return _pendingReleases.Count; } }
        public bool IsInTransition { get { return _gate.CurrentCount == 0 || _overlayGate.CurrentCount == 0; } }

        public NavigationService(IViewLoader loader, UIRoot uiRoot)
        {
            _loader = loader ?? throw new ArgumentNullException(nameof(loader));
            _uiRoot = uiRoot ?? throw new ArgumentNullException(nameof(uiRoot));
        }

        /// <summary>Opens a screen and returns its component instance.</summary>
        public UniTask<T> ShowScreenAsync<T>(string key, bool stack = true,
            CancellationToken cancellationToken = default) where T : MonoBehaviour
        {
            return ShowScreenCoreAsync<T>(key, stack, cancellationToken);
        }

        /// <summary>Opens a modal instance and returns its component instance.</summary>
        public UniTask<T> ShowModalAsync<T>(string key,
            CancellationToken cancellationToken = default) where T : MonoBehaviour
        {
            return ShowModalCoreAsync<T>(key, cancellationToken);
        }

        /// <summary>Opens one overlay for the key and returns its component instance.</summary>
        public UniTask<T> ShowOverlayAsync<T>(string key,
            CancellationToken cancellationToken = default) where T : MonoBehaviour
        {
            return ShowOverlayCoreAsync<T>(key, cancellationToken);
        }

        private async UniTask<T> ShowScreenCoreAsync<T>(string key, bool stack,
            CancellationToken cancellationToken) where T : MonoBehaviour
        {
            ThrowIfUnavailable();
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token))
            {
                var token = linked.Token;
                await _gate.WaitAsync(token);
                try
                {
                    token.ThrowIfCancellationRequested();
                    ValidateKey(key);
                    if (stack && _screens.Count > 0 && _screens.Peek().Key == key)
                        return GetComponent<T>(_screens.Peek(), key);

                    if (stack)
                    {
                        OpenView existing = FindScreen(key);
                        if (existing != null)
                        {
                            T result = GetComponent<T>(existing, key);
                            OpenView current = _screens.Peek();
                            SetActive(current, false);
                            try
                            {
                                SetActive(existing, true);
                                token.ThrowIfCancellationRequested();
                            }
                            catch (Exception operationException)
                            {
                                Exception recovery = null;
                                try { SetActive(existing, false); SetActive(current, true); }
                                catch (Exception recoveryException) { recovery = recoveryException; }
                                if (recovery != null)
                                    throw new AggregateException("Screen reorder recovery failed.",
                                        operationException, recovery);
                                throw;
                            }
                            while (!ReferenceEquals(_screens.Peek(), existing))
                                _reorderBuffer.Push(_screens.Pop());
                            _screens.Pop();
                            while (_reorderBuffer.Count > 0)
                                _screens.Push(_reorderBuffer.Pop());
                            _screens.Push(existing);
                            return result;
                        }
                    }

                    OpenView opened = await CreateAsync<T>(key, _uiRoot.ScreenRoot, token);
                    bool committed = false;
                    try
                    {
                        token.ThrowIfCancellationRequested();
                        if (_screens.Count > 0) SetActive(_screens.Peek(), false);
                        SetActive(opened, true);
                        token.ThrowIfCancellationRequested();
                        if (stack)
                        {
                            _screens.Push(opened);
                            committed = true;
                            return GetComponent<T>(opened, key);
                        }

                        var oldScreens = new List<OpenView>(_screens);
                        _screens.Clear();
                        _screens.Push(opened);
                        committed = true;
                        List<Exception> errors = null;
                        foreach (OpenView oldScreen in oldScreens)
                            AddError(ref errors, await TryReleaseAsync(oldScreen));
                        if (errors != null)
                            ReportCleanupFailure(new AggregateException("Screen replacement cleanup failed.", errors));
                        return GetComponent<T>(opened, key);
                    }
                    catch (Exception operationException)
                    {
                        if (committed) throw;
                        Exception recovery = null;
                        if (_screens.Count > 0)
                        {
                            try { SetActive(_screens.Peek(), true); }
                            catch (Exception exception) { recovery = exception; }
                        }
                        AddError(ref recovery, await TryReleaseAsync(opened));
                        if (recovery != null)
                            throw new AggregateException("Screen open and recovery both failed.",
                                operationException, recovery);
                        throw;
                    }
                }
                finally { _gate.Release(); }
            }
        }

        private async UniTask<T> ShowModalCoreAsync<T>(string key,
            CancellationToken cancellationToken) where T : MonoBehaviour
        {
            ThrowIfUnavailable();
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token))
            {
                var token = linked.Token;
                await _gate.WaitAsync(token);
                try
                {
                    token.ThrowIfCancellationRequested();
                    ValidateKey(key);
                    OpenView opened = await CreateAsync<T>(key, _uiRoot.ModalRoot, token);
                    try
                    {
                        SetActive(opened, true);
                        token.ThrowIfCancellationRequested();
                        _modals.Push(opened);
                        return GetComponent<T>(opened, key);
                    }
                    catch (Exception openException)
                    {
                        Exception cleanup = await TryReleaseAsync(opened);
                        if (cleanup != null)
                            throw new AggregateException("Modal open and cleanup both failed.",
                                openException, cleanup);
                        throw;
                    }
                }
                finally { _gate.Release(); }
            }
        }

        private async UniTask<T> ShowOverlayCoreAsync<T>(string key,
            CancellationToken cancellationToken) where T : MonoBehaviour
        {
            ThrowIfUnavailable();
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token))
            {
                var token = linked.Token;
                await _overlayGate.WaitAsync(token);
                try
                {
                    token.ThrowIfCancellationRequested();
                    ValidateKey(key);
                    if (_overlays.ContainsKey(key))
                        throw new InvalidOperationException("Overlay " + key + " is already open.");
                    OpenView opened = await CreateAsync<T>(key, _uiRoot.OverlayRoot, token);
                    try
                    {
                        SetActive(opened, true);
                        token.ThrowIfCancellationRequested();
                        _overlays.Add(key, opened);
                        return GetComponent<T>(opened, key);
                    }
                    catch (Exception openException)
                    {
                        Exception cleanup = await TryReleaseAsync(opened);
                        if (cleanup != null)
                            throw new AggregateException("Overlay open and cleanup both failed.",
                                openException, cleanup);
                        throw;
                    }
                }
                finally { _overlayGate.Release(); }
            }
        }

        /// <summary>Returns to the previous screen, or false when there is no previous screen.</summary>
        public async UniTask<bool> BackToPreviousScreenAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfUnavailable();
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token))
            {
                var token = linked.Token;
                await _gate.WaitAsync(token);
                try
                {
                    token.ThrowIfCancellationRequested();
                    if (_screens.Count < 2) return false;
                    OpenView opened = _screens.Pop();
                    List<Exception> errors = null;
                    AddError(ref errors, await TryReleaseAsync(opened));
                    try { SetActive(_screens.Peek(), true); }
                    catch (Exception exception) { AddError(ref errors, exception); }
                    if (errors != null) ReportCleanupFailure(new AggregateException("Back navigation cleanup failed.", errors));
                    return true;
                }
                finally { _gate.Release(); }
            }
        }

        /// <summary>Closes the top modal, returning false when none is open.</summary>
        public async UniTask<bool> CloseModalAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfUnavailable();
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token))
            {
                var token = linked.Token;
                await _gate.WaitAsync(token);
                try
                {
                    token.ThrowIfCancellationRequested();
                    if (_modals.Count == 0) return false;
                    Exception error = await TryReleaseAsync(_modals.Pop());
                    if (error != null) ReportCleanupFailure(error);
                    return true;
                }
                finally { _gate.Release(); }
            }
        }

        /// <summary>Closes every open modal and reports cleanup failures.</summary>
        public async UniTask CloseAllModalsAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfUnavailable();
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token))
            {
                var token = linked.Token;
                await _gate.WaitAsync(token);
                try
                {
                    token.ThrowIfCancellationRequested();
                    List<Exception> errors = null;
                    while (_modals.Count > 0)
                        AddError(ref errors, await TryReleaseAsync(_modals.Pop()));
                    if (errors != null) ReportCleanupFailure(new AggregateException("Modal cleanup failed.", errors));
                }
                finally { _gate.Release(); }
            }
        }

        /// <summary>Hides an overlay by key, returning false when it is not open.</summary>
        public async UniTask<bool> HideOverlayAsync(string key,
            CancellationToken cancellationToken = default)
        {
            ThrowIfUnavailable();
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token))
            {
                var token = linked.Token;
                await _overlayGate.WaitAsync(token);
                try
                {
                    token.ThrowIfCancellationRequested();
                    ValidateKey(key);
                    OpenView opened;
                    if (!_overlays.TryGetValue(key, out opened)) return false;
                    _overlays.Remove(key);
                    Exception error = await TryReleaseAsync(opened);
                    if (error != null) ReportCleanupFailure(error);
                    return true;
                }
                finally { _overlayGate.Release(); }
            }
        }

        /// <summary>Stops requests and releases every UI owned by this service.</summary>
        public UniTask ShutdownAsync()
        {
            LifecycleContext.ThrowIfActive();
            lock (_shutdownSync)
            {
                if (_shutdownCompletion != null) return _shutdownCompletion.Task;
                _shutdownRequested = true;
                _shutdownCompletion = new UniTaskCompletionSource();
            }
            _lifetime.Cancel();
            ShutdownCoreAsync().Forget();
            return _shutdownCompletion.Task;
        }

        /// <summary>Retries releases that previously failed.</summary>
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
                    foreach (OpenView opened in _pendingReleases.ToArray())
                        AddError(ref errors, await TryReleaseAsync(opened));
                    if (errors != null) throw new AggregateException("Some UI releases are still pending.", errors);
                }
                finally { _overlayGate.Release(); }
            }
            finally { _gate.Release(); }
        }

        private async UniTask<OpenView> CreateAsync<T>(string key, Transform root,
            CancellationToken token) where T : MonoBehaviour
        {
            if (root == null) throw new InvalidOperationException("UI root is not assigned.");
            T view = null;
            try
            {
                view = await _loader.LoadAsync<T>(key, root, token);
                token.ThrowIfCancellationRequested();
                if (view == null) throw new InvalidOperationException("Loader returned no UI for " + key + ".");
                return new OpenView(key, view);
            }
            catch (Exception openException)
            {
                if (view != null)
                {
                    Exception cleanup = await TryReleaseAsync(new OpenView(key, view));
                    if (cleanup != null)
                        throw new AggregateException("UI creation and cleanup both failed.", openException, cleanup);
                }
                throw;
            }
        }

        private async UniTask<Exception> TryReleaseAsync(OpenView opened)
        {
            try { await ReleaseAsync(opened); return null; }
            catch (Exception exception) { return exception; }
        }

        private async UniTask ReleaseAsync(OpenView opened)
        {
            try { SetActive(opened, false); }
            catch (Exception exception) { ReportCleanupFailure(exception); }
            try
            {
                await _loader.ReleaseAsync(opened.View);
                _pendingReleases.Remove(opened);
            }
            catch
            {
                if (!_pendingReleases.Contains(opened)) _pendingReleases.Add(opened);
                throw;
            }
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
                        foreach (OpenView pending in _pendingReleases.ToArray())
                            AddError(ref errors, await TryReleaseAsync(pending));
                        foreach (OpenView overlay in new List<OpenView>(_overlays.Values))
                            AddError(ref errors, await TryReleaseAsync(overlay));
                        _overlays.Clear();
                        while (_modals.Count > 0) AddError(ref errors, await TryReleaseAsync(_modals.Pop()));
                        while (_screens.Count > 0) AddError(ref errors, await TryReleaseAsync(_screens.Pop()));
                        if (errors != null) throw new AggregateException("Navigation shutdown failed.", errors);
                    }
                    finally { _overlayGate.Release(); }
                }
                finally { _gate.Release(); }
                _shutdownCompletion.TrySetResult();
            }
            catch (Exception exception) { _shutdownCompletion.TrySetException(exception); }
        }

        private OpenView FindScreen(string key)
        {
            foreach (OpenView screen in _screens)
                if (screen.Key == key) return screen;
            return null;
        }

        private static T GetComponent<T>(OpenView opened, string key) where T : MonoBehaviour
        {
            T result = opened.View as T;
            if (result == null)
                throw new InvalidOperationException("Key " + key + " is already owned by " +
                    opened.View.GetType().Name + ", not " + typeof(T).Name + ".");
            return result;
        }

        private static void ValidateKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("UI key is required.", nameof(key));
        }

        private void SetActive(OpenView opened, bool active)
        {
            using (LifecycleContext.Enter())
                if (opened.View != null) opened.View.gameObject.SetActive(active);
        }

        private void ThrowIfUnavailable()
        {
            LifecycleContext.ThrowIfActive();
            if (_shutdownRequested) throw new ObjectDisposedException(nameof(NavigationService));
        }

        private void ReportCleanupFailure(Exception exception)
        {
            Action<Exception> handler = CleanupFailed;
            if (handler == null) { Debug.LogException(exception); return; }
            foreach (Action<Exception> callback in handler.GetInvocationList())
            {
                try { callback(exception); }
                catch (Exception callbackException) { Debug.LogException(callbackException); }
            }
        }

        private static void AddError(ref List<Exception> errors, Exception error)
        {
            if (error != null) (errors ??= new List<Exception>()).Add(error);
        }

        private static void AddError(ref Exception error, Exception additional)
        {
            if (additional == null) return;
            error = error == null ? additional : new AggregateException(error, additional);
        }

        private sealed class OpenView
        {
            internal readonly string Key;
            internal readonly Component View;
            internal OpenView(string key, Component view) { Key = key; View = view; }
        }
    }
}
