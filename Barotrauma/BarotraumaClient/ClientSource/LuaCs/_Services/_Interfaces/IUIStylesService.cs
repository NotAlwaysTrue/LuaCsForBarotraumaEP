using System.Collections.Immutable;
using Barotrauma.LuaCs.Data;
using FluentResults;

namespace Barotrauma.LuaCs;

public interface IUIStylesService : IReusableService
{
    /// <summary>
    /// Gets the first loaded <see cref="GUIColor"/>.
    /// </summary>
    /// <param name="package">The target <see cref="ContentPackage"/></param>
    /// <param name="internalName">The targets <see cref="IDataInfo.InternalName"/> as specified in the ModConfig.xml.</param>
    /// <param name="assetName">The asset's name as specified in the styles XML file.</param>
    /// <returns>A <see cref="FluentResults.Result"/> indicating success, and the target if succeeded.</returns>
    public Result<GUIColor> GetColor(ContentPackage package, string internalName, string assetName);
    /// <summary>
    /// Gets the loaded <see cref="GUICursor"/>.
    /// </summary>
    /// <param name="package">The target <see cref="ContentPackage"/></param>
    /// <param name="internalName">The targets <see cref="IDataInfo.InternalName"/> as specified in the ModConfig.xml.</param>
    /// <param name="assetName">The asset's name as specified in the styles XML file.</param>
    /// <returns>A <see cref="FluentResults.Result"/> indicating success, and the target if succeeded.</returns>
    public Result<GUICursor> GetCursor(ContentPackage package, string internalName, string assetName);
    /// <summary>
    /// Gets the loaded <see cref="GUIFont"/>.
    /// </summary>
    /// <param name="package">The target <see cref="ContentPackage"/></param>
    /// <param name="internalName">The targets <see cref="IDataInfo.InternalName"/> as specified in the ModConfig.xml.</param>
    /// <param name="assetName">The asset's name as specified in the styles XML file.</param>
    /// <returns>A <see cref="FluentResults.Result"/> indicating success, and the target if succeeded.</returns>
    public Result<GUIFont> GetFont(ContentPackage package, string internalName, string assetName);
    /// <summary>
    /// Gets the loaded <see cref="GUISprite"/>.
    /// </summary>
    /// <param name="package">The target <see cref="ContentPackage"/></param>
    /// <param name="internalName">The targets <see cref="IDataInfo.InternalName"/> as specified in the ModConfig.xml.</param>
    /// <param name="assetName">The asset's name as specified in the styles XML file.</param>
    /// <returns>A <see cref="FluentResults.Result"/> indicating success, and the target if succeeded.</returns>
    public Result<GUISprite> GetSprite(ContentPackage package, string internalName, string assetName);
    /// <summary>
    /// Gets the loaded <see cref="GUISpriteSheet"/>.
    /// </summary>
    /// <param name="package">The target <see cref="ContentPackage"/></param>
    /// <param name="internalName">The targets <see cref="IDataInfo.InternalName"/> as specified in the ModConfig.xml.</param>
    /// <param name="assetName">The asset's name as specified in the styles XML file.</param>
    /// <returns>A <see cref="FluentResults.Result"/> indicating success, and the target if succeeded.</returns>
    public Result<GUISpriteSheet> GetSpriteSheet(ContentPackage package, string internalName, string assetName);
    
    public FluentResults.Result LoadAssets(ImmutableArray<IStylesResourceInfo> resources);
    
    public FluentResults.Result UnloadPackages(ImmutableArray<ContentPackage> packages);
    
    public FluentResults.Result UnloadPackage(ContentPackage package);

    public FluentResults.Result UnloadAllPackages();
}
