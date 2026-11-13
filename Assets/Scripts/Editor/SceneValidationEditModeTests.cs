// SceneValidationEditModeTests.cs — EditMode test opening each scene in the build list and asserting 0 missing scripts, 0 broken references, and 0 logged errors.
using NUnit.Framework;
using UnityEditor;
using UnityEngine.TestTools;

namespace F1Sim.Editor.Tests
{
    public class SceneValidationEditModeTests
    {
        [Test]
        public void ValidateAllBuildScenesHaveNoMissingScriptsOrBrokenReferences()
        {
            var scenes = EditorBuildSettings.scenes;
            Assert.IsNotEmpty(scenes, "Build scenes list in EditorBuildSettings must not be empty.");

            foreach (var sceneEntry in scenes)
            {
                if (!sceneEntry.enabled) continue;

                var result = SceneValidator.ValidateScene(sceneEntry.path);

                Assert.AreEqual(0, result.loggedErrors.Count,
                    $"Scene '{sceneEntry.path}' logged errors during opening:\n" + string.Join("\n", result.loggedErrors));

                Assert.AreEqual(0, result.missingScripts.Count,
                    $"Scene '{sceneEntry.path}' has missing scripts:\n" + string.Join("\n", result.missingScripts));

                Assert.AreEqual(0, result.brokenReferences.Count,
                    $"Scene '{sceneEntry.path}' has broken object references:\n" + string.Join("\n", result.brokenReferences));

                LogAssert.NoUnexpectedReceived();
            }
        }
    }
}

