using UnityEngine;
using UnityEditor;

// Utility di editor: controlla che gli oggetti "muro" abbiano il tag 'wall'.
// Uso: menu  Tools > Check Wall Tags  (con la scena aperta).
public class WallTagChecker
{
    // Parole chiave che fanno sospettare un muro, per intercettare quelli senza tag.
    static readonly string[] wallKeywords = { "wall", "barrier", "fence", "rail", "muro" };

    [MenuItem("Tools/Check Wall Tags")]
    static void CheckWallTags()
    {
        var colliders = Object.FindObjectsOfType<Collider>();
        int tagged = 0;
        int suspects = 0;

        foreach (var c in colliders)
        {
            bool isWall = c.CompareTag("wall");
            if (isWall) { tagged++; continue; }

            string n = c.gameObject.name.ToLower();
            foreach (var kw in wallKeywords)
            {
                if (n.Contains(kw))
                {
                    suspects++;
                    // Clic sul warning nella Console per selezionare l'oggetto in scena.
                    Debug.LogWarning($"⚠️ Sembra un muro ma NON ha tag 'wall': {c.gameObject.name}", c.gameObject);
                    break;
                }
            }
        }

        Debug.Log($"✅ Collider con tag 'wall': {tagged}. Possibili muri SENZA tag: {suspects}. " +
                  (suspects == 0 ? "Nessun sospetto trovato." : "Controlla i warning sopra (clicca per selezionarli)."));
    }
}
