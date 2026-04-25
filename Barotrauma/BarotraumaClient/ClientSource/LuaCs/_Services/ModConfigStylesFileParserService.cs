using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading.Tasks;
using Barotrauma.LuaCs.Data;
using FluentResults;

namespace Barotrauma.LuaCs;

public sealed partial class ModConfigFileParserService :
    IParserServiceAsync<ResourceParserInfo, IStylesResourceInfo>
{
    async Task<Result<IStylesResourceInfo>> IParserServiceAsync<ResourceParserInfo, IStylesResourceInfo>.TryParseResourceAsync(ResourceParserInfo src)
    {
        using var lck = await _operationsLock.AcquireReaderLock();
        IService.CheckDisposed(this);
        
        if (CheckThrowNullRefs(src, "Style") is { IsFailed: true } fail)
            return fail;

        var runtimeEnv = GetRuntimeEnvironment(src.Element);
        var fileResults = await UnsafeGetCheckedFiles(src.Element, src.Owner, ".xml");
        
        if (fileResults.IsFailed)
            return FluentResults.Result.Fail(fileResults.Errors);

        return new StylesResourceInfo()
        {
            SupportedPlatforms = runtimeEnv.Platform,
            SupportedTargets =  Target.Client,  // clientside only
            LoadPriority = src.Element.GetAttributeInt("LoadPriority", 0),
            FilePaths = fileResults.Value,
            Optional =  src.Element.GetAttributeBool("Optional", false),
            InternalName = src.Element.GetAttributeString("Name", string.Empty),
            OwnerPackage =  src.Owner,
            RequiredPackages = src.Required,
            IncompatiblePackages =  src.Incompatible
        };
    }

    public async Task<ImmutableArray<Result<IStylesResourceInfo>>> TryParseResourcesAsync(IEnumerable<ResourceParserInfo> sources)
    {
        return await this.TryParseGenericResourcesAsync<IStylesResourceInfo>(sources);
    }
}
