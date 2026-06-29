namespace VitaSync
{
    /// <summary>
    /// Almacén estático de sesión. Persiste el token JWT y el player ID
    /// durante toda la vida del proceso, sobreviviendo la destrucción
    /// del LoginHUDPanel tras el login exitoso.
    ///
    /// P7: agrega el registro de upgrades LSG pendientes de reaplicación.
    /// El flujo de R.E.P.O. es Tienda → Nivel previo → Nivel nuevo.
    /// PunManager.UpgradePlayer*() escribe en StatsManager, pero
    /// PlayerController.LateStart() y PhysGrabber.LateStart() leen
    /// StatsManager al cargar cada nivel y recalculan los stats desde cero.
    /// Para que los upgrades LSG sobrevivan esa recarga, los registramos
    /// aquí y los reaplicamos vía Postfix sobre PlayerController.LateStart().
    /// </summary>
    public static class SessionManager
    {
        public static string BearerToken { get; private set; } = null;
        public static int PlayerId { get; private set; } = -1;

        public static bool IsActive =>
            !string.IsNullOrEmpty(BearerToken) && PlayerId >= 0;

        // ── Upgrades LSG pendientes ───────────────────────────────────
        // Contadores acumulados de upgrades LSG aplicados en esta run.
        // Se incrementan al canjear en tienda y se reaaplican en cada
        // LateStart() de nivel para sobrevivir la recarga de stats.
        public static int UpgradesStamina { get; private set; } = 0;
        public static int UpgradesGrip { get; private set; } = 0;
        public static int UpgradesHealth { get; private set; } = 0;
        public static int UpgradesSpeed { get; private set; } = 0;

        public static bool TieneUpgradesPendientes =>
            UpgradesStamina > 0 || UpgradesGrip > 0 ||
            UpgradesHealth > 0 || UpgradesSpeed > 0;

        // ── SESIÓN ────────────────────────────────────────────────────
        public static void Save(string token, int playerId)
        {
            BearerToken = token;
            PlayerId = playerId;
            VitaSyncPlugin.Log.LogInfo(
                "[Session] Sesión guardada. PlayerID=" + playerId);
        }

        /// <summary>
        /// Reemplaza el token JWT con uno nuevo obtenido por el
        /// endpoint POST /token/refresh, sin alterar el PlayerId.
        /// </summary>
        public static void UpdateToken(string newToken)
        {
            BearerToken = newToken;
            VitaSyncPlugin.Log.LogInfo(
                "[Session] Token JWT refrescado correctamente.");
        }

        public static void Clear()
        {
            BearerToken = null;
            PlayerId = -1;
            ClearUpgrades();
            VitaSyncPlugin.Log.LogInfo("[Session] Sesión limpiada.");
        }

        // ── Upgrades LSG ──────────────────────────────────────────────

        /// <summary>
        /// Registra un upgrade LSG recién canjeado en tienda.
        /// idx: 0=Stamina, 1=Grip, 2=Health, 3=Speed
        /// </summary>
        public static void AddUpgrade(int idx)
        {
            switch (idx)
            {
                case 0: UpgradesStamina++; break;
                case 1: UpgradesGrip++; break;
                case 2: UpgradesHealth++; break;
                case 3: UpgradesSpeed++; break;
            }
            VitaSyncPlugin.Log.LogInfo(
                "[Session] Upgrade registrado idx=" + idx +
                " | STM=" + UpgradesStamina +
                " GRP=" + UpgradesGrip +
                " HP=" + UpgradesHealth +
                " SPD=" + UpgradesSpeed);
        }

        /// <summary>
        /// Limpia los contadores de upgrades. Se llama al cerrar sesión
        /// o al iniciar una run nueva (no implementado aquí: lo gestiona
        /// el Plugin si en algún momento se detecta un reset de run).
        /// </summary>
        public static void ClearUpgrades()
        {
            UpgradesStamina = 0;
            UpgradesGrip = 0;
            UpgradesHealth = 0;
            UpgradesSpeed = 0;
            VitaSyncPlugin.Log.LogInfo("[Session] Upgrades LSG limpiados.");
        }
    }
}