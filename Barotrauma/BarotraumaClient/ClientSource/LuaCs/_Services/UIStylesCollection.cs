using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using Barotrauma.Extensions;
using Barotrauma.LuaCs.Data;
using FluentResults;
using Microsoft.Toolkit.Diagnostics;

namespace Barotrauma.LuaCs;

public class UIStylesCollection : HashlessFile, IUIStylesCollection
{
    public class Factory : IUIStylesCollection.IFactory
    {
        public IEnumerable<IUIStylesCollection> CreateInstance(IStylesResourceInfo info, IStorageService storageService)
        {
            Guard.IsNotNull(info, nameof(info));
            Guard.IsNotNull(info.OwnerPackage, nameof(info.OwnerPackage));
            if (info.FilePaths.IsDefaultOrEmpty)
            {
                return ImmutableArray<IUIStylesCollection>.Empty;
            }

            var builder = ImmutableArray.CreateBuilder<IUIStylesCollection>();
            foreach (var contentPath in info.FilePaths)
            {
                builder.Add(new UIStylesCollection(contentPath, storageService));
            }
            return builder.ToImmutable();
        }

        public void Dispose()
        {
            //ignore, stateless service
        }

        public bool IsDisposed => false;
    }
    
    private readonly ConcurrentDictionary<string, GUIFont> _fonts = new();
    private readonly ConcurrentDictionary<string, GUISprite> _sprites = new();
    private readonly ConcurrentDictionary<string, GUISpriteSheet> _spriteSheets = new();
    private readonly ConcurrentDictionary<string, GUICursor> _cursors = new();
    private readonly ConcurrentDictionary<string, GUIColor> _colors = new();

    /// <summary>
    /// Only for internal reference.
    /// </summary>
    private UIStyleFile _fakeFile;

    private IStorageService _storageService;
    
    public UIStylesCollection(ContentPath path, IStorageService storageService) : base(path.ContentPackage, path)
    {
        Guard.IsNotNull(path, nameof(path));
        Guard.IsNotNull(path.ContentPackage, nameof(path.ContentPackage));
        _storageService = storageService;
        _fakeFile = new UIStyleFile(path.ContentPackage, path);
    }

    public new ContentPath Path => base.Path;

    public Result<GUIFont> GetFont(string name)
    {
        using var lck =  _lock.AcquireReaderLock().ConfigureAwait(false).GetAwaiter().GetResult();
        IService.CheckDisposed(this);
        if (_fonts.TryGetValue(name, out var asset))
        {
            return asset;
        }

        return FluentResults.Result.Fail($"{nameof(GetFont)}: Failed to find the font with the name '{name}'");
    }

    public Result<GUISprite> GetSprite(string name)
    {
        using var lck =  _lock.AcquireReaderLock().ConfigureAwait(false).GetAwaiter().GetResult();
        IService.CheckDisposed(this);
        if (_sprites.TryGetValue(name, out var asset))
        {
            return asset;
        }

        return FluentResults.Result.Fail($"{nameof(GetSprite)}: Failed to find the sprite with the name '{name}'");
    }

    public Result<GUISpriteSheet> GetSpriteSheet(string name)
    {
        using var lck =  _lock.AcquireReaderLock().ConfigureAwait(false).GetAwaiter().GetResult();
        IService.CheckDisposed(this);
        if (_spriteSheets.TryGetValue(name, out var asset))
        {
            return asset;
        }

        return FluentResults.Result.Fail($"{nameof(GetSpriteSheet)}: Failed to find the spritesheet with the name '{name}'");
    }

    public Result<GUICursor> GetCursor(string name)
    {
        using var lck =  _lock.AcquireReaderLock().ConfigureAwait(false).GetAwaiter().GetResult();
        IService.CheckDisposed(this);
        if (_cursors.TryGetValue(name, out var asset))
        {
            return asset;
        }

        return FluentResults.Result.Fail($"{nameof(GetCursor)}: Failed to find the cursor with the name '{name}'");
    }

    public Result<GUIColor> GetColor(string name)
    {
        using var lck =  _lock.AcquireReaderLock().ConfigureAwait(false).GetAwaiter().GetResult();
        IService.CheckDisposed(this);
        if (_colors.TryGetValue(name, out var asset))
        {
            return asset;
        }

        return FluentResults.Result.Fail($"{nameof(GetColor)}: Failed to find the color with the name '{name}'");
    }

    public override void LoadFile()
    {
        using var lck =  _lock.AcquireWriterLock().ConfigureAwait(false).GetAwaiter().GetResult();
        IService.CheckDisposed(this);

        if (_storageService.LoadPackageXml(Path) is not { IsSuccess: true } result)
        {
            DebugConsole.LogError($"Failed to load xml from {Path.FullPath}.");
            ThrowHelper.ThrowArgumentException($"Failed to load xml from {Path.FullPath}.");
            return;
        }

        var root = result.Value.Root?.FromPackage(Path.ContentPackage);
        if (root is null)
        {
            return;
        }
        
        var styleElement = root.Name.LocalName.ToLowerInvariant() == "style" ? root : root.GetChildElement("style");
        if (styleElement is null)
            return; 
        
        var childElements = styleElement.GetChildElements("Font");
        if (childElements is not null)
            AddToList<GUIFont, GUIFontPrefab>(_fonts, childElements, _fakeFile);

        childElements = styleElement.GetChildElements("Sprite");
        if (childElements is not null)
            AddToList<GUISprite, GUISpritePrefab>(_sprites, childElements, _fakeFile);
        
        childElements = styleElement.GetChildElements("Spritesheet");
        if (childElements is not null)
            AddToList<GUISpriteSheet, GUISpriteSheetPrefab>(_spriteSheets, childElements, _fakeFile);
        
        childElements = styleElement.GetChildElements("Cursor");
        if (childElements is not null)
            AddToList<GUICursor, GUICursorPrefab>(_cursors, childElements, _fakeFile);
        
        childElements = styleElement.GetChildElements("Color");
        if (childElements is not null)
            AddToList<GUIColor, GUIColorPrefab>(_colors, childElements, _fakeFile);
        
        void AddToList<T1, T2>(ConcurrentDictionary<string, T1> dict, IEnumerable<ContentXElement> elem, UIStyleFile file) where T1 : GUISelector<T2> where T2 : GUIPrefab
        {
            foreach (ContentXElement prefabElement in elem)
            {
                string name = prefabElement.GetAttributeString("name", string.Empty);
                if (name != string.Empty)
                {
                    var prefab = (T2)Activator.CreateInstance(typeof(T2), new object[]{ prefabElement, file })!;
                    if (!dict.ContainsKey(name))
                        dict[name] = (T1)Activator.CreateInstance(typeof(T1), new object[] { name })!;
                    dict[name].Prefabs.Add(prefab, false);
                }
            }
        }
    }

    public override void UnloadFile()
    {
        using var lck =  _lock.AcquireWriterLock().ConfigureAwait(false).GetAwaiter().GetResult();
        IService.CheckDisposed(this);
        
        _fonts.Values.ForEach(p => p.Prefabs.RemoveByFile(_fakeFile));
        _sprites.Values.ForEach(p => p.Prefabs.RemoveByFile(_fakeFile));
        _spriteSheets.Values.ForEach(p => p.Prefabs.RemoveByFile(_fakeFile));
        _cursors.Values.ForEach(p => p.Prefabs.RemoveByFile(_fakeFile));
        _colors.Values.ForEach(p => p.Prefabs.RemoveByFile(_fakeFile));
    }

    public override void Sort()
    {
        using var lck =  _lock.AcquireWriterLock().ConfigureAwait(false).GetAwaiter().GetResult();
        IService.CheckDisposed(this);
        
        _fonts.Values.ForEach(p => p.Prefabs.Sort());
        _sprites.Values.ForEach(p => p.Prefabs.Sort());
        _spriteSheets.Values.ForEach(p => p.Prefabs.Sort());
        _cursors.Values.ForEach(p => p.Prefabs.Sort());
        _colors.Values.ForEach(p => p.Prefabs.Sort());
    }

    #region INTERNAL_DISPOSE
    
    private readonly AsyncReaderWriterLock _lock = new();

    public void Dispose()
    {
        using var lck = _lock.AcquireWriterLock().ConfigureAwait(false).GetAwaiter().GetResult();
        if (!ModUtils.Threading.CheckIfClearAndSetBool(ref _isDisposed))
        {
            return;
        }
        
        _fonts.Values.ForEach(p => p.Prefabs.RemoveByFile(_fakeFile));
        _sprites.Values.ForEach(p => p.Prefabs.RemoveByFile(_fakeFile));
        _spriteSheets.Values.ForEach(p => p.Prefabs.RemoveByFile(_fakeFile));
        _cursors.Values.ForEach(p => p.Prefabs.RemoveByFile(_fakeFile));
        _colors.Values.ForEach(p => p.Prefabs.RemoveByFile(_fakeFile));
        
        _fonts.Clear();
        _sprites.Clear();
        _spriteSheets.Clear();
        _cursors.Clear();
        _colors.Clear();
    }

    private int _isDisposed;
    public bool IsDisposed
    {
        get => ModUtils.Threading.GetBool(ref _isDisposed);
        private set => ModUtils.Threading.SetBool(ref _isDisposed, value);
    }

    #endregion
}
