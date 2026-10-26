using MSCLoader;
using MscDuo.Net;
using MscDuo.Sync;
using UnityEngine;

namespace MscDuo.Tools
{
    internal class NetCommand : ConsoleCommand
    {
        public override string Name => "duo_net";
        public override string Help => "duo_net host | invite | join <código> | loopback [ping=80] [jitter=10] [perda%=2] [distância=3] | lag <ping> <jitter> <perda%> | off | hud";

        public override void Run(string[] args)
        {
            if (args.Length == 0) { ModConsole.Print(Help); return; }

            switch (args[0])
            {
                case "host":
                    Host();
                    break;
                case "join":
                    if (args.Length < 2 || !Join(args[1])) { ModConsole.Error("uso: duo_net join <código do lobby>"); return; }
                    break;
                case "invite":
                    if (!Invite()) { ModConsole.Error("[MSC Duo] crie um lobby antes com 'duo_net host'"); return; }
                    break;
                case "loopback":
                    var ping = Arg(args, 1, 80f);
                    var jitter = Arg(args, 2, 10f);
                    var loss = Arg(args, 3, 2f);
                    var distance = Arg(args, 4, 3f);
                    NetSession.Start(new LoopbackTransport(ping, jitter, loss), new Vector3(distance, 0f, 0f));
                    NetHud.Visible = true;
                    break;
                case "lag":
                    var loopback = NetSession.Current?.Transport as LoopbackTransport;
                    if (loopback == null) { ModConsole.Error("[MSC Duo] só funciona com 'duo_net loopback' ativo"); return; }
                    loopback.PingMs = Arg(args, 1, loopback.PingMs);
                    loopback.JitterMs = Arg(args, 2, loopback.JitterMs);
                    loopback.LossPercent = Arg(args, 3, loopback.LossPercent);
                    ModConsole.Print($"<color=orange>[MSC Duo]</color> {loopback.Name}");
                    break;
                case "off":
                    NetSession.Stop();
                    break;
                case "hud":
                    NetHud.Visible = !NetHud.Visible;
                    break;
                default:
                    ModConsole.Error(Help);
                    break;
            }
        }

        internal static void Host()
        {
            Guarded(() => NetSession.Start(SteamTransport.Host(), Vector3.zero));
            NetHud.Visible = true;
        }

        internal static bool Join(string code)
        {
            ulong lobby;
            if (!ulong.TryParse(code.Trim(), out lobby)) return false;
            Guarded(() => NetSession.Start(SteamTransport.Join(lobby), Vector3.zero));
            NetHud.Visible = true;
            return true;
        }

        internal static bool Invite()
        {
            var steam = NetSession.Current?.Transport as SteamTransport;
            if (steam == null) return false;
            steam.OpenInviteOverlay();
            return true;
        }

        static void Guarded(System.Action start)
        {
            try { start(); }
            catch (System.Exception e) { ModConsole.Error($"[MSC Duo] não foi possível iniciar a sessão Steam: {e.Message}"); }
        }

        static float Arg(string[] args, int index, float fallback)
        {
            float value;
            return args.Length > index && DevCommands.TryFloat(args[index], out value) ? value : fallback;
        }
    }

    internal static class NetHud
    {
        internal static bool Visible = true;
        static GUIStyle style;

        internal static void Draw()
        {
            var session = NetSession.Current;
            if (!Visible || session == null) return;
            if (style == null)
            {
                style = new GUIStyle(GUI.skin.label) { fontSize = 14, richText = true };
                style.normal.textColor = Color.white;
            }

            var text = $"<b><color=orange>MSC Duo</color></b>  {session.Transport.Name}\n" +
                       $"enviados {session.SentPerSecond}/s  recebidos {session.ReceivedPerSecond}/s  {session.BytesOutPerSecond / 1024f:0.0} KB/s";
            foreach (var peer in session.Peers)
            {
                var p = peer.Player;
                var lossRate = p.Received + p.Lost > 0 ? 100f * p.Lost / (p.Received + p.Lost) : 0f;
                text += $"\njogador {peer.Id}: ping {peer.RttMs:0} ms  perda {lossRate:0.#}%  atraso {p.InterpolationDelay * 1000f:0} ms  buffer {p.Buffered}" +
                        (p.Extrapolating ? "  <color=yellow>extrapolando</color>" : "");
            }

            foreach (var car in CarSync.Links)
                if (car.Owned || car.Sending || car.Applying)
                    text += $"\n{car.Label}: " + (car.Sending ? "enviando" : car.Applying ? "seguindo o outro jogador" : "parada");

            text += (session.Connected ? $"\nobjetos: {ItemRegistry.Status}" : "\nesperando o outro jogador entrar") + (ItemSync.Enabled ? $"  ({ItemSync.OwnedByMe} meus, {ItemSync.OwnedByPeer} do outro agora)" : session.Connected ? "  <color=yellow>sincronização desligada</color>" : "");

            var rect = new Rect(Screen.width - 520, 40, 510, 210);
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(rect.x + 8, rect.y + 4, rect.width - 16, rect.height - 8), text, style);
        }
    }
}
