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
            var buttonObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var dispenser = buttonObject.AddComponent<GachaDispenser>();
            var material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            var serialized = new SerializedObject(dispenser);
            serialized.FindProperty("capsuleSpawnPoint").objectReferenceValue = spawnPointObject.transform;
            serialized.FindProperty("capsuleMaterial").objectReferenceValue = material;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            try
            {
                dispenser.Dispense();

                var capsule = GameObject.Find("Gacha Capsule 1");
                Assert.That(capsule, Is.Not.Null);
                dispenser.Dispense();
                Assert.That(GameObject.Find("Gacha Capsule 2"), Is.Null);
                Assert.That(capsule.transform.position, Is.EqualTo(spawnPointObject.transform.position));
                Assert.That(capsule.transform.localScale, Is.EqualTo(Vector3.one * 0.12f));
                Assert.That(capsule.GetComponent<Renderer>().sharedMaterial, Is.EqualTo(material));
                Assert.That(capsule.GetComponent<SphereCollider>(), Is.Not.Null);
                Assert.That(capsule.GetComponent<Rigidbody>().isKinematic, Is.False);
                Assert.That(capsule.GetComponent<Rigidbody>().useGravity, Is.True);
                Assert.That(capsule.GetComponent<XRGrabInteractable>(), Is.Not.Null);
                Assert.That(capsule.GetComponent<GachaCapsuleTriggerGrab>(), Is.Not.Null);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(buttonObject);
                UnityEngine.Object.DestroyImmediate(spawnPointObject);
                UnityEngine.Object.DestroyImmediate(material);
                var capsule = GameObject.Find("Gacha Capsule 1");
                if (capsule != null) UnityEngine.Object.DestroyImmediate(capsule);
            }
        }
    }
}
