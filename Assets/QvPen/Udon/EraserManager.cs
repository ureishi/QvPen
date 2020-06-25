using UdonSharp;
using UnityEngine;

namespace QvPen.Udon
{
    public class EraserManager : UdonSharpBehaviour
    {
        [SerializeField] private Eraser eraser;

        public void ResetAll()
        {
            eraser.Respawn();
        }
    }
}
