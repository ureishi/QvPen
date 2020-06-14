using UdonSharp;
using UnityEngine;

namespace ureishi.Udon.QvPen
{
    public class SmoothFollow : UdonSharpBehaviour
    {
        [SerializeField] private Transform target;
        [SerializeField] private float followSpeed = 3;

        private void LateUpdate()
        {
            transform.position = Vector3.Lerp(transform.position, target.position, Time.deltaTime * followSpeed);
            transform.rotation = Quaternion.Lerp(transform.rotation, target.rotation, Time.deltaTime * followSpeed);
        }
    }
}