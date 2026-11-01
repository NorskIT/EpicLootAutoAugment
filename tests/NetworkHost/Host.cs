using System;
using System.Collections;
using System.IO;
using System.Net;
using System.Net.Sockets;
using BepInEx;
using BepInEx.Bootstrap;
using HarmonyLib;
using UnityEngine;

namespace AutoAugment.TestHost;

// Intentionally has no assembly reference or dependency on Auto Augment.
[BepInPlugin("norskit.autoaugment.testhost", "Isolated EpicLoot Host", "0.1.0")]
[BepInDependency("randyknapp.mods.epicloot")]
public sealed class Host : BaseUnityPlugin
{
    private TcpListener? listener;
    private static bool Focus(ref bool __result) { __result = true; return false; }
    private void Update()
    {
        if (listener != null && listener.Pending())
            AccessTools.Method(typeof(ZNet), "OnNewConnection").Invoke(ZNet.instance,
                new object[] { new ZNetPeer(new ZSocket2(listener.AcceptTcpClient(), "127.0.0.1"), false) });
    }
    private IEnumerator Start()
    {
        string coord = Environment.GetEnvironmentVariable("AUTOAUGMENT_NETWORK_COORD") ?? "";
        if (coord.Length == 0) yield break;
        Application.runInBackground = true;
        new Harmony("norskit.autoaugment.testhost.focus").Patch(AccessTools.PropertyGetter(typeof(Application), "isFocused"), prefix: new HarmonyMethod(typeof(Host), nameof(Focus)));
        yield return new WaitForSecondsRealtime(20);
        var run = Run(coord);
        while (true)
        {
            bool next;
            try { next = run.MoveNext(); }
            catch (Exception e) { File.WriteAllText(Path.Combine(coord, "host-result.txt"), "FAIL\n" + e); break; }
            if (!next) break;
            yield return run.Current;
        }
        listener?.Stop(); Application.Quit();
    }
    private IEnumerator Run(string coord)
    {
        if (Chainloader.PluginInfos.ContainsKey("norskit.epiclootautoaugment")) throw new Exception("Host must not load Auto Augment.");
        var profile = new PlayerProfile("autoaugment_testhost", FileHelpers.FileSource.Local);
        profile.SetName("Isolated host"); profile.m_firstSpawn = false; profile.Save(); Game.SetProfile(profile.GetFilename(), profile.m_fileSource);
        ZNet.m_onlineBackend = OnlineBackendType.CustomSocket;
        ZNet.SetServer(true, false, false, "AutoAugment test host", "", new World("AutoAugmentNetwork", "AutoAugmentNetwork")); ZNet.ResetServerHost();
        AccessTools.Method(typeof(FejdStartup), "LoadMainScene").Invoke(FejdStartup.instance, null);
        float deadline = Time.realtimeSinceStartup + 180;
        while (!Player.m_localPlayer || Player.m_localPlayer.InCutscene())
        { if (Time.realtimeSinceStartup > deadline) throw new Exception("Host startup timeout"); yield return new WaitForSecondsRealtime(.2f); }
        Player.m_localPlayer.SetGodMode(true);
        listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        File.WriteAllText(Path.Combine(coord, "port"), ((IPEndPoint)listener.LocalEndpoint).Port.ToString());
        deadline = Time.realtimeSinceStartup + 240;
        while (!File.Exists(Path.Combine(coord, "client-done")))
        { if (Time.realtimeSinceStartup > deadline) throw new Exception("Client test timeout"); yield return new WaitForSecondsRealtime(.2f); }
        File.WriteAllText(Path.Combine(coord, "host-result.txt"), "PASS\nHost ran EpicLoot without Auto Augment. See client result for actual augment checks.");
    }
}
