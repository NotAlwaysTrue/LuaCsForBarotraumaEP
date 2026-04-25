using System.Collections.Generic;
using System.Collections.Immutable;
using Barotrauma.LuaCs.Data;
using FluentResults;

namespace Barotrauma.LuaCs;

public interface IUIStylesCollection : IService
{
    public interface IFactory : IService
    {
        /// <summary>
        /// Returns a new <see cref="IUIStylesCollection"/> for-each <see cref="ContentPath"/> in the given
        /// <see cref="IStylesResourceInfo.FilePaths"/> or empty is none.
        /// </summary>
        /// <param name="info"></param>
        /// <param name="storageService"></param>
        /// <returns></returns>
        IEnumerable<IUIStylesCollection> CreateInstance(IStylesResourceInfo info, IStorageService storageService);
    }
    
    /// <summary>
    /// The assigned/target <see cref="ContentPath"/> for this collection.
    /// </summary>
    public ContentPath Path { get; }

    /// <summary>
    /// Gets the <see cref="GUIFont"/> with the given name.
    /// </summary>
    /// <param name="name"></param>
    /// <returns></returns>
    public Result<GUIFont> GetFont(string name);
    /// <summary>
    /// Gets the <see cref="GUISprite"/> with the given name.
    /// </summary>
    /// <param name="name"></param>
    /// <returns></returns>
    public Result<GUISprite> GetSprite(string name);
    /// <summary>
    /// Gets the <see cref="GUISpriteSheet"/> with the given name.
    /// </summary>
    /// <param name="name"></param>
    /// <returns></returns>
    public Result<GUISpriteSheet> GetSpriteSheet(string name);
    /// <summary>
    /// Gets the <see cref="GUICursor"/> with the given name.
    /// </summary>
    /// <param name="name"></param>
    /// <returns></returns>
    public Result<GUICursor> GetCursor(string name);
    /// <summary>
    /// Gets the <see cref="GUIColor"/> with the given name.
    /// </summary>
    /// <param name="name"></param>
    /// <returns></returns>
    public Result<GUIColor> GetColor(string name);
    
    #region BAROTRAUMA.UISTYLEFILE

    /// <summary>
    /// Definition of <see cref="HashlessFile.LoadFile"/>
    /// </summary>
    internal void LoadFile();
    /// <summary>
    /// Definition of <see cref="HashlessFile.UnloadFile"/>
    /// </summary>
    internal void UnloadFile();
    /// <summary>
    /// Definition of <see cref="HashlessFile.Sort"/>
    /// </summary>
    internal void Sort();

    #endregion
    
}
