using MSCLoader;
using MscDuo.Duplication;
using MscDuo.Net;
using MscDuo.Player;
using MscDuo.Tools;
using UnityEngine;

namespace MscDuo
{
    public class MscDuo : Mod
    {
        public override string ID => "MscDuo";
        public override string Name => "MSC Duo";
        public override string Version => "0.1.0";
        public override string Author => "Kauan";
        public override string Description =>
            "Multiplayer P2P com progresso individual. Contains AI-generated code (Claude), reviewed by the author.";

        internal static MscDuo Instance { get; private set; }

        SettingsKeybind dumpKey;
        internal SettingsKeybind WakeKey { get; private set; }
        SettingsKeybind adminKey;
        SettingsKeybind passengerKey;
        internal SettingsKeybind GateKey { get; private set; }

        public override void ModSetup()
        {
            Instance = this;
            SetupFunction(Setup.ModSettings, ModSettings);
            SetupFunction(Setup.OnMenuLoad, OnMenuLoad);
            SetupFunction(Setup.OnLoad, OnGameLoad);
            SetupFunction(Setup.PostLoad, OnPostLoad);
            SetupFunction(Setup.OnSave, OnSave);
            SetupFunction(Setup.Update, OnUpdate);
            SetupFunction(Setup.OnGUI, OnGui);
        }

        void ModSettings()
        {
            dumpKey = Keybind.Add("dump", "Exportar YARD, Satsuma e globais", KeyCode.F8);
            WakeKey = Keybind.Add("wake", "Levantar da cama enquanto espera o outro jogador", KeyCode.F);
            adminKey = Keybind.Add("admin", "Abrir o painel de admin", KeyCode.F10);
            passengerKey = Keybind.Add("passenger", "Sentar e levantar do banco do passageiro", KeyCode.Return);
            GateKey = Keybind.Add("portao", "Controle remoto do portão do galpão (o mais perto)", KeyCode.V);
        }

        void OnMenuLoad()
        {
            DevCommands.Register();
            SteamInvites.Listen();
            NetRunner.Ensure();
            Sync.ItemSync.Reset();
            Sync.ItemRegistry.Clear();
            if (NetSession.Current != null && !NetSession.Current.Transport.IsHost) NetSession.Stop();
            Sync.SaveTransfer.Restore();
        }

        void OnGameLoad()
        {
            DevCommands.Register();
            SteamInvites.Listen();
            NetRunner.Ensure();
            ModConsole.Print("<color=orange>[MSC Duo]</color> carregado. Comandos: duo_player, duo_car, duo_check, duo_export, duo_build, duo_net, duo_house, duo_tp, duo_dump, duo_map, duo_shots, duo_probe.");
        }

        void OnPostLoad()
        {
            HarmonyPatches.Apply();
            HouseCloner.Spawn();
            try { Warehouse.Spawn(); }
            catch (System.Exception e) { ModConsole.Error($"[MSC Duo] galpão falhou: {e}"); }
            PlayerContext.Init();
            Sync.TrafficSync.Build();
            Sync.ItemRegistry.Build();
            Sync.ItemRegistry.AddClone(SatsumaCloner.Clone, SatsumaCloner.Parts);
            Sync.SleepSync.Patch();
            Sync.InteractionSync.Patch();
            Sync.ItemPurchases.Patch();
            Sync.TimeSync.Patch();
        }

        void OnSave()
        {
            VehicleCloner.MarkHomeSaved();
            WarehouseGates.Save();
        }

        void OnGui()
        {
            PlayerHud.Draw();
            AdminPanel.Draw();
            Sync.SleepSync.Draw();
        }

        static System.Reflection.FieldInfo consoleField;
        static System.Reflection.FieldInfo containerField;

        static bool Typing()
        {
            if (AdminPanel.Open) return true;
            if (consoleField == null) consoleField = typeof(ModConsole).GetField("console", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            var view = consoleField != null ? consoleField.GetValue(null) : null;
            if (view == null) return false;
            if (containerField == null) containerField = view.GetType().GetField("viewContainer", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            var container = containerField != null ? containerField.GetValue(view) as GameObject : null;
            return container != null && container.activeSelf;
        }

        void OnUpdate()
        {
            PlayerContext.Tick();
            var typing = Typing();
            Sync.Passenger.Tick(!typing && passengerKey != null && passengerKey.GetKeybindDown());
            if (adminKey != null && adminKey.GetKeybindDown()) AdminPanel.Toggle();
            AdminPanel.Tick();
            if (!typing && GateKey != null && GateKey.GetKeybindDown()) WarehouseGates.Remote();
            if (NetSession.Current == null) Sync.SleepSync.Tick(Time.realtimeSinceStartup, false, null);
            if (dumpKey != null && dumpKey.GetKeybindDown())
                SceneDumper.DumpDefault();
        }

        internal string SettingsFolder => ModLoader.GetModSettingsFolder(this);
    }
}
