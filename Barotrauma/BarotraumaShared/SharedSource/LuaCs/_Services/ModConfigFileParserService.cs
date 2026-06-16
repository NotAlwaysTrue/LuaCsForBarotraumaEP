using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using Barotrauma.LuaCs.Data;
using FarseerPhysics.Common;
using FluentResults;
using Microsoft.Toolkit.Diagnostics;

namespace Barotrauma.LuaCs;

public sealed partial class ModConfigFileParserService : 
    IParserServiceAsync<ResourceParserInfo, IAssemblyResourceInfo>, 
    IParserServiceAsync<ResourceParserInfo, ILuaScriptResourceInfo>, 
    IParserServiceAsync<ResourceParserInfo, IConfigResourceInfo>
{
    private IStorageService _storageService;
    private readonly AsyncReaderWriterLock _operationsLock = new();

    public ModConfigFileParserService(IStorageService storageService)
    {
        _storageService = storageService;
        _storageService.UseCaching = false;
    }

    #region Dispose

    public void Dispose()
    {
        using var lck = _operationsLock.AcquireWriterLock().ConfigureAwait(false).GetAwaiter().GetResult();
        if (!ModUtils.Threading.CheckIfClearAndSetBool(ref _isDisposed))
            return;
        try
        {
            _storageService.Dispose();
            this._storageService = null;
        }
        catch
        {
            // ignored
        }
    }

    private int _isDisposed = 0;
    public bool IsDisposed
    {
        get => ModUtils.Threading.GetBool(ref _isDisposed);
        private set => ModUtils.Threading.SetBool(ref _isDisposed, value);
    }

    #endregion

    // --- Assemblies
    async Task<Result<IAssemblyResourceInfo>> IParserServiceAsync<ResourceParserInfo, IAssemblyResourceInfo>.TryParseResourceAsync(ResourceParserInfo src)
    {
        using var lck = await _operationsLock.AcquireReaderLock();
        IService.CheckDisposed(this);
        
        if (CheckThrowNullRefs(src, "Assembly") is { IsFailed: true } fail)
            return fail;

        var isScript = src.Element.GetAttributeBool("IsScript", false);
        var runtimeEnv = GetRuntimeEnvironment(src.Element);
        var fileResults = await UnsafeGetCheckedFiles(src.Element, src.Owner, isScript ? ".cs" : ".dll");
        
        if (fileResults.IsFailed)
            return FluentResults.Result.Fail(fileResults.Errors);

        return new AssemblyResourceInfo()
        {
            SupportedPlatforms = runtimeEnv.Platform,
            SupportedTargets =  runtimeEnv.Target,
            LoadPriority = src.Element.GetAttributeInt("LoadPriority", 0),
            FilePaths = fileResults.Value,
            Optional =  src.Element.GetAttributeBool("Optional", false),
            InternalName = src.Element.GetAttributeString("Name", string.Empty),
            OwnerPackage =  src.Owner,
            RequiredPackages = src.Required,
            IncompatiblePackages =  src.Incompatible,
            // Type Specific
            FriendlyName = src.Element.GetAttributeString("FriendlyName", GetFallbackCompliantAssemblyName(src.Owner)),
            IsScript = isScript,
            UseInternalAccessName = src.Element.GetAttributeBool("UseInternalAccessName", false),
            IsReferenceModeOnly = src.Element.GetAttributeBool("IsReferenceModeOnly", false)
        };
        
        
        // helper methods
        string GetFallbackCompliantAssemblyName(ContentPackage package)
        {
            if (package.Name.IsNullOrWhiteSpace())
            {
                return "FallbackAssemblyName";
            }
            
            // replace non az chars with '_'
            var sanitizedPackageName = Regex.Replace(package.Name, @"[^a-zA-Z0-9_]", "_");
            if (char.IsDigit(sanitizedPackageName[0]))
            {
                sanitizedPackageName = "ASM" + sanitizedPackageName;
            }

            // replace consecutive '_'
            return Regex.Replace(sanitizedPackageName, @"[_.]{2,}", "_");
        }
    }
    
    async Task<ImmutableArray<Result<IAssemblyResourceInfo>>> IParserServiceAsync<ResourceParserInfo, IAssemblyResourceInfo>.TryParseResourcesAsync(IEnumerable<ResourceParserInfo> sources)
    {
        return await this.TryParseGenericResourcesAsync<IAssemblyResourceInfo>(sources);
    }

    // --- Config
    
    async Task<Result<IConfigResourceInfo>> IParserServiceAsync<ResourceParserInfo, IConfigResourceInfo>.TryParseResourceAsync(ResourceParserInfo src)
    {
        using var lck = await _operationsLock.AcquireReaderLock();
        IService.CheckDisposed(this);
        
        if (CheckThrowNullRefs(src, "Config") is { IsFailed: true } fail)
            return fail;

        var runtimeEnv = GetRuntimeEnvironment(src.Element);
        var fileResults = await UnsafeGetCheckedFiles(src.Element, src.Owner, ".xml");
        
        if (fileResults.IsFailed)
            return FluentResults.Result.Fail(fileResults.Errors);

        return new ConfigResourceInfo()
        {
            SupportedPlatforms = runtimeEnv.Platform,
            SupportedTargets =  runtimeEnv.Target,
            LoadPriority = src.Element.GetAttributeInt("LoadPriority", 0),
            FilePaths = fileResults.Value,
            Optional =  src.Element.GetAttributeBool("Optional", false),
            InternalName = src.Element.GetAttributeString("Name", string.Empty),
            OwnerPackage =  src.Owner,
            RequiredPackages = src.Required,
            IncompatiblePackages =  src.Incompatible
        };
    }
    
    async Task<ImmutableArray<Result<IConfigResourceInfo>>> IParserServiceAsync<ResourceParserInfo, IConfigResourceInfo>.TryParseResourcesAsync(IEnumerable<ResourceParserInfo> sources)
    {
        return await this.TryParseGenericResourcesAsync<IConfigResourceInfo>(sources);
    }

    // --- Lua Scripts    
    async Task<Result<ILuaScriptResourceInfo>> IParserServiceAsync<ResourceParserInfo, ILuaScriptResourceInfo>.TryParseResourceAsync(ResourceParserInfo src)
    {
        using var lck = await _operationsLock.AcquireReaderLock();
        IService.CheckDisposed(this);
        
        if (CheckThrowNullRefs(src, "Lua") is { IsFailed: true } fail)
            return fail;

        var runtimeEnv = GetRuntimeEnvironment(src.Element);
        var fileResults = await UnsafeGetCheckedFiles(src.Element, src.Owner, ".lua");
        
        if (fileResults.IsFailed)
            return FluentResults.Result.Fail(fileResults.Errors);

        return new LuaScriptsResourceInfo()
        {
            SupportedPlatforms = runtimeEnv.Platform,
            SupportedTargets =  runtimeEnv.Target,
            LoadPriority = src.Element.GetAttributeInt("LoadPriority", 0),
            FilePaths = fileResults.Value,
            Optional =  src.Element.GetAttributeBool("Optional", false),
            InternalName = src.Element.GetAttributeString("Name", string.Empty),
            OwnerPackage =  src.Owner,
            RequiredPackages = src.Required,
            IncompatiblePackages =  src.Incompatible,
            // Type Specific
            IsAutorun = src.Element.GetAttributeBool("IsAutorun", false),
            RunUnrestricted = src.Element.GetAttributeBool("RunUnrestricted", false)
        };
    }

    private FluentResults.Result CheckThrowNullRefs(ResourceParserInfo src, string elementName)
    {
        Guard.IsNotNull(src, nameof(src));
        Guard.IsNotNull(src.Owner, nameof(src.Owner));
        Guard.IsNotNull(src.Element, nameof(src.Element));
        
        if (src.Element.Name != elementName)
        {
            return FluentResults.Result.Fail($"Element name '{elementName}' is incorrect");
        }
        
        return FluentResults.Result.Ok();
    }

    async Task<ImmutableArray<Result<ILuaScriptResourceInfo>>> IParserServiceAsync<ResourceParserInfo, ILuaScriptResourceInfo>.TryParseResourcesAsync(IEnumerable<ResourceParserInfo> sources)
    {
        return await this.TryParseGenericResourcesAsync<ILuaScriptResourceInfo>(sources);
    }
    
    // --- Helpers
    private async Task<Result<ImmutableArray<ContentPath>>> UnsafeGetCheckedFiles(XElement srcElement, ContentPackage srcOwner, string fileExtension)
    {
        var builder = ImmutableArray.CreateBuilder<ContentPath>();
        var filePath = srcElement.GetAttributeContentPath("File",  srcOwner);
        var folderPath = srcElement.GetAttributeContentPath("Folder",  srcOwner);

        var res = new FluentResults.Result<ImmutableArray<ContentPath>>();
        
        if ((!filePath?.Value.IsNullOrWhiteSpace()) ?? false)
        {
            if (_storageService.FileExists(filePath.FullPath) is { IsSuccess: true, Value: true })
            {
                builder.Add(filePath);
            }
            else
            {
                if (srcElement.GetAttributeBool("IsFileRequired", true))
                {
                    res.WithError($"{srcOwner.Name}: The file '{filePath}' is missing!");    
                }
                else
                {
                    res.WithSuccess($"Skipped missing not-required file: '{filePath}'");
                }
            }
        }

        if ((!folderPath?.Value.IsNullOrWhiteSpace()) ?? false)
        {
            if (_storageService.DirectoryExists(folderPath.FullPath) is { IsSuccess: true, Value: true })
            {
                var searchLocation = System.IO.Path.GetRelativePath(srcOwner.Dir, folderPath.Value);
                var files = _storageService.FindFilesInPackage(srcOwner, searchLocation, "*"+fileExtension, true);
                if (files.IsFailed)
                {
                    res.WithError($"{srcOwner.Name}: Failed to load files from {folderPath}!");
                }
                else
                {
                    foreach (var file in files.Value)
                    {
                        builder.Add(ContentPath.FromRaw(srcOwner, $"%ModDir%/{System.IO.Path.GetRelativePath(System.IO.Path.GetFullPath(srcOwner.Dir), file)}"));
                    }
                }
            }
            else
            {
                if (srcElement.GetAttributeBool("IsFileRequired", true))
                {
                    res.WithError($"{srcOwner.Name}: The file '{folderPath}' is missing!");    
                }
                else
                {
                    res.WithSuccess($"Skipped missing not-required folder: '{folderPath}'");
                }
            }
        }

        return res.WithValue(builder.ToImmutable());
    }    
    private (Platform Platform, Target Target) GetRuntimeEnvironment(XElement element)
    {
        return (
            Platform: element.GetAttributeEnum("Platform", Platform.Any),
            Target: element.GetAttributeEnum("Target", Target.Any));
    }
    
    private async Task<ImmutableArray<Result<T>>> TryParseGenericResourcesAsync<T>(IEnumerable<ResourceParserInfo> sources)
    {
        // ReSharper disable once PossibleMultipleEnumeration
        Guard.IsNotNull(sources,  nameof(IParserServiceAsync<ResourceParserInfo, T>.TryParseResourcesAsync));
        var builder =  ImmutableArray.CreateBuilder<Result<T>>();
        foreach (var info in sources)
        {
            builder.Add(await Unsafe.As<IParserServiceAsync<ResourceParserInfo, T>>(this).TryParseResourceAsync(info));
        }
        return builder.ToImmutable();
    }
    
}
