using UnityEngine;

namespace SheetHappens
{
    public enum PropKind { WallFurniture, Table, Chair, Carpet, HideTable, Barricade, Throwable, HideCupboard, FloorDecor, WallDecor, Clutter }

    /// <summary>
    /// Marks a furniture piece or prop placed in the house. Pieces under a "[FURNITURE]" object in the scene are used
    /// as the saved layout (move, delete or duplicate them freely); without one, the game furnishes the house itself.
    /// </summary>
    public class PlacedProp : MonoBehaviour
    {
        [Tooltip("What the game does with it: HideTable = crouch here to hide, Barricade = pushable, Throwable = pick up with E.")]
        public PropKind kind = PropKind.WallFurniture;
        [HideInInspector] public GameObject prefab;
    }

}
