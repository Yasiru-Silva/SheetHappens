using UnityEngine;

namespace SheetHappens
{
    /// <summary>Marks a saved "[FURNITURE]" layout and the furniture size it was made for (older layouts are ignored).
    /// Lives in its own file so Unity can save it in scenes (a MonoBehaviour must share its file's name).</summary>
    public class FurnitureLayoutInfo : MonoBehaviour
    {
        public int version = 2;
    }
}
