using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VirtualMuseum.Tests
{
    public class MuseumSceneTests
    {
        [Test]
        public void StudentSceneHasBackendXRAndNoMissingScripts()
        {
            const string path = "Assets/Scenes/VirtualMuseum.unity";
            Assert.That(EditorBuildSettings.scenes.Length, Is.EqualTo(1));
            Assert.That(EditorBuildSettings.scenes[0].path, Is.EqualTo(path));
            Assert.That(EditorBuildSettings.scenes[0].enabled, Is.True);
            var scene = SceneManager.GetSceneByPath(path);
            bool opened = !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            try
            {
                var objects = scene.GetRootGameObjects().SelectMany(go => go.GetComponentsInChildren<Transform>(true)).Select(t => t.gameObject).ToArray();
                foreach (var go in objects)
                    Assert.That(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(go), Is.Zero, go.name);
                var components = objects.SelectMany(go => go.GetComponents<MonoBehaviour>()).Where(c => c != null).ToArray();
                Assert.That(components.Count(c => c is MuseumBackend), Is.EqualTo(1));
                Assert.That(components.Any(c => c.GetType().FullName == "Unity.XR.CoreUtils.XROrigin"), Is.True);
                var backend = components.OfType<MuseumBackend>().Single();
                Assert.That(backend.Api.Validate(), Is.Empty);
                Assert.That(backend.Api.Data.artworks.Count, Is.EqualTo(6));
                Assert.That(objects.SelectMany(go => go.GetComponents<Camera>()).Count(), Is.EqualTo(1));
            }
            finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
        }
    }
}
