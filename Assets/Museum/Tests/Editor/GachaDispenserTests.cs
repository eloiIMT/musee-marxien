using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using VirtualMuseum.App;

namespace VirtualMuseum.App.Tests
{
    public class GachaDispenserTests
    {
        [Test]
        public void DispensingCreatesACapsuleAtTheConfiguredOutput()
        {
            var spawnPointObject = new GameObject("Test Capsule Spawn Point");
            spawnPointObject.transform.position = new Vector3(1f, 2f, 3f);
            var machineObject = new GameObject("Test Gacha Machine");
            var dispenser = machineObject.AddComponent<GachaDispenser>();
            var material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            var serialized = new SerializedObject(dispenser);
            serialized.FindProperty("capsuleSpawnPoint").objectReferenceValue = spawnPointObject.transform;
            serialized.FindProperty("capsuleMaterial").objectReferenceValue = material;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            try
            {
                Assert.That(dispenser.Dispense(), Is.True);

                var capsule = GameObject.Find("Gacha Capsule 1");
                Assert.That(capsule, Is.Not.Null);
                Assert.That(dispenser.HasWaitingCapsule, Is.True);
                Assert.That(dispenser.Dispense(), Is.False);
                Assert.That(GameObject.Find("Gacha Capsule 2"), Is.Null);
                Assert.That(capsule.transform.position, Is.EqualTo(spawnPointObject.transform.position));
                Assert.That(capsule.transform.localScale, Is.EqualTo(Vector3.one * 0.12f));
                Assert.That(capsule.GetComponent<Renderer>().sharedMaterial, Is.EqualTo(material));
                Assert.That(capsule.GetComponent<SphereCollider>(), Is.Not.Null);
                Assert.That(capsule.GetComponent<Rigidbody>().isKinematic, Is.False, "no rest point: plain physics drop");
                Assert.That(capsule.GetComponent<Rigidbody>().useGravity, Is.True);
                Assert.That(capsule.GetComponent<XRGrabInteractable>(), Is.Not.Null);
                Assert.That(capsule.GetComponent<GachaCapsuleTriggerGrab>(), Is.Not.Null);
            }
            finally
            {
                Object.DestroyImmediate(machineObject);
                Object.DestroyImmediate(spawnPointObject);
                Object.DestroyImmediate(material);
                var capsule = GameObject.Find("Gacha Capsule 1");
                if (capsule != null) Object.DestroyImmediate(capsule);
            }
        }

        [Test]
        public void CapsuleWaitsKinematicInTheCupWhenARestPointIsSet()
        {
            var spawnPointObject = new GameObject("Test Capsule Spawn Point");
            var restPointObject = new GameObject("Test Capsule Rest Point");
            var machineObject = new GameObject("Test Gacha Machine");
            var dispenser = machineObject.AddComponent<GachaDispenser>();
            var material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            var serialized = new SerializedObject(dispenser);
            serialized.FindProperty("capsuleSpawnPoint").objectReferenceValue = spawnPointObject.transform;
            serialized.FindProperty("capsuleRestPoint").objectReferenceValue = restPointObject.transform;
            serialized.FindProperty("capsuleMaterial").objectReferenceValue = material;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            try
            {
                Assert.That(dispenser.Dispense(), Is.True);
                var capsule = GameObject.Find("Gacha Capsule 1");
                Assert.That(capsule.GetComponent<Rigidbody>().isKinematic, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(machineObject);
                Object.DestroyImmediate(spawnPointObject);
                Object.DestroyImmediate(restPointObject);
                Object.DestroyImmediate(material);
                var capsule = GameObject.Find("Gacha Capsule 1");
                if (capsule != null) Object.DestroyImmediate(capsule);
            }
        }

        [Test]
        public void TwistAngleMeasuresRollAroundTheKnobAxis()
        {
            var axis = new Vector3(0.88f, 0f, 0.48f).normalized;
            Assert.That(GachaCrank.TwistAngle(Quaternion.AngleAxis(30f, axis), axis), Is.EqualTo(30f).Within(0.01f));
            Assert.That(GachaCrank.TwistAngle(Quaternion.AngleAxis(-20f, axis), axis), Is.EqualTo(-20f).Within(0.01f));
            Assert.That(GachaCrank.TwistAngle(Quaternion.AngleAxis(40f, Vector3.up), axis), Is.EqualTo(0f).Within(0.01f));
        }
    }
}
