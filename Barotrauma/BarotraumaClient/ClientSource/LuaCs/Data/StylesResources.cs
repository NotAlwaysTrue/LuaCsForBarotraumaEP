using System.Collections.Immutable;

namespace Barotrauma.LuaCs.Data;

public interface IStylesResourceInfo : IBaseResourceInfo { }

public record StylesResourceInfo : BaseResourceInfo, IStylesResourceInfo { }

public partial interface IModConfigInfo
{
    public ImmutableArray<IStylesResourceInfo> Styles { get; }
}

public partial record ModConfigInfo
{
    public ImmutableArray<IStylesResourceInfo> Styles { get; init; }
}
