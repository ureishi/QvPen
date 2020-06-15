using UdonSharp;
using UnityEngine;

namespace QvPen.Udon
{
    public class Ink : UdonSharpBehaviour
    {
        [SerializeField] private TrailRenderer trailRenderer;
        [SerializeField] private MeshFilter meshFilter;
        [SerializeField] private MeshCollider meshCollider;

        public void FinishDrawing()
        {
            var positionCount = trailRenderer.positionCount;
            if (positionCount < 2)
            {
                positionCount = 2;
            }

            var verticesParPoint = 2;
            var trianglesCountParPoint = 6;
            var positions = new Vector3[positionCount];
            var vertices = new Vector3[positionCount * verticesParPoint];
            var triangles = new int[(positionCount - 1) * trianglesCountParPoint];

            var colliderWidth = 0.005f;
            var offsetXP = new Vector3(colliderWidth, 0, 0);
            var offsetXN = new Vector3(-colliderWidth, 0, 0);

            trailRenderer.GetPositions(positions);
            if (positionCount == 2)
            {
                positions[0] = transform.position;
                positions[1] = transform.position + Vector3.down * colliderWidth;
            }

            // Create vertices
            for (var i = 0; i < positionCount; i++)
            {
                var position = transform.InverseTransformPoint(positions[i]);
                vertices[i * verticesParPoint + 0] = position + offsetXP;
                vertices[i * verticesParPoint + 1] = position + offsetXN;
            }

            // Create triangles
            for (var i = 0; i < positionCount - 1; i++)
            {
                triangles[i * trianglesCountParPoint + 0] = i * verticesParPoint + 0;
                triangles[i * trianglesCountParPoint + 1] = i * verticesParPoint + 1;
                triangles[i * trianglesCountParPoint + 2] = i * verticesParPoint + 2;
                triangles[i * trianglesCountParPoint + 3] = i * verticesParPoint + 3;
                triangles[i * trianglesCountParPoint + 4] = i * verticesParPoint + 2;
                triangles[i * trianglesCountParPoint + 5] = i * verticesParPoint + 1;
            }

            // Instantiate a mesh
            var mesh = meshFilter.mesh;

            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();

            meshFilter.mesh = mesh;
            meshCollider.sharedMesh = mesh;
        }
    }
}
