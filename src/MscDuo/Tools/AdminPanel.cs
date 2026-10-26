using System.Collections.Generic;
using System.Globalization;
using HutongGames.PlayMaker;
using MSCLoader;
using MscDuo.Duplication;
using MscDuo.Net;
using MscDuo.Player;
using MscDuo.Sync;
using UnityEngine;

namespace MscDuo.Tools
{
    internal static class AdminPanel
    {
        const float ClaimSeconds = 2f;
        const float FuelFallback = 30f;

        static readonly string[] Needs = { "PlayerHunger", "PlayerThirst", "PlayerFatigue", "PlayerStress", "PlayerUrine", "PlayerDirtiness" };
        static readonly string[] NeedLabels = { "Fome", "Sede", "Cansaço", "Estresse", "Mijo", "Sujeira" };
        static readonly string[] Tabs = { "Sessão", "Jogador", "Veículos", "Teleporte", "Mundo", "Inspetor" };
        static readonly float[] MoveSteps = { 0.01f, 0.1f, 1f };
        static readonly float[] TurnSteps = { 1f, 5f, 15f, 90f };
        static readonly string[] Blocks = { "06-08", "10-12", "14-16", "18-20", "22-00", "02-04" };
        static readonly string[] BlockLabels = { "6h", "10h", "14h", "18h", "22h", "2h" };
        static readonly string[][] VehicleNames =
        {
            new[] { "jonnez", "Moto Jonnez" }, new[] { "hayosiko", "Van Hayosiko" }, new[] { "gifu", "Caminhão Gifu" },
            new[] { "kekmet", "Trator Kekmet" }, new[] { "flatbed", "Carreta do trator" }, new[] { "ferndale", "Ferndale" },
            new[] { "rco_ruscko", "Ruscko" }, new[] { "boat", "Barco" }, new[] { "combine", "Colheitadeira" },
            new[] { "ram1500", "Dodge Ram" }, new[] { "campershell", "Capota da Ram" },
        };
        static readonly string[][] Places =
        {
            new[] { "REPAIRSHOP", "Oficina do Fleetari" }, new[] { "STORE", "Loja do Teimo" },
            new[] { "INSPECTION", "Inspeção" }, new[] { "DANCEHALL", "Salão de dança" },
            new[] { "COTTAGE", "Chalé" }, new[] { "CABIN", "Cabana" },
            new[] { "LANDFILL", "Lixão" }, new[] { "DRAGRACE", "Pista de arrancada" },
        };
        static readonly Dictionary<Transform, FsmFloat[]> tanks = new Dictionary<Transform, FsmFloat[]>();
        static readonly string[] WeatherLabels = { "Sol", "Chuva", "Tempestade" };

        static readonly bool[] frozen = new bool[Needs.Length];
        static bool noMosquitos;
        static bool loaded;
        const string PrefsFile = "painel.txt";
        static PlayMakerFSM mosquito;
        static bool open;
        static int tab;
        static string moneyText = "";
        static string joinText = "";
        static string status = "";
        static Rect window = new Rect(60f, 60f, 430f, 520f);
        static Vector2 scroll;
        static FsmBool inMenu;
        static bool menuWas;

        internal static bool Open => open;

        internal static void Toggle()
        {
            if (ModLoader.CurrentScene != CurrentScene.Game) return;
            open = !open;
            if (inMenu == null) inMenu = FsmVariables.GlobalVariables.FindFsmBool("PlayerInMenu");
            if (open)
            {
                menuWas = inMenu != null && inMenu.Value;
                status = "";
            }
            else
            {
                if (inMenu != null) inMenu.Value = menuWas;
                Cursor.visible = false;
                Cursor.lockState = CursorLockMode.Locked;
            }
        }

        static string PrefsPath => System.IO.Path.Combine(MscDuo.Instance.SettingsFolder, PrefsFile);

        static void LoadPrefs()
        {
            loaded = true;
            try
            {
                if (!System.IO.File.Exists(PrefsPath)) return;
                foreach (var raw in System.IO.File.ReadAllLines(PrefsPath))
                {
                    var parts = raw.Split('=');
                    if (parts.Length != 2) continue;
                    var key = parts[0].Trim();
                    var on = parts[1].Trim() == "1";
                    if (key == "mosquitos") noMosquitos = on;
                    for (int i = 0; i < Needs.Length; i++) if (key == Needs[i]) frozen[i] = on;
                }
            }
            catch (System.Exception e) { ModConsole.Warning("[MSC Duo] não foi possível ler " + PrefsFile + ": " + e.Message); }
        }

        static void SavePrefs()
        {
            try
            {
                var lines = new List<string> { "mosquitos=" + (noMosquitos ? "1" : "0") };
                for (int i = 0; i < Needs.Length; i++) lines.Add(Needs[i] + "=" + (frozen[i] ? "1" : "0"));
                System.IO.File.WriteAllLines(PrefsPath, lines.ToArray());
            }
            catch (System.Exception e) { ModConsole.Warning("[MSC Duo] não foi possível gravar " + PrefsFile + ": " + e.Message); }
        }

        internal static void Tick()
        {
            if (!loaded) LoadPrefs();
            for (int i = 0; i < Needs.Length; i++)
            {
                if (!frozen[i]) continue;
                var need = FsmVariables.GlobalVariables.FindFsmFloat(Needs[i]);
                if (need != null) need.Value = 0f;
            }
            if (noMosquitos) KeepMosquitosAway();
            if (!open) return;
            if (ModLoader.CurrentScene != CurrentScene.Game) { open = false; return; }
            if (inMenu != null) inMenu.Value = true;
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
        }

        internal static void Draw()
        {
            if (!open) return;
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
            window = GUI.Window(0x4D5344, window, DrawWindow, "MSC Duo  admin");
        }

        static void DrawWindow(int id)
        {
            tab = GUILayout.Toolbar(tab, Tabs);
            scroll = GUILayout.BeginScrollView(scroll);
            switch (tab)
            {
                case 0: SessionTab(); break;
                case 1: PlayerTab(); break;
                case 2: VehiclesTab(); break;
                case 3: TeleportTab(); break;
                case 4: WorldTab(); break;
                case 5: InspectorTab(); break;
            }
            GUILayout.EndScrollView();
            if (status.Length > 0) GUILayout.Label(status);
            GUILayout.Label("Feche no X ou com a mesma tecla que abriu.");
            if (GUI.Button(new Rect(window.width - 26f, 1f, 22f, 16f), "X")) Toggle();
            GUI.DragWindow();
        }

        static void SessionTab()
        {
            var session = NetSession.Current;
            var steam = session != null ? session.Transport as SteamTransport : null;
            GUILayout.Label("Conexão");
            if (session == null) GUILayout.Label("Sem sessão. Crie uma e convide seu amigo, ou cole o código dele e entre.");
            else
            {
                GUILayout.Label(session.Connected ? "Estado: conectado com " + PeerNames(session, steam) : "Estado: " + session.Transport.Name + ", esperando o outro jogador");
                GUILayout.Label($"{(session.Transport.IsHost ? "anfitrião" : "convidado")} · jogador {PlayerContext.Active} · protocolo {Protocol.Version}");
                foreach (var peer in session.Peers)
                {
                    var p = peer.Player;
                    var loss = p.Received + p.Lost > 0 ? 100f * p.Lost / (p.Received + p.Lost) : 0f;
                    GUILayout.Label($"ping {peer.RttMs:0} ms · perda {loss:0.#}% · atraso {p.InterpolationDelay * 1000f:0} ms");
                }
                if (session.Connected) GUILayout.Label("objetos: " + ItemRegistry.Status);
            }

            GUILayout.Space(8f);
            GUILayout.Label("Jogar junto");
            GUILayout.BeginHorizontal();
            GUI.enabled = session == null;
            if (GUILayout.Button("Criar sessão")) { NetCommand.Host(); status = "criando sessão; quando aparecer o código, convide seu amigo"; }
            GUI.enabled = steam != null && steam.IsHost;
            if (GUILayout.Button("Convidar amigo (Steam)")) status = NetCommand.Invite() ? "convite da Steam aberto" : "crie a sessão antes";
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            if (steam != null && steam.IsHost && steam.Lobby != 0)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Código: " + steam.Lobby);
                if (GUILayout.Button("Copiar", GUILayout.Width(80f))) { CopyToClipboard(steam.Lobby.ToString()); status = "código copiado"; }
                GUILayout.EndHorizontal();
            }
            GUILayout.BeginHorizontal();
            joinText = GUILayout.TextField(joinText, GUILayout.Width(180f));
            if (GUILayout.Button("Colar")) joinText = Clipboard().Trim();
            GUI.enabled = session == null;
            if (GUILayout.Button("Entrar com código")) status = NetCommand.Join(joinText) ? "entrando na sessão do amigo" : "código inválido: são só números";
            GUI.enabled = true;
            GUILayout.EndHorizontal();

            GUILayout.Space(8f);
            GUILayout.BeginHorizontal();
            GUI.enabled = session != null;
            if (GUILayout.Button("Encerrar sessão")) { NetSession.Stop(); status = "sessão encerrada"; }
            GUI.enabled = true;
            if (GUILayout.Button(NetHud.Visible ? "Esconder HUD de rede" : "Mostrar HUD de rede")) NetHud.Visible = !NetHud.Visible;
            GUILayout.EndHorizontal();
        }

        static string PeerNames(NetSession session, SteamTransport steam)
        {
            var names = new List<string>();
            foreach (var peer in session.Peers) names.Add(steam != null ? SteamTransport.PersonaName(peer.Id) : "jogador " + peer.Id);
            return names.Count > 0 ? string.Join(", ", names.ToArray()) : "?";
        }

        static void PlayerTab()
        {
            GUILayout.Label("Necessidades travadas em zero");
            var changed = false;
            for (int i = 0; i < Needs.Length; i++)
            {
                var need = GUILayout.Toggle(frozen[i], " " + NeedLabels[i]);
                if (need != frozen[i]) { frozen[i] = need; changed = true; }
            }
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Travar todas")) { for (int i = 0; i < frozen.Length; i++) frozen[i] = true; changed = true; }
            if (GUILayout.Button("Soltar todas")) { for (int i = 0; i < frozen.Length; i++) frozen[i] = false; changed = true; }
            GUILayout.EndHorizontal();

            GUILayout.Space(6f);
            var mosquitos = GUILayout.Toggle(noMosquitos, " Sem mosquitos (como se tivesse passado spray)");
            if (mosquitos != noMosquitos) { noMosquitos = mosquitos; changed = true; }
            if (changed) SavePrefs();

            GUILayout.Space(10f);
            var money = FsmVariables.GlobalVariables.FindFsmFloat("PlayerMoney");
            GUILayout.Label($"Dinheiro: {(money != null ? money.Value.ToString("0") : "-")} mk");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("+100")) AddMoney(money, 100f);
            if (GUILayout.Button("+1000")) AddMoney(money, 1000f);
            if (GUILayout.Button("-100")) AddMoney(money, -100f);
            if (GUILayout.Button("-1000")) AddMoney(money, -1000f);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            moneyText = GUILayout.TextField(moneyText, GUILayout.Width(120f));
            float value;
            if (GUILayout.Button("Definir") && money != null && float.TryParse(moneyText, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                money.Value = Mathf.Max(0f, value);
                status = $"dinheiro definido em {money.Value:0} mk";
            }
            GUILayout.EndHorizontal();
        }

        static void KeepMosquitosAway()
        {
            if (mosquito == null)
            {
                var player = Player();
                if (player == null) return;
                foreach (var fsm in player.GetComponentsInChildren<PlayMakerFSM>(true))
                    if (fsm.gameObject.name == "Mosquito" && fsm.FsmName == "Move") { mosquito = fsm; break; }
                if (mosquito == null) return;
            }
            var shield = mosquito.FsmVariables.FindFsmFloat("PlayerMosquitoShield");
            if (shield != null) shield.Value = 200f;
            var audio = mosquito.transform.Find("Audio");
            if (audio != null && audio.gameObject.activeSelf) audio.gameObject.SetActive(false);
        }

        static void AddMoney(FsmFloat money, float amount)
        {
            if (money == null) return;
            money.Value = Mathf.Max(0f, money.Value + amount);
            status = $"dinheiro: {money.Value:0} mk";
        }

        static void VehiclesTab()
        {
            var player = Player();
            if (player == null) { GUILayout.Label("Jogador não encontrado."); return; }
            GUILayout.Label("Ir leva você até o veículo; Trazer coloca o veículo ao seu lado.");
            var groups = new[] { new List<CarSync.CarLink>(), new List<CarSync.CarLink>(), new List<CarSync.CarLink>(), new List<CarSync.CarLink>() };
            foreach (var link in CarSync.Links) groups[GroupOf(link)].Add(link);
            var me = PlayerContext.Active;
            var titles = new[] { "Jogador 1" + (me == 1 ? " (você)" : ""), "Jogador 2" + (me == 2 ? " (você)" : ""), "Mods", "Outros" };
            for (int g = 0; g < groups.Length; g++)
            {
                if (groups[g].Count == 0) continue;
                GUILayout.Space(4f);
                GUILayout.Label(titles[g]);
                foreach (var link in groups[g]) VehicleRow(player, link);
            }
        }

        static int GroupOf(CarSync.CarLink link)
        {
            if (link.Index == 1) return 0;
            if (link.Index == 2) return 1;
            if (link.RootName == null) return 3;
            if (link.RootName.EndsWith(VehicleCloner.Suffix)) return 1;
            foreach (var spec in VehicleCloner.Specs) if (spec.Root == link.RootName) return 0;
            if (ExtraVehicles.Roots.Contains(link.RootName)) return 2;
            return 3;
        }

        static string NameOf(CarSync.CarLink link)
        {
            var label = link.Label.ToLowerInvariant();
            if (label.StartsWith("satsuma")) return "Satsuma";
            foreach (var pair in VehicleNames)
                if (label.StartsWith(pair[0])) return pair[1];
            return link.Label;
        }

        static void VehicleRow(Transform player, CarSync.CarLink link)
        {
            GUILayout.BeginHorizontal();
            if (!link.Ready || link.Root == null)
            {
                GUILayout.Label(NameOf(link) + "  (não está no jogo agora)");
                GUILayout.EndHorizontal();
                return;
            }
            var distance = (link.Root.position - player.position).magnitude;
            GUILayout.Label($"{NameOf(link)}  {distance:0} m", GUILayout.Width(175f));
            var tank = TankOf(link);
            GUILayout.Label(tank.Length == 0 ? "-" : tank[1] != null && tank[1].Value > 0f ? $"{tank[0].Value:0}/{tank[1].Value:0} L" : $"{tank[0].Value:0} L", GUILayout.Width(55f));
            if (GUILayout.Button("Ir")) GoTo(player, link.Root);
            if (GUILayout.Button("Trazer")) Bring(player, link);
            GUI.enabled = tank.Length > 0;
            if (GUILayout.Button("Encher")) Refuel(link, tank);
            GUI.enabled = true;
            GUILayout.EndHorizontal();
        }

        static FsmFloat[] TankOf(CarSync.CarLink link)
        {
            FsmFloat[] tank;
            if (tanks.TryGetValue(link.Root, out tank)) return tank;
            tank = new FsmFloat[0];
            var roots = new List<GameObject> { link.Root.gameObject };
            if (link.Index == 1) roots.Add(SatsumaCloner.OriginalDatabase);
            if (link.Index == 2) roots.Add(SatsumaCloner.Database);
            foreach (var root in roots)
            {
                if (root == null || tank.Length > 0) continue;
                foreach (var fsm in root.GetComponentsInChildren<PlayMakerFSM>(true))
                {
                    if (fsm.FsmName != "Data") continue;
                    var level = fsm.FsmVariables.FindFsmFloat("FuelLevel");
                    if (level == null) continue;
                    var max = fsm.FsmVariables.FindFsmFloat("MaxCapacity") ?? fsm.FsmVariables.FindFsmFloat("FuelCapacity") ?? fsm.FsmVariables.FindFsmFloat("Capacity");
                    tank = new[] { level, max };
                    break;
                }
            }
            tanks[link.Root] = tank;
            return tank;
        }

        static void GoTo(Transform player, Transform target)
        {
            if (InVehicle(player)) { status = "saia do veículo antes de teleportar."; return; }
            var right = target.right;
            right.y = 0f;
            right.Normalize();
            player.position = target.position + right * 3f + Vector3.up * 1.5f;
            status = $"teleportado para perto de {target.name}";
        }

        static void Bring(Transform player, CarSync.CarLink link)
        {
            if (player.IsChildOf(link.Root)) { status = "você está dentro desse veículo."; return; }
            var vehicle = link.Root.gameObject;
            CarSync.Claim(link, ClaimSeconds);
            if (!Placement.Beside(vehicle, player.gameObject))
            {
                var forward = player.forward;
                forward.y = 0f;
                vehicle.transform.position = player.position + forward.normalized * 5f + Vector3.up;
                vehicle.transform.rotation = Quaternion.Euler(0f, player.eulerAngles.y, 0f);
                VehicleCloner.Snap(vehicle);
            }
            var body = vehicle.GetComponent<Rigidbody>();
            if (body != null && !body.isKinematic) { body.velocity = Vector3.zero; body.angularVelocity = Vector3.zero; }
            status = $"{link.Label} trazido para perto de você";
        }

        static void Refuel(CarSync.CarLink link, FsmFloat[] tank)
        {
            if (tank.Length == 0) { status = "tanque não encontrado nesse veículo"; return; }
            tank[0].Value = tank[1] != null && tank[1].Value > 0f ? tank[1].Value : Mathf.Max(tank[0].Value, FuelFallback);
            status = $"{NameOf(link)}: tanque cheio ({tank[0].Value:0} L)";
        }

        static void TeleportTab()
        {
            var player = Player();
            if (player == null) { GUILayout.Label("Jogador não encontrado."); return; }
            GUILayout.Label("Casas");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Casa 1") && HouseCloner.Original != null) Teleport(player, HouseCloner.Original.transform.position, "casa 1");
            if (GUILayout.Button("Casa 2") && SatsumaCloner.Clone != null) Teleport(player, SatsumaCloner.Clone.transform.position + SatsumaCloner.Clone.transform.right * 2.5f, "casa 2");
            GUI.enabled = Warehouse.LightSwitch != null;
            if (GUILayout.Button("Galpão") && Warehouse.LightSwitch != null) Teleport(player, Warehouse.LightSwitch.position + Vector3.forward * 1.2f - Vector3.up * 1.3f, "galpão");
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            var remote = NetSession.Current != null ? NetSession.Current.RemotePosition : null;
            GUI.enabled = remote.HasValue;
            if (GUILayout.Button("Até o outro jogador") && remote.HasValue) Teleport(player, remote.Value + Vector3.right * 1.5f, "o outro jogador");
            GUI.enabled = true;

            GUILayout.Space(8f);
            GUILayout.Label("Lugares do mapa");
            for (int i = 0; i < Places.Length; i += 2)
            {
                GUILayout.BeginHorizontal();
                for (int j = i; j < i + 2 && j < Places.Length; j++)
                    if (GUILayout.Button(Places[j][1])) GoToPlace(player, Places[j]);
                GUILayout.EndHorizontal();
            }
        }

        static void GoToPlace(Transform player, string[] place)
        {
            var root = SatsumaCloner.FindRoot(place[0]);
            if (root == null) { status = place[1] + " não encontrado neste save"; return; }
            Teleport(player, root.transform.position, place[1].ToLowerInvariant());
        }

        static void Teleport(Transform player, Vector3 target, string label)
        {
            if (InVehicle(player)) { status = "saia do veículo antes de teleportar."; return; }
            player.position = Ground(player, target);
            status = $"teleportado para {label}";
        }

        static Vector3 Ground(Transform player, Vector3 target)
        {
            var best = float.MaxValue;
            var spot = target + Vector3.up * 1.5f;
            foreach (var hit in Physics.RaycastAll(target + Vector3.up * 2f, Vector3.down, 8f))
            {
                if (hit.collider.isTrigger || hit.collider.transform.IsChildOf(player) || hit.distance >= best) continue;
                best = hit.distance;
                spot = hit.point + Vector3.up * 1f;
            }
            return spot;
        }

        static void WorldTab()
        {
            var session = NetSession.Current;
            var guest = session != null && session.Connected && !session.Transport.IsHost;
            if (guest) GUILayout.Label("Só o anfitrião muda a hora e o clima.");
            GUI.enabled = !guest;
            var block = TimeSync.CurrentBlock;
            GUILayout.Label($"Hora (agora: {(block.Length > 0 ? block : "-")})");
            GUILayout.BeginHorizontal();
            for (int i = 0; i < Blocks.Length; i++)
                if (GUILayout.Toggle(block == Blocks[i], BlockLabels[i], "Button") && block != Blocks[i])
                    status = TimeSync.SetBlock(Blocks[i]) ? $"hora: {BlockLabels[i]}" : "relógio não encontrado";
            GUILayout.EndHorizontal();
            var weather = TimeSync.CurrentWeather;
            GUILayout.Label($"Clima (agora: {(weather >= 0 && weather < WeatherLabels.Length ? WeatherLabels[weather] : "-")})");
            GUILayout.BeginHorizontal();
            for (int i = 0; i < WeatherLabels.Length; i++)
                if (GUILayout.Toggle(weather == i, WeatherLabels[i], "Button") && weather != i)
                    status = TimeSync.SetWeather(i) ? $"clima: {WeatherLabels[i]}" : "nuvens não encontradas";
            GUILayout.EndHorizontal();
            GUI.enabled = true;
        }

        static Transform selected;
        static string search = "";
        static int moveStep = 1;
        static int turnStep = 1;

        static void InspectorTab()
        {
            GUILayout.Label("Mire no objeto com o painel fechado, abra o painel e clique em Pegar.");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Pegar o que estou mirando")) Pick();
            if (selected != null && selected.parent != null && GUILayout.Button("Pai")) selected = selected.parent;
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            search = GUILayout.TextField(search, GUILayout.Width(240f));
            if (GUILayout.Button("Procurar nome")) Search();
            GUILayout.EndHorizontal();
            if (selected == null) { GUILayout.Label("Nada selecionado."); return; }

            GUILayout.Label("Caminho: " + PathOf(selected));
            var p = selected.position;
            var e = selected.eulerAngles;
            GUILayout.Label(string.Format(CultureInfo.InvariantCulture, "Posição: {0:0.###}  {1:0.###}  {2:0.###}", p.x, p.y, p.z));
            GUILayout.Label(string.Format(CultureInfo.InvariantCulture, "Rotação: {0:0.#}  {1:0.#}  {2:0.#}", e.x, e.y, e.z));
            GUILayout.Label(string.Format(CultureInfo.InvariantCulture, "Escala: {0:0.###}  {1:0.###}  {2:0.###}   ativo: {3}", selected.localScale.x, selected.localScale.y, selected.localScale.z, selected.gameObject.activeSelf));
            if (Warehouse.Root != null && selected.IsChildOf(Warehouse.Root.transform))
            {
                var g = Warehouse.ToLocal(p);
                GUILayout.Label(string.Format(CultureInfo.InvariantCulture, "No galpão: x {0:0.##}  y {1:0.##}  altura {2:0.##}", g.x, g.y, g.z));
            }
            var components = new List<string>();
            foreach (var c in selected.GetComponents<Component>())
                if (c != null && !(c is Transform)) components.Add(c is PlayMakerFSM ? "FSM " + ((PlayMakerFSM)c).FsmName : c.GetType().Name);
            GUILayout.Label("Componentes: " + (components.Count > 0 ? string.Join(", ", components.ToArray()) : "nenhum"));

            GUILayout.Space(6f);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Passo:", GUILayout.Width(50f));
            for (int i = 0; i < MoveSteps.Length; i++)
                if (GUILayout.Toggle(moveStep == i, MoveSteps[i].ToString(CultureInfo.InvariantCulture) + " m", "Button")) moveStep = i;
            GUILayout.EndHorizontal();
            var step = MoveSteps[moveStep];
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("X-")) selected.position += Vector3.left * step;
            if (GUILayout.Button("X+")) selected.position += Vector3.right * step;
            if (GUILayout.Button("Y-")) selected.position += Vector3.down * step;
            if (GUILayout.Button("Y+")) selected.position += Vector3.up * step;
            if (GUILayout.Button("Z-")) selected.position += Vector3.back * step;
            if (GUILayout.Button("Z+")) selected.position += Vector3.forward * step;
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("Giro:", GUILayout.Width(50f));
            for (int i = 0; i < TurnSteps.Length; i++)
                if (GUILayout.Toggle(turnStep == i, TurnSteps[i] + "°", "Button")) turnStep = i;
            if (GUILayout.Button("⟲")) selected.Rotate(Vector3.up, -TurnSteps[turnStep], Space.World);
            if (GUILayout.Button("⟳")) selected.Rotate(Vector3.up, TurnSteps[turnStep], Space.World);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(selected.gameObject.activeSelf ? "Esconder" : "Mostrar")) selected.gameObject.SetActive(!selected.gameObject.activeSelf);
            if (GUILayout.Button("Copiar dados"))
            {
                var text = string.Format(CultureInfo.InvariantCulture, "{0}  pos {1:0.###} {2:0.###} {3:0.###}  rot {4:0.#} {5:0.#} {6:0.#}", PathOf(selected), p.x, p.y, p.z, e.x, e.y, e.z);
                CopyToClipboard(text);
                ModConsole.Print("[MSC Duo] " + text);
                status = "dados copiados e escritos no console";
            }
            GUILayout.EndHorizontal();
        }

        static void Pick()
        {
            var camera = Camera.main;
            if (camera == null) { status = "câmera não encontrada"; return; }
            var player = Player();
            RaycastHit best = default(RaycastHit);
            var found = false;
            foreach (var hit in Physics.RaycastAll(camera.transform.position, camera.transform.forward, 100f))
            {
                if (player != null && hit.collider.transform.IsChildOf(player)) continue;
                if (!found || hit.distance < best.distance) { best = hit; found = true; }
            }
            if (!found) { status = "nada na mira"; return; }
            selected = best.collider.transform;
            status = string.Format(CultureInfo.InvariantCulture, "pegou {0} a {1:0.0} m", selected.name, best.distance);
        }

        static void Search()
        {
            if (string.IsNullOrEmpty(search)) return;
            var term = search.ToLowerInvariant();
            foreach (var t in Resources.FindObjectsOfTypeAll<Transform>())
                if (t != null && t.hideFlags == HideFlags.None && t.name.ToLowerInvariant().Contains(term))
                {
                    selected = t;
                    status = "achou " + PathOf(t);
                    return;
                }
            status = "nenhum objeto com esse nome";
        }

        static void CopyToClipboard(string text)
        {
            var property = typeof(GUIUtility).GetProperty("systemCopyBuffer", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            if (property != null) property.SetValue(null, text, null);
        }

        static string Clipboard()
        {
            var property = typeof(GUIUtility).GetProperty("systemCopyBuffer", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            return property != null ? property.GetValue(null, null) as string ?? "" : "";
        }

        static string PathOf(Transform t) => t.parent == null ? t.name : PathOf(t.parent) + "/" + t.name;

        static Transform Player()
        {
            var go = GameObject.Find("PLAYER");
            return go != null ? go.transform : null;
        }

        static bool InVehicle(Transform player)
        {
            var current = FsmVariables.GlobalVariables.FindFsmString("PlayerCurrentVehicle");
            return current != null && !string.IsNullOrEmpty(current.Value);
        }
    }
}
