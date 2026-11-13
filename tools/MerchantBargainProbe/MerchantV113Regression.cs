#if !STS2_V107_1
using System.Collections;
using System.Reflection;
using System.Runtime.Loader;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Game.Sync;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.TestSupport;
using MegaCrit.Sts2.Core.Unlocks;
using STS2_Things.Config;
using STS2_Things.Features.MerchantBargain;

public static class MerchantV113Regression
{
    private static readonly Assembly Mod = typeof(STS2_ThingsInit).Assembly;
    private static readonly Type Manager = Mod.GetType("STS2_Things.Features.MerchantBargain.MerchantBargainManager", true)!;
    private const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;
    private static NStandardMerchantRoom? _room;
    private static int _fights;
    private static int _checks;

    public static async Task Run()
    {
        AssemblyLoadContext.Default.Resolving += (_, name) =>
        {
            string path = Path.Combine(Path.GetDirectoryName(typeof(MerchantV113Regression).Assembly.Location)!, name.Name + ".dll");
            return File.Exists(path) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null;
        };
        TestMode.TurnOnInternal();
        InitializeModels();
        SaveManager.Instance.InitSettingsDataForTest();
        SaveManager.Instance.InitPrefsDataForTest();
        ThingsModConfig.Load();
        ThingsModConfig.SetValue(ThingsModConfig.FeatureMerchantBargainEnabled, true);
        var harmony = new Harmony("MerchantBargainProbe.V113Sync");
        foreach (string name in new[] { "MerchantEntryOnTryPurchaseWrapperPatch", "MerchantEntryCostPatch",
                     "MerchantBargainSyncBindPatch", "MerchantBargainSyncUnbindPatch", "MerchantBargainNativePurchaseCleanupPatch" })
            harmony.CreateClassProcessor(Mod.GetType("STS2_Things.Features.MerchantBargain." + name, true)!).Patch();
        harmony.Patch(AccessTools.PropertyGetter(typeof(NMerchantRoom), nameof(NMerchantRoom.Instance)),
            prefix: new HarmonyMethod(typeof(MerchantV113Regression), nameof(LocalRoom)));
        harmony.Patch(Manager.GetMethod("ResolveFightAndPurchaseAsync", PrivateStatic),
            prefix: new HarmonyMethod(typeof(MerchantV113Regression), nameof(WinWithoutAnimation)));
        try
        {
            foreach (int count in new[] { 1, 2, 3, 4 }) await Party(count);
            GD.Print($"Merchant V113 native sync regression: PASS ({_checks} assertions; 1-4 player inventories, price-before-purchase ordering)");
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
            _room?.Inventory?.Free(); _room?.Free(); _room = null;
            LocalContext.NetId = null;
        }
    }

    private static bool LocalRoom(ref NMerchantRoom? __result) { __result = _room; return false; }

    private static bool WinWithoutAnimation(object[] __args, ref Task<bool> __result)
    {
        _fights++;
        var entry = (MerchantEntry)__args[0]; var inventory = (MerchantInventory)__args[1];
        object state = __args[3]; object session = state.GetType().GetProperty("Session")!.GetValue(state)!;
        session.GetType().GetMethod("MarkFightWon")!.Invoke(session, [entry, inventory.Player.Gold]);
        __result = (Task<bool>)Manager.GetMethod("PurchaseAtNegotiatedPriceAsync", PrivateStatic)!
            .Invoke(null, [entry, inventory, state, inventory.Player.Gold])!;
        session.GetType().GetMethod("EndFight")!.Invoke(session, null);
        return false;
    }

    private static async Task Party(int count)
    {
        var players = Enumerable.Range(1, count).Select(i => Player.CreateForNewRun<Ironclad>(UnlockState.all, (ulong)i)).ToList();
        var run = ProbeRuntimeApi.CreateForNewRun(players, ActModel.GetDefaultList().Select(a => a.ToMutable()).ToList(), [],
            GameMode.Standard, false, 0, "merchant-beta-" + count);
        typeof(RunManager).GetProperty("State", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(RunManager.Instance, run);
        run.PushRoom(new MerchantRoom()); run.AppendToMapPointHistory(MapPointType.Shop, RoomType.Shop, null);
        var entries = players.Select(p => new MerchantRelicEntry(ModelDb.Relic<Anchor>().ToMutable(), p)).ToArray();
        var inventories = players.Select((p, i) => new MerchantInventory(p, [], [], [entries[i]], [], null, [])).ToList();
        for (int i = 0; i < count; i++)
        {
            inventories[i].Setup();
            typeof(MerchantEntry).GetField("_cost", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(entries[i], 120);
            typeof(Player).GetProperty(nameof(Player.Gold))!.SetValue(players[i], 110);
        }
        for (int local = 0; local < count; local++)
        {
            LocalContext.NetId = players[local].NetId;
            var (service, wire) = ConfigNetworkFake.Host(); wire.Id = players[local].NetId;
            typeof(RunManager).GetProperty(nameof(RunManager.NetService))!.SetValue(RunManager.Instance, service);
            var buffer = new RunLocationTargetedMessageBuffer(service);
            using var reward = new RewardSynchronizer(buffer, service, run, players[local].NetId);
            typeof(RunManager).GetProperty(nameof(RunManager.RewardSynchronizer))!.SetValue(RunManager.Instance, reward);
            using var sync = new MerchantSynchronizer(service, run, buffer);
            sync.OnMerchantEntered(inventories);
            typeof(RunManager).GetProperty(nameof(RunManager.MerchantSynchronizer))!.SetValue(RunManager.Instance, sync);
            _room?.Inventory?.Free(); _room?.Free(); _room = new NStandardMerchantRoom();
            var ui = new NMerchantInventory();
            typeof(NMerchantRoom).GetProperty(nameof(NMerchantRoom.Inventory))!.SetValue(_room, ui);
            typeof(NMerchantInventory).GetProperty(nameof(NMerchantInventory.Inventory))!.SetValue(ui, inventories[local]);
            typeof(NMerchantInventory).GetProperty(nameof(NMerchantInventory.IsOpen))!.SetValue(ui, true);
            int before = _fights;
            for (int i = 0; i < 4; i++) Require(!await sync.PurchaseLocalEntry(entries[local]), "First four clicks remain normal insufficient-gold purchases.");
            Require(_fights == before && entries[local].IsStocked, "Bargain is not triggered early.");
            Require(await sync.PurchaseLocalEntry(entries[local]), "Fifth click wins a negotiated native purchase.");
            Require(_fights == before + 1 && players[local].Gold == 0 && !entries[local].IsStocked, "One fight, one gold spend, one stock removal.");
            Require(players[local].Relics.Count(r => r.Id == ModelDb.Relic<Anchor>().Id) == 1, "Native relic is granted once.");
            int priceIndex = wire.Sent.FindIndex(m => m is MerchantBargainPriceMessage);
            int purchaseIndex = wire.Sent.FindLastIndex(m => m is MerchantEntryPurchasedMessage);
            Require(priceIndex >= 0 && priceIndex < purchaseIndex, "Reliable price announcement precedes native purchase.");
            Require(wire.Sent.OfType<MerchantBargainPriceMessage>().Single().Price == 110, "Announced price equals actual spend.");
            Require(entries[local].Cost == 120, "Temporary negotiated price is cleared after purchase.");
            for (int other = local + 1; other < count; other++)
                Require(players[other].Gold == 110 && entries[other].IsStocked, "Other inventories remain untouched.");
        }
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
        _checks++;
    }

    // Uses the same real ModManager/ModelDb registration as the legacy regression.
    private static void InitializeModels()
    {
        var types = Mod.GetTypes();
        typeof(ReflectionHelper).GetField("_modTypes", PrivateStatic)!.SetValue(null, types);
        var game = typeof(ModManager).Assembly;
        Type modType = game.GetType("MegaCrit.Sts2.Core.Modding.Mod", true)!;
        Type manifestType = game.GetType("MegaCrit.Sts2.Core.Modding.ModManifest", true)!;
        object manifest = Activator.CreateInstance(manifestType)!;
        manifestType.GetField("id")!.SetValue(manifest, "STS2_Things");
        manifestType.GetField("name")!.SetValue(manifest, "Things Merchant Beta Probe");
        object mod = Activator.CreateInstance(modType)!;
        modType.GetField("manifest")!.SetValue(mod, manifest);
        modType.GetField("path")!.SetValue(mod, "probe://merchant-beta");
        var state = modType.GetField("state")!; state.SetValue(mod, Enum.Parse(state.FieldType, "Loaded"));
        ((IList)modType.GetField("assemblies")!.GetValue(mod)!).Add(Mod);
        var mods = (IList)typeof(ModManager).GetField("_mods", PrivateStatic)!.GetValue(null)!; mods.Clear(); mods.Add(mod);
        var managerState = typeof(ModManager).GetProperty("State")!;
        managerState.SetValue(null, Enum.Parse(managerState.PropertyType, "Initialized"));
        game.GetType("MegaCrit.Sts2.Core.Modding.AssemblyInfo")?.GetMethod("Init")?.Invoke(null, null);
        Type[] models = AbstractModelSubtypes.All.Concat(types.Where(t => !t.IsAbstract && typeof(AbstractModel).IsAssignableFrom(t))).Distinct().ToArray();
        var init = typeof(ModelDb).GetMethods().Single(m => m.Name == "Init");
        init.Invoke(null, init.GetParameters().Length == 0 ? null : [models]);
        game.GetType("MegaCrit.Sts2.Core.Multiplayer.Serialization.ModelIdSerializationCache", true)!.GetMethod("Init")!.Invoke(null, null);
        typeof(ModelDb).GetMethod("InitIds")!.Invoke(null, null); MessageTypes.Initialize();
    }
}
#endif
