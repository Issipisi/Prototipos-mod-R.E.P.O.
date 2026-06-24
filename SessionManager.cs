namespace VitaSync
{
    /// <summary>
    /// Almacén estático de sesión. Persiste el token JWT y el player ID
    /// durante toda la vida del proceso, sobreviviendo la destrucción
    /// del LoginHUDPanel tras el login exitoso.
    /// </summary>
    public static class SessionManager
    {
        public static string BearerToken { get; private set; } = null;
        public static int PlayerId { get; private set; } = -1;
        public static bool IsActive => !string.IsNullOrEmpty(BearerToken)
                                            && PlayerId >= 0;

        public static void Save(string token, int playerId)
        {
            BearerToken = token;
            PlayerId = playerId;
            VitaSyncPlugin.Log.LogInfo(
                "[Session] Sesión guardada. PlayerID=" + playerId);
        }

        public static void Clear()
        {
            BearerToken = null;
            PlayerId = -1;
            VitaSyncPlugin.Log.LogInfo("[Session] Sesión limpiada.");
        }
    }
}