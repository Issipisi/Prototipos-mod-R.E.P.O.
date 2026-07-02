using System;
using UnityEngine;

namespace VitaSync
{
    /// <summary>
    /// Utilidades compartidas del módulo cloud LifeSync-Games.
    /// El flujo de autenticación y balance se gestiona en LoginHUDPanel.
    /// Esta clase expone solo ExtractString, ParseDimensionBalance
    /// y PhysicalProfile, que son los únicos elementos usados externamente.
    /// </summary>
    public class LifeSyncClient : MonoBehaviour
    {
        internal static string ExtractString(string json, string key)
        {
            string k = "\"" + key + "\"";
            int ki = json.IndexOf(k, StringComparison.Ordinal);
            if (ki < 0) return null;
            int ci = json.IndexOf(':', ki + k.Length);
            if (ci < 0) return null;
            int vs = ci + 1;
            while (vs < json.Length && char.IsWhiteSpace(json[vs])) vs++;
            if (vs >= json.Length) return null;
            if (json[vs] == '"')
            {
                int ve = json.IndexOf('"', vs + 1);
                return ve < 0 ? null : json.Substring(vs + 1, ve - vs - 1);
            }
            int end = vs;
            while (end < json.Length &&
                   json[end] != ',' && json[end] != '}' && json[end] != ']') end++;
            return json.Substring(vs, end - vs).Trim();
        }

        internal static int ParseDimensionBalance(string json, string dimId)
        {
            int idx = 0;
            while (true)
            {
                int di = json.IndexOf("\"id_point_dimension\"", idx, StringComparison.Ordinal);
                if (di < 0) break;
                int oe = json.IndexOf('}', di);
                if (oe < 0) break;
                string slice = json.Substring(di, oe - di + 1);
                string dimVal = ExtractString(slice, "id_point_dimension");
                if (dimVal == dimId)
                {
                    string balStr = ExtractString(slice, "balance");
                    if (int.TryParse(balStr, out int b)) return b;
                }
                idx = oe + 1;
            }
            return 0;
        }

        public class PhysicalProfile
        {
            public int Puntos { get; set; }
            public int CostoStamina { get; private set; }
            public int CostoGrip { get; private set; }
            public int CostoHealth { get; private set; }
            public int CostoSpeed { get; private set; }
            public int CanjesUsados { get; set; }
            public int CanjesMax { get; set; }

            public bool PuedeCanjeaMas => CanjesUsados < CanjesMax;

            public PhysicalProfile()
            {
                Puntos = 0;
                CostoStamina = 20;
                CostoGrip = 20;
                CostoHealth = 30;
                CostoSpeed = 30;
                CanjesUsados = 0;
                CanjesMax = 2;
            }

            public static PhysicalProfile FromPoints(int pts) =>
                new PhysicalProfile { Puntos = pts };

            public bool PuedePagar(int costo) =>
                PuedeCanjeaMas && Puntos >= costo;

            public override string ToString() =>
                "Puntos=" + Puntos + " | Canjes=" + CanjesUsados + "/" + CanjesMax;
        }
    }
}