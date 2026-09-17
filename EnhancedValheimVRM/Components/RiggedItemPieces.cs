using System.Collections.Generic;
using UnityEngine;

namespace EnhancedValheimVRM
{
    // sits on a rigged weapon and remembers which of its hand and forearm bones got moved onto the avatar.
    // those bones are not on the weapon anymore, so this destroys them when the weapon is destroyed, like on unequip
    public sealed class RiggedItemPieces : MonoBehaviour
    {
        internal Animator Avatar;
        internal readonly List<Transform> Moved = new List<Transform>();

        private void OnDestroy()
        {
            foreach (var piece in Moved)
            {
                if (piece != null) Destroy(piece.gameObject);
            }
        }
    }
}
