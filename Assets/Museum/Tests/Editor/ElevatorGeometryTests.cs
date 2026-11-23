using NUnit.Framework;
using UnityEngine;

namespace VirtualMuseum.App.Tests
{
    public class ElevatorGeometryTests
    {
        [Test]
        public void TubeFacesPointTowardsTheAxis()
        {
            var mesh = ElevatorGeometry.Tube(0.5f, 0f, 3f, 16, false);
            var v = mesh.vertices; var t = mesh.triangles;
            for (int i = 0; i < t.Length; i += 3)
            {
                Vector3 a = v[t[i]], b = v[t[i + 1]], c = v[t[i + 2]];
                var face = Vector3.Cross(b - a, c - a);
                var centre = (a + b + c) / 3f;
                var toAxis = new Vector3(-centre.x, 0, -centre.z);
                Assert.Greater(Vector3.Dot(face, toAxis), 0f, "triangle " + i / 3 + " faces outwards");
            }
            Object.DestroyImmediate(mesh);
        }

        [Test]
        public void UpTubeCapFacesDownIntoTheTube()
        {
            var mesh = ElevatorGeometry.Tube(0.5f, 2f, 5f, 8, true);
            var v = mesh.vertices; var t = mesh.triangles;
            int caps = 0;
            for (int i = 0; i < t.Length; i += 3)
            {
                Vector3 a = v[t[i]], b = v[t[i + 1]], c = v[t[i + 2]];
                if (Mathf.Abs(a.y - 5f) > 1e-4f || Mathf.Abs(b.y - 5f) > 1e-4f || Mathf.Abs(c.y - 5f) > 1e-4f) continue;
                caps++;
                Assert.Less(Vector3.Cross(b - a, c - a).y, 0f);
            }
            Assert.AreEqual(8, caps);
            Object.DestroyImmediate(mesh);
        }

        [Test]
        public void CollarFillsTheSquareOutsideTheCircle()
        {
            var mesh = ElevatorGeometry.Collar(0.5f, 0.55f, -0.05f, 0f, 32);
            foreach (var p in mesh.vertices)
            {
                float r = new Vector2(p.x, p.z).magnitude;
                Assert.GreaterOrEqual(r, 0.5f - 1e-4f);
                Assert.LessOrEqual(Mathf.Max(Mathf.Abs(p.x), Mathf.Abs(p.z)), 0.55f + 1e-4f);
            }
            // top faces up, bottom faces down
            var v = mesh.vertices; var t = mesh.triangles;
            for (int i = 0; i < t.Length; i += 3)
            {
                Vector3 a = v[t[i]], b = v[t[i + 1]], c = v[t[i + 2]];
                float ny = Vector3.Cross(b - a, c - a).y;
                if (a.y > -0.01f) Assert.Greater(ny, 0f); else Assert.Less(ny, 0f);
            }
            Object.DestroyImmediate(mesh);
        }

        [Test]
        public void SquarePointLiesOnTheSquare()
        {
            for (int i = 0; i < 32; i++)
            {
                var p = ElevatorGeometry.SquarePoint(i * Mathf.PI / 16f, 0.6f);
                Assert.AreEqual(0.6f, Mathf.Max(Mathf.Abs(p.x), Mathf.Abs(p.z)), 1e-4f);
            }
        }

        [Test]
        public void LeverAngleFollowsTheHand()
        {
            Assert.AreEqual(0f, ElevatorGeometry.AngleTowards(Vector3.up), 1e-3f);
            // handle towards +Z (away from the user) = positive angle, towards -Z (the user) = negative
            Assert.AreEqual(45f, ElevatorGeometry.AngleTowards(new Vector3(0, 1, 1)), 1e-3f);
            Assert.AreEqual(-45f, ElevatorGeometry.AngleTowards(new Vector3(0.3f, 1, -1)), 1e-3f);
            // consistent with Quaternion.Euler(angle, 0, 0) applied to the upright handle
            var dir = Quaternion.Euler(ElevatorLever.OnAngle, 0, 0) * Vector3.up;
            Assert.AreEqual(ElevatorLever.OnAngle, ElevatorGeometry.AngleTowards(dir), 1e-3f);
            Assert.Less(dir.z, 0f);
        }

        [Test]
        public void LeverSide_AvoidsArtworks()
        {
            var right = new Vector2(1f, 0f); var left = new Vector2(-1f, 0f);
            var none = new System.Collections.Generic.List<Rect>();
            Assert.AreEqual(1, ElevatorGeometry.LeverSide(right, left, none));
            var onTheRight = new System.Collections.Generic.List<Rect> { Rect.MinMaxRect(1.2f, -0.5f, 2f, 0.5f) };
            Assert.AreEqual(-1, ElevatorGeometry.LeverSide(right, left, onTheRight));
        }

        [Test]
        public void DistanceToRect_IsZeroInsideAndEuclideanOutside()
        {
            var r = Rect.MinMaxRect(0f, 0f, 1f, 1f);
            Assert.AreEqual(0f, ElevatorGeometry.DistanceToRect(new Vector2(0.5f, 0.5f), r));
            Assert.AreEqual(1f, ElevatorGeometry.DistanceToRect(new Vector2(2f, 0.5f), r), 1e-5f);
            Assert.AreEqual(Mathf.Sqrt(2f), ElevatorGeometry.DistanceToRect(new Vector2(2f, 2f), r), 1e-5f);
        }

        [Test]
        public void EaseStartsAndEndsFlat()
        {
            Assert.AreEqual(0f, ElevatorGeometry.Ease(0f));
            Assert.AreEqual(1f, ElevatorGeometry.Ease(1f));
            Assert.AreEqual(0.5f, ElevatorGeometry.Ease(0.5f), 1e-5f);
            Assert.Less(ElevatorGeometry.Ease(0.05f), 0.05f);
        }
    }
}
