using System;
using System.IO;
using System.Text;
using UnityEngine;
// Los siguientes usings se activan junto con el envío al endpoint LSG.
// Descomentar cuando se habilite SendLog() para producción.
// using System.Collections;
// using UnityEngine.Networking;

namespace VitaSync
{
    /// <summary>
    /// Registra eventos de sesión de juego escribiendo cada uno
    /// inmediatamente en disco (append), garantizando persistencia
    /// ante cierres abruptos del proceso.
    ///
    /// Archivo: BepInEx/VitaSync_Events.csv
    /// Formato: una fila por evento con campo data en clave=valor.
    /// Timestamps: hora local en CSV, UTC disponible para JSON futuro.
    /// </summary>
    public static class SessionLogger
    {
        private static string _sessionId = null;
        private static string _sessionStart = null;
        private static string _sessionStartUtc = null;
        private static bool _active = false;

        // Guards contra eventos duplicados por polling del juego.
        private static bool _teamDeadRegistrado = false;
        private static string _ultimoNivelStart = null;
        private static string _ultimoNivelEnd = null;

        private static string _csvPath = null;
        private static string CsvPath
        {
            get
            {
                if (_csvPath != null) return _csvPath;
                try
                {
                    _csvPath = Path.Combine(
                        BepInEx.Paths.BepInExRootPath, "VitaSync_Events.csv");
                }
                catch
                {
                    _csvPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                        "VitaSync_Events.csv");
                }
                return _csvPath;
            }
        }

        // ── API pública ───────────────────────────────────────────────

        public static void StartSession()
        {
            if (_active) return;
            _sessionId = DateTime.UtcNow.Ticks.ToString();
            _sessionStart = LocalNow();
            _sessionStartUtc = UtcNow();
            _active = true;
            _teamDeadRegistrado = false;
            _ultimoNivelStart = null;
            _ultimoNivelEnd = null;

            bool isMP = false; int players = 1;
            try
            {
                isMP = SemiFunc.IsMultiplayer();
                if (isMP && GameDirector.instance != null)
                    players = GameDirector.instance.PlayerList.Count;
            }
            catch { }

            Write("session_start",
                "modo=" + (isMP ? "multiplayer" : "singleplayer") +
                "|jugadores=" + players);
            VitaSyncPlugin.Log.LogInfo("[SL] Sesión iniciada. id=" + _sessionId);
        }

        public static void EndSession()
        {
            if (!_active) return;

            bool gameOver = GetGameOver();
            int niveles = 0;
            try { niveles = RunManager.instance?.levelsCompleted ?? 0; } catch { }

            bool isMP = false; int players = 1;
            try
            {
                isMP = SemiFunc.IsMultiplayer();
                if (isMP && GameDirector.instance != null)
                    players = GameDirector.instance.PlayerList.Count;
            }
            catch { }

            Write("session_end",
                "niveles=" + niveles +
                "|game_over=" + (gameOver ? "true" : "false") +
                "|modo=" + (isMP ? "multiplayer" : "singleplayer") +
                "|jugadores=" + players);

            _active = false; _sessionId = null;
            _sessionStart = null; _sessionStartUtc = null;
            _teamDeadRegistrado = false;
            _ultimoNivelStart = null;
            _ultimoNivelEnd = null;
            VitaSyncPlugin.Log.LogInfo("[SL] Sesión cerrada.");
        }

        public static void LogLevelStart(string level)
        {
            if (!_active) return;
            // Solo registrar si es un nivel diferente al anterior.
            // Mientras RunManager llame ChangeLevel en loop con el mismo
            // nivel (game_over), este guard bloquea todos los duplicados.
            if (_ultimoNivelStart == level) return;
            _ultimoNivelStart = level;
            _ultimoNivelEnd = null; // permitir un level_end para este nivel
            _teamDeadRegistrado = false;
            int n = 0;
            try { n = RunManager.instance?.levelsCompleted ?? 0; } catch { }
            Write("level_start", "level=" + Esc(level) + "|niveles=" + n);
        }

        public static void LogLevelEnd(string level)
        {
            if (!_active) return;
            // Solo registrar si hubo level_start para este nivel
            // y no se ha registrado aún el level_end correspondiente.
            if (_ultimoNivelStart != level) return;
            if (_ultimoNivelEnd == level) return;
            _ultimoNivelEnd = level;
            // NO limpiamos _ultimoNivelStart aquí: si el juego llama
            // ChangeLevel en loop con el mismo nivel, el guard de
            // LogLevelStart bloqueará los duplicados.
            bool go = GetGameOver(); int n = 0;
            try { n = RunManager.instance?.levelsCompleted ?? 0; } catch { }
            Write("level_end",
                "level=" + Esc(level) + "|niveles=" + n +
                "|game_over=" + (go ? "true" : "false"));
        }

        public static void LogShopOpen(int puntos)
        {
            if (!_active) return;
            int n = 0;
            try { n = RunManager.instance?.levelsCompleted ?? 0; } catch { }
            Write("shop_open", "puntos=" + puntos + "|despues_nivel=" + n);
        }

        public static void LogShopClose(int canjes)
        {
            if (!_active) return;
            Write("shop_close", "canjes=" + canjes);
        }

        public static void LogUpgradeRedeemed(string attr, int costo, int puntosRestantes)
        {
            if (!_active) return;
            Write("upgrade_redeemed",
                "atributo=" + Esc(attr) +
                "|costo=" + costo +
                "|puntos_restantes=" + puntosRestantes);
        }

        public static void LogUpgradesApplied(int hp, int stamina, int grip, int speed)
        {
            if (!_active) return;
            Write("upgrades_applied",
                "health=" + hp + "|stamina=" + stamina +
                "|grip=" + grip + "|speed=" + speed);
        }

        public static void LogTeamDead(int niveles)
        {
            if (!_active) return;
            // AllPlayersDeadSet(true) se llama en polling, no solo una vez.
            // El flag evita registrar la muerte múltiples veces.
            if (_teamDeadRegistrado) return;
            _teamDeadRegistrado = true;
            bool isMP = false; int players = 1;
            try
            {
                isMP = SemiFunc.IsMultiplayer();
                if (isMP && GameDirector.instance != null)
                    players = GameDirector.instance.PlayerList.Count;
            }
            catch { }
            Write("team_dead",
                "niveles=" + niveles +
                "|modo=" + (isMP ? "multiplayer" : "singleplayer") +
                "|jugadores=" + players);
        }

        // ── Escritura inmediata ───────────────────────────────────────

        private static void Write(string eventType, string data)
        {
            try
            {
                string path = CsvPath;
                bool exists = File.Exists(path);
                using (var sw = new StreamWriter(path, true, Encoding.UTF8))
                {
                    if (!exists)
                        sw.WriteLine(
                            "session_id,session_start,timestamp," +
                            "player_id,event_type,data");
                    sw.WriteLine(
                        (_sessionId ?? "?") + "," +
                        (_sessionStart ?? "?") + "," +
                        LocalNow() + "," +
                        SessionManager.PlayerId + "," +
                        eventType + "," +
                        "\"" + data.Replace("\"", "\"\"") + "\"");
                }
            }
            catch (Exception ex)
            {
                VitaSyncPlugin.Log.LogWarning(
                    "[SL] Error CSV: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        // ── Utilidades ────────────────────────────────────────────────

        private static System.Reflection.FieldInfo _gameOverField;
        private static bool GetGameOver()
        {
            try
            {
                if (RunManager.instance == null) return false;
                if (_gameOverField == null)
                    _gameOverField = typeof(RunManager).GetField("gameOver",
                        System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.NonPublic |
                        System.Reflection.BindingFlags.Public);
                if (_gameOverField == null) return false;
                return (bool)_gameOverField.GetValue(RunManager.instance);
            }
            catch { return false; }
        }

        private static string LocalNow() =>
            DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        private static string UtcNow() =>
            DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");

        private static string Esc(string s) =>
            s == null ? "" : s.Replace(",", ";").Replace("\"", "'");
    }
}