using Barotrauma;
using Barotrauma.Items.Components;
using Barotrauma.Networking;
using System;
using System.Reflection;
using static Barotrauma.Items.Components.Quality;

namespace Barotrauma;

static class MapEntityExtensions
{
    public static void AddLinked(this MapEntity entity, MapEntity other)
    {
        entity.linkedTo.Add(other);
    }
}


static class ClientExtensions
{
#if SERVER
    public static void SetClientCharacter(this Client client, Character character)
    {
        GameMain.Server.SetClientCharacter(client, character);
    }

    public static void Kick(this Client client, string reason = "")
    {
        GameMain.Server.KickClient(client.Connection, reason);
    }

    public static void Ban(this Client client, string reason = "", float seconds = -1)
    {
        if (seconds == -1)
        {
            GameMain.Server.BanClient(client, reason, null);
        }
        else
        {
            GameMain.Server.BanClient(client, reason, TimeSpan.FromSeconds(seconds));
        }
    }

    public static bool CheckPermission(this Client client, ClientPermissions permissions)
    {
        return client.Permissions.HasFlag(permissions);
    }
#endif
}

static class ItemExtensions
{
    public static object GetComponentString(this Item item, string component)
    {
        Type type = LuaCsSetup.Instance.PluginManagementService
            .GetType("Barotrauma.Items.Components." + component);

        if (type == null)
        {
            return null;
        }

        MethodInfo method = typeof(Item).GetMethod(nameof(Item.GetComponent));
        MethodInfo generic = method.MakeGenericMethod(type);
        return generic.Invoke(item, null);
    }

#if SERVER
    public static object CreateServerEventString(this Item item, string component)
    {
        var comp = item.GetComponentString(component);

        if (comp == null)
            return null;

        MethodInfo method = typeof(Item).GetMethod(
            nameof(Item.CreateServerEvent),
            new Type[] { Type.MakeGenericMethodParameter(0) });

        MethodInfo generic = method.MakeGenericMethod(comp.GetType());
        return generic.Invoke(item, new object[] { comp });
    }

    public static object CreateServerEventString(this Item item, string component, object[] extraData)
    {
        var comp = item.GetComponentString(component);

        if (comp == null)
            return null;

        MethodInfo method = typeof(Item).GetMethod(
            nameof(Item.CreateServerEvent),
            new Type[] { Type.MakeGenericMethodParameter(0), typeof(object[]) });

        MethodInfo generic = method.MakeGenericMethod(comp.GetType());
        return generic.Invoke(item, new object[] { comp, extraData });
    }
#endif
}

static class QualityExtensions
{
    public static void SetValue(this Quality quality, StatType statType, float value)
    {
        quality.statValues[statType] = value;
    }
}
