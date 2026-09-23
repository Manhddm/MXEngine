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
        private readonly ViewCatalog _catalog;
        private readonly UIRoot _uiRoot;
        private readonly SemaphoreSlim _gate = new(1, 1);
        private readonly Stack<OpenView> _screens = new();
        private readonly Stack<OpenView> _modals = new();
        private readonly Dictionary<ViewId, OpenView> _overlays = new();

        public int ScreenCount => _screens.Count;
        public int ModalCount => _modals.Count;
        public bool IsInTransition => _gate.CurrentCount == 0;

        public NavigationService(IViewLoader loader, ViewCatalog catalog, UIRoot uiRoot)
        {
            _loader = loader ?? throw new ArgumentNullException(nameof(loader));
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _uiRoot = uiRoot ?? throw new ArgumentNullException(nameof(uiRoot));
        }

        public async UniTask<TPresenter> ShowScreenAsync<TPresenter, TView, TState>(
            ViewId id, Func<TView, TPresenter> createPresenter, bool stack = true,
            Action<TPresenter> configurePresenter = null)
            where TPresenter : ScreenPresenter<TView, TState>
            where TView : View<TState>
            where TState : ViewState, new()
        {
            await _gate.WaitAsync();
            try
            {
                var (presenter, opened) = await CreateAsync<TPresenter, TView, TState>(id, ViewLayer.Screen,
                    _uiRoot.ScreenRoot, createPresenter, configurePresenter);
                try
                {
                    if (stack)
                    {
                        if (_screens.Count > 0)
                            _screens.Peek().View.gameObject.SetActive(false);
                    }
                    else
                    {
                        while (_screens.Count > 0)
                            await ReleaseAsync(_screens.Pop());
                    }

                    opened.View.gameObject.SetActive(true);
                    _screens.Push(opened);
                    return presenter;
                }
                catch
                {
                    if (_screens.Count > 0)
                        _screens.Peek().View.gameObject.SetActive(true);
                    await ReleaseAsync(opened);
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
            Action<TPresenter> configurePresenter = null)
            where TPresenter : ModalPresenter<TView, TState>
            where TView : View<TState>
            where TState : ViewState, new()
        {
            await _gate.WaitAsync();
            try
            {
                var (presenter, opened) = await CreateAsync<TPresenter, TView, TState>(id, ViewLayer.Modal,
                    _uiRoot.ModalRoot, createPresenter, configurePresenter);
                try
                {
                    opened.View.gameObject.SetActive(true);
                    _modals.Push(opened);
                    return presenter;
                }
                catch
                {
                    await ReleaseAsync(opened);
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
            Action<TPresenter> configurePresenter = null)
            where TPresenter : Presentr<TView, TState>
            where TView : View<TState>
            where TState : ViewState, new()
        {
            await _gate.WaitAsync();
            try
            {
                if (_overlays.ContainsKey(id))
                    throw new InvalidOperationException($"Overlay {id} is already open.");

                var (presenter, opened) = await CreateAsync<TPresenter, TView, TState>(id, ViewLayer.Overlay,
                    _uiRoot.OverlayRoot, createPresenter, configurePresenter);
                try
                {
                    opened.View.gameObject.SetActive(true);
                    _overlays.Add(id, opened);
                    return presenter;
                }
                catch
                {
                    await ReleaseAsync(opened);
                    throw;
                }
            }
            finally
            {
                _gate.Release();
            }
        }

        // Useful for loading overlays with no state or presenter.
        public async UniTask<TView> ShowOverlayAsync<TView>(ViewId id) where TView : Component
        {
            await _gate.WaitAsync();
            try
            {
                if (_overlays.ContainsKey(id))
                    throw new InvalidOperationException($"Overlay {id} is already open.");
                if (_uiRoot.OverlayRoot == null)
                    throw new InvalidOperationException("UI root for Overlay is not assigned.");

                var entry = GetEntry(id, ViewLayer.Overlay);
                var view = await _loader.LoadAsync<TView>(entry.Reference, _uiRoot.OverlayRoot);
                _overlays.Add(id, new OpenView(view, null));
                return view;
            }
            finally
            {
                _gate.Release();
            }
        }

        public async UniTask<bool> BackToPreviousScreenAsync()
        {
            await _gate.WaitAsync();
            try
            {
                if (_screens.Count < 2)
                    return false;

                var opened = _screens.Pop();
                try
                {
                    await ReleaseAsync(opened);
                }
                finally
                {
                    _screens.Peek().View.gameObject.SetActive(true);
                }
                return true;
            }
            finally
            {
                _gate.Release();
            }
        }

        public async UniTask<bool> CloseModalAsync()
        {
            await _gate.WaitAsync();
            try
            {
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

        public async UniTask CloseAllModalsAsync()
        {
            await _gate.WaitAsync();
            try
            {
                while (_modals.Count > 0)
                    await ReleaseAsync(_modals.Pop());
            }
            finally
            {
                _gate.Release();
            }
        }

        public async UniTask<bool> HideOverlayAsync(ViewId id)
        {
            await _gate.WaitAsync();
            try
            {
                if (!_overlays.TryGetValue(id, out var opened))
                    return false;

                _overlays.Remove(id);
                await ReleaseAsync(opened);
                return true;
            }
            finally
            {
                _gate.Release();
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

        private async UniTask<(TPresenter presenter, OpenView opened)> CreateAsync<TPresenter, TView, TState>(
            ViewId id, ViewLayer layer, Transform root, Func<TView, TPresenter> createPresenter,
            Action<TPresenter> configurePresenter)
            where TPresenter : Presentr<TView, TState>
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
                view.gameObject.SetActive(false);
                presenter = createPresenter(view) ??
                    throw new InvalidOperationException($"Presenter factory for {id} returned null.");
                configurePresenter?.Invoke(presenter);
                await presenter.InitializeAsync();
                return (presenter, new OpenView(view, presenter.DisposeAsync));
            }
            catch
            {
                try
                {
                    if (presenter != null)
                        await presenter.DisposeAsync();
                }
                finally
                {
                    await _loader.ReleaseAsync(view);
                }
                throw;
            }
        }

        private async UniTask ReleaseAsync(OpenView opened)
        {
            try
            {
                if (opened.DisposePresenter != null)
                    await opened.DisposePresenter();
            }
            finally
            {
                await _loader.ReleaseAsync(opened.View);
            }
        }

        private sealed class OpenView
        {
            public readonly Component View;
            public readonly Func<UniTask> DisposePresenter;

            public OpenView(Component view, Func<UniTask> disposePresenter)
            {
                View = view;
                DisposePresenter = disposePresenter;
            }
        }
    }
}
