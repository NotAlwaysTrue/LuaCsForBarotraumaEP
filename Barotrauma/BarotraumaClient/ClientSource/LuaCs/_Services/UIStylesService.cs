using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Linq;
using Barotrauma.LuaCs.Data;
using FluentResults;
using Microsoft.Toolkit.Diagnostics;

namespace Barotrauma.LuaCs;

public class UIStylesService : IUIStylesService
{
    #region DISPOSAL

    public void Dispose()
    {
        using var lck = _lock.AcquireWriterLock().ConfigureAwait(false).GetAwaiter().GetResult();
        if (!ModUtils.Threading.CheckIfClearAndSetBool(ref _isDisposed))
        {
            return;
        }

        foreach (var collection in _stylesCollections.Values.SelectMany(c => c))
        {
            try
            {
                collection.Dispose();
            }
            catch
            {
                //ignored
            }
        }
        
        _stylesCollections.Clear();
        _storageService.Dispose();
        _stylesCollectionFactory.Dispose();
        
        _storageService = null;
        _stylesCollectionFactory  = null;
    }

    private int _isDisposed = 0;
    public bool IsDisposed
    {
        get => ModUtils.Threading.GetBool(ref _isDisposed);
        private set => ModUtils.Threading.SetBool(ref _isDisposed, value);
    }
    public FluentResults.Result Reset()
    {
        using var lck = _lock.AcquireWriterLock().ConfigureAwait(false).GetAwaiter().GetResult();
        IService.CheckDisposed(this);

        var result = FluentResults.Result.Ok();
        
        foreach (var collection in _stylesCollections.Values.SelectMany(c => c))
        {
            try
            {
                collection.Dispose();
            }
            catch (Exception e)
            {
                result.WithError(new ExceptionalError(e));
            }
        }
        
        _stylesCollections.Clear();

        return result;
    }
    
    private readonly AsyncReaderWriterLock _lock = new();

    #endregion

    private IStorageService _storageService;
    private IUIStylesCollection.IFactory _stylesCollectionFactory;
    
    private ConcurrentDictionary<(ContentPackage Package, string InternalName), ImmutableArray<IUIStylesCollection>> 
        _stylesCollections = new();

    public UIStylesService(IUIStylesCollection.IFactory stylesCollectionFactory, IStorageService storageService)
    {
        _stylesCollectionFactory = stylesCollectionFactory;
        _storageService = storageService;
    }

    public Result<GUIColor> GetColor(ContentPackage package, string internalName, string assetName)
    {
        using var lck = _lock.AcquireReaderLock().ConfigureAwait(false).GetAwaiter().GetResult();
        IService.CheckDisposed(this);
        Guard.IsNotNull(package, nameof(package));
        Guard.IsNotNullOrWhiteSpace(internalName, nameof(internalName));
        Guard.IsNotNullOrWhiteSpace(assetName, nameof(assetName));

        if (!_stylesCollections.TryGetValue((package, internalName), out var collection)
            || collection.IsDefaultOrEmpty)
        {
            return FluentResults.Result.Fail(
                $"{nameof(UIStylesService)}: No styles loaded for [ContentPackage].[InternalName] of: [{package.Name}].[{internalName}]");
        }

        var failedResult = new FluentResults.Result();
        
        foreach (var stylesCollection in collection)
        {
            var res = stylesCollection.GetColor(assetName);
            if (res.IsSuccess)
            {
                return res;
            }

            failedResult.WithErrors(res.Errors);
        }
        
        return failedResult;
    }

    public Result<GUICursor> GetCursor(ContentPackage package, string internalName, string assetName)
    {
        using var lck = _lock.AcquireReaderLock().ConfigureAwait(false).GetAwaiter().GetResult();
        IService.CheckDisposed(this);
        Guard.IsNotNull(package, nameof(package));
        Guard.IsNotNullOrWhiteSpace(internalName, nameof(internalName));
        Guard.IsNotNullOrWhiteSpace(assetName, nameof(assetName));
        
        if (!_stylesCollections.TryGetValue((package, internalName), out var collection)
            || collection.IsDefaultOrEmpty)
        {
            return FluentResults.Result.Fail(
                $"{nameof(UIStylesService)}: No styles loaded for [ContentPackage].[InternalName] of: [{package.Name}].[{internalName}]");
        }

        var failedResult = new FluentResults.Result();
        
        foreach (var stylesCollection in collection)
        {
            var res = stylesCollection.GetCursor(assetName);
            if (res.IsSuccess)
            {
                return res;
            }

            failedResult.WithErrors(res.Errors);
        }
        
        return failedResult;
    }

    public Result<GUIFont> GetFont(ContentPackage package, string internalName, string assetName)
    {
        using var lck = _lock.AcquireReaderLock().ConfigureAwait(false).GetAwaiter().GetResult();
        IService.CheckDisposed(this);
        Guard.IsNotNull(package, nameof(package));
        Guard.IsNotNullOrWhiteSpace(internalName, nameof(internalName));
        Guard.IsNotNullOrWhiteSpace(assetName, nameof(assetName));
        
        if (!_stylesCollections.TryGetValue((package, internalName), out var collection)
            || collection.IsDefaultOrEmpty)
        {
            return FluentResults.Result.Fail(
                $"{nameof(UIStylesService)}: No styles loaded for [ContentPackage].[InternalName] of: [{package.Name}].[{internalName}]");
        }

        var failedResult = new FluentResults.Result();
        
        foreach (var stylesCollection in collection)
        {
            var res = stylesCollection.GetFont(assetName);
            if (res.IsSuccess)
            {
                return res;
            }

            failedResult.WithErrors(res.Errors);
        }
        
        return failedResult;
    }

    public Result<GUISprite> GetSprite(ContentPackage package, string internalName, string assetName)
    {
        using var lck = _lock.AcquireReaderLock().ConfigureAwait(false).GetAwaiter().GetResult();
        IService.CheckDisposed(this);
        Guard.IsNotNull(package, nameof(package));
        Guard.IsNotNullOrWhiteSpace(internalName, nameof(internalName));
        Guard.IsNotNullOrWhiteSpace(assetName, nameof(assetName));
        
        if (!_stylesCollections.TryGetValue((package, internalName), out var collection)
            || collection.IsDefaultOrEmpty)
        {
            return FluentResults.Result.Fail(
                $"{nameof(UIStylesService)}: No styles loaded for [ContentPackage].[InternalName] of: [{package.Name}].[{internalName}]");
        }

        var failedResult = new FluentResults.Result();
        
        foreach (var stylesCollection in collection)
        {
            var res = stylesCollection.GetSprite(assetName);
            if (res.IsSuccess)
            {
                return res;
            }

            failedResult.WithErrors(res.Errors);
        }
        
        return failedResult;
    }

    public Result<GUISpriteSheet> GetSpriteSheet(ContentPackage package, string internalName, string assetName)
    {
        using var lck = _lock.AcquireReaderLock().ConfigureAwait(false).GetAwaiter().GetResult();
        IService.CheckDisposed(this);
        Guard.IsNotNull(package, nameof(package));
        Guard.IsNotNullOrWhiteSpace(internalName, nameof(internalName));
        Guard.IsNotNullOrWhiteSpace(assetName, nameof(assetName));
        
        if (!_stylesCollections.TryGetValue((package, internalName), out var collection)
            || collection.IsDefaultOrEmpty)
        {
            return FluentResults.Result.Fail(
                $"{nameof(UIStylesService)}: No styles loaded for [ContentPackage].[InternalName] of: [{package.Name}].[{internalName}]");
        }

        var failedResult = new FluentResults.Result();
        
        foreach (var stylesCollection in collection)
        {
            var res = stylesCollection.GetSpriteSheet(assetName);
            if (res.IsSuccess)
            {
                return res;
            }

            failedResult.WithErrors(res.Errors);
        }
        
        return failedResult;
    }

    public FluentResults.Result LoadAssets(ImmutableArray<IStylesResourceInfo> resources)
    {
        using var lck = _lock.AcquireReaderLock().ConfigureAwait(false).GetAwaiter().GetResult();
        IService.CheckDisposed(this);
        if (resources.IsDefaultOrEmpty)
        {
            ThrowHelper.ThrowArgumentNullException(nameof(resources));
        }

        var operationSuccess = FluentResults.Result.Ok();
        
        foreach (var resource in resources)
        {
            var builder = ImmutableArray.CreateBuilder<IUIStylesCollection>();
            if (_stylesCollections.TryGetValue((resource.OwnerPackage, resource.InternalName), out var collection))
            {
                builder.AddRange(collection);
            }

            try
            {
                var newCollections = _stylesCollectionFactory.CreateInstance(resource, _storageService).ToImmutableArray();
                foreach (var stylesCollection in newCollections)
                {
                    stylesCollection.LoadFile();
                }
                builder.AddRange(newCollections);
            }
            catch (Exception e)
            {
                operationSuccess.WithError(new ExceptionalError(e));
                continue;
            }
            
            _stylesCollections[(resource.OwnerPackage, resource.InternalName)] = builder.ToImmutable();
        }
        
        return operationSuccess;
    }

    public FluentResults.Result UnloadPackages(ImmutableArray<ContentPackage> packages)
    {
        using var lck = _lock.AcquireWriterLock().ConfigureAwait(false).GetAwaiter().GetResult();
        IService.CheckDisposed(this);

        var toRemove = _stylesCollections
            .Select(c => c.Key)
            .Where(c => packages.Contains(c.Package))
            .ToImmutableArray();
        
        var result = FluentResults.Result.Ok();
        
        foreach (var key in toRemove)
        {
            if (_stylesCollections.TryRemove(key, out var collection) && !collection.IsDefaultOrEmpty)
            {
                foreach (var stylesCollection in collection)
                {
                    try
                    {
                        stylesCollection.UnloadFile();
                    }
                    catch (Exception e)
                    {
                        result.WithError(new ExceptionalError(e));
                    }
                }
            }
        }
        
        return result;
    }

    public FluentResults.Result UnloadPackage(ContentPackage package)
    {
        // Yes, this is very cursed/inefficient. We don't care.
        return UnloadPackages(new [] {  package }.ToImmutableArray());
    }

    public FluentResults.Result UnloadAllPackages()
    {
        using var lck = _lock.AcquireWriterLock().ConfigureAwait(false).GetAwaiter().GetResult();
        IService.CheckDisposed(this);
        
        var result = FluentResults.Result.Ok();
        
        foreach (var key in _stylesCollections.Keys.ToImmutableArray())
        {
            if (_stylesCollections.TryRemove(key, out var collection) && !collection.IsDefaultOrEmpty)
            {
                foreach (var stylesCollection in collection)
                {
                    try
                    {
                        stylesCollection.UnloadFile();
                    }
                    catch (Exception e)
                    {
                        result.WithError(new ExceptionalError(e));
                    }
                }
            }
        }
        
        return result;
    }
}
