using System.Collections.Generic;
using MXEngine.MVP;
using UnityEngine;

namespace MXEngine
{
    public sealed class NavigationService
    {
        private readonly IViewLoader _loader;
        private readonly ViewCatalog _catalog;
        private readonly UIRoot _uiRoot;

        private readonly Stack<ViewEntry> _screens = new();
        private readonly Stack<ViewEntry> _modals = new();
        
        public NavigationService(IViewLoader loader, ViewCatalog catalog, UIRoot uiRoot)
        {
            _loader = loader;
            _catalog = catalog;
            _uiRoot = uiRoot;
        }
    }
}