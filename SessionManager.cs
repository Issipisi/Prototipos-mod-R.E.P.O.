namespace VitaSync
{
    public static class SessionManager
    {
        public static string BearerToken { get; private set; } = null;
        public static int PlayerId { get; private set; } = -1;

        public static bool IsActive =>
            !string.IsNullOrEmpty(BearerToken) && PlayerId >= 0;

        // Contadores de upgrades LSG acumulados en la run actual.
        public static int UpgradesStamina { get; private set; } = 0;
        public static int UpgradesGrip { get; private set; } = 0;
        public static int UpgradesHealth { get; private set; } = 0;
        public static int UpgradesSpeed { get; private set; } = 0;

        public static bool TieneUpgradesPendientes =>
            UpgradesStamina > 0 || UpgradesGrip > 0 ||
            UpgradesHealth > 0 || UpgradesSpeed > 0;

        public static void Save(string token, int playerId)
        {
            BearerToken = token;
            PlayerId = playerId;
            VitaSyncPlugin.Log.LogInfo(
                "[Session] Sesión guardada. PlayerID=" + playerId);
        }

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

        public static void ClearUpgrades()
        {
            UpgradesStamina = 0;
            UpgradesGrip = 0;
            UpgradesHealth = 0;
            UpgradesSpeed = 0;
        }
    }
}