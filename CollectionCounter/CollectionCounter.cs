using UnityEngine;
using HarmonyLib;
using System.Collections.Generic;
using UnityEngine.SceneManagement;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using Steamworks;
using UnityEngine.UI;
using RaftModLoader;
using HMLLibrary;

public class CollectionCounter : Mod
{
    static Dictionary<Item_Base, int> items = new Dictionary<Item_Base, int>();
    static Dictionary<ulong, Dictionary<Item_Base, int>> playerItems = new Dictionary<ulong, Dictionary<Item_Base, int>>();
    static Dictionary<Item_Base, int> changes = new Dictionary<Item_Base, int>();
    static CanvasHelper _can = null;
    public static CanvasHelper canvas
    {
        get
        {
            if (_can == null)
                _can = ComponentManager<CanvasHelper>.Value;
            return _can;
        }
    }
    public static RectTransform imageContainer;
    static Text display;
    static Text display2;
    static Text hud;
    static bool holdToShow;
    static string showStats;
    static bool showing;
    Harmony harmony;
    public void Start()
    {
        harmony = new Harmony("com.aidanamite.CollectionCounter");
        harmony.PatchAll();
        SceneManager.sceneLoaded += (x, y) =>
        {
            if (x.name == global::Raft_Network.MenuSceneName)
            {
                items.Clear();
                playerItems.Clear();
            }
        };
        Log("Mod has been loaded!");
    }

    public override void WorldEvent_WorldLoaded()
    {
        CreateUI();
        if (global::Raft_Network.IsHost)
        {
            System.Func<string, Dictionary<Item_Base, int>> getItems = (x) =>
            {
                var d = new Dictionary<Item_Base, int>();
                if (int.TryParse(ExtraSettingsAPI_GetDataValue("collected", x + "item_count"), out var c))
                    for (int i = 0; i < c; i++)
                    {
                        var j = ItemManager.GetItemByName(ExtraSettingsAPI_GetDataValue("collected", x + "item_" + i + "_id"));
                        if (j && int.TryParse(ExtraSettingsAPI_GetDataValue("collected", x + "item_" + i + "_amt"), out var k))
                            d.Add(j, k);

                    }
                return d;
            };
            items = getItems("");
            if (int.TryParse(ExtraSettingsAPI_GetDataValue("collected", "player_count"), out var pc))
                for (int i = 0; i < pc; i++)
                    if (ulong.TryParse(ExtraSettingsAPI_GetDataValue("collected", "player_" + i + "_id"), out var j))
                        playerItems.Add(j, getItems("player_" + i + "_"));
        }
    }

    public override void WorldEvent_OnPlayerConnected(CSteamID steamid, RGD_Settings_Character characterSettings)
    {
        if (global::Raft_Network.IsHost && ComponentManager<global::Raft_Network>.Value.HostID != steamid)
        {
            new Message_ChangeItems(items, Message_ChangeItems.Type.World).Message.Send(steamid);
            if (playerItems.ContainsKey(steamid.m_SteamID))
                new Message_ChangeItems(playerItems[steamid.m_SteamID], Message_ChangeItems.Type.Host).Message.Send(steamid);
        }
    }

    public void OnModUnload()
    {
        if (imageContainer)
            Destroy(imageContainer.gameObject);
        if (hud)
            Destroy(hud.gameObject);
        harmony.UnpatchAll(harmony.Id);
        Log("Mod has been unloaded!");
    }

    void Update()
    {
        bool flag = false;
        if (changes.Count != 0)
        {
            if (global::Raft_Network.IsHost)
                new Message_ChangeItems(changes, Message_ChangeItems.Type.World).Message.Broadcast();
            else
                new Message_ChangeItems(changes, Message_ChangeItems.Type.World | Message_ChangeItems.Type.Player).Message.Send(ComponentManager<global::Raft_Network>.Value.HostID);
            changes.Clear();
            flag = true;
        }
        var message = RAPI.ListenForNetworkMessagesOnChannel(MessageType.ChannelID);
        while (message != null && message.message != null && message.message is Message_InitiateConnection && message.message.Type == MessageType.MessageID)
        {
            var msg = (Message_InitiateConnection)message.message;
            if (msg.appBuildID == MessageType.ChangeItems)
            {
                var change = new Message_ChangeItems(msg);
                if (change.Player) {
                    if (!playerItems.ContainsKey(message.steamid.m_SteamID))
                        playerItems.Add(message.steamid.m_SteamID, new Dictionary<Item_Base, int>(change.items));
                    else
                        playerItems[message.steamid.m_SteamID].Add(change.items);
                }
                if (change.World)
                    items.Add(change.items);
                if (change.Host)
                {
                    if (!playerItems.ContainsKey(ComponentManager<global::Raft_Network>.Value.LocalSteamID.m_SteamID))
                        playerItems.Add(ComponentManager<global::Raft_Network>.Value.LocalSteamID.m_SteamID, new Dictionary<Item_Base, int>(change.items));
                    else
                        playerItems[ComponentManager<global::Raft_Network>.Value.LocalSteamID.m_SteamID].Add(change.items);
                }
                flag = true;
            }
            message = RAPI.ListenForNetworkMessagesOnChannel(MessageType.ChannelID);
        }
        if (global::Raft_Network.IsHost && flag)
        {
            System.Action<string, Dictionary<Item_Base, int>> putItems = (x,y) =>
            {
                var i = 0;
                foreach (var p in y)
                {
                    ExtraSettingsAPI_SetDataValue("collected", x + "item_" + i + "_id", p.Key.UniqueName);
                    ExtraSettingsAPI_SetDataValue("collected", x + "item_" + i + "_amt", p.Value.ToString());
                    i++;
                }
                ExtraSettingsAPI_SetDataValue("collected", x + "item_count", i.ToString());
            };
            putItems("", items);
            var j = 0;
            foreach (var p in playerItems)
            {
                ExtraSettingsAPI_SetDataValue("collected", "player_" + j + "_id", p.Key.ToString());
                putItems("player_" + j + "_", p.Value);
                j++;
            }
            ExtraSettingsAPI_SetDataValue("collected", "player_count", j.ToString());
        }
        if (ExtraSettingsAPI_Loaded && SceneManager.GetActiveScene().name == global::Raft_Network.GameSceneName)
        {
            if (CanvasHelper.ActiveMenu == MenuType.None)
            {
                if (holdToShow)
                {
                    if (MyInput.GetButtonDown(showStats))
                        showing = true;
                    if (MyInput.GetButtonUp(showStats))
                        showing = false;
                }
                else if (MyInput.GetButtonDown(showStats))
                    showing = !showing;
            }
            else
                showing = false;
            if (showing && (!imageContainer || !imageContainer.gameObject.activeSelf))
            {
                if (!imageContainer)
                    CreateUI();
                imageContainer.gameObject.SetActive(true);
                var s = "Global:";
                foreach (var p in items)
                    s += "\n - " + p.Key.settings_Inventory.DisplayName + " x " + p.Value;
                display.text = s;
                s = "Personal:";
                if (playerItems.ContainsKey(ComponentManager<global::Raft_Network>.Value.LocalSteamID.m_SteamID))
                    foreach (var p in playerItems[ComponentManager<global::Raft_Network>.Value.LocalSteamID.m_SteamID])
                        s += "\n - " + p.Key.settings_Inventory.DisplayName + " x " + p.Value;
                display2.text = s;
                var header = imageContainer.Find("Header").GetComponent<Text>();
                var divider = imageContainer.Find("Divider(Clone)").GetComponent<RectTransform>();
                imageContainer.sizeDelta = new Vector2(Mathf.Max(header.preferredWidth, Mathf.Max(display.preferredWidth, display2.preferredWidth) * 2 + canvas.dropText.fontSize) + canvas.dropText.fontSize, 0);
                var displayHeight = Mathf.Max(display.preferredHeight, display2.preferredHeight);
                header.rectTransform.offsetMin = new Vector2(canvas.dropText.fontSize / 2f, -canvas.dropText.fontSize / 2f - header.preferredHeight);
                header.rectTransform.offsetMax = new Vector2(-canvas.dropText.fontSize / 2f, -canvas.dropText.fontSize / 2f);
                divider.offsetMin = new Vector2(canvas.dropText.fontSize / 2f, -canvas.dropText.fontSize / 2f - header.preferredHeight - divider.sizeDelta.y);
                divider.offsetMax = new Vector2(-canvas.dropText.fontSize / 2f, -canvas.dropText.fontSize / 2f - header.preferredHeight);
                display.rectTransform.offsetMin = new Vector2(canvas.dropText.fontSize / 2f, canvas.dropText.fontSize / 2f);
                display.rectTransform.offsetMax = new Vector2(-canvas.dropText.fontSize / 2f, canvas.dropText.fontSize / 2f + displayHeight);
                display2.rectTransform.offsetMin = new Vector2(canvas.dropText.fontSize / 2f, canvas.dropText.fontSize / 2f);
                display2.rectTransform.offsetMax = new Vector2(-canvas.dropText.fontSize / 2f, canvas.dropText.fontSize / 2f + displayHeight);
                imageContainer.sizeDelta = new Vector2(
                    Mathf.Max(header.preferredWidth, display.preferredWidth + canvas.dropText.fontSize + display2.preferredWidth) + canvas.dropText.fontSize,
                    divider.sizeDelta.y + displayHeight + header.preferredHeight + canvas.dropText.fontSize
                );
            }
            else if (!showing && imageContainer && imageContainer.gameObject.activeSelf)
                imageContainer.gameObject.SetActive(false);
            if (hud)
            {
                var t = 0;
                foreach (var p in items)
                    t += p.Value;
                var pt = 0;
                if (playerItems.ContainsKey(ComponentManager<global::Raft_Network>.Value.LocalSteamID.m_SteamID))
                    foreach (var p in playerItems[ComponentManager<global::Raft_Network>.Value.LocalSteamID.m_SteamID])
                        pt += p.Value;
                hud.text = $"Global Total: {t}\nPersonal Total: {pt}";
                hud.rectTransform.offsetMax = new Vector2(hud.preferredWidth + canvas.dropText.fontSize, 0);
                hud.rectTransform.offsetMax = new Vector2(hud.preferredWidth + canvas.dropText.fontSize, hud.preferredHeight / 2);
                hud.rectTransform.offsetMin = new Vector2(canvas.dropText.fontSize, -hud.preferredHeight / 2);
            }
        }
    }

    public static void CreateUI()
    {
        if (imageContainer)
            Destroy(imageContainer.gameObject);
        AddImageObject(canvas.transform, 0);
        if (hud)
            Destroy(hud.gameObject);
        hud = CreateText(canvas.transform, 0, 0.5f, "", canvas.dropText.fontSize, canvas.dropText.color, 0, 0, canvas.dropText.font).GetComponent<Text>();
        CopyTextShadow(hud.gameObject, canvas.dropText.gameObject);
    }
    public static void AddImageObject(Transform transform, float scale)
    {
        var OptionMenuContainer = Traverse.Create(ComponentManager<Settings>.Value).Field("optionsCanvas").GetValue<GameObject>().transform.FindChildRecursively("OptionMenuParent").gameObject;
        GameObject backgroundImg = OptionMenuContainer.transform.FindChildRecursively("BrownBackground").gameObject;
        GameObject divider = OptionMenuContainer.transform.FindChildRecursively("Divider").gameObject;

        imageContainer = Instantiate(backgroundImg, transform, false).GetComponent<RectTransform>();
        imageContainer.anchorMin = new Vector2(0.5f, 0.5f);
        imageContainer.anchorMax = new Vector2(0.5f, 0.5f);
        var header = CreateText(imageContainer, 0, 1, "Collection Stats:", canvas.dropText.fontSize, canvas.dropText.color, 1, 0, canvas.dropText.font,"Header").GetComponent<Text>();
        CopyTextShadow(header.gameObject, canvas.dropText.gameObject);
        var newDiv = Instantiate(divider, imageContainer, false).GetComponent<RectTransform>();
        newDiv.anchorMin = new Vector2(0, 1);
        newDiv.anchorMax = new Vector2(1, 1);
        newDiv.sizeDelta = new Vector2(0, canvas.dropText.fontSize);
        display = CreateText(imageContainer, 0, 0, "", canvas.dropText.fontSize, canvas.dropText.color, 0.5f, 0, canvas.dropText.font).GetComponent<Text>();
        CopyTextShadow(display.gameObject, canvas.dropText.gameObject);
        display2 = CreateText(imageContainer, 0.5f, 0, "", canvas.dropText.fontSize, canvas.dropText.color, 0.5f, 0, canvas.dropText.font).GetComponent<Text>();
        CopyTextShadow(display2.gameObject, canvas.dropText.gameObject);
        imageContainer.gameObject.SetActive(false);
    }

    public static GameObject CreateText(Transform canvas_transform, float x, float y, string text_to_print, int font_size, Color text_color, float width, float height, Font font, string name = "Text")
    {
        GameObject UItextGO = new GameObject("Text");
        UItextGO.transform.SetParent(canvas_transform, false);
        RectTransform trans = UItextGO.AddComponent<RectTransform>();
        trans.anchorMin = new Vector2(x, y);
        trans.anchorMax = trans.anchorMin + new Vector2(width, height);
        Text text = UItextGO.AddComponent<Text>();
        text.text = text_to_print;
        text.font = font;
        text.fontSize = font_size;
        text.color = text_color;
        text.name = name;
        Shadow shadow = UItextGO.AddComponent<Shadow>();
        shadow.effectColor = new Color();
        return UItextGO;
    }
    public static void AddTextShadow(GameObject textObject, Color shadowColor, Vector2 shadowOffset)
    {
        Shadow shadow = textObject.AddComponent<Shadow>();
        shadow.effectColor = shadowColor;
        shadow.effectDistance = shadowOffset;
    }
    public static void CopyTextShadow(GameObject textObject, GameObject shadowSource)
    {
        Shadow sourcesShadow = shadowSource.GetComponent<Shadow>();
        if (sourcesShadow == null)
            sourcesShadow = shadowSource.GetComponentInChildren<Shadow>();
        AddTextShadow(textObject, sourcesShadow.effectColor, sourcesShadow.effectDistance);
    }

    public void ExtraSettingsAPI_Load()
    {
        showStats = ExtraSettingsAPI_GetKeybindName("showStats");
        holdToShow = ExtraSettingsAPI_GetCheckboxState("holdToShow");
        try
        {
            if (SceneManager.GetActiveScene().name == global::Raft_Network.GameSceneName)
                WorldEvent_WorldLoaded();
        } catch (System.Exception e)
        {
            UnityEngine.Debug.LogError(e);
        }
    }

    public void ExtraSettingsAPI_SettingsClose()
    {
        holdToShow = ExtraSettingsAPI_GetCheckboxState("holdToShow");
    }


    static Traverse ExtraSettingsAPI_Traverse;
    static bool ExtraSettingsAPI_Loaded = false;
    public string ExtraSettingsAPI_GetDataValue(string SettingName, string subname)
    {
        if (ExtraSettingsAPI_Loaded)
            return ExtraSettingsAPI_Traverse.Method("getDataValue", new object[] { this, SettingName, subname }).GetValue<string>();
        return "";
    }

    public string[] ExtraSettingsAPI_GetDataNames(string SettingName)
    {
        if (ExtraSettingsAPI_Loaded)
            return ExtraSettingsAPI_Traverse.Method("getDataNames", new object[] { this, SettingName }).GetValue<string[]>();
        return new string[0];
    }
    public void ExtraSettingsAPI_SetDataValue(string SettingName, string subname, string value)
    {
        if (ExtraSettingsAPI_Loaded)
            ExtraSettingsAPI_Traverse.Method("setDataValue", new object[] { this, SettingName, subname, value }).GetValue();
    }
    public bool ExtraSettingsAPI_GetCheckboxState(string SettingName)
    {
        if (ExtraSettingsAPI_Loaded)
            return ExtraSettingsAPI_Traverse.Method("getCheckboxState", new object[] { this, SettingName }).GetValue<bool>();
        return false;
    }
    public string ExtraSettingsAPI_GetKeybindName(string SettingName)
    {
        if (ExtraSettingsAPI_Loaded)
            return ExtraSettingsAPI_Traverse.Method("getKeybindName", new object[] { this, SettingName }).GetValue<string>();
        return "";
    }

    public static void Process(Inventory inventory, Item_Base item, int count)
    {
        if (inventory != ComponentManager<PlayerInventory>.Value)
            return;
        if (!(Patch_PickupItem.pickingUp && Patch_PickupItem.pickingUp.GetComponent<DropItem>()) && ((Patch_PickupItem.pickingUp && Patch_PickupItem.pickingUp.GetComponentInParent<ItemCollector>()) || (Patch_PickupItem.pickingUp && Patch_PickupItem.pickingUp.transform.parent == ComponentManager<ObjectSpawnerManager>.Value.floatingObjectParent)))
        {
            changes.AddTo(item, count);
            items.AddTo(item, count);
            if (playerItems.ContainsKey(ComponentManager<global::Raft_Network>.Value.LocalSteamID.m_SteamID))
                playerItems[ComponentManager<global::Raft_Network>.Value.LocalSteamID.m_SteamID].AddTo(item, count);
            else
                playerItems.Add(ComponentManager<global::Raft_Network>.Value.LocalSteamID.m_SteamID,new Dictionary<Item_Base, int> { [item] = count });
        }
    }
}

static class ExtentionMethods
{
    public static bool Exists<T>(this T[] array, System.Predicate<T> predicate)
    {
        foreach (var v in array)
            if (predicate(v))
                return true;
        return false;
    }
    public static List<T> Find<T>(this T[] array, System.Predicate<T> predicate)
    {
        var n = new List<T>();
        foreach (var v in array)
            if (predicate(v))
                n.Add(v);
        return n;
    }

    public static void Add(this Dictionary<Item_Base, int> main, Dictionary<Item_Base, int> data) {
        foreach (var i in data)
            main.AddTo(i.Key, i.Value);
    }

    public static void AddTo(this Dictionary<Item_Base, int> main, Item_Base item, int count)
    {
        if (!main.TryAdd(item, count))
            main[item] += count;
    }

    public static string String(this byte[] bytes, int length = -1, int offset = 0)
    {
        if (bytes.Length % 2 == 1)
        {
            var n = new byte[bytes.Length + 1];
            bytes.CopyTo(n, 0);
            bytes = n;
        }
        string str = "";
        if (length == -1)
            length = (bytes.Length - offset) / 2;
        while (str.Length < length)
        {
            str += System.BitConverter.ToChar(bytes, offset + str.Length * 2);
        }
        return str;

    }
    public static string String(this List<byte> bytes) => bytes.ToArray().String();
    public static byte[] Bytes(this string str)
    {
        var data = new List<byte>();
        foreach (char chr in str)
            data.AddRange(System.BitConverter.GetBytes(chr));
        return data.ToArray();
    }
    public static int Integer(this byte[] bytes, int offset = 0) => System.BitConverter.ToInt32(bytes, offset);
    public static uint UInteger(this byte[] bytes, int offset = 0) => System.BitConverter.ToUInt32(bytes, offset);
    public static float Float(this byte[] bytes, int offset = 0) => System.BitConverter.ToSingle(bytes, offset);
    public static Vector3 Vector3(this byte[] bytes, int offset = 0) => new Vector3(bytes.Float(offset), bytes.Float(offset + 4), bytes.Float(offset + 8));
    public static byte[] Bytes(this int value) => System.BitConverter.GetBytes(value);
    public static byte[] Bytes(this uint value) => System.BitConverter.GetBytes(value);
    public static byte[] Bytes(this float value) => System.BitConverter.GetBytes(value);
    public static byte[] Bytes(this Vector3 value)
    {
        var data = new byte[12];
        value.x.Bytes().CopyTo(data, 0);
        value.y.Bytes().CopyTo(data, 4);
        value.z.Bytes().CopyTo(data, 8);
        return data;
    }

    public static void Broadcast(this Message message, NetworkChannel channel = MessageType.Channel) => ComponentManager<global::Raft_Network>.Value.RPC(message, Target.Other, EP2PSend.k_EP2PSendReliable, channel);
    public static void Send(this Message message, CSteamID steamID, NetworkChannel channel = MessageType.Channel) => ComponentManager<global::Raft_Network>.Value.SendP2P(steamID, message, EP2PSend.k_EP2PSendReliable, channel);
}

[HarmonyPatch(typeof(Inventory), "AddItem")]
class Patch_AddItem
{

    [HarmonyPatch(new System.Type[] { typeof(string), typeof(int) })]
    static void Prefix(Inventory __instance, string uniqueItemName, int amount)
    {
        var item = ItemManager.GetItemByName(uniqueItemName);
        if (amount > 0 && item) CollectionCounter.Process(__instance, item, amount);
    }

    [HarmonyPatch(new System.Type[] { typeof(ItemInstance), typeof(bool) })]
    static void Prefix(Inventory __instance, ItemInstance itemInstance)
    {
        if (itemInstance != null && itemInstance.baseItem && itemInstance.Amount > 0) CollectionCounter.Process(__instance, itemInstance.baseItem, itemInstance.Amount);
    }

    [HarmonyPatch(new System.Type[] { typeof(string), typeof(Slot), typeof(int) })]
    static void Prefix(Inventory __instance, string uniqueItemName, Slot slot, int amount)
    {
        var item = ItemManager.GetItemByName(uniqueItemName);
        if (amount > 0 && item) CollectionCounter.Process(__instance, item, amount);
    }
}

[HarmonyPatch(typeof(Pickup), "AddItemToInventory")]
class Patch_PickupItem
{
    public static PickupItem pickingUp = null;
    static void Prefix(PickupItem item) => pickingUp = item;
    static void Postfix() => pickingUp = null;
}

static class MessageType
{
    public const Messages MessageID = (Messages)4660;
    public const int ChannelID = 431136;
    public const NetworkChannel Channel = (NetworkChannel)ChannelID;
    public const int ChangeItems = 0;
}

class Message_ChangeItems
{
    public Message_InitiateConnection Message {
        get {
            var data = new List<byte>();
            data.Add((byte)worldData);
            data.AddRange(items.Count.Bytes());
            foreach (var p in items) {
                data.AddRange(p.Key.UniqueName.Length.Bytes());
                data.AddRange(p.Key.UniqueName.Bytes());
                data.AddRange(p.Value.Bytes());
            }
            return new Message_InitiateConnection(MessageType.MessageID, MessageType.ChangeItems, data.String());
        }
    }
    public Dictionary<Item_Base, int> items;
    Type worldData;
    public bool World => (worldData & Type.World) != 0;
    public bool Player => (worldData & Type.Player) != 0;
    public bool Host => (worldData & Type.Host) != 0;
    public Message_ChangeItems(Dictionary<Item_Base, int> Items, Type type)
    {
        items = new Dictionary<Item_Base, int>(Items);
        worldData = type;
    }
    public Message_ChangeItems(Message_InitiateConnection message)
    {
        items = new Dictionary<Item_Base, int>();
        var data = message.password.Bytes();
        worldData = (Type)data[0];
        var c = data.Integer(1);
        var p = 5;
        while (c > 0)
        {
            var l = data.Integer(p);
            var n = ItemManager.GetItemByName(data.String(l, p + 4));
            if (n)
                items.Add(n, data.Integer(p + 4 + l * 2));
            p += l * 2 + 8;
            c--;
        }
    }
    public enum Type : byte
    {
        None = 0,
        Player = 1,
        World = 2,
        Host = 4,
    }
}