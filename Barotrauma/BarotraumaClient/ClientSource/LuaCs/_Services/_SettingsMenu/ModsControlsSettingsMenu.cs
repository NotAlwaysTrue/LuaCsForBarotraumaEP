namespace Barotrauma.LuaCs;

internal sealed class ModsControlsSettingsMenu : ModsSettingsMenuBase
{
    public ModsControlsSettingsMenu(GUIFrame contentFrame, 
        IPackageManagementService packageManagementService, 
        IConfigService configService, 
        SettingsMenu settingsMenuInstance) : base(contentFrame, packageManagementService, configService, settingsMenuInstance)
    {
        
    }

    protected override void DisposeInternal()
    {
        // TODO: Finish this later.
    }

    public override void ApplyInstalledModChanges()
    {
        // TODO: Finish this later.
    }
}
